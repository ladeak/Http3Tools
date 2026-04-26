using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class QPackDecoder
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

    public void Reset()
    {
        _status = DecoderState.ReadRequiredInsertionCount;
        //_huffmanEncoding = false;
        //_fieldNameIndex = 0;
        //_fieldNameLength = 0;
        //_fieldName = ReadOnlySequence<byte>.Empty;
        //_fieldValueLength = 0;
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

        var staticHeader = QPackStaticTable.Instance[index];
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
            handler.OnHeader(QPackStaticTable.Instance[_fieldNameIndex], new ReadOnlySequence<byte>(decodedValue.Memory[0..decodedLength]));
            decodedValue.Dispose();
        }
        else
        {
            handler.OnHeader(QPackStaticTable.Instance[_fieldNameIndex], source);
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
}
