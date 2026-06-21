using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace CHttpServer.Http3;

internal class Http3FramingStreamWriter(Stream responseStream, byte frameType, ArrayPool<byte>? memoryPool = null, Func<CancellationToken, Task>? onResponseStartingCallback = null) : PipeWriter
{
    private readonly struct Segment()
    {
        public byte[] Reference { get; init; } = [];
        public Memory<byte> Used { get; init; } = Memory<byte>.Empty;

        public bool IsEmpty => Used.IsEmpty;

        public bool IsAllocated => Reference.Length > 0;
    }

    private const int MaxFrameHeaderLength = 9;
    private readonly byte _frameType = frameType;
    private readonly List<Segment> _segments = [with(64)];
    private readonly ArrayPool<byte> _memoryPool = memoryPool ?? ArrayPool<byte>.Shared;
    private Stream _responseStream = responseStream;
    private bool _isCompleted = false;
    private long _unflushedBytes;
    private Segment _currentSegment = new Segment() { Reference = (memoryPool ?? ArrayPool<byte>.Shared).Rent(4096) };
    private Func<CancellationToken, Task>? _onResponseStartingCallback = onResponseStartingCallback;

    public override long UnflushedBytes => _unflushedBytes;

    public override bool CanGetUnflushedBytes => true;

    public void Reset(Stream responseStream, Func<CancellationToken, Task>? onResponseStartingCallback = null)
    {
        _responseStream = responseStream;
        ClearSegments(CollectionsMarshal.AsSpan(_segments), false);
        _isCompleted = false;
        _onResponseStartingCallback = onResponseStartingCallback;
    }

    public override void Advance(int bytes)
    {
        ThrowIfCompleted();
        var current = _currentSegment;
        var usedLength = current.Used.Length;
        var available = current.Reference.Length - usedLength;
        ArgumentOutOfRangeException.ThrowIfLessThan(available, bytes);
        _currentSegment = current with { Used = current.Reference.AsMemory(0, usedLength + bytes) };
        _unflushedBytes += bytes;
    }

