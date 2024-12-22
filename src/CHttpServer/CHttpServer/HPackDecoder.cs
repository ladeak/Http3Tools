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
    private IntegerDecoder _integerDecoder;
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
                    throw new HPackDecodingException("Invalid header name");
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
                            throw new HPackDecodingException("Dynamic table size updates not allowed");
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
                throw new HPackDecodingException("Incomplete header");
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
                _dynamicTable.Insert(_headerStaticIndex, H2StaticTable.Get(_headerStaticIndex - 1).Name, headerValueSpan);
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
            throw new HPackDecodingException("Unexpected header end");
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
                throw new HPackDecodingException($"Headers exceeded max length: {_maxHeadersLength}");
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
            throw new HPackDecodingException($"Headers exceeded max length: {index}");
        }
    }

    private void SetDynamicHeaderTableSize(int size)
    {
        if (size > _maxDynamicTableSize)
        {
            throw new HPackDecodingException($"Cannot set dynamic table size larger to {_maxDynamicTableSize}");
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

internal struct IntegerDecoder
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
            throw new HPackDecodingException("Bad Integer");
        }

        _i += ((b & 0x7f) << _m);

        // If the addition overflowed, the result will be negative.
        if (_i < 0)
        {
            throw new HPackDecodingException("Bad Integer");
        }

        _m += 7;

        if ((b & 128) == 0)
        {
            if (b == 0 && _m / 7 > 1)
            {
                // Do not accept overlong encodings.
                throw new HPackDecodingException("Bad Integer");
            }

            result = _i;
            return true;
        }

        result = 0;
        return false;
    }
}

internal sealed class HPackDecodingException : Exception
{
    public HPackDecodingException()
    {
    }

    public HPackDecodingException(string message) : base(message)
    {
    }

