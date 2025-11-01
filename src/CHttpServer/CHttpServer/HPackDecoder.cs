using System.Buffers;
using System.Diagnostics;
using System.Numerics;

namespace CHttpServer.System.Net.Http.HPack;

internal interface IHttpStreamHeadersHandler
{
    void OnStaticIndexedHeader(int index);
    void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value);
    void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value);
    void OnHeadersComplete(bool endStream);
    void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value);
}

internal sealed class HPackDecoder
{
    private enum State : byte
    {
        Ready,
        HeaderFieldIndex,
        HeaderNameIndex,
        HeaderNameLength,
        HeaderNameLengthContinue,
        HeaderName,
        HeaderValueLength,
        HeaderValueLengthContinue,
        HeaderValue,
        DynamicTableSizeUpdate
    }

    public const int DefaultHeaderTableSize = 4096;
    public const int DefaultStringOctetsSize = 4096;
    public const int DefaultMaxHeadersLength = 64 * 1024;

    // http://httpwg.org/specs/rfc7541.html#rfc.section.6.1
    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 1 |        Index (7+)         |
    // +---+---------------------------+
    private const byte IndexedHeaderFieldMask = 0x80;

    // http://httpwg.org/specs/rfc7541.html#rfc.section.6.2.1
    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 0 | 1 |      Index (6+)       |
    // +---+---+-----------------------+
    private const byte LiteralHeaderFieldWithIncrementalIndexingMask = 0xc0;

    // http://httpwg.org/specs/rfc7541.html#rfc.section.6.2.2
    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 0 | 0 | 0 | 0 |  Index (4+)   |
    // +---+---+-----------------------+
    private const byte LiteralHeaderFieldWithoutIndexingMask = 0xf0;

    // http://httpwg.org/specs/rfc7541.html#rfc.section.6.2.3
    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 0 | 0 | 0 | 1 |  Index (4+)   |
    // +---+---+-----------------------+
    private const byte LiteralHeaderFieldNeverIndexedMask = 0xf0;

    // http://httpwg.org/specs/rfc7541.html#rfc.section.6.3
    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | 0 | 0 | 1 |   Max size (5+)   |
    // +---+---------------------------+
    private const byte DynamicTableSizeUpdateMask = 0xe0;

    // http://httpwg.org/specs/rfc7541.html#rfc.section.5.2
    //   0   1   2   3   4   5   6   7
    // +---+---+---+---+---+---+---+---+
    // | H |    String Length (7+)     |
    // +---+---------------------------+
    private const byte HuffmanMask = 0x80;

    private const int IndexedHeaderFieldPrefix = 7;
    private const int LiteralHeaderFieldWithIncrementalIndexingPrefix = 6;
    private const int LiteralHeaderFieldWithoutIndexingPrefix = 4;
    private const int LiteralHeaderFieldNeverIndexedPrefix = 4;
    private const int DynamicTableSizeUpdatePrefix = 5;
    private const int StringLengthPrefix = 7;

    private readonly int _maxDynamicTableSize;
    private readonly int _maxHeadersLength;
    private readonly DynamicTable _dynamicTable;
    private HPackIntegerDecoder _integerDecoder;
    private byte[] _stringOctets;
    private byte[] _headerNameOctets;
    private byte[] _headerValueOctets;
    private (int start, int length)? _headerNameRange;
    private (int start, int length)? _headerValueRange;

    private State _state = State.Ready;
    private byte[]? _headerName;
    private int _headerStaticIndex;
    private int _stringIndex;
    private int _stringLength;
    private int _headerNameLength;
    private int _headerValueLength;
    private bool _index;
    private bool _huffman;
    private bool _headersObserved;

    public HPackDecoder(int maxDynamicTableSize = DefaultHeaderTableSize, int maxHeadersLength = DefaultMaxHeadersLength)
        : this(maxDynamicTableSize, maxHeadersLength, new DynamicTable(maxDynamicTableSize))
    {
    }

    // For testing.
    internal HPackDecoder(int maxDynamicTableSize, int maxHeadersLength, DynamicTable dynamicTable)
    {
        _maxDynamicTableSize = maxDynamicTableSize;
        _maxHeadersLength = maxHeadersLength;
        _dynamicTable = dynamicTable;

        _stringOctets = new byte[DefaultStringOctetsSize];
        _headerNameOctets = new byte[DefaultStringOctetsSize];
        _headerValueOctets = new byte[DefaultStringOctetsSize];
    }

