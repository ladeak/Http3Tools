using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using static CHttpServer.System.Net.Http.HPack.H2StaticTable;

namespace CHttpServer.System.Net.Http.HPack;

internal sealed class DynamicHPackEncoder
{
    public const int DefaultHeaderTableSize = 0; //4096;

    // Internal for testing
    internal readonly EncoderHeaderEntry Head;

    private readonly bool _allowDynamicCompression;
    private readonly EncoderHeaderEntry[] _headerBuckets;
    private readonly byte _hashMask;
    private uint _headerTableSize;
    private uint _maxHeaderTableSize;
    private bool _pendingTableSizeUpdate;
    private EncoderHeaderEntry? _removed;

    internal uint TableSize => _headerTableSize;

    public DynamicHPackEncoder(bool allowDynamicCompression = true, uint maxHeaderTableSize = DefaultHeaderTableSize)
    {
        _allowDynamicCompression = allowDynamicCompression;
        _maxHeaderTableSize = maxHeaderTableSize;
        Head = new EncoderHeaderEntry();
        Head.Initialize(-1, string.Empty, string.Empty, 0, int.MaxValue, null);
        // Bucket count balances memory usage and the expected low number of headers (constrained by the header table size).
        // Performance with different bucket counts hasn't been measured in detail.
        _headerBuckets = new EncoderHeaderEntry[16];
        _hashMask = (byte)(_headerBuckets.Length - 1);
        Head.Before = Head.After = Head;
    }

    public void UpdateMaxHeaderTableSize(uint maxHeaderTableSize)
    {
        if (_maxHeaderTableSize != maxHeaderTableSize)
        {
            _maxHeaderTableSize = maxHeaderTableSize;

            // Dynamic table size update will be written next HEADERS frame
            _pendingTableSizeUpdate = true;

            // Check capacity and remove entries that exceed the new capacity
            EnsureCapacity(0);
        }
    }

    public bool EnsureDynamicTableSizeUpdate(Span<byte> buffer, out int length)
    {
        // Check if there is a table size update that should be encoded
        if (_pendingTableSizeUpdate)
        {
            bool success = HPackEncoder.EncodeDynamicTableSizeUpdate((int)_maxHeaderTableSize, buffer, out length);
            _pendingTableSizeUpdate = false;
            return success;
        }

        length = 0;
        return true;
    }

    public bool EncodeHeader(Span<byte> buffer, int staticTableIndex, HeaderEncodingHint encodingHint, string name, string value,
        Encoding? valueEncoding, out int bytesWritten)
    {
        Debug.Assert(!_pendingTableSizeUpdate, "Dynamic table size update should be encoded before headers.");

        // Never index sensitive value.
        if (encodingHint == HeaderEncodingHint.NeverIndex)
        {
            int index = ResolveDynamicTableIndex(staticTableIndex, name);

            return index == -1
                ? HPackEncoder.EncodeLiteralHeaderFieldNeverIndexingNewName(name, value, valueEncoding, buffer, out bytesWritten)
                : HPackEncoder.EncodeLiteralHeaderFieldNeverIndexing(index, value, valueEncoding, buffer, out bytesWritten);
        }

        // No dynamic table. Only use the static table.
        if (!_allowDynamicCompression || _maxHeaderTableSize == 0 || encodingHint == HeaderEncodingHint.IgnoreIndex)
        {
            return staticTableIndex == -1
                ? HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingNewName(name, value, valueEncoding, buffer, out bytesWritten)
                : HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexing(staticTableIndex, value, valueEncoding, buffer, out bytesWritten);
        }

        // Header is greater than the maximum table size.
        // Don't attempt to add dynamic header as all existing dynamic headers will be removed.
        var headerLength = HeaderField.GetLength(name.Length, valueEncoding?.GetByteCount(value) ?? value.Length);
        if (headerLength > _maxHeaderTableSize)
        {
            int index = ResolveDynamicTableIndex(staticTableIndex, name);

            return index == -1
                ? HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexingNewName(name, value, valueEncoding, buffer, out bytesWritten)
                : HPackEncoder.EncodeLiteralHeaderFieldWithoutIndexing(index, value, valueEncoding, buffer, out bytesWritten);
        }

        return EncodeDynamicHeader(buffer, staticTableIndex, name, value, headerLength, valueEncoding, out bytesWritten);
    }

    private int ResolveDynamicTableIndex(int staticTableIndex, string name)
    {
        if (staticTableIndex != -1)
        {
            // Prefer static table index.
            return staticTableIndex;
        }

        return CalculateDynamicTableIndex(name);
    }

    private bool EncodeDynamicHeader(Span<byte> buffer, int staticTableIndex, string name, string value,
        int headerLength, Encoding? valueEncoding, out int bytesWritten)
    {
        EncoderHeaderEntry? headerField = GetEntry(name, value);
        if (headerField != null)
        {
            // Already exists in dynamic table. Write index.
            int index = CalculateDynamicTableIndex(headerField.Index);
            return HPackEncoder.EncodeIndexedHeaderField(index, buffer, out bytesWritten);
        }
        else
        {
            // Doesn't exist in dynamic table. Add new entry to dynamic table.

            int index = ResolveDynamicTableIndex(staticTableIndex, name);
            bool success = index == -1
                ? HPackEncoder.EncodeLiteralHeaderFieldIndexingNewName(name, value, valueEncoding, buffer, out bytesWritten)
                : HPackEncoder.EncodeLiteralHeaderFieldIndexing(index, value, valueEncoding, buffer, out bytesWritten);

            if (success)
            {
                uint headerSize = (uint)headerLength;
                EnsureCapacity(headerSize);
                AddHeaderEntry(name, value, headerSize);
            }

            return success;
        }
    }