    public HPackDecodingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

internal static class Huffman
{
    // HPack static huffman code. see: https://httpwg.org/specs/rfc7541.html#huffman.code
    private static ReadOnlySpan<uint> EncodingTableCodes => // 257
    [
        0b11111111_11000000_00000000_00000000,
            0b11111111_11111111_10110000_00000000,
            0b11111111_11111111_11111110_00100000,
            0b11111111_11111111_11111110_00110000,
            0b11111111_11111111_11111110_01000000,
            0b11111111_11111111_11111110_01010000,
            0b11111111_11111111_11111110_01100000,
            0b11111111_11111111_11111110_01110000,
            0b11111111_11111111_11111110_10000000,
            0b11111111_11111111_11101010_00000000,
            0b11111111_11111111_11111111_11110000,
            0b11111111_11111111_11111110_10010000,
            0b11111111_11111111_11111110_10100000,
            0b11111111_11111111_11111111_11110100,
            0b11111111_11111111_11111110_10110000,
            0b11111111_11111111_11111110_11000000,
            0b11111111_11111111_11111110_11010000,
            0b11111111_11111111_11111110_11100000,
            0b11111111_11111111_11111110_11110000,
            0b11111111_11111111_11111111_00000000,
            0b11111111_11111111_11111111_00010000,
            0b11111111_11111111_11111111_00100000,
            0b11111111_11111111_11111111_11111000,
            0b11111111_11111111_11111111_00110000,
            0b11111111_11111111_11111111_01000000,
            0b11111111_11111111_11111111_01010000,
            0b11111111_11111111_11111111_01100000,
            0b11111111_11111111_11111111_01110000,
            0b11111111_11111111_11111111_10000000,
            0b11111111_11111111_11111111_10010000,
            0b11111111_11111111_11111111_10100000,
            0b11111111_11111111_11111111_10110000,
            0b01010000_00000000_00000000_00000000,
            0b11111110_00000000_00000000_00000000,
            0b11111110_01000000_00000000_00000000,
            0b11111111_10100000_00000000_00000000,
            0b11111111_11001000_00000000_00000000,
            0b01010100_00000000_00000000_00000000,
            0b11111000_00000000_00000000_00000000,
            0b11111111_01000000_00000000_00000000,
            0b11111110_10000000_00000000_00000000,
            0b11111110_11000000_00000000_00000000,
            0b11111001_00000000_00000000_00000000,
            0b11111111_01100000_00000000_00000000,
            0b11111010_00000000_00000000_00000000,
            0b01011000_00000000_00000000_00000000,
            0b01011100_00000000_00000000_00000000,
            0b01100000_00000000_00000000_00000000,
            0b00000000_00000000_00000000_00000000,
            0b00001000_00000000_00000000_00000000,
            0b00010000_00000000_00000000_00000000,
            0b01100100_00000000_00000000_00000000,
            0b01101000_00000000_00000000_00000000,
            0b01101100_00000000_00000000_00000000,
            0b01110000_00000000_00000000_00000000,
            0b01110100_00000000_00000000_00000000,
            0b01111000_00000000_00000000_00000000,
            0b01111100_00000000_00000000_00000000,
            0b10111000_00000000_00000000_00000000,
            0b11111011_00000000_00000000_00000000,
            0b11111111_11111000_00000000_00000000,
            0b10000000_00000000_00000000_00000000,
            0b11111111_10110000_00000000_00000000,
            0b11111111_00000000_00000000_00000000,
            0b11111111_11010000_00000000_00000000,
            0b10000100_00000000_00000000_00000000,
            0b10111010_00000000_00000000_00000000,
            0b10111100_00000000_00000000_00000000,
            0b10111110_00000000_00000000_00000000,
            0b11000000_00000000_00000000_00000000,
            0b11000010_00000000_00000000_00000000,
            0b11000100_00000000_00000000_00000000,
            0b11000110_00000000_00000000_00000000,
            0b11001000_00000000_00000000_00000000,
            0b11001010_00000000_00000000_00000000,
            0b11001100_00000000_00000000_00000000,
            0b11001110_00000000_00000000_00000000,
            0b11010000_00000000_00000000_00000000,
            0b11010010_00000000_00000000_00000000,
            0b11010100_00000000_00000000_00000000,
            0b11010110_00000000_00000000_00000000,
            0b11011000_00000000_00000000_00000000,
            0b11011010_00000000_00000000_00000000,
            0b11011100_00000000_00000000_00000000,
            0b11011110_00000000_00000000_00000000,
            0b11100000_00000000_00000000_00000000,
            0b11100010_00000000_00000000_00000000,
            0b11100100_00000000_00000000_00000000,
            0b11111100_00000000_00000000_00000000,
            0b11100110_00000000_00000000_00000000,
            0b11111101_00000000_00000000_00000000,
            0b11111111_11011000_00000000_00000000,
            0b11111111_11111110_00000000_00000000,
            0b11111111_11100000_00000000_00000000,
            0b11111111_11110000_00000000_00000000,
            0b10001000_00000000_00000000_00000000,
            0b11111111_11111010_00000000_00000000,
            0b00011000_00000000_00000000_00000000,
            0b10001100_00000000_00000000_00000000,
            0b00100000_00000000_00000000_00000000,
            0b10010000_00000000_00000000_00000000,
            0b00101000_00000000_00000000_00000000,
            0b10010100_00000000_00000000_00000000,
            0b10011000_00000000_00000000_00000000,
            0b10011100_00000000_00000000_00000000,
            0b00110000_00000000_00000000_00000000,
            0b11101000_00000000_00000000_00000000,
            0b11101010_00000000_00000000_00000000,
            0b10100000_00000000_00000000_00000000,
            0b10100100_00000000_00000000_00000000,
            0b10101000_00000000_00000000_00000000,
            0b00111000_00000000_00000000_00000000,
            0b10101100_00000000_00000000_00000000,
            0b11101100_00000000_00000000_00000000,
            0b10110000_00000000_00000000_00000000,
            0b01000000_00000000_00000000_00000000,
            0b01001000_00000000_00000000_00000000,
            0b10110100_00000000_00000000_00000000,
            0b11101110_00000000_00000000_00000000,
            0b11110000_00000000_00000000_00000000,
            0b11110010_00000000_00000000_00000000,
            0b11110100_00000000_00000000_00000000,
            0b11110110_00000000_00000000_00000000,
            0b11111111_11111100_00000000_00000000,
            0b11111111_10000000_00000000_00000000,
            0b11111111_11110100_00000000_00000000,
            0b11111111_11101000_00000000_00000000,
            0b11111111_11111111_11111111_11000000,
            0b11111111_11111110_01100000_00000000,
            0b11111111_11111111_01001000_00000000,
            0b11111111_11111110_01110000_00000000,
            0b11111111_11111110_10000000_00000000,
            0b11111111_11111111_01001100_00000000,
            0b11111111_11111111_01010000_00000000,
            0b11111111_11111111_01010100_00000000,
            0b11111111_11111111_10110010_00000000,
            0b11111111_11111111_01011000_00000000,
            0b11111111_11111111_10110100_00000000,
            0b11111111_11111111_10110110_00000000,
            0b11111111_11111111_10111000_00000000,
            0b11111111_11111111_10111010_00000000,
            0b11111111_11111111_10111100_00000000,
            0b11111111_11111111_11101011_00000000,
            0b11111111_11111111_10111110_00000000,
            0b11111111_11111111_11101100_00000000,
            0b11111111_11111111_11101101_00000000,
            0b11111111_11111111_01011100_00000000,
            0b11111111_11111111_11000000_00000000,
            0b11111111_11111111_11101110_00000000,
            0b11111111_11111111_11000010_00000000,
            0b11111111_11111111_11000100_00000000,
            0b11111111_11111111_11000110_00000000,
            0b11111111_11111111_11001000_00000000,
            0b11111111_11111110_11100000_00000000,
            0b11111111_11111111_01100000_00000000,
            0b11111111_11111111_11001010_00000000,
            0b11111111_11111111_01100100_00000000,
            0b11111111_11111111_11001100_00000000,
            0b11111111_11111111_11001110_00000000,
            0b11111111_11111111_11101111_00000000,
            0b11111111_11111111_01101000_00000000,
            0b11111111_11111110_11101000_00000000,
            0b11111111_11111110_10010000_00000000,
            0b11111111_11111111_01101100_00000000,
            0b11111111_11111111_01110000_00000000,
            0b11111111_11111111_11010000_00000000,
            0b11111111_11111111_11010010_00000000,
            0b11111111_11111110_11110000_00000000,
            0b11111111_11111111_11010100_00000000,
            0b11111111_11111111_01110100_00000000,
            0b11111111_11111111_01111000_00000000,
            0b11111111_11111111_11110000_00000000,
            0b11111111_11111110_11111000_00000000,
            0b11111111_11111111_01111100_00000000,
            0b11111111_11111111_11010110_00000000,
            0b11111111_11111111_11011000_00000000,
            0b11111111_11111111_00000000_00000000,
            0b11111111_11111111_00001000_00000000,
            0b11111111_11111111_10000000_00000000,
            0b11111111_11111111_00010000_00000000,
            0b11111111_11111111_11011010_00000000,
            0b11111111_11111111_10000100_00000000,
            0b11111111_11111111_11011100_00000000,
            0b11111111_11111111_11011110_00000000,
            0b11111111_11111110_10100000_00000000,
            0b11111111_11111111_10001000_00000000,
            0b11111111_11111111_10001100_00000000,
            0b11111111_11111111_10010000_00000000,
            0b11111111_11111111_11100000_00000000,
            0b11111111_11111111_10010100_00000000,
            0b11111111_11111111_10011000_00000000,
            0b11111111_11111111_11100010_00000000,
            0b11111111_11111111_11111000_00000000,
            0b11111111_11111111_11111000_01000000,
            0b11111111_11111110_10110000_00000000,
            0b11111111_11111110_00100000_00000000,
            0b11111111_11111111_10011100_00000000,
            0b11111111_11111111_11100100_00000000,
            0b11111111_11111111_10100000_00000000,
            0b11111111_11111111_11110110_00000000,
            0b11111111_11111111_11111000_10000000,
            0b11111111_11111111_11111000_11000000,
            0b11111111_11111111_11111001_00000000,
            0b11111111_11111111_11111011_11000000,
            0b11111111_11111111_11111011_11100000,
            0b11111111_11111111_11111001_01000000,
            0b11111111_11111111_11110001_00000000,
            0b11111111_11111111_11110110_10000000,
            0b11111111_11111110_01000000_00000000,
            0b11111111_11111111_00011000_00000000,
            0b11111111_11111111_11111001_10000000,
            0b11111111_11111111_11111100_00000000,
            0b11111111_11111111_11111100_00100000,
            0b11111111_11111111_11111001_11000000,
            0b11111111_11111111_11111100_01000000,
            0b11111111_11111111_11110010_00000000,
            0b11111111_11111111_00100000_00000000,
            0b11111111_11111111_00101000_00000000,
            0b11111111_11111111_11111010_00000000,
            0b11111111_11111111_11111010_01000000,
            0b11111111_11111111_11111111_11010000,
            0b11111111_11111111_11111100_01100000,
            0b11111111_11111111_11111100_10000000,
            0b11111111_11111111_11111100_10100000,
            0b11111111_11111110_11000000_00000000,
            0b11111111_11111111_11110011_00000000,
            0b11111111_11111110_11010000_00000000,
            0b11111111_11111111_00110000_00000000,
            0b11111111_11111111_10100100_00000000,
            0b11111111_11111111_00111000_00000000,
            0b11111111_11111111_01000000_00000000,
            0b11111111_11111111_11100110_00000000,
            0b11111111_11111111_10101000_00000000,
            0b11111111_11111111_10101100_00000000,
            0b11111111_11111111_11110111_00000000,
            0b11111111_11111111_11110111_10000000,
            0b11111111_11111111_11110100_00000000,
            0b11111111_11111111_11110101_00000000,
            0b11111111_11111111_11111010_10000000,
            0b11111111_11111111_11101000_00000000,
            0b11111111_11111111_11111010_11000000,
            0b11111111_11111111_11111100_11000000,
            0b11111111_11111111_11111011_00000000,
            0b11111111_11111111_11111011_01000000,
            0b11111111_11111111_11111100_11100000,
            0b11111111_11111111_11111101_00000000,
            0b11111111_11111111_11111101_00100000,
            0b11111111_11111111_11111101_01000000,
            0b11111111_11111111_11111101_01100000,
            0b11111111_11111111_11111111_11100000,
            0b11111111_11111111_11111101_10000000,
            0b11111111_11111111_11111101_10100000,
            0b11111111_11111111_11111101_11000000,
            0b11111111_11111111_11111101_11100000,
            0b11111111_11111111_11111110_00000000,
            0b11111111_11111111_11111011_10000000,
            0b11111111_11111111_11111111_11111100,
        ];