    public void Decode(in ReadOnlySequence<byte> data, bool endHeaders, IHttpStreamHeadersHandler handler)
    {
        foreach (ReadOnlyMemory<byte> segment in data)
        {
            DecodeInternal(segment.Span, handler);
        }

        CheckIncompleteHeaderBlock(endHeaders);
    }

    public void Decode(ReadOnlySpan<byte> data, bool endHeaders, IHttpStreamHeadersHandler handler)
    {
        DecodeInternal(data, handler);
        CheckIncompleteHeaderBlock(endHeaders);
    }

    private void DecodeInternal(ReadOnlySpan<byte> data, IHttpStreamHeadersHandler handler)
    {
        int currentIndex = 0;

        do
        {
            switch (_state)
            {
                case State.Ready:
                    Parse(data, ref currentIndex, handler);
                    break;
                case State.HeaderFieldIndex:
                    ParseHeaderFieldIndex(data, ref currentIndex, handler);
                    break;
                case State.HeaderNameIndex:
                    ParseHeaderNameIndex(data, ref currentIndex, handler);
                    break;
                case State.HeaderNameLength:
                    ParseHeaderNameLength(data, ref currentIndex, handler);
                    break;
                case State.HeaderNameLengthContinue:
                    ParseHeaderNameLengthContinue(data, ref currentIndex, handler);
                    break;
                case State.HeaderName:
                    ParseHeaderName(data, ref currentIndex, handler);
                    break;
                case State.HeaderValueLength:
                    ParseHeaderValueLength(data, ref currentIndex, handler);
                    break;
                case State.HeaderValueLengthContinue:
                    ParseHeaderValueLengthContinue(data, ref currentIndex, handler);
                    break;
                case State.HeaderValue:
                    ParseHeaderValue(data, ref currentIndex, handler);
                    break;
                case State.DynamicTableSizeUpdate:
                    ParseDynamicTableSizeUpdate(data, ref currentIndex);
                    break;
                default:
                    // Can't happen
                    Debug.Fail("HPACK decoder reach an invalid state");
                    throw new NotImplementedException(_state.ToString());
            }
        }
        // Parse methods each check the length. This check is to see whether there is still data available
        // and to continue parsing.
        while (currentIndex < data.Length);

        // If a header range was set, but the value was not in the data, then copy the range
        // to the name buffer. Must copy because the data will be replaced and the range
        // will no longer be valid.
        if (_headerNameRange != null)
        {
            EnsureStringCapacity(ref _headerNameOctets, _headerNameLength);
            _headerName = _headerNameOctets;

            ReadOnlySpan<byte> headerBytes = data.Slice(_headerNameRange.GetValueOrDefault().start, _headerNameRange.GetValueOrDefault().length);
            headerBytes.CopyTo(_headerName);
            _headerNameRange = null;
        }
    }

    private void ParseDynamicTableSizeUpdate(ReadOnlySpan<byte> data, ref int currentIndex)
    {
        if (TryDecodeInteger(data, ref currentIndex, out int intResult))
        {
            SetDynamicHeaderTableSize(intResult);
            _state = State.Ready;
        }
    }

    private void ParseHeaderValueLength(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (currentIndex < data.Length)
        {
            byte b = data[currentIndex++];

            _huffman = IsHuffmanEncoded(b);

            if (_integerDecoder.BeginTryDecode((byte)(b & ~HuffmanMask), StringLengthPrefix, out int intResult))
            {
                OnStringLength(intResult, nextState: State.HeaderValue);

                if (intResult == 0)
                {
                    OnString(nextState: State.Ready);
                    ProcessHeaderValue(data, handler);
                }
                else
                {
                    ParseHeaderValue(data, ref currentIndex, handler);
                }
            }
            else
            {
                _state = State.HeaderValueLengthContinue;
                ParseHeaderValueLengthContinue(data, ref currentIndex, handler);
            }
        }
    }

    private void ParseHeaderNameLengthContinue(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (TryDecodeInteger(data, ref currentIndex, out int intResult))
        {
            // IntegerDecoder disallows overlong encodings, where an integer is encoded with more bytes than is strictly required.
            // 0 should always be represented by a single byte, so we shouldn't need to check for it in the continuation case.
            Debug.Assert(intResult != 0, "A header name length of 0 should never be encoded with a continuation byte.");

            OnStringLength(intResult, nextState: State.HeaderName);
            ParseHeaderName(data, ref currentIndex, handler);
        }
    }