    /// <summary>
    /// Ensure there is capacity for the new header. If there is not enough capacity then remove
    /// existing headers until space is available.
    /// </summary>
    private void EnsureCapacity(uint headerSize)
    {
        Debug.Assert(headerSize <= _maxHeaderTableSize, "Header is bigger than dynamic table size.");

        while (_maxHeaderTableSize - _headerTableSize < headerSize)
        {
            EncoderHeaderEntry? removed = RemoveHeaderEntry();
            Debug.Assert(removed != null);

            // Removed entries are tracked to be reused.
            PushRemovedEntry(removed);
        }
    }

    private EncoderHeaderEntry? GetEntry(string name, string value)
    {
        if (_headerTableSize == 0)
        {
            return null;
        }
        int hash = name.GetHashCode();
        int bucketIndex = CalculateBucketIndex(hash);
        for (EncoderHeaderEntry? e = _headerBuckets[bucketIndex]; e != null; e = e.Next)
        {
            // We've already looked up entries based on a hash of the name.
            // Compare value before name as it is more likely to be different.
            if (e.Hash == hash &&
                string.Equals(value, e.Value, StringComparison.Ordinal) &&
                string.Equals(name, e.Name, StringComparison.Ordinal))
            {
                return e;
            }
        }
        return null;
    }

    private int CalculateDynamicTableIndex(string name)
    {
        if (_headerTableSize == 0)
        {
            return -1;
        }
        int hash = name.GetHashCode();
        int bucketIndex = CalculateBucketIndex(hash);
        for (EncoderHeaderEntry? e = _headerBuckets[bucketIndex]; e != null; e = e.Next)
        {
            if (e.Hash == hash && string.Equals(name, e.Name, StringComparison.Ordinal))
            {
                return CalculateDynamicTableIndex(e.Index);
            }
        }
        return -1;
    }

    private int CalculateDynamicTableIndex(int index)
    {
        return index == -1 ? -1 : index - Head.Before!.Index + 1 + H2StaticTable.Count;
    }

    private void AddHeaderEntry(string name, string value, uint headerSize)
    {
        Debug.Assert(headerSize <= _maxHeaderTableSize, "Header is bigger than dynamic table size.");
        Debug.Assert(headerSize <= _maxHeaderTableSize - _headerTableSize, "Not enough room in dynamic table.");

        int hash = name.GetHashCode();
        int bucketIndex = CalculateBucketIndex(hash);
        EncoderHeaderEntry? oldEntry = _headerBuckets[bucketIndex];
        // Attempt to reuse removed entry
        EncoderHeaderEntry? newEntry = PopRemovedEntry() ?? new EncoderHeaderEntry();
        newEntry.Initialize(hash, name, value, headerSize, Head.Before!.Index - 1, oldEntry);
        _headerBuckets[bucketIndex] = newEntry;
        newEntry.AddBefore(Head);
        _headerTableSize += headerSize;
    }

    private void PushRemovedEntry(EncoderHeaderEntry removed)
    {
        if (_removed != null)
        {
            removed.Next = _removed;
        }
        _removed = removed;
    }

    private EncoderHeaderEntry? PopRemovedEntry()
    {
        if (_removed != null)
        {
            EncoderHeaderEntry? removed = _removed;
            _removed = _removed.Next;
            return removed;
        }

        return null;
    }

    /// <summary>
    /// Remove the oldest entry.
    /// </summary>
    private EncoderHeaderEntry? RemoveHeaderEntry()
    {
        if (_headerTableSize == 0)
        {
            return null;
        }
        EncoderHeaderEntry? eldest = Head.After;
        int hash = eldest!.Hash;
        int bucketIndex = CalculateBucketIndex(hash);
        EncoderHeaderEntry? prev = _headerBuckets[bucketIndex];
        EncoderHeaderEntry? e = prev;
        while (e != null)
        {
            EncoderHeaderEntry? next = e.Next;
            if (e == eldest)
            {
                if (prev == eldest)
                {
                    _headerBuckets[bucketIndex] = next!;
                }
                else
                {
                    prev.Next = next;
                }
                _headerTableSize -= eldest.Size;
                eldest.Remove();
                return eldest;
            }
            prev = e;
            e = next;
        }
        return null;
    }

    private int CalculateBucketIndex(int hash)
    {
        return hash & _hashMask;
    }
}

/// <summary>
/// Hint for how the header should be encoded as HPack. This value can be overriden.
/// For example, a header that is larger than the dynamic table won't be indexed.
/// </summary>
internal enum HeaderEncodingHint
{
    Index,
    IgnoreIndex,
    NeverIndex
}


[DebuggerDisplay("Name = {Name} Value = {Value}")]
internal sealed class EncoderHeaderEntry
{
    // Header name and value
    public string? Name;
    public string? Value;
    public uint Size;