    private static ReadOnlySpan<byte> EncodingTableBitLengths => // 257
    [
        13,
            23,
            28,
            28,
            28,
            28,
            28,
            28,
            28,
            24,
            30,
            28,
            28,
            30,
            28,
            28,
            28,
            28,
            28,
            28,
            28,
            28,
            30,
            28,
            28,
            28,
            28,
            28,
            28,
            28,
            28,
            28,
            6,
            10,
            10,
            12,
            13,
            6,
            8,
            11,
            10,
            10,
            8,
            11,
            8,
            6,
            6,
            6,
            5,
            5,
            5,
            6,
            6,
            6,
            6,
            6,
            6,
            6,
            7,
            8,
            15,
            6,
            12,
            10,
            13,
            6,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            7,
            8,
            7,
            8,
            13,
            19,
            13,
            14,
            6,
            15,
            5,
            6,
            5,
            6,
            5,
            6,
            6,
            6,
            5,
            7,
            7,
            6,
            6,
            6,
            5,
            6,
            7,
            6,
            5,
            5,
            6,
            7,
            7,
            7,
            7,
            7,
            15,
            11,
            14,
            13,
            28,
            20,
            22,
            20,
            20,
            22,
            22,
            22,
            23,
            22,
            23,
            23,
            23,
            23,
            23,
            24,
            23,
            24,
            24,
            22,
            23,
            24,
            23,
            23,
            23,
            23,
            21,
            22,
            23,
            22,
            23,
            23,
            24,
            22,
            21,
            20,
            22,
            22,
            23,
            23,
            21,
            23,
            22,
            22,
            24,
            21,
            22,
            23,
            23,
            21,
            21,
            22,
            21,
            23,
            22,
            23,
            23,
            20,
            22,
            22,
            22,
            23,
            22,
            22,
            23,
            26,
            26,
            20,
            19,
            22,
            23,
            22,
            25,
            26,
            26,
            26,
            27,
            27,
            26,
            24,
            25,
            19,
            21,
            26,
            27,
            27,
            26,
            27,
            24,
            21,
            21,
            26,
            26,
            28,
            27,
            27,
            27,
            20,
            24,
            20,
            21,
            22,
            21,
            21,
            23,
            22,
            22,
            25,
            25,
            24,
            24,
            26,
            23,
            26,
            27,
            26,
            26,
            27,
            27,
            27,
            27,
            27,
            28,
            27,
            27,
            27,
            27,
            27,
            26,
            30
    ];

