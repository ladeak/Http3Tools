using System.Buffers.Binary;
using System.Collections.Frozen;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace CHttpServer.Http3;

internal sealed partial class QPackDecoder
{
    private static FrozenDictionary<int, EncodingKnownHeaderField> BuildStatusCodeEndoderTable(KnownHeaderField[] source)
    {
        var dict = new Dictionary<int, EncodingKnownHeaderField>();
        foreach (var header in source)
        {
            if (header.Name != ":status")
                continue;
            var value = int.Parse(header.Value);
            dict[value] = new EncodingKnownHeaderField(header.StaticTableIndex, header.Value);
        }
        return dict.ToFrozenDictionary();
    }

    private static readonly FrozenDictionary<int, EncodingKnownHeaderField> _statusCodesEncoderTable = BuildStatusCodeEndoderTable(QPackStaticTable.Instance);

    private static readonly FrozenDictionary<string, EncodingKnownHeaderField[]> _staticEncoderTable = BuildEndoderTable(QPackStaticTable.Instance);

    private static FrozenDictionary<string, EncodingKnownHeaderField[]> BuildEndoderTable(KnownHeaderField[] source)
    {
        var dict = new Dictionary<string, List<EncodingKnownHeaderField>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in source)
        {
            if (!dict.TryGetValue(header.Name, out var current))
            {
                current = [];
                dict[header.Name] = current;
            }
            current.Add(new EncodingKnownHeaderField(header.StaticTableIndex, header.Value));
        }
        return dict.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Encodes a header dictionary into a response stream.
    /// </summary>
    internal void Encode(int statusCode, Http3ResponseHeaderCollection headers, PipeWriter destinationWriter)
    {
        EncodeFieldSectionPrefix(destinationWriter);
        if (_statusCodesEncoderTable.TryGetValue(statusCode, out var knownHeader))
            EncodeIndexedFieldLine(knownHeader, destinationWriter);
        else
            EncodeIndexedFieldWithLiteralValue(_statusCodesEncoderTable.First().Value, statusCode.ToString(), destinationWriter);

        EncodeFieldLines(headers, destinationWriter);
    }

    internal void Encode(Http3ResponseHeaderCollection headers, PipeWriter destinationWriter)
    {
        EncodeFieldSectionPrefix(destinationWriter);
        EncodeFieldLines(headers, destinationWriter);
    }

    private void EncodeFieldLines(Http3ResponseHeaderCollection headers, PipeWriter destinationWriter)
    {
        foreach (var (headerName, headerValue) in headers)
        {
            // Not known header, encode liternal name and literal values
            if (!_staticEncoderTable.TryGetValue(headerName, out var knownHeaderFields))
                EncodeLiteralFieldWithLiteralValue(headerName, headerValue.ToString(), destinationWriter);
            else
            {
                if (!TryEncodeIndexedFieldAndValue(knownHeaderFields, headerValue, destinationWriter))
                    EncodeIndexedFieldWithLiteralValue(knownHeaderFields[0], headerValue, destinationWriter);
            }
        }
    }

    private static bool TryEncodeIndexedFieldAndValue(EncodingKnownHeaderField[] knownHeaderFields, StringValues headerValues, PipeWriter destinationWriter)
    {
        if (headerValues.Count == 0 || (headerValues.Count == 1 && headerValues[0] == string.Empty))
            return false;
        var rawHeaderValue = headerValues.ToString();
        foreach (var item in knownHeaderFields)
        {
            if (item.Value == rawHeaderValue)
            {
                EncodeIndexedFieldLine(item, destinationWriter);
                return true;
            }
        }
        return false;
    }

    private static void EncodeFieldSectionPrefix(PipeWriter destinationWriter)
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
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, 0);
        destinationWriter.Advance(2);
    }

    internal static void EncodeIndexedFieldLine(EncodingKnownHeaderField header, PipeWriter destinationWriter)
    {
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T | Index(6 +)            |
        // +---+---+-----------------------+
        var buffer = destinationWriter.GetSpan(2);
        var writtenLength = header.CopySixBitTo(buffer);
        destinationWriter.Advance(writtenLength);
    }

    internal static void EncodeIndexedFieldWithLiteralValue(EncodingKnownHeaderField header, StringValues headerValue, PipeWriter writer)
    {
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 | N | T |Name Index (4+)|
        // +---+---+---+---+---------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // |  Value String (Length bytes)  |
        // +-------------------------------+
        var rawHeaderValue = headerValue.Count > 0 ? headerValue.ToString() : string.Empty;
        var valueLength = Encoding.Latin1.GetByteCount(rawHeaderValue);
        var buffer = writer.GetSpan(1 + QPackIntegerEncoder.MaxLength + valueLength);
        var writtenLength = header.CopyFourBitTo(buffer);
        buffer[writtenLength] = 0;
        writtenLength += QPackIntegerEncoder.Encode(buffer[writtenLength..], valueLength, 7);
        writtenLength += Encoding.Latin1.GetBytes(rawHeaderValue, buffer[writtenLength..]);
        writer.Advance(writtenLength);
    }

    internal static void EncodeLiteralFieldWithLiteralValue(string name, string value, PipeWriter writer)
    {
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
        var nameLength = Encoding.Latin1.GetByteCount(name);
        var valueLength = Encoding.Latin1.GetByteCount(value);

        var buffer = writer.GetSpan(1 + QPackIntegerEncoder.MaxLength + nameLength + QPackIntegerEncoder.MaxLength + valueLength);
        buffer[0] = 0b00100000;
        var writtenLength = QPackIntegerEncoder.Encode(buffer[0..], nameLength, 3);
        writtenLength += Encoding.Latin1.GetBytes(name, buffer[writtenLength..]);
        buffer[writtenLength] = 0;
        writtenLength += QPackIntegerEncoder.Encode(buffer[writtenLength..], valueLength, 7);
        writtenLength += Encoding.Latin1.GetBytes(value, buffer[writtenLength..]);
        writer.Advance(writtenLength);
    }

    internal readonly record struct EncodingKnownHeaderField
    {
        private readonly InlineArray2<byte> _sixBitPrefixedTableIndex;
        private readonly InlineArray2<byte> _fourBitPrefixedTableIndex;
        private readonly byte _sixBitPrefixedTableLength;
        private readonly byte _fourBitPrefixedTableLength;

        public EncodingKnownHeaderField(int index, string value)
        {
            Value = value;
            _sixBitPrefixedTableLength = (byte)QPackIntegerEncoder.Encode(_sixBitPrefixedTableIndex, index, 6);
            _sixBitPrefixedTableIndex[0] |= 0b1100_0000;
            _fourBitPrefixedTableLength = (byte)QPackIntegerEncoder.Encode(_fourBitPrefixedTableIndex, index, 4);
            _fourBitPrefixedTableIndex[0] |= 0b01110000;
        }

        public string Value { get; init; }

        public int CopySixBitTo(Span<byte> destination)
        {
            destination[1] = _sixBitPrefixedTableIndex[1];
            destination[0] = _sixBitPrefixedTableIndex[0];
            return _sixBitPrefixedTableLength;
        }

        public int CopyFourBitTo(Span<byte> destination)
        {
            destination[1] = _fourBitPrefixedTableIndex[1];
            destination[0] = _fourBitPrefixedTableIndex[0];
            return _fourBitPrefixedTableLength;
        }

        public InlineArray2<byte> SixBitPrefixedTableIndex => _sixBitPrefixedTableIndex;

        public InlineArray2<byte> FourBitPrefixedTableIndex => _fourBitPrefixedTableIndex;
    }

}