    // Chained list of headers in the same bucket
    public EncoderHeaderEntry? Next;
    public int Hash;

    // Compute dynamic table index
    public int Index;

    // Doubly linked list
    public EncoderHeaderEntry? Before;
    public EncoderHeaderEntry? After;

    /// <summary>
    /// Initialize header values. An entry will be reinitialized when reused.
    /// </summary>
    public void Initialize(int hash, string name, string value, uint size, int index, EncoderHeaderEntry? next)
    {
        Debug.Assert(name != null);
        Debug.Assert(value != null);

        Name = name;
        Value = value;
        Size = size;
        Index = index;
        Hash = hash;
        Next = next;
    }

    /// <summary>
    /// Remove entry from the linked list and reset header values.
    /// </summary>
    public void Remove()
    {
        Before!.After = After;
        After!.Before = Before;
        Before = null;
        After = null;
        Next = null;
        Hash = 0;
        Name = null;
        Value = null;
        Size = 0;
    }

    /// <summary>
    /// Add before an entry in the linked list.
    /// </summary>
    public void AddBefore(EncoderHeaderEntry existingEntry)
    {
        After = existingEntry;
        Before = existingEntry.Before;
        Before!.After = this;
        After!.Before = this;
    }
}

internal static class HPackEncoder
{
    // Things we should add:
    // * Huffman encoding
    //
    // Things we should consider adding:
    // * Dynamic table encoding:
    //   This would make the encoder stateful, which complicates things significantly.
    //   Additionally, it's not clear exactly what strings we would add to the dynamic table
    //   without some additional guidance from the user about this.
    //   So for now, don't do dynamic encoding.

