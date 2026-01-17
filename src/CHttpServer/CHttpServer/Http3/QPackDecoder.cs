using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using System.Text;
using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class QPackDecoder
{
    private enum DecoderState
    {
        ReadRequiredInsertionCount,
        ReadBase,
        FieldLineFirstByte,
        NameReferenceWithLength,
        NameReferenceValue,
        LiteralNameWithLength,
        LiteralFieldValueWithLength,
        LiteralFieldValue,
    }

    private const string UnsupportedDynamicTable = "Dynamic table size is 0.";

    private QuicStream? _qpackEncodingStream;
    private QuicStream? _qpackDecodingStream;
    private Task? _reading;
    private DecoderState _status = DecoderState.ReadRequiredInsertionCount;

    private bool _huffmanEncoding;
    private int _fieldNameIndex;
    private int _fieldNameLength;
    private ReadOnlySequence<byte> _fieldName;
    private int _fieldValueLength;

    public void SetEncodingStream(QuicStream stream, PipeReader reader)
    {
        _qpackEncodingStream = stream;
    }

    public void SetDecodingStream(QuicStream stream, CancellationToken token)
    {
        _qpackDecodingStream = stream;
        _reading = RunAsync(token);
    }

    public async Task RunAsync(CancellationToken token)
    {
        Debug.Assert(_qpackDecodingStream != null);
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!token.IsCancellationRequested)
                await _qpackDecodingStream.ReadExactlyAsync(buffer.AsMemory(), token);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task Reset()
    {
        _status = DecoderState.ReadRequiredInsertionCount;
    }

    public async Task Close()
    {
        _qpackEncodingStream?.Abort(QuicAbortDirection.Read, ErrorCodes.H3StreamCreationError);
        _qpackEncodingStream = null;
        _qpackDecodingStream?.Abort(QuicAbortDirection.Write, ErrorCodes.H3StreamCreationError);
        _qpackDecodingStream = null;
        await (_reading ?? Task.CompletedTask);
    }

    public bool DecodeHeader(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out long consumed)
    {
        consumed = 0;
        int currentConsumedBytes = 0;
        bool stepCompleted = true;
        while (stepCompleted && source.Length > 0)
        {
            switch (_status)
            {
                case DecoderState.ReadRequiredInsertionCount:
                    stepCompleted = DecodeRequiredInsertionCount(source, handler, out currentConsumedBytes);
                    break;
                case DecoderState.ReadBase:
                    stepCompleted = DecodeBase(source, handler, out currentConsumedBytes);
                    break;
                case DecoderState.FieldLineFirstByte:
                    stepCompleted = DecodeFieldLineFirstByte(source, handler, out currentConsumedBytes);
                    break;
                case DecoderState.NameReferenceWithLength:
                    stepCompleted = DecodeLiteralNamReferenceFieldValueLength(source, handler, source.FirstSpan[0], out currentConsumedBytes);
                    break;
                case DecoderState.NameReferenceValue:
                    stepCompleted = DecodeLiteralNameReferenceFieldValue(source, handler, out currentConsumedBytes);
                    break;
                case DecoderState.LiteralNameWithLength:
                    stepCompleted = DecodeLiteralFieldName(source, handler, out currentConsumedBytes);
                    break;
                case DecoderState.LiteralFieldValueWithLength:
                    stepCompleted = DecodeLiteralFieldValueLength(source, handler, source.FirstSpan[0], out currentConsumedBytes);
                    break;
                case DecoderState.LiteralFieldValue:
                    stepCompleted = DecodeLiteralFieldValue(source, handler, out currentConsumedBytes);
                    break;
            }
            consumed += currentConsumedBytes;
            source = source.Slice(currentConsumedBytes);
        }
        return stepCompleted;
    }

    private bool DecodeFieldLineFirstByte(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out int consumed)
    {
        var firstByte = source.FirstSpan[0];
        if ((firstByte & 0b1000_0000) == 0b1000_0000)
        {
            return DecodeIndexedFieldLine(source, handler, firstByte, out consumed);
        }
        else if ((firstByte & 0b1100_0000) == 0b0100_0000)
        {
            return DecodeLiteralFieldLineWithNameReference(source, handler, firstByte, out consumed);
        }
        else if ((firstByte & 0b1110_0000) == 0b0010_0000)
        {
            return DecodeLiteralFieldLineWithLiteralName(source, handler, firstByte, out consumed);
        }
        else if ((firstByte & 0b1111_0000) == 0b0001_0000 || (firstByte & 0b1111_0000) == 0b0000_0000)
        {
            throw new HeaderDecodingException(UnsupportedDynamicTable);
        }
        consumed = 0;
        return false;
    }

    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 1 | T |      Index (6+)       |
    // +---+---+-----------------------+
    private static bool DecodeIndexedFieldLine(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, byte firstByte, out int consumed)
    {
        if ((firstByte & 0b0100_0000) == 0b0000_0000)
            throw new HeaderDecodingException(UnsupportedDynamicTable);
        var decoder = new QPackIntegerDecoder();
        consumed = 1;
        int index = 0;
        if (!decoder.BeginTryDecode((byte)(firstByte & 0b0011_1111), 6, out index))
        {
            if (!decoder.TryDecodeInteger(source, ref consumed, out index))
            {
                // Need more data
                consumed = 0;
                return false;
            }
        }

        var staticHeader = _staticDecoderTable[index];
        handler.OnHeader(staticHeader);
        return true;
    }

    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 0 | 1 | N | T |Name Index (4+)|
    // +---+---+---+---+---------------+
    // | H |     Value Length (7+)     |
    // +---+---------------------------+
    // |  Value String (Length bytes)  |
    // +-------------------------------+
    private bool DecodeLiteralFieldLineWithNameReference(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, byte firstByte, out int consumed)
    {
        if ((firstByte & 0b0001_0000) == 0b0000_0000)
            throw new HeaderDecodingException(UnsupportedDynamicTable);
        var decoder = new QPackIntegerDecoder();
        consumed = 1;
        int index = 0;
        if (!decoder.BeginTryDecode((byte)(firstByte & 0b0000_1111), 4, out index))
        {
            if (!decoder.TryDecodeInteger(source, ref consumed, out index))
            {
                // Need more data
                consumed = 0;
                return false;
            }
        }
        _fieldNameIndex = index;
        _status = DecoderState.NameReferenceWithLength;

        source = source.Slice(consumed);
        if (source.IsEmpty)
            return false;
        var result = DecodeLiteralNamReferenceFieldValueLength(source, handler, source.FirstSpan[0], out int consumedLiteral);
        consumed += consumedLiteral;
        return result;
    }

    private bool DecodeLiteralNamReferenceFieldValueLength(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, byte firstByte, out int consumed)
    {
        _huffmanEncoding = (firstByte & 0b1000_0000) == 0b1000_0000;
        var decoder = new QPackIntegerDecoder();
        consumed = 1;
        if (!decoder.BeginTryDecode((byte)(firstByte & 0b0111_1111), 7, out var literalLength))
        {
            if (!decoder.TryDecodeInteger(source, ref consumed, out literalLength))
            {
                // Need more data
                consumed = 0;
                return false;
            }
        }
        _fieldValueLength = literalLength;
        _status = DecoderState.NameReferenceValue;
        source = source.Slice(consumed);
        var result = DecodeLiteralNameReferenceFieldValue(source, handler, out int consumedLiteral);
        consumed += consumedLiteral;
        return result;
    }

    private bool DecodeLiteralNameReferenceFieldValue(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out int consumed)
    {
        consumed = 0;
        if (source.Length < _fieldValueLength)
            return false;
        source = source.Slice(0, _fieldValueLength);
        if (_huffmanEncoding)
        {
            IMemoryOwner<byte> decodedValue = MemoryPool<byte>.Shared.Rent(_fieldValueLength * 2);
            int decodedLength;
            if (source.IsSingleSegment)
                decodedLength = Huffman.Decode(source.FirstSpan, ref decodedValue);
            else
            {
                var input = ArrayPool<byte>.Shared.Rent(_fieldValueLength);
                var buffer = input.AsSpan(0, _fieldValueLength);
                source.CopyTo(buffer);
                decodedLength = Huffman.Decode(buffer, ref decodedValue);
                ArrayPool<byte>.Shared.Return(input);
            }
            handler.OnHeader(_staticDecoderTable[_fieldNameIndex], new ReadOnlySequence<byte>(decodedValue.Memory[0..decodedLength]));
            decodedValue.Dispose();
        }
        else
        {
            handler.OnHeader(_staticDecoderTable[_fieldNameIndex], source);
        }
        consumed = _fieldValueLength;
        _fieldValueLength = 0;
        _huffmanEncoding = false;
        _status = DecoderState.FieldLineFirstByte;
        return true;
    }

    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 0 | 0 | 1 | N | H |NameLen(3+)|
    // +---+---+---+---+---+-----------+
    // |  Name String (Length bytes)   |
    // +---+---------------------------+
    // | H |     Value Length (7+)     |
    // +---+---------------------------+
    // |  Value String (Length bytes)  |
    // +-------------------------------+
    private bool DecodeLiteralFieldLineWithLiteralName(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, byte firstByte, out int consumed)
    {
        _huffmanEncoding = (firstByte & 0b0000_1000) == 0b0000_1000;
        var decoder = new QPackIntegerDecoder();
        consumed = 1;
        if (!decoder.BeginTryDecode((byte)(firstByte & 0b0000_0111), 3, out var nameLength))
        {
            if (!decoder.TryDecodeInteger(source, ref consumed, out nameLength))
            {
                // Need more data
                consumed = 0;
                return false;
            }
        }
        _fieldNameLength = nameLength;
        _status = DecoderState.LiteralNameWithLength;
        source = source.Slice(consumed);
        var result = DecodeLiteralFieldName(source, handler, out int consumedLiteral);
        consumed += consumedLiteral;
        return result;
    }

    private bool DecodeLiteralFieldName(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out int consumed)
    {
        consumed = 0;
        if (source.Length < _fieldNameLength)
            return false;
        if (_huffmanEncoding)
        {
            var decodedValue = new byte[_fieldNameLength * 2];
            int decodedLength;
            if (source.IsSingleSegment)
                decodedLength = Huffman.Decode(source.FirstSpan, ref decodedValue);
            else
            {
                var input = ArrayPool<byte>.Shared.Rent(_fieldValueLength);
                var buffer = input.AsSpan(0, _fieldValueLength);
                source.CopyTo(buffer);
                decodedLength = Huffman.Decode(buffer, ref decodedValue);
                ArrayPool<byte>.Shared.Return(input);
            }
            _fieldName = new(decodedValue, 0, decodedLength);
        }
        else
        {
            _fieldName = source.Slice(0, _fieldNameLength);
        }
        consumed = _fieldNameLength;
        _status = DecoderState.LiteralFieldValueWithLength;
        source = source.Slice(consumed);
        if (source.IsEmpty)
            return false;
        var result = DecodeLiteralFieldValueLength(source, handler, source.FirstSpan[0], out int consumedLiteral);
        consumed += consumedLiteral;
        return result;
    }

    private bool DecodeLiteralFieldValueLength(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, byte firstByte, out int consumed)
    {
        _huffmanEncoding = (firstByte & 0b1000_1000) == 0b1000_0000;
        var decoder = new QPackIntegerDecoder();
        consumed = 1;
        if (!decoder.BeginTryDecode((byte)(firstByte & 0b0111_1111), 7, out var length))
        {
            if (!decoder.TryDecodeInteger(source, ref consumed, out length))
            {
                // Need more data
                consumed = 0;
                return false;
            }
        }
        _fieldValueLength = length;
        _status = DecoderState.LiteralFieldValue;
        source = source.Slice(consumed);
        var result = DecodeLiteralFieldValue(source, handler, out int consumedLiteral);
        consumed += consumedLiteral;
        return result;
    }

    private bool DecodeLiteralFieldValue(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out int consumed)
    {
        consumed = 0;
        if (source.Length < _fieldValueLength)
            return false;
        if (_huffmanEncoding)
        {
            IMemoryOwner<byte> decodedValue = MemoryPool<byte>.Shared.Rent(_fieldValueLength * 2);
            int decodedLength;
            if (source.IsSingleSegment)
                decodedLength = Huffman.Decode(source.FirstSpan, ref decodedValue);
            else
            {
                var input = ArrayPool<byte>.Shared.Rent(_fieldValueLength);
                var buffer = input.AsSpan(0, _fieldValueLength);
                source.CopyTo(buffer);
                decodedLength = Huffman.Decode(buffer, ref decodedValue);
                ArrayPool<byte>.Shared.Return(input);
            }
            handler.OnHeader(_fieldName, new ReadOnlySequence<byte>(decodedValue.Memory[0..decodedLength]));
            decodedValue.Dispose();
        }
        else
        {
            handler.OnHeader(_fieldName, source.Slice(0, _fieldValueLength));
        }
        consumed = _fieldValueLength;
        _status = DecoderState.FieldLineFirstByte;
        _fieldName = ReadOnlySequence<byte>.Empty;
        _fieldNameIndex = 0;
        _huffmanEncoding = false;
        _fieldValueLength = 0;
        _fieldNameLength = 0;
        return true;
    }


    private bool DecodeBase(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out int consumed)
    {
        var data = source.FirstSpan[0];
        var decoder = new QPackIntegerDecoder();
        if (!decoder.BeginTryDecode(data, 8, out var delta) || delta != 0)
            throw new HeaderDecodingException(UnsupportedDynamicTable);
        consumed = 1;
        _status = DecoderState.FieldLineFirstByte;
        return true;
    }

    private bool DecodeRequiredInsertionCount(ReadOnlySequence<byte> source, IQPackHeaderHandler handler, out int consumed)
    {
        var data = source.FirstSpan[0];
        var decoder = new QPackIntegerDecoder();
        if (!decoder.BeginTryDecode(data, 8, out var ric) || ric != 0)
            throw new HeaderDecodingException(UnsupportedDynamicTable);
        consumed = 1;
        _status = DecoderState.ReadBase;
        return true;
    }

    /// <summary>
    /// Encodes a header dictionary into a response stream.
    /// </summary>
    internal void Encode(int statusCode, Http3ResponseHeaderCollection headers, PipeWriter destinationWriter)
    {
        EncodeFieldSectionPrefix(destinationWriter);
        if (_statusCodesEncoderTable.TryGetValue(statusCode, out var knownHeader))
            EncodeFieldLine(knownHeader, destinationWriter);
        else
            EncodeFieldLine((":status", statusCode.ToString()), destinationWriter);

        Encode(headers, destinationWriter);
    }

    internal void Encode(Http3ResponseHeaderCollection headers, PipeWriter destinationWriter)
    {
        EncodeFieldSectionPrefix(destinationWriter);
        EncodeFieldLines(headers, destinationWriter);
    }

    private void EncodeFieldLines(Http3ResponseHeaderCollection headers, PipeWriter destinationWriter)
    {
        // length?
        // iterate headers, match _staticEncoderTable
        // write, wire format
        foreach (var header in headers)
        {
        }
    }

    private void EncodeFieldLine(KnownHeaderField header, PipeWriter destinationWriter)
    {
        if (header.Value != string.Empty)
            EncodeIndexedFieldLine(header, destinationWriter);

        //...
    }

    private void EncodeFieldLine((string Name, string Value) header, PipeWriter destinationWriter)
    {
    }

    private void EncodeFieldSectionPrefix(PipeWriter destinationWriter)
    {
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | Required Insert Count(8 +)    |
        // +---+---------------------------+
        // | S | Delta Base(7 +)           |
        // +---+---------------------------+
        // | Encoded Field Lines         ...
        // +-------------------------------+
        var buffer = destinationWriter.GetSpan(2);
        buffer[0] = 0;
        buffer[1] = 0;
        destinationWriter.Advance(2);
    }

    private void EncodeIndexedFieldLine(KnownHeaderField header, PipeWriter destinationWriter)
    {
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T | Index(6 +)            |
        // +---+---+-----------------------+
        var buffer = destinationWriter.GetSpan(2);
        QPackIntegerEncoder.TryEncode(buffer, header.StaticTableIndex, 6, out var writtenLength);
        buffer[0] |= 0b1100000;
        destinationWriter.Advance(writtenLength);
    }

    private static readonly KnownHeaderField[] _staticDecoderTable =
    [
        CreateHeaderField(0, ":authority", ""),
        CreateHeaderField(1, ":path", "/"),
        CreateHeaderField(2, "age", "0"),
        CreateHeaderField(3, "content-disposition", ""),
        CreateHeaderField(4, "content-length", "0"),
        CreateHeaderField(5, "cookie", ""),
        CreateHeaderField(6, "date", ""),
        CreateHeaderField(7, "etag", ""),
        CreateHeaderField(8, "if-modified-since", ""),
        CreateHeaderField(9, "if-none-match", ""),
        CreateHeaderField(10, "last-modified", ""),
        CreateHeaderField(11, "link", ""),
        CreateHeaderField(12, "location", ""),
        CreateHeaderField(13, "referer", ""),
        CreateHeaderField(14, "set-cookie", ""),
        CreateHeaderField(15, ":method", "CONNECT"),
        CreateHeaderField(16, ":method", "DELETE"),
        CreateHeaderField(17, ":method", "GET"),
        CreateHeaderField(18, ":method", "HEAD"),
        CreateHeaderField(19, ":method", "OPTIONS"),
        CreateHeaderField(20, ":method", "POST"),
        CreateHeaderField(21, ":method", "PUT"),
        CreateHeaderField(22, ":scheme", "http"),
        CreateHeaderField(23, ":scheme", "https"),
        CreateHeaderField(24, ":status", "103"),
        CreateHeaderField(25, ":status", "200"),
        CreateHeaderField(26, ":status", "304"),
        CreateHeaderField(27, ":status", "404"),
        CreateHeaderField(28, ":status", "503"),
        CreateHeaderField(29, "accept", "*/*"),
        CreateHeaderField(30, "accept", "application/dns-message"),
        CreateHeaderField(31, "accept-encoding", "gzip, deflate, br"),
        CreateHeaderField(32, "accept-ranges", "bytes"),
        CreateHeaderField(33, "access-control-allow-headers", "cache-control"),
        CreateHeaderField(34, "access-control-allow-headers", "content-type"),
        CreateHeaderField(35, "access-control-allow-origin", "*"),
        CreateHeaderField(36, "cache-control", "max-age=0"),
        CreateHeaderField(37, "cache-control", "max-age=2592000"),
        CreateHeaderField(38, "cache-control", "max-age=604800"),
        CreateHeaderField(39, "cache-control", "no-cache"),
        CreateHeaderField(40, "cache-control", "no-store"),
        CreateHeaderField(41, "cache-control", "public, max-age=31536000"),
        CreateHeaderField(42, "content-encoding", "br"),
        CreateHeaderField(43, "content-encoding", "gzip"),
        CreateHeaderField(44, "content-type", "application/dns-message"),
        CreateHeaderField(45, "content-type", "application/javascript"),
        CreateHeaderField(46, "content-type", "application/json"),
        CreateHeaderField(47, "content-type", "application/x-www-form-urlencoded"),
        CreateHeaderField(48, "content-type", "image/gif"),
        CreateHeaderField(49, "content-type", "image/jpeg"),
        CreateHeaderField(50, "content-type", "image/png"),
        CreateHeaderField(51, "content-type", "text/css"),
        CreateHeaderField(52, "content-type", "text/html; charset=utf-8"),
        CreateHeaderField(53, "content-type", "text/plain"),
        CreateHeaderField(54, "content-type", "text/plain;charset=utf-8"),
        CreateHeaderField(55, "range", "bytes=0-"),
        CreateHeaderField(56, "strict-transport-security", "max-age=31536000"),
        CreateHeaderField(57, "strict-transport-security", "max-age=31536000; includesubdomains"),
        CreateHeaderField(58, "strict-transport-security", "max-age=31536000; includesubdomains; preload"),
        CreateHeaderField(59, "vary", "accept-encoding"),
        CreateHeaderField(60, "vary", "origin"),
        CreateHeaderField(61, "x-content-type-options", "nosniff"),
        CreateHeaderField(62, "x-xss-protection", "1; mode=block"),
        CreateHeaderField(63, ":status", "100"),
        CreateHeaderField(64, ":status", "204"),
        CreateHeaderField(65, ":status", "206"),
        CreateHeaderField(66, ":status", "302"),
        CreateHeaderField(67, ":status", "400"),
        CreateHeaderField(68, ":status", "403"),
        CreateHeaderField(69, ":status", "421"),
        CreateHeaderField(70, ":status", "425"),
        CreateHeaderField(71, ":status", "500"),
        CreateHeaderField(72, "accept-language", ""),
        CreateHeaderField(73, "access-control-allow-credentials", "FALSE"),
        CreateHeaderField(74, "access-control-allow-credentials", "TRUE"),
        CreateHeaderField(75, "access-control-allow-headers", "*"),
        CreateHeaderField(76, "access-control-allow-methods", "get"),
        CreateHeaderField(77, "access-control-allow-methods", "get, post, options"),
        CreateHeaderField(78, "access-control-allow-methods", "options"),
        CreateHeaderField(79, "access-control-expose-headers", "content-length"),
        CreateHeaderField(80, "access-control-request-headers", "content-type"),
        CreateHeaderField(81, "access-control-request-method", "get"),
        CreateHeaderField(82, "access-control-request-method", "post"),
        CreateHeaderField(83, "alt-svc", ""),
        CreateHeaderField(84, "authorization", ""),
        CreateHeaderField(85, "content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"),
        CreateHeaderField(86, "early-data", "1"),
        CreateHeaderField(87, "expect-ct", ""),
        CreateHeaderField(88, "forwarded", ""),
        CreateHeaderField(89, "if-range", ""),
        CreateHeaderField(90, "origin", ""),
        CreateHeaderField(91, "purpose", "prefetch"),
        CreateHeaderField(92, "server", ""),
        CreateHeaderField(93, "timing-allow-origin", ""),
        CreateHeaderField(94, "upgrade-insecure-requests", "1"),
        CreateHeaderField(95, "user-agent", ""),
        CreateHeaderField(96, "x-forwarded-for", ""),
        CreateHeaderField(97, "x-frame-options", "deny"),
        CreateHeaderField(98, "x-frame-options", "sameorigin")
    ];

    private static readonly FrozenDictionary<string, KnownHeaderField[]> _staticEncoderTable = BuildEndoderTable(_staticDecoderTable);

    private static readonly FrozenDictionary<int, KnownHeaderField> _statusCodesEncoderTable = BuildStatusCodeEndoderTable(_staticDecoderTable);

    private static FrozenDictionary<int, KnownHeaderField> BuildStatusCodeEndoderTable(KnownHeaderField[] source)
    {
        var dict = new Dictionary<int, KnownHeaderField>();
        foreach (var header in source)
        {
            if (header.Name != ":status")
                continue;
            var value = int.Parse(header.Value);
            dict[value] = header;
        }
        return dict.ToFrozenDictionary();
    }

    private static KnownHeaderField CreateHeaderField(int index, string name, string value) =>
        new(index, name, Encoding.ASCII.GetBytes(name), value, Encoding.ASCII.GetBytes(value));

    private static FrozenDictionary<string, KnownHeaderField[]> BuildEndoderTable(KnownHeaderField[] source)
    {
        var dict = new Dictionary<string, List<KnownHeaderField>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in source)
        {
            if (!dict.TryGetValue(header.Name, out var current))
            {
                current = [];
                dict[header.Name] = current;
            }
            current.Add(header);
        }
        return dict.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }
}