    public override void CancelPendingFlush() => throw new PlatformNotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        if (_isCompleted)
            return;
        _isCompleted = true;
        try
        {
            if (_unflushedBytes != 0)
                Flush();
        }
        finally
        {
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _onResponseStartingCallback = null;
        }
    }

    public override async ValueTask CompleteAsync(Exception? exception = null)
    {
        if (_isCompleted)
            return;
        _isCompleted = true;
        try
        {
            if (_unflushedBytes != 0)
                await FlushAsync();
        }
        finally
        {
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _onResponseStartingCallback = null;
        }
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeHint, 0);
        ref var current = ref _currentSegment;

        // At the very first write allocate MaxFrameHeaderLength more for the header.
        if (_unflushedBytes == 0 && current.IsEmpty)
        {
            if (current.Reference.Length >= MaxFrameHeaderLength)
                current = current with { Used = current.Reference.AsMemory(0, MaxFrameHeaderLength) };
            else
            {
                if (current.IsAllocated)
                    _memoryPool.Return(current.Reference, true);
                var slice = _memoryPool.Rent(sizeHint + MaxFrameHeaderLength);
                current = new Segment() { Reference = slice, Used = slice.AsMemory(0, MaxFrameHeaderLength) }; // Minimum size left for ArrayPool.
            }
        }

        var available = current.Reference.Length - current.Used.Length;
        if (sizeHint <= available)
            return current.Reference.AsMemory(current.Used.Length);

        // Not enough memory left in the current segment.
        if (!current.IsEmpty)
            _segments.Add(current);
        else if (current.IsAllocated)
            _memoryPool.Return(current.Reference, true);

        current = new Segment() { Reference = _memoryPool.Rent(sizeHint) }; // Minimum size left for ArrayPool.
        return current.Reference.AsMemory();
    }

    public override Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        if (source.Length == 0)
            return new FlushResult(false, false);
        try
        {
            if (_onResponseStartingCallback != null)
            {
                await _onResponseStartingCallback.Invoke(cancellationToken);
                _onResponseStartingCallback = null;
            }

            // Create a copy to avoid double write to the stream.
            if (source.Length < (1 << 15))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(source.Length + MaxFrameHeaderLength);
                var frameHeaderLength = PrepareFrameHeader(buffer.AsSpan(), source.Length, _frameType);
                source.CopyTo(buffer.AsMemory(frameHeaderLength));
                await _responseStream.WriteAsync(buffer.AsMemory(0..(source.Length + frameHeaderLength)), cancellationToken);
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
            else
            {
                Span<byte> buffer = stackalloc byte[MaxFrameHeaderLength];
                var frameHeaderLength = PrepareFrameHeader(buffer, source.Length, _frameType);
                _responseStream.Write(buffer[0..frameHeaderLength]);
                await _responseStream.WriteAsync(source, cancellationToken);
            }
            _responseStream.Flush();
            return new FlushResult(isCanceled: false, isCompleted: false);
        }
        catch (Exception)
        {
            _isCompleted = true;
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _onResponseStartingCallback = null;
            throw;
        }
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_unflushedBytes == 0)
            return new FlushResult(false, false);
        try
        {
            if (_onResponseStartingCallback != null)
            {
                await _onResponseStartingCallback.Invoke(cancellationToken);
                _onResponseStartingCallback = null;
            }

            var startOffset = PrepareFrameHeader(_unflushedBytes);

            // Shortcut: if flushed after every write, no need to address segments
            if (_segments.Count == 0)
            {
                if (!_currentSegment.IsEmpty)
                    await _responseStream.WriteAsync(_currentSegment.Used[startOffset..], cancellationToken);
                if (_currentSegment.IsAllocated)
                    _currentSegment = _currentSegment with { Used = Memory<byte>.Empty };
                _unflushedBytes = 0;
                _responseStream.Flush();
                return new FlushResult(false, false);
            }
            return await FlushAllSegmentsAsync(startOffset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _isCompleted = true;
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _onResponseStartingCallback = null;
            throw;
        }
        catch (Exception)
        {
            _isCompleted = true;
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _onResponseStartingCallback = null;
            throw;
        }
    }

    private async Task<FlushResult> FlushAllSegmentsAsync(int startOffset, CancellationToken localToken)
    {
        // First segment handled with offset.
        var memory = _segments[0];
        Debug.Assert(!memory.IsEmpty);
        await _responseStream.WriteAsync(memory.Used[startOffset..], localToken);
        _memoryPool.Return(memory.Reference, true);

        // Remaining segments.
        for (int i = 1; i < _segments.Count; i++)
        {
            memory = _segments[i];
            if (!memory.IsEmpty)
                await _responseStream.WriteAsync(memory.Used, localToken);
            _memoryPool.Return(memory.Reference, true);
        }

        // Last segment (the _currentSegment is not returned to the memory pool.
        if (!_currentSegment.IsEmpty)
            await _responseStream.WriteAsync(_currentSegment.Used, localToken);
        _currentSegment = _currentSegment with { Used = Memory<byte>.Empty };

        _segments.Clear();
        _unflushedBytes = 0;
        _responseStream.Flush();
        return new FlushResult(isCanceled: false, isCompleted: false);
    }

    public void Flush()
    {
        if (_unflushedBytes == 0)
            return;

        if (_onResponseStartingCallback != null)
        {
            _onResponseStartingCallback.Invoke(CancellationToken.None).GetAwaiter().GetResult();
            _onResponseStartingCallback = null;
        }

        var startOffset = PrepareFrameHeader(_unflushedBytes);

        // Shortcut: if flushed after every write, no need to address segments
        if (_segments.Count == 0)
        {
            if (!_currentSegment.IsEmpty)
                _responseStream.Write(_currentSegment.Used.Span[startOffset..]);
            if (_currentSegment.IsAllocated)
                _currentSegment = _currentSegment with { Used = Memory<byte>.Empty };
            _unflushedBytes = 0;
            _responseStream.Flush();
            return;
        }

        FlushAllSegments(startOffset);
    }

    private void FlushAllSegments(int startOffset)
    {
        // First segment handled with offset.
        var source = CollectionsMarshal.AsSpan(_segments);
        ref var initialMemory = ref source[0];
        Debug.Assert(!initialMemory.IsEmpty);
        _responseStream.Write(initialMemory.Used.Span[startOffset..]);
        _memoryPool.Return(initialMemory.Reference, true);

        // Remaining segments.
        for (int i = 1; i < _segments.Count; i++)
        {
            ref var memory = ref source[i];
            if (memory.Used.Length > 0)
                _responseStream.Write(memory.Used.Span);
            _memoryPool.Return(memory.Reference, true);
        }

        // Last segment (the _currentSegment is not returned to the memory pool.
        if (!_currentSegment.IsEmpty)
            _responseStream.Write(_currentSegment.Used.Span);
        _currentSegment = _currentSegment with { Used = Memory<byte>.Empty };

        _segments.Clear();
        _unflushedBytes = 0;
        _responseStream.Flush();
    }

    /// <summary>
    /// Write the frame header into the local <paramref cref="buffer"/>
    /// DATA/HEADER Frame {
    /// Type(i) = 0x00,
    ///   Length(i),
    ///   Data(..),
    /// }
    /// </summary>
    /// <param name="length">The length of the DATA frame payload.</param>
    /// <returns>The length of the frame header in bytes.</returns>
    private static int PrepareFrameHeader(Span<byte> buffer, long length, byte frameType)
    {
        var success = VariableLenghtIntegerDecoder.TryWrite(buffer[1..], length, out var writtenCount);
        Debug.Assert(success);
        buffer[0] = frameType;
        return writtenCount + 1;
    }

    /// <summary>
    /// Write the frame header into the currnet segment.
    /// DATA/HEADER Frame {
    /// Type(i) = 0x00,
    ///   Length(i),
    ///   Data(..),
    /// }
    /// </summary>
    /// <param name="length">The length of the DATA frame payload.</param>
    /// <returns>The length of the frame header in bytes.</returns>
    private int PrepareFrameHeader(long length)
    {
        // Shortcut: if flushed after every write, no need to address segments
        Segment head;
        if (_segments.Count == 0)
            head = _currentSegment;
        else
            head = _segments[0];

        Span<byte> buffer = head.Used.Span[0..MaxFrameHeaderLength];
        var success = VariableLenghtIntegerDecoder.TryWriteEndAligned(buffer, length, out var writtenCount);
        Debug.Assert(success);
        buffer[^(writtenCount + 1)] = _frameType;
        return MaxFrameHeaderLength - (writtenCount + 1);
    }

    private void ClearSegments(Span<Segment> source, bool clearCurrent = true)
    {
        for (int i = 0; i < source.Length; i++)
        {
            ref var memory = ref source[i];
            if (memory.IsAllocated)
                _memoryPool.Return(memory.Reference, true);
        }
        _unflushedBytes = 0;
        _segments.Clear();
        if (clearCurrent)
        {
            if (_currentSegment.IsAllocated)
                _memoryPool.Return(_currentSegment.Reference, true);
            _currentSegment = new Segment();
        }
        else
        {
            if (_currentSegment.IsAllocated)
                _currentSegment = _currentSegment with { Used = Memory<byte>.Empty };
        }
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
            throw new InvalidOperationException("PipeWriter already completed");
    }
}