    private void ParseHeaderValueLengthContinue(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (TryDecodeInteger(data, ref currentIndex, out int intResult))
        {
            // 0 should always be represented by a single byte, so we shouldn't need to check for it in the continuation case.
            Debug.Assert(intResult != 0, "A header value length of 0 should never be encoded with a continuation byte.");

            OnStringLength(intResult, nextState: State.HeaderValue);
            ParseHeaderValue(data, ref currentIndex, handler);
        }
    }

    private void ParseHeaderFieldIndex(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (TryDecodeInteger(data, ref currentIndex, out int intResult))
        {
            OnIndexedHeaderField(intResult, handler);
        }
    }

    private void ParseHeaderNameIndex(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (TryDecodeInteger(data, ref currentIndex, out int intResult))
        {
            OnIndexedHeaderName(intResult);
            ParseHeaderValueLength(data, ref currentIndex, handler);
        }
    }

    private void ParseHeaderNameLength(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (currentIndex < data.Length)
        {
            byte b = data[currentIndex++];

            _huffman = IsHuffmanEncoded(b);

            if (_integerDecoder.BeginTryDecode((byte)(b & ~HuffmanMask), StringLengthPrefix, out int intResult))
            {
                if (intResult == 0)
                {
                    throw new HeaderDecodingException("Invalid header name");
                }

                OnStringLength(intResult, nextState: State.HeaderName);
                ParseHeaderName(data, ref currentIndex, handler);
            }
            else
            {
                _state = State.HeaderNameLengthContinue;
                ParseHeaderNameLengthContinue(data, ref currentIndex, handler);
            }
        }
    }

    private void Parse(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        if (currentIndex < data.Length)
        {
            Debug.Assert(_state == State.Ready, "Should be ready to parse a new header.");

            byte b = data[currentIndex++];

            switch (BitOperations.LeadingZeroCount(b) - 24) // byte 'b' is extended to uint, so will have 24 extra 0s.
            {
                case 0: // Indexed Header Field
                    {
                        _headersObserved = true;

                        int val = b & ~IndexedHeaderFieldMask;

                        if (_integerDecoder.BeginTryDecode((byte)val, IndexedHeaderFieldPrefix, out int intResult))
                        {
                            OnIndexedHeaderField(intResult, handler);
                        }
                        else
                        {
                            _state = State.HeaderFieldIndex;
                            ParseHeaderFieldIndex(data, ref currentIndex, handler);
                        }
                        break;
                    }
                case 1: // Literal Header Field with Incremental Indexing
                    ParseLiteralHeaderField(
                        data,
                        ref currentIndex,
                        b,
                        LiteralHeaderFieldWithIncrementalIndexingMask,
                        LiteralHeaderFieldWithIncrementalIndexingPrefix,
                        index: true,
                        handler);
                    break;
                case 4:
                default: // Literal Header Field without Indexing
                    ParseLiteralHeaderField(
                        data,
                        ref currentIndex,
                        b,
                        LiteralHeaderFieldWithoutIndexingMask,
                        LiteralHeaderFieldWithoutIndexingPrefix,
                        index: false,
                        handler);
                    break;
                case 3: // Literal Header Field Never Indexed
                    ParseLiteralHeaderField(
                        data,
                        ref currentIndex,
                        b,
                        LiteralHeaderFieldNeverIndexedMask,
                        LiteralHeaderFieldNeverIndexedPrefix,
                        index: false,
                        handler);
                    break;
                case 2: // Dynamic Table Size Update
                    {
                        // https://tools.ietf.org/html/rfc7541#section-4.2
                        // This dynamic table size
                        // update MUST occur at the beginning of the first header block
                        // following the change to the dynamic table size.
                        if (_headersObserved)
                        {
                            throw new HeaderDecodingException("Dynamic table size updates not allowed");
                        }

                        if (_integerDecoder.BeginTryDecode((byte)(b & ~DynamicTableSizeUpdateMask), DynamicTableSizeUpdatePrefix, out int intResult))
                        {
                            SetDynamicHeaderTableSize(intResult);
                        }
                        else
                        {
                            _state = State.DynamicTableSizeUpdate;
                            ParseDynamicTableSizeUpdate(data, ref currentIndex);
                        }
                        break;
                    }
            }
        }
    }