    private static readonly ushort[] s_decodingTree = GenerateDecodingLookupTree();

    public static (uint encoded, int bitLength) Encode(int data)
    {
        return (EncodingTableCodes[data], EncodingTableBitLengths[data]);
    }

    private static ushort[] GenerateDecodingLookupTree()
    {
        // Decoding lookup tree is a tree of 8 bit lookup tables stored in
        // one dimensional array of ushort to reduce allocations.
        // First 256 ushort is lookup table with index 0, next 256 ushort is lookup table with index 1, etc...
        // lookup_value = [(lookup_table_index << 8) + lookup_index]

        // lookup_index is next 8 bits of huffman code, if there is less than 8 bits in source.
        // lookup_index MUST be aligned to 8 bits with LSB bits set to anything (zeros are recommended).

        // Lookup value is encoded in ushort as either.
        // -----------------------------------------------------------------
        //  15  14  13  12  11  10   9   8   7   6   5   4   3   2   1   0
        // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
        // | 1 |   next_lookup_table_index |            not_used           |
        // +---+---------------------------+-------------------------------+
        // or
        // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
        // | 0 |     number_of_used_bits   |              octet            |
        // +---+---------------------------+-------------------------------+

        // Bit 15 unset indicates a leaf value of decoding tree.
        // For example value 0x0241 means that we have reached end of huffman code
        // with result byte 0x41 'A' and from lookup bits only rightmost 2 bits were used
        // and rest of bits are part of next huffman code.

        // Bit 15 set indicates that code is not yet decoded and next lookup table index shall be used
        // for next n bits of huffman code.
        // 0 in 'next lookup table index' is considered as decoding error - invalid huffman code

        // Because HPack uses static huffman code defined in RFC https://httpwg.org/specs/rfc7541.html#huffman.code
        // it is guaranteed that for this huffman code generated decoding lookup tree MUST consist of exactly 15 lookup tables
        var decodingTree = new ushort[15 * 256];

        ReadOnlySpan<uint> encodingTableCodes = EncodingTableCodes;
        ReadOnlySpan<byte> encodingTableBitLengths = EncodingTableBitLengths;

        int allocatedLookupTableIndex = 0;
        // Create traverse path for all 0..256 octets, 256 is EOS, see: http://httpwg.org/specs/rfc7541.html#rfc.section.5.2
        for (int octet = 0; octet <= 256; octet++)
        {
            uint code = encodingTableCodes[octet];
            int bitLength = encodingTableBitLengths[octet];

            int lookupTableIndex = 0;
            int bitsLeft = bitLength;
            while (bitsLeft > 0)
            {
                // read next 8 bits from huffman code
                int indexInLookupTable = (int)(code >> (32 - 8));

                if (bitsLeft <= 8)
                {
                    // Reached last lookup table for this huffman code.

                    // Identical lookup value has to be stored for every combination of unused bits,
                    // For example: 12 bit code could be looked up during decoding as this:
                    // ---------------------------------
                    //   7   6   5   4   3   2   1   0
                    // +---+---+---+---+---+---+---+---+
                    // |last_code_bits | next_code_bits|
                    // +-------------------------------+
                    // next_code_bits are 'random' bits of next huffman code, so in order for lookup
                    // to work, lookup value has to be stored for all 4 unused bits, in this case for suffix 0..15
                    int suffixCount = 1 << (8 - bitsLeft);
                    for (int suffix = 0; suffix < suffixCount; suffix++)
                    {
                        if (octet == 256)
                        {
                            // EOS (in our case 256) have special meaning in HPack static huffman code
                            // see: http://httpwg.org/specs/rfc7541.html#rfc.section.5.2
                            // > A Huffman-encoded string literal containing the EOS symbol MUST be treated as a decoding error.
                            // To force decoding error we store 0 as 'next lookup table index' which MUST be treated as decoding error.

                            // Invalid huffman code - EOS
                            // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                            // | 1 | 0   0   0   0   0   0   0 | 1   1   1   1   1   1   1   1 |
                            // +---+---------------------------+-------------------------------+
                            decodingTree[(lookupTableIndex << 8) + (indexInLookupTable | suffix)] = 0x80ff;
                        }
                        else
                        {
                            // Leaf lookup value
                            // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                            // | 0 |     number_of_used_bits   |              code             |
                            // +---+---------------------------+-------------------------------+
                            decodingTree[(lookupTableIndex << 8) + (indexInLookupTable | suffix)] = (ushort)((bitsLeft << 8) | octet);
                        }
                    }
                }
                else
                {
                    // More than 8 bits left in huffman code means that we need to traverse to another lookup table for next 8 bits
                    ushort lookupValue = decodingTree[(lookupTableIndex << 8) + indexInLookupTable];

                    // Because next_lookup_table_index can not be 0, as 0 is index of root table, default value of array element
                    // means that we have not initialized it yet => lookup table MUST be allocated and its index assigned to that lookup value
                    // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                    // | 1 |   next_lookup_table_index |            not_used           |
                    // +---+---------------------------+-------------------------------+
                    if (lookupValue == 0)
                    {
                        ++allocatedLookupTableIndex;
                        decodingTree[(lookupTableIndex << 8) + indexInLookupTable] = (ushort)((0x80 | allocatedLookupTableIndex) << 8);
                        lookupTableIndex = allocatedLookupTableIndex;
                    }
                    else
                    {
                        lookupTableIndex = (lookupValue & 0x7f00) >> 8;
                    }
                }

                bitsLeft -= 8;
                code <<= 8;
            }
        }

        return decodingTree;
    }