    /// <summary>Encodes an "Indexed Header Field".</summary>
    public static bool EncodeIndexedHeaderField(int index, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.1
        // ----------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 |        Index (7+)         |
        // +---+---------------------------+

        if (destination.Length != 0)
        {
            destination[0] = 0x80;
            return IntegerEncoder.Encode(index, 7, destination, out bytesWritten);
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>Encodes the status code of a response to the :status field.</summary>
    public static bool EncodeStatusHeader(int statusCode, Span<byte> destination, out int bytesWritten)
    {
        // Bytes written depend on whether the status code value maps directly to an index
        if (H2StaticTable.TryGetStatusIndex(statusCode, out var index))
        {
            // Status codes which exist in the HTTP/2 StaticTable.
            return EncodeIndexedHeaderField(index, destination, out bytesWritten);
        }
        else
        {
            // If the status code doesn't have a static index then we need to include the full value.
            // Write a status index and then the number bytes as a string literal.
            if (!EncodeLiteralHeaderFieldWithoutIndexing(H2StaticTable.Status200, destination, out var nameLength))
            {
                bytesWritten = 0;
                return false;
            }

            var statusBytes = StatusCodes.ToStatusBytes(statusCode);

            if (!EncodeStringLiteral(statusBytes, destination.Slice(nameLength), out var valueLength))
            {
                bytesWritten = 0;
                return false;
            }

            bytesWritten = nameLength + valueLength;
            return true;
        }
    }

    /// <summary>Encodes a "Literal Header Field without Indexing".</summary>
    public static bool EncodeLiteralHeaderFieldWithoutIndexing(int index, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 |  Index (4+)   |
        // +---+---+-----------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        if ((uint)destination.Length >= 2)
        {
            destination[0] = 0;
            if (IntegerEncoder.Encode(index, 4, destination, out int indexLength))
            {
                Debug.Assert(indexLength >= 1);
                if (EncodeStringLiteral(value, valueEncoding, destination.Slice(indexLength), out int nameLength))
                {
                    bytesWritten = indexLength + nameLength;
                    return true;
                }
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>Encodes a "Literal Header Field never Indexing".</summary>
    public static bool EncodeLiteralHeaderFieldNeverIndexing(int index, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.3
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 1 |  Index (4+)   |
        // +---+---+-----------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        if ((uint)destination.Length >= 2)
        {
            destination[0] = 0x10;
            if (IntegerEncoder.Encode(index, 4, destination, out int indexLength))
            {
                Debug.Assert(indexLength >= 1);
                if (EncodeStringLiteral(value, valueEncoding, destination.Slice(indexLength), out int nameLength))
                {
                    bytesWritten = indexLength + nameLength;
                    return true;
                }
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>Encodes a "Literal Header Field with Indexing".</summary>
    public static bool EncodeLiteralHeaderFieldIndexing(int index, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 |      Index (6+)       |
        // +---+---+-----------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        if ((uint)destination.Length >= 2)
        {
            destination[0] = 0x40;
            if (IntegerEncoder.Encode(index, 6, destination, out int indexLength))
            {
                Debug.Assert(indexLength >= 1);
                if (EncodeStringLiteral(value, valueEncoding, destination.Slice(indexLength), out int nameLength))
                {
                    bytesWritten = indexLength + nameLength;
                    return true;
                }
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>
    /// Encodes a "Literal Header Field without Indexing", but only the index portion;
    /// a subsequent call to <c>EncodeStringLiteral</c> must be used to encode the associated value.
    /// </summary>
    public static bool EncodeLiteralHeaderFieldWithoutIndexing(int index, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 |  Index (4+)   |
        // +---+---+-----------------------+
        //
        // ... expected after this:
        //
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        if ((uint)destination.Length != 0)
        {
            destination[0] = 0;
            if (IntegerEncoder.Encode(index, 4, destination, out int indexLength))
            {
                Debug.Assert(indexLength >= 1);
                bytesWritten = indexLength;
                return true;
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>Encodes a "Literal Header Field with Indexing - New Name".</summary>
    public static bool EncodeLiteralHeaderFieldIndexingNewName(string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 |           0           |
        // +---+---+-----------------------+
        // | H |     Name Length (7+)      |
        // +---+---------------------------+
        // |  Name String (Length octets)  |
        // +---+---------------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        return EncodeLiteralHeaderNewNameCore(0x40, name, value, valueEncoding, destination, out bytesWritten);
    }

    /// <summary>Encodes a "Literal Header Field without Indexing - New Name".</summary>
    public static bool EncodeLiteralHeaderFieldWithoutIndexingNewName(string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 |       0       |
        // +---+---+-----------------------+
        // | H |     Name Length (7+)      |
        // +---+---------------------------+
        // |  Name String (Length octets)  |
        // +---+---------------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        return EncodeLiteralHeaderNewNameCore(0, name, value, valueEncoding, destination, out bytesWritten);
    }

    /// <summary>Encodes a "Literal Header Field never Indexing - New Name".</summary>
    public static bool EncodeLiteralHeaderFieldNeverIndexingNewName(string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.3
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 1 |       0       |
        // +---+---+-----------------------+
        // | H |     Name Length (7+)      |
        // +---+---------------------------+
        // |  Name String (Length octets)  |
        // +---+---------------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        return EncodeLiteralHeaderNewNameCore(0x10, name, value, valueEncoding, destination, out bytesWritten);
    }

    private static bool EncodeLiteralHeaderNewNameCore(byte mask, string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 3)
        {
            destination[0] = mask;
            if (EncodeLiteralHeaderName(name, destination.Slice(1), out int nameLength) &&
                EncodeStringLiteral(value, valueEncoding, destination.Slice(1 + nameLength), out int valueLength))
            {
                bytesWritten = 1 + nameLength + valueLength;
                return true;
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>Encodes a "Literal Header Field without Indexing - New Name".</summary>
    public static bool EncodeLiteralHeaderFieldWithoutIndexingNewName(string name, ReadOnlySpan<string> values, byte[] separator, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 |       0       |
        // +---+---+-----------------------+
        // | H |     Name Length (7+)      |
        // +---+---------------------------+
        // |  Name String (Length octets)  |
        // +---+---------------------------+
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        if ((uint)destination.Length >= 3)
        {
            destination[0] = 0;
            if (EncodeLiteralHeaderName(name, destination.Slice(1), out int nameLength) &&
                EncodeStringLiterals(values, separator, valueEncoding, destination.Slice(1 + nameLength), out int valueLength))
            {
                bytesWritten = 1 + nameLength + valueLength;
                return true;
            }
        }

        bytesWritten = 0;
        return false;
    }

    /// <summary>
    /// Encodes a "Literal Header Field without Indexing - New Name", but only the name portion;
    /// a subsequent call to <c>EncodeStringLiteral</c> must be used to encode the associated value.
    /// </summary>
    public static bool EncodeLiteralHeaderFieldWithoutIndexingNewName(string name, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.2.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 |       0       |
        // +---+---+-----------------------+
        // | H |     Name Length (7+)      |
        // +---+---------------------------+
        // |  Name String (Length octets)  |
        // +---+---------------------------+
        //
        // ... expected after this:
        //
        // | H |     Value Length (7+)     |
        // +---+---------------------------+
        // | Value String (Length octets)  |
        // +-------------------------------+

        if ((uint)destination.Length >= 2)
        {
            destination[0] = 0;
            if (EncodeLiteralHeaderName(name, destination.Slice(1), out int nameLength))
            {
                bytesWritten = 1 + nameLength;
                return true;
            }
        }

        bytesWritten = 0;
        return false;
    }

    private static bool EncodeLiteralHeaderName(string value, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-5.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | H |    String Length (7+)     |
        // +---+---------------------------+
        // |  String Data (Length octets)  |
        // +-------------------------------+

        Debug.Assert(Ascii.IsValid(value));

        if (destination.Length != 0)
        {
            destination[0] = 0; // TODO: Use Huffman encoding
            if (IntegerEncoder.Encode(value.Length, 7, destination, out int integerLength))
            {
                Debug.Assert(integerLength >= 1);

                destination = destination.Slice(integerLength);
                if (value.Length <= destination.Length)
                {
                    OperationStatus status = Ascii.ToLower(value, destination, out int valueBytesWritten);
                    Debug.Assert(status == OperationStatus.Done);
                    Debug.Assert(valueBytesWritten == value.Length);

                    bytesWritten = integerLength + value.Length;
                    return true;
                }
            }
        }

        bytesWritten = 0;
        return false;
    }

    private static void EncodeValueStringPart(string value, Span<byte> destination)
    {
        Debug.Assert(destination.Length >= value.Length);

        OperationStatus status = Ascii.FromUtf16(value, destination, out int bytesWritten);

        if (status == OperationStatus.InvalidData)
        {
            throw new HttpRequestException("Invalid character encoding");
        }

        Debug.Assert(status == OperationStatus.Done);
        Debug.Assert(bytesWritten == value.Length);
    }

    public static bool EncodeStringLiteral(ReadOnlySpan<byte> value, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-5.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | H |    String Length (7+)     |
        // +---+---------------------------+
        // |  String Data (Length octets)  |
        // +-------------------------------+

        if (destination.Length != 0)
        {
            destination[0] = 0; // TODO: Use Huffman encoding
            if (IntegerEncoder.Encode(value.Length, 7, destination, out int integerLength))
            {
                Debug.Assert(integerLength >= 1);

                destination = destination.Slice(integerLength);
                if (value.Length <= destination.Length)
                {
                    // Note: No validation. Bytes should have already been validated.
                    value.CopyTo(destination);

                    bytesWritten = integerLength + value.Length;
                    return true;
                }
            }
        }

        bytesWritten = 0;
        return false;
    }

    public static bool EncodeStringLiteral(string value, Span<byte> destination, out int bytesWritten)
    {
        return EncodeStringLiteral(value, valueEncoding: null, destination, out bytesWritten);
    }

    public static bool EncodeStringLiteral(string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-5.2
        // ------------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | H |    String Length (7+)     |
        // +---+---------------------------+
        // |  String Data (Length octets)  |
        // +-------------------------------+

        if (destination.Length != 0)
        {
            destination[0] = 0; // TODO: Use Huffman encoding

            int encodedStringLength = valueEncoding is null || ReferenceEquals(valueEncoding, Encoding.Latin1)
                ? value.Length
                : valueEncoding.GetByteCount(value);

            if (IntegerEncoder.Encode(encodedStringLength, 7, destination, out int integerLength))
            {
                Debug.Assert(integerLength >= 1);
                destination = destination.Slice(integerLength);
                if (encodedStringLength <= destination.Length)
                {
                    if (valueEncoding is null)
                    {
                        EncodeValueStringPart(value, destination);
                    }
                    else
                    {
                        int written = valueEncoding.GetBytes(value, destination);
                        Debug.Assert(written == encodedStringLength);
                    }

                    bytesWritten = integerLength + encodedStringLength;
                    return true;
                }
            }
        }

        bytesWritten = 0;
        return false;
    }

    public static bool EncodeDynamicTableSizeUpdate(int value, Span<byte> destination, out int bytesWritten)
    {
        // From https://tools.ietf.org/html/rfc7541#section-6.3
        // ----------------------------------------------------
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 1 |   Max size (5+)   |
        // +---+---------------------------+

        if (destination.Length != 0)
        {
            destination[0] = 0x20;
            return IntegerEncoder.Encode(value, 5, destination, out bytesWritten);
        }

        bytesWritten = 0;
        return false;
    }

    public static bool EncodeStringLiterals(ReadOnlySpan<string> values, byte[]? separator, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        if (values.Length == 0)
        {
            return EncodeStringLiteral("", valueEncoding: null, destination, out bytesWritten);
        }
        else if (values.Length == 1)
        {
            return EncodeStringLiteral(values[0], valueEncoding, destination, out bytesWritten);
        }

        if (destination.Length != 0)
        {
            Debug.Assert(separator != null);
            Debug.Assert(Ascii.IsValid(separator));
            int valueLength = checked((values.Length - 1) * separator.Length);

            // Calculate length of all values.
            if (valueEncoding is null || ReferenceEquals(valueEncoding, Encoding.Latin1))
            {
                foreach (string part in values)
                {
                    valueLength = checked(valueLength + part.Length);
                }
            }
            else
            {
                foreach (string part in values)
                {
                    valueLength = checked(valueLength + valueEncoding.GetByteCount(part));
                }
            }

            destination[0] = 0;
            if (IntegerEncoder.Encode(valueLength, 7, destination, out int integerLength))
            {
                Debug.Assert(integerLength >= 1);
                destination = destination.Slice(integerLength);
                if (destination.Length >= valueLength)
                {
                    if (valueEncoding is null)
                    {
                        string value = values[0];
                        EncodeValueStringPart(value, destination);
                        destination = destination.Slice(value.Length);

                        for (int i = 1; i < values.Length; i++)
                        {
                            separator.CopyTo(destination);
                            destination = destination.Slice(separator.Length);

                            value = values[i];
                            EncodeValueStringPart(value, destination);
                            destination = destination.Slice(value.Length);
                        }
                    }
                    else
                    {
                        int written = valueEncoding.GetBytes(values[0], destination);
                        destination = destination.Slice(written);

                        for (int i = 1; i < values.Length; i++)
                        {
                            separator.CopyTo(destination);
                            destination = destination.Slice(separator.Length);

                            written = valueEncoding.GetBytes(values[i], destination);
                            destination = destination.Slice(written);
                        }
                    }

                    bytesWritten = integerLength + valueLength;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Encodes a "Literal Header Field without Indexing" to a new array, but only the index portion;
    /// a subsequent call to <c>EncodeStringLiteral</c> must be used to encode the associated value.
    /// </summary>
    public static byte[] EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(int index)
    {
        Span<byte> span = stackalloc byte[256];
        bool success = EncodeLiteralHeaderFieldWithoutIndexing(index, span, out int length);
        Debug.Assert(success, $"Stack-allocated space was too small for index '{index}'.");
        return span.Slice(0, length).ToArray();
    }

    /// <summary>
    /// Encodes a "Literal Header Field without Indexing - New Name" to a new array, but only the name portion;
    /// a subsequent call to <c>EncodeStringLiteral</c> must be used to encode the associated value.
    /// </summary>
    public static byte[] EncodeLiteralHeaderFieldWithoutIndexingNewNameToAllocatedArray(string name)
    {
        Span<byte> span = stackalloc byte[256];
        bool success = EncodeLiteralHeaderFieldWithoutIndexingNewName(name, span, out int length);
        Debug.Assert(success, $"Stack-allocated space was too small for \"{name}\".");
        return span.Slice(0, length).ToArray();
    }

    /// <summary>Encodes a "Literal Header Field without Indexing" to a new array.</summary>
    public static byte[] EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(int index, string value)
    {
        Span<byte> span =
#if DEBUG
            stackalloc byte[4]; // to validate growth algorithm
#else
                stackalloc byte[512];
#endif
        while (true)
        {
            if (EncodeLiteralHeaderFieldWithoutIndexing(index, value, valueEncoding: null, span, out int length))
            {
                return span.Slice(0, length).ToArray();
            }

            // This is a rare path, only used once per HTTP/2 connection and only
            // for very long host names.  Just allocate rather than complicate
            // the code with ArrayPool usage.  In practice we should never hit this,
            // as hostnames should be <= 255 characters.
            span = new byte[span.Length * 2];
        }
    }
}

internal static class IntegerEncoder
{
    /// <summary>
    /// The maximum bytes required to encode a 32-bit int, regardless of prefix length.
    /// </summary>
    public const int MaxInt32EncodedLength = 6;

    /// <summary>
    /// Encodes an integer into one or more bytes.
    /// </summary>
    /// <param name="value">The value to encode. Must not be negative.</param>
    /// <param name="numBits">The length of the prefix, in bits, to encode <paramref name="value"/> within. Must be between 1 and 8.</param>
    /// <param name="destination">The destination span to encode <paramref name="value"/> to.</param>
    /// <param name="bytesWritten">The number of bytes used to encode <paramref name="value"/>.</param>
    /// <returns>If <paramref name="destination"/> had enough storage to encode <paramref name="value"/>, true. Otherwise, false.</returns>
    public static bool Encode(int value, int numBits, Span<byte> destination, out int bytesWritten)
    {
        Debug.Assert(value >= 0);
        Debug.Assert(numBits >= 1 && numBits <= 8);

        if (destination.Length == 0)
        {
            bytesWritten = 0;
            return false;
        }

        destination[0] &= MaskHigh(8 - numBits);

        if (value < (1 << numBits) - 1)
        {
            destination[0] |= (byte)value;

            bytesWritten = 1;
            return true;
        }
        else
        {
            destination[0] |= (byte)((1 << numBits) - 1);

            if (1 == destination.Length)
            {
                bytesWritten = 0;
                return false;
            }

            value -= ((1 << numBits) - 1);
            int i = 1;

            while (value >= 128)
            {
                destination[i++] = (byte)(value % 128 + 128);

                if (i >= destination.Length)
                {
                    bytesWritten = 0;
                    return false;
                }

                value /= 128;
            }
            destination[i++] = (byte)value;

            bytesWritten = i;
            return true;
        }
    }

    private static byte MaskHigh(int n) => (byte)(sbyte.MinValue >> (n - 1));
}

internal static class H2StaticTable
{
    public static ref readonly HeaderField Get(int index) => ref _staticDecoderTable[index];

    // Values for encoding.
    // Unused values are omitted.
    public const int Authority = 1;
    public const int MethodGet = 2;
    public const int MethodPost = 3;
    public const int PathSlash = 4;
    public const int SchemeHttp = 6;
    public const int SchemeHttps = 7;
    public const int Status200 = 8;
    public const int AcceptCharset = 15;
    public const int AcceptEncoding = 16;
    public const int AcceptLanguage = 17;
    public const int AcceptRanges = 18;
    public const int Accept = 19;
    public const int AccessControlAllowOrigin = 20;
    public const int Age = 21;
    public const int Allow = 22;
    public const int Authorization = 23;
    public const int CacheControl = 24;
    public const int ContentDisposition = 25;
    public const int ContentEncoding = 26;
    public const int ContentLanguage = 27;
    public const int ContentLength = 28;
    public const int ContentLocation = 29;
    public const int ContentRange = 30;
    public const int ContentType = 31;
    public const int Cookie = 32;
    public const int Date = 33;
    public const int ETag = 34;
    public const int Expect = 35;
    public const int Expires = 36;
    public const int From = 37;
    public const int Host = 38;
    public const int IfMatch = 39;
    public const int IfModifiedSince = 40;
    public const int IfNoneMatch = 41;
    public const int IfRange = 42;
    public const int IfUnmodifiedSince = 43;
    public const int LastModified = 44;
    public const int Link = 45;
    public const int Location = 46;
    public const int MaxForwards = 47;
    public const int ProxyAuthenticate = 48;
    public const int ProxyAuthorization = 49;
    public const int Range = 50;
    public const int Referer = 51;
    public const int Refresh = 52;
    public const int RetryAfter = 53;
    public const int Server = 54;
    public const int SetCookie = 55;
    public const int StrictTransportSecurity = 56;
    public const int TransferEncoding = 57;
    public const int UserAgent = 58;
    public const int Vary = 59;
    public const int Via = 60;
    public const int WwwAuthenticate = 61;

    public static int Count => _staticDecoderTable.Length;

    private static HeaderField CreateHeaderField(int staticTableIndex, string name, string value) =>
    new HeaderField(
        staticTableIndex,
        Encoding.ASCII.GetBytes(name),
        value.Length != 0 ? Encoding.ASCII.GetBytes(value) : Array.Empty<byte>());

    private static readonly HeaderField[] _staticDecoderTable =
    [
            CreateHeaderField(0, "Invalid", ""),
            CreateHeaderField(1, ":authority", ""),
            CreateHeaderField(2, ":method", "GET"),
            CreateHeaderField(3, ":method", "POST"),
            CreateHeaderField(4, ":path", "/"),
            CreateHeaderField(5, ":path", "/index.html"),
            CreateHeaderField(6, ":scheme", "http"),
            CreateHeaderField(7, ":scheme", "https"),
            CreateHeaderField(8, ":status", "200"),
            CreateHeaderField(9, ":status", "204"),
            CreateHeaderField(10, ":status", "206"),
            CreateHeaderField(11, ":status", "304"),
            CreateHeaderField(12, ":status", "400"),
            CreateHeaderField(13, ":status", "404"),
            CreateHeaderField(14, ":status", "500"),
            CreateHeaderField(15, "accept-charset", ""),
            CreateHeaderField(16, "accept-encoding", "gzip, deflate"),
            CreateHeaderField(17, "accept-language", ""),
            CreateHeaderField(18, "accept-ranges", ""),
            CreateHeaderField(19, "accept", ""),
            CreateHeaderField(20, "access-control-allow-origin", ""),
            CreateHeaderField(21, "age", ""),
            CreateHeaderField(22, "allow", ""),
            CreateHeaderField(23, "authorization", ""),
            CreateHeaderField(24, "cache-control", ""),
            CreateHeaderField(25, "content-disposition", ""),
            CreateHeaderField(26, "content-encoding", ""),
            CreateHeaderField(27, "content-language", ""),
            CreateHeaderField(28, "content-length", ""),
            CreateHeaderField(29, "content-location", ""),
            CreateHeaderField(30, "content-range", ""),
            CreateHeaderField(31, "content-type", ""),
            CreateHeaderField(32, "cookie", ""),
            CreateHeaderField(33, "date", ""),
            CreateHeaderField(34, "etag", ""),
            CreateHeaderField(35, "expect", ""),
            CreateHeaderField(36, "expires", ""),
            CreateHeaderField(37, "from", ""),
            CreateHeaderField(38, "host", ""),
            CreateHeaderField(39, "if-match", ""),
            CreateHeaderField(40, "if-modified-since", ""),
            CreateHeaderField(41, "if-none-match", ""),
            CreateHeaderField(42, "if-range", ""),
            CreateHeaderField(43, "if-unmodified-since", ""),
            CreateHeaderField(44, "last-modified", ""),
            CreateHeaderField(45, "link", ""),
            CreateHeaderField(46, "location", ""),
            CreateHeaderField(47, "max-forwards", ""),
            CreateHeaderField(48, "proxy-authenticate", ""),
            CreateHeaderField(49, "proxy-authorization", ""),
            CreateHeaderField(50, "range", ""),
            CreateHeaderField(51, "referer", ""),
            CreateHeaderField(52, "refresh", ""),
            CreateHeaderField(53, "retry-after", ""),
            CreateHeaderField(54, "server", ""),
            CreateHeaderField(55, "set-cookie", ""),
            CreateHeaderField(56, "strict-transport-security", ""),
            CreateHeaderField(57, "transfer-encoding", ""),
            CreateHeaderField(58, "user-agent", ""),
            CreateHeaderField(59, "vary", ""),
            CreateHeaderField(60, "via", ""),
            CreateHeaderField(61, "www-authenticate", "")
    ];

    private static readonly FrozenDictionary<string, int> _staticEncoderTable = new Dictionary<string, int>(
        _staticDecoderTable.Skip(14).Select(x => new KeyValuePair<string, int>(Encoding.ASCII.GetString(x.Name), x.StaticTableIndex))).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetStatusIndex(int status, out int index)
    {
        index = status switch
        {
            200 => 8,
            204 => 9,
            206 => 10,
            304 => 11,
            400 => 12,
            404 => 13,
            500 => 14,
            _ => -1
        };

        return index != -1;
    }

    internal static int GetStaticTableHeaderIndex(string headerName)
    {
        if (!_staticEncoderTable.TryGetValue(headerName, out int staticTableIndex))
            return -1;
        return staticTableIndex;
    }

    internal static class StatusCodes
    {
        public static ReadOnlySpan<byte> ToStatusBytes(int statusCode)
        {
            switch (statusCode)
            {
                case (int)HttpStatusCode.Continue:
                    return "100"u8;
                case (int)HttpStatusCode.SwitchingProtocols:
                    return "101"u8;
                case (int)HttpStatusCode.Processing:
                    return "102"u8;

                case (int)HttpStatusCode.OK:
                    return "200"u8;
                case (int)HttpStatusCode.Created:
                    return "201"u8;
                case (int)HttpStatusCode.Accepted:
                    return "202"u8;
                case (int)HttpStatusCode.NonAuthoritativeInformation:
                    return "203"u8;
                case (int)HttpStatusCode.NoContent:
                    return "204"u8;
                case (int)HttpStatusCode.ResetContent:
                    return "205"u8;
                case (int)HttpStatusCode.PartialContent:
                    return "206"u8;
                case (int)HttpStatusCode.MultiStatus:
                    return "207"u8;
                case (int)HttpStatusCode.AlreadyReported:
                    return "208"u8;
                case (int)HttpStatusCode.IMUsed:
                    return "226"u8;

                case (int)HttpStatusCode.MultipleChoices:
                    return "300"u8;
                case (int)HttpStatusCode.MovedPermanently:
                    return "301"u8;
                case (int)HttpStatusCode.Found:
                    return "302"u8;
                case (int)HttpStatusCode.SeeOther:
                    return "303"u8;
                case (int)HttpStatusCode.NotModified:
                    return "304"u8;
                case (int)HttpStatusCode.UseProxy:
                    return "305"u8;
                case (int)HttpStatusCode.Unused:
                    return "306"u8;
                case (int)HttpStatusCode.TemporaryRedirect:
                    return "307"u8;
                case (int)HttpStatusCode.PermanentRedirect:
                    return "308"u8;

                case (int)HttpStatusCode.BadRequest:
                    return "400"u8;
                case (int)HttpStatusCode.Unauthorized:
                    return "401"u8;
                case (int)HttpStatusCode.PaymentRequired:
                    return "402"u8;
                case (int)HttpStatusCode.Forbidden:
                    return "403"u8;
                case (int)HttpStatusCode.NotFound:
                    return "404"u8;
                case (int)HttpStatusCode.MethodNotAllowed:
                    return "405"u8;
                case (int)HttpStatusCode.NotAcceptable:
                    return "406"u8;
                case (int)HttpStatusCode.ProxyAuthenticationRequired:
                    return "407"u8;
                case (int)HttpStatusCode.RequestTimeout:
                    return "408"u8;
                case (int)HttpStatusCode.Conflict:
                    return "409"u8;
                case (int)HttpStatusCode.Gone:
                    return "410"u8;
                case (int)HttpStatusCode.LengthRequired:
                    return "411"u8;
                case (int)HttpStatusCode.PreconditionFailed:
                    return "412"u8;
                case (int)HttpStatusCode.RequestEntityTooLarge:
                    return "413"u8;
                case (int)HttpStatusCode.RequestUriTooLong:
                    return "414"u8;
                case (int)HttpStatusCode.UnsupportedMediaType:
                    return "415"u8;
                case (int)HttpStatusCode.RequestedRangeNotSatisfiable:
                    return "416"u8;
                case (int)HttpStatusCode.ExpectationFailed:
                    return "417"u8;
                case (int)418:
                    return "418"u8;
                case (int)419:
                    return "419"u8;
                case (int)HttpStatusCode.MisdirectedRequest:
                    return "421"u8;
                case (int)HttpStatusCode.UnprocessableEntity:
                    return "422"u8;
                case (int)HttpStatusCode.Locked:
                    return "423"u8;
                case (int)HttpStatusCode.FailedDependency:
                    return "424"u8;
                case (int)HttpStatusCode.UpgradeRequired:
                    return "426"u8;
                case (int)HttpStatusCode.PreconditionRequired:
                    return "428"u8;
                case (int)HttpStatusCode.TooManyRequests:
                    return "429"u8;
                case (int)HttpStatusCode.RequestHeaderFieldsTooLarge:
                    return "431"u8;
                case (int)HttpStatusCode.UnavailableForLegalReasons:
                    return "451"u8;

                case (int)HttpStatusCode.InternalServerError:
                    return "500"u8;
                case (int)HttpStatusCode.NotImplemented:
                    return "501"u8;
                case (int)HttpStatusCode.BadGateway:
                    return "502"u8;
                case (int)HttpStatusCode.ServiceUnavailable:
                    return "503"u8;
                case (int)HttpStatusCode.GatewayTimeout:
                    return "504"u8;
                case (int)HttpStatusCode.HttpVersionNotSupported:
                    return "505"u8;
                case (int)HttpStatusCode.VariantAlsoNegotiates:
                    return "506"u8;
                case (int)HttpStatusCode.InsufficientStorage:
                    return "507"u8;
                case (int)HttpStatusCode.LoopDetected:
                    return "508"u8;
                case (int)HttpStatusCode.NotExtended:
                    return "510"u8;
                case (int)HttpStatusCode.NetworkAuthenticationRequired:
                    return "511"u8;

                default:
                    return Encoding.ASCII.GetBytes(statusCode.ToString(CultureInfo.InvariantCulture));

            }
        }
    }
}