    private void ParseLiteralHeaderField(ReadOnlySpan<byte> data, ref int currentIndex, byte b, byte mask, byte indexPrefix, bool index, IHttpStreamHeadersHandler handler)
    {
        _headersObserved = true;

        _index = index;
        int val = b & ~mask;

        if (val == 0)
        {
            _state = State.HeaderNameLength;
            ParseHeaderNameLength(data, ref currentIndex, handler);
        }
        else
        {
            if (_integerDecoder.BeginTryDecode((byte)val, indexPrefix, out int intResult))
            {
                OnIndexedHeaderName(intResult);
                ParseHeaderValueLength(data, ref currentIndex, handler);
            }
            else
            {
                _state = State.HeaderNameIndex;
                ParseHeaderNameIndex(data, ref currentIndex, handler);
            }
        }
    }

    private void ParseHeaderName(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        // Read remaining chars, up to the length of the current data
        int count = Math.Min(_stringLength - _stringIndex, data.Length - currentIndex);

        // Check whether the whole string is available in the data and no decompression required.
        // If string is good then mark its range.
        // NOTE: it may need to be copied to buffer later the if value is not current data.
        if (count == _stringLength && !_huffman)
        {
            // Fast path. Store the range rather than copying.
            _headerNameRange = (start: currentIndex, count);
            _headerNameLength = _stringLength;
            currentIndex += count;

            _state = State.HeaderValueLength;
            ParseHeaderValueLength(data, ref currentIndex, handler);
        }
        else if (count == 0)
        {
            // no-op
        }
        else
        {
            // Copy string to temporary buffer.
            // _stringOctets was already
            data.Slice(currentIndex, count).CopyTo(_stringOctets.AsSpan(_stringIndex));
            _stringIndex += count;
            currentIndex += count;

            if (_stringIndex == _stringLength)
            {
                OnString(nextState: State.HeaderValueLength);
                ParseHeaderValueLength(data, ref currentIndex, handler);
            }
        }
    }

    private void ParseHeaderValue(ReadOnlySpan<byte> data, ref int currentIndex, IHttpStreamHeadersHandler handler)
    {
        // Read remaining chars, up to the length of the current data
        int count = Math.Min(_stringLength - _stringIndex, data.Length - currentIndex);

        // Check whether the whole string is available in the data and no decompressed required.
        // If string is good then mark its range.
        if (count == _stringLength && !_huffman)
        {
            // Fast path. Store the range rather than copying.
            _headerValueRange = (start: currentIndex, count);
            currentIndex += count;

            _state = State.Ready;
            ProcessHeaderValue(data, handler);
        }
        else
        {
            // Copy string to temporary buffer.
            data.Slice(currentIndex, count).CopyTo(_stringOctets.AsSpan(_stringIndex));
            _stringIndex += count;
            currentIndex += count;

            if (_stringIndex == _stringLength)
            {
                OnString(nextState: State.Ready);
                ProcessHeaderValue(data, handler);
            }
        }
    }

    private void CheckIncompleteHeaderBlock(bool endHeaders)
    {
        if (endHeaders)
        {
            if (_state != State.Ready)
            {
                throw new HeaderDecodingException("Incomplete header");
            }

            _headersObserved = false;
        }
    }

    private void ProcessHeaderValue(ReadOnlySpan<byte> data, IHttpStreamHeadersHandler handler)
    {
        ReadOnlySpan<byte> headerValueSpan = _headerValueRange == null
            ? _headerValueOctets.AsSpan(0, _headerValueLength)
            : data.Slice(_headerValueRange.GetValueOrDefault().start, _headerValueRange.GetValueOrDefault().length);

        if (_headerStaticIndex > 0)
        {
            handler.OnStaticIndexedHeader(_headerStaticIndex, headerValueSpan);

            if (_index)
            {
                _dynamicTable.Insert(_headerStaticIndex, H2StaticTable.Get(_headerStaticIndex).Name, headerValueSpan);
            }
        }
        else
        {
            ReadOnlySpan<byte> headerNameSpan = _headerNameRange == null
                ? _headerName.AsSpan(0, _headerNameLength)
                : data.Slice(_headerNameRange.GetValueOrDefault().start, _headerNameRange.GetValueOrDefault().length);

            handler.OnHeader(headerNameSpan, headerValueSpan);

            if (_index)
            {
                _dynamicTable.Insert(headerNameSpan, headerValueSpan);
            }
        }

        _headerStaticIndex = 0;
        _headerNameRange = null;
        _headerValueRange = null;
    }

    public void CompleteDecode()
    {
        if (_state != State.Ready)
        {
            // Incomplete header block
            throw new HeaderDecodingException("Unexpected header end");
        }
    }