    /// <summary>
    /// Decodes a Huffman encoded string from a byte array.
    /// </summary>
    /// <param name="src">The source byte array containing the encoded data.</param>
    /// <param name="dstArray">The destination byte array to store the decoded data.  This may grow if its size is insufficient.</param>
    /// <returns>The number of decoded symbols.</returns>
    public static int Decode(ReadOnlySpan<byte> src, ref byte[] dstArray)
    {
        // The code below implements the decoding logic for an HPack huffman encoded literal values.
        // https://httpwg.org/specs/rfc7541.html#string.literal.representation
        //
        // To decode a symbol, we traverse the decoding lookup table tree by 8 bits for each lookup
        // until we found a leaf - which contains decoded symbol (octet)
        //
        // see comments in GenerateDecodingLookupTree() describing decoding table

        Span<byte> dst = dstArray;
        Debug.Assert(dst.Length > 0);

        ushort[] decodingTree = s_decodingTree;

        int lookupTableIndex = 0;
        int lookupIndex;

        uint acc = 0;
        int bitsInAcc = 0;

        int i = 0;
        int j = 0;
        while (i < src.Length)
        {
            // Load next 8 bits into accumulator.
            acc <<= 8;
            acc |= src[i++];
            bitsInAcc += 8;

            // Decode bits in accumulator.
            do
            {
                lookupIndex = (byte)(acc >> (bitsInAcc - 8));

                int lookupValue = decodingTree[(lookupTableIndex << 8) + lookupIndex];

                if (lookupValue < 0x80_00)
                {
                    // Octet found.
                    // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                    // | 0 |     number_of_used_bits   |              octet            |
                    // +---+---------------------------+-------------------------------+
                    if (j == dst.Length)
                    {
                        Array.Resize(ref dstArray, dst.Length * 2);
                        dst = dstArray;
                    }
                    dst[j++] = (byte)lookupValue;

                    // Start lookup of next symbol
                    lookupTableIndex = 0;
                    bitsInAcc -= lookupValue >> 8;
                }
                else
                {
                    // Traverse to next lookup table.
                    // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                    // | 1 |   next_lookup_table_index |            not_used           |
                    // +---+---------------------------+-------------------------------+
                    lookupTableIndex = (lookupValue & 0x7f00) >> 8;
                    if (lookupTableIndex == 0)
                    {
                        // No valid symbol could be decoded or EOS was decoded
                        throw new Exception("Huffman decoding failed");
                    }
                    bitsInAcc -= 8;
                }
            } while (bitsInAcc >= 8);
        }

        // Finish decoding last < 8 bits of src.
        // Processing of the last byte has to handle several corner cases
        // so it's extracted outside of the main loop for performance reasons.
        while (bitsInAcc > 0)
        {
            Debug.Assert(bitsInAcc < 8);

            // Check for correct EOS, which is padding with ones till end of byte
            // when we STARTED new huffman code in last 8 bits (lookupTableIndex was reset to 0 -> root lookup table).
            if (lookupTableIndex == 0)
            {
                // Check if all remaining bits are ones.
                uint ones = uint.MaxValue >> (32 - bitsInAcc);
                if ((acc & ones) == ones)
                {
                    // Is it a EOS. See: http://httpwg.org/specs/rfc7541.html#rfc.section.5.2
                    break;
                }
            }

            // Lookup index has to be 8 bits aligned to MSB
            lookupIndex = (byte)(acc << (8 - bitsInAcc));

            int lookupValue = decodingTree[(lookupTableIndex << 8) + lookupIndex];

            if (lookupValue < 0x80_00)
            {
                // Octet found.
                // +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
                // | 0 |     number_of_used_bits   |              octet            |
                // +---+---------------------------+-------------------------------+
                bitsInAcc -= lookupValue >> 8;

                if (bitsInAcc < 0)
                {
                    // Last looked up code had more bits than was left in accumulator which indicated invalid or incomplete source
                    throw new Exception("Huffman decoding failed");
                }

                if (j == dst.Length)
                {
                    Array.Resize(ref dstArray, dst.Length * 2);
                    dst = dstArray;
                }
                dst[j++] = (byte)lookupValue;

                // Set table index to root - start of new huffman code.
                lookupTableIndex = 0;
            }
            else
            {
                // Src was depleted in middle of lookup tree or EOS was decoded.
                throw new Exception("Huffman decoding failed");
            }
        }

        if (lookupTableIndex != 0)
        {
            // Finished in middle of traversing - no valid symbol could be decoded
            // or too long EOS padding (7 bits plus). See: http://httpwg.org/specs/rfc7541.html#rfc.section.5.2
            throw new Exception("Huffman decoding failed");
        }

        return j;
    }
}