    private void OnIndexedHeaderField(int index, IHttpStreamHeadersHandler handler)
    {
        if (index <= H2StaticTable.Count)
        {
            handler.OnStaticIndexedHeader(index);
        }
        else
        {
            ref readonly HeaderField header = ref GetDynamicHeader(index);
            handler.OnDynamicIndexedHeader(header.StaticTableIndex, header.Name, header.Value);
        }

        _state = State.Ready;
    }

    private void OnIndexedHeaderName(int index)
    {
        if (index <= H2StaticTable.Count)
        {
            _headerStaticIndex = index;
        }
        else
        {
            _headerName = GetDynamicHeader(index).Name;
            _headerNameLength = _headerName.Length;
        }
        _state = State.HeaderValueLength;
    }

    private void OnStringLength(int length, State nextState)
    {
        if (length > _stringOctets.Length)
        {
            if (length > _maxHeadersLength)
            {
                throw new HeaderDecodingException($"Headers exceeded max length: {_maxHeadersLength}");
            }

            _stringOctets = new byte[Math.Max(length, Math.Min(_stringOctets.Length * 2, _maxHeadersLength))];
        }

        _stringLength = length;
        _stringIndex = 0;
        _state = nextState;
    }

    private void OnString(State nextState)
    {
        int Decode(ref byte[] dst)
        {
            if (_huffman)
            {
                return Huffman.Decode(new ReadOnlySpan<byte>(_stringOctets, 0, _stringLength), ref dst);
            }
            else
            {
                EnsureStringCapacity(ref dst);
                Buffer.BlockCopy(_stringOctets, 0, dst, 0, _stringLength);
                return _stringLength;
            }
        }

        if (_state == State.HeaderName)
        {
            _headerNameLength = Decode(ref _headerNameOctets);
            _headerName = _headerNameOctets;
        }
        else
        {
            _headerValueLength = Decode(ref _headerValueOctets);
        }
        _state = nextState;
    }

    private void EnsureStringCapacity(ref byte[] dst, int stringLength = -1)
    {
        stringLength = stringLength >= 0 ? stringLength : _stringLength;
        if (dst.Length < stringLength)
        {
            dst = new byte[Math.Max(stringLength, Math.Min(dst.Length * 2, _maxHeadersLength))];
        }
    }

    private bool TryDecodeInteger(ReadOnlySpan<byte> data, ref int currentIndex, out int result)
    {
        for (; currentIndex < data.Length; currentIndex++)
        {
            if (_integerDecoder.TryDecode(data[currentIndex], out result))
            {
                currentIndex++;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool IsHuffmanEncoded(byte b)
    {
        return (b & HuffmanMask) != 0;
    }

    private ref readonly HeaderField GetDynamicHeader(int index)
    {
        try
        {
            return ref _dynamicTable[index - H2StaticTable.Count - 1];
        }
        catch (IndexOutOfRangeException)
        {
            // Header index out of range.
            throw new HeaderDecodingException($"Headers exceeded max length: {index}");
        }
    }

    private void SetDynamicHeaderTableSize(int size)
    {
        if (size > _maxDynamicTableSize)
        {
            throw new HeaderDecodingException($"Cannot set dynamic table size larger to {_maxDynamicTableSize}");
        }

        _dynamicTable.Resize(size);
    }
}

internal sealed class DynamicTable
{
    private HeaderField[] _buffer;
    private int _maxSize;
    private int _size;
    private int _count;
    private int _insertIndex;
    private int _removeIndex;

    public DynamicTable(int maxSize)
    {
        _buffer = new HeaderField[maxSize / HeaderField.RfcOverhead];
        _maxSize = maxSize;
    }

    public int Count => _count;

    public int Size => _size;

    public int MaxSize => _maxSize;

    public ref readonly HeaderField this[int index]
    {
        get
        {
            if (index >= _count)
            {
#pragma warning disable CA2201 // Do not raise reserved exception types
                // Helpful to act like static table (array)
                throw new IndexOutOfRangeException();
#pragma warning restore CA2201
            }

            index = _insertIndex - index - 1;

            if (index < 0)
            {
                // _buffer is circular; wrap the index back around.
                index += _buffer.Length;
            }

            return ref _buffer[index];
        }
    }

    public void Insert(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        Insert(staticTableIndex: null, name, value);
    }

    public void Insert(int? staticTableIndex, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        int entryLength = HeaderField.GetLength(name.Length, value.Length);
        EnsureAvailable(entryLength);

        if (entryLength > _maxSize)
        {
            // http://httpwg.org/specs/rfc7541.html#rfc.section.4.4
            // It is not an error to attempt to add an entry that is larger than the maximum size;
            // an attempt to add an entry larger than the maximum size causes the table to be emptied
            // of all existing entries and results in an empty table.
            return;
        }

        var entry = new HeaderField(staticTableIndex ?? -1, name, value);
        _buffer[_insertIndex] = entry;
        _insertIndex = (_insertIndex + 1) % _buffer.Length;
        _size += entry.Length;
        _count++;
    }

    public void Resize(int maxSize)
    {
        if (maxSize > _maxSize)
        {
            var newBuffer = new HeaderField[maxSize / HeaderField.RfcOverhead];

            int headCount = Math.Min(_buffer.Length - _removeIndex, _count);
            int tailCount = _count - headCount;

            Array.Copy(_buffer, _removeIndex, newBuffer, 0, headCount);
            Array.Copy(_buffer, 0, newBuffer, headCount, tailCount);

            _buffer = newBuffer;
            _removeIndex = 0;
            _insertIndex = _count;
            _maxSize = maxSize;
        }
        else
        {
            _maxSize = maxSize;
            EnsureAvailable(0);
        }
    }

    private void EnsureAvailable(int available)
    {
        while (_count > 0 && _maxSize - _size < available)
        {
            ref HeaderField field = ref _buffer[_removeIndex];
            _size -= field.Length;
            field = default;

            _count--;
            _removeIndex = (_removeIndex + 1) % _buffer.Length;
        }
    }
}


internal struct HPackIntegerDecoder
{
    private int _i;
    private int _m;

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="b">
    /// The first byte of the variable-length encoded integer.
    /// </param>
    /// <param name="prefixLength">
    /// The number of lower bits in this prefix byte that the
    /// integer has been encoded into. Must be between 1 and 8.
    /// Upper bits must be zero.
    /// </param>
    /// <param name="result">
    /// If decoded successfully, contains the decoded integer.
    /// </param>
    /// <returns>
    /// If the integer has been fully decoded, true.
    /// Otherwise, false -- <see cref="TryDecode(byte, out int)"/> must be called on subsequent bytes.
    /// </returns>
    /// <remarks>
    /// The term "prefix" can be confusing. From the HPACK spec:
    /// An integer is represented in two parts: a prefix that fills the current octet and an
    /// optional list of octets that are used if the integer value does not fit within the prefix.
    /// </remarks>
    public bool BeginTryDecode(byte b, int prefixLength, out int result)
    {
        Debug.Assert(prefixLength >= 1 && prefixLength <= 8);
        Debug.Assert((b & ~((1 << prefixLength) - 1)) == 0, "bits other than prefix data must be set to 0.");

        if (b < ((1 << prefixLength) - 1))
        {
            result = b;
            return true;
        }

        _i = b;
        _m = 0;
        result = 0;
        return false;
    }

    /// <summary>
    /// Decodes subsequent bytes of an integer.
    /// </summary>
    /// <param name="b">The next byte.</param>
    /// <param name="result">
    /// If decoded successfully, contains the decoded integer.
    /// </param>
    /// <returns>If the integer has been fully decoded, true. Otherwise, false -- <see cref="TryDecode(byte, out int)"/> must be called on subsequent bytes.</returns>
    public bool TryDecode(byte b, out int result)
    {
        // Check if shifting b by _m would result in > 31 bits.
        // No masking is required: if the 8th bit is set, it indicates there is a
        // bit set in a future byte, so it is fine to check that here as if it were
        // bit 0 on the next byte.
        // This is a simplified form of:
        //   int additionalBitsRequired = 32 - BitOperations.LeadingZeroCount((uint)b);
        //   if (_m + additionalBitsRequired > 31)
        if (BitOperations.LeadingZeroCount((uint)b) <= _m)
        {
            throw new HeaderDecodingException("Bad Integer");
        }

        _i += ((b & 0x7f) << _m);

        // If the addition overflowed, the result will be negative.
        if (_i < 0)
        {
            throw new HeaderDecodingException("Bad Integer");
        }

        _m += 7;

        if ((b & 128) == 0)
        {
            if (b == 0 && _m / 7 > 1)
            {
                // Do not accept overlong encodings.
                throw new HeaderDecodingException("Bad Integer");
            }

            result = _i;
            return true;
        }

        result = 0;
        return false;
    }
}