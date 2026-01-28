using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace CHttpServer.Http3;

internal class Http3FramingStreamWriter(Stream responseStream, byte frameType, ArrayPool<byte>? memoryPool = null, Func<CancellationToken, Task>? onResponseStartingCallback = null) : PipeWriter
{
    private readonly struct Segment()
    {
        public static Segment Empty { get; } = new Segment();

        public byte[] Reference { get; init; } = [];
        public Memory<byte> Used { get; init; } = Memory<byte>.Empty;

        public bool IsEmpty => Used.IsEmpty;

        public bool IsAllocated => Reference.Length > 0;
    }

    private readonly byte[] _buffer = new byte[9];
    private readonly Lock _lockObject = new();
    private readonly List<Segment> _segments = new List<Segment>(128);
    private readonly ArrayPool<byte> _memoryPool = memoryPool ?? ArrayPool<byte>.Shared;
    private Stream _responseStream = responseStream;
    private readonly byte _frameType = frameType;
    private CancellationTokenSource? _cts;
    private bool _isCompleted = false;
    private long _unflushedBytes;
    private Segment _currentSegment = Segment.Empty;
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<CancellationToken, Task>? _onResponseStartingCallback = onResponseStartingCallback;

    public override long UnflushedBytes => _unflushedBytes;

    public override bool CanGetUnflushedBytes => true;

    public Task Completion => _tcs.Task;

    private CancellationTokenSource InternalCancellation
    {
        get
        {
            lock (_lockObject)
            {
                return _cts ??= new CancellationTokenSource();
            }
        }
    }

    public void Reset(Stream responseStream, Func<CancellationToken, Task>? onResponseStartingCallback = null)
    {
        _responseStream = responseStream;
        ClearSegments(CollectionsMarshal.AsSpan(_segments));
        lock (_lockObject)
        {
            _cts = null;
        }
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    public override void CancelPendingFlush() => InternalCancellation.Cancel();

    public override void Complete(Exception? exception = null)
    {
        if (_isCompleted)
            return;
        _isCompleted = true;
        try
        {
            Flush();
        }
        finally
        {
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _tcs.TrySetResult();
            _cts?.Dispose();
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
            await FlushAsync();
        }
        finally
        {
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            _tcs.TrySetResult();
            _cts?.Dispose();
            _onResponseStartingCallback = null;
        }
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfCompleted();
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeHint, 0);
        var current = _currentSegment;
        var available = current.Reference.Length - current.Used.Length;
        if (sizeHint <= available)
            return current.Reference.AsMemory(current.Used.Length);

        // Not enough memory left in the current segment.
        if (!current.IsEmpty)
            _segments.Add(current);
        else if (current.IsAllocated)
            _memoryPool.Return(current.Reference, true);

        _currentSegment = new Segment() { Reference = _memoryPool.Rent(sizeHint) }; // Minimum size left for ArrayPool.
        return _currentSegment.Reference.AsMemory();
    }

    public override Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        if (source.Length == 0)
            return new FlushResult(false, false);
        var frameHeaderLength = PrepareFrameHeader(source.Length);
        try
        {
            if (_onResponseStartingCallback != null)
            {
                await _onResponseStartingCallback.Invoke(cancellationToken);
                _onResponseStartingCallback = null;
            }
            await _responseStream.WriteAsync(_buffer.AsMemory(0, frameHeaderLength), cancellationToken);
            await _responseStream.WriteAsync(source, cancellationToken);
            await _responseStream.FlushAsync(cancellationToken);
            return new FlushResult(isCanceled: false, isCompleted: false);
        }
        catch (Exception ex)
        {
            _isCompleted = true;
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            lock (_lockObject)
            {
                _cts = null;
            }
            _tcs.SetException(ex);
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
            CancellationToken localToken = InternalCancellation.Token;
            if (localToken.IsCancellationRequested)
            {
                lock (_lockObject)
                {
                    _cts = null;
                }
                return new FlushResult(true, false);
            }
            using var _ = cancellationToken.Register(static (object? state) => { ((Http3FramingStreamWriter)state!).InternalCancellation.Cancel(); }, this);

            if (_onResponseStartingCallback != null)
            {
                await _onResponseStartingCallback.Invoke(localToken);
                _onResponseStartingCallback = null;
            }
            var dataFrameHeaderLength = PrepareFrameHeader(_unflushedBytes);
            await _responseStream.WriteAsync(_buffer.AsMemory(0, dataFrameHeaderLength), localToken);

            // Shortcut: if flushed after every write, no need to address segments
            if (_segments.Count == 0)
            {
                if (!_currentSegment.IsEmpty)
                    await _responseStream.WriteAsync(_currentSegment.Used, localToken);
                if (_currentSegment.IsAllocated)
                    _memoryPool.Return(_currentSegment.Reference, true);
                _currentSegment = Segment.Empty;
                _unflushedBytes = 0;
                await _responseStream.FlushAsync();
                return new FlushResult(false, false);
            }

            return await FlushAllSegmentsAsync(localToken);
        }
        catch (OperationCanceledException)
        {
            _isCompleted = true;
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            lock (_lockObject)
            {
                _cts = null;
            }
            _tcs.SetCanceled();
            _onResponseStartingCallback = null;
            if (!cancellationToken.IsCancellationRequested)
                return new FlushResult(isCanceled: true, isCompleted: false);
            throw;
        }
        catch (Exception ex)
        {
            _isCompleted = true;
            ClearSegments(CollectionsMarshal.AsSpan(_segments));
            lock (_lockObject)
            {
                _cts = null;
            }
            _onResponseStartingCallback = null;
            _tcs.SetException(ex);
            throw;
        }
    }

    private async Task<FlushResult> FlushAllSegmentsAsync(CancellationToken localToken)
    {
        if (!_currentSegment.IsEmpty)
            _segments.Add(_currentSegment);
        else if (_currentSegment.IsAllocated)
            _memoryPool.Return(_currentSegment.Reference, true);
        _currentSegment = Segment.Empty;

        for (int i = 0; i < _segments.Count; i++)
        {
            var memory = _segments[i];
            if (!memory.IsEmpty)
                await _responseStream.WriteAsync(memory.Used, localToken);
            _memoryPool.Return(memory.Reference, true);
        }
        _segments.Clear();
        _unflushedBytes = 0;
        await _responseStream.FlushAsync(localToken);
        return new FlushResult(isCanceled: false, isCompleted: false);
    }

    private void Flush()
    {
        if (_unflushedBytes == 0)
            return;

        if (_onResponseStartingCallback != null)
        {
            _onResponseStartingCallback.Invoke(CancellationToken.None).GetAwaiter().GetResult();
            _onResponseStartingCallback = null;
        }
        var dataFrameHeaderLength = PrepareFrameHeader(_unflushedBytes);
        _responseStream.Write(_buffer.AsSpan(0, dataFrameHeaderLength));

        // Shortcut: if flushed after every write, no need to address segments
        if (_segments.Count == 0)
        {
            if (!_currentSegment.IsEmpty)
                _responseStream.Write(_currentSegment.Used.Span);
            if (_currentSegment.IsAllocated)
                _memoryPool.Return(_currentSegment.Reference, true);
            _currentSegment = Segment.Empty;
            _unflushedBytes = 0;
            _responseStream.Flush();
            return;
        }

        FlushAllSegments();
    }

    private void FlushAllSegments()
    {
        if (!_currentSegment.IsEmpty)
            _segments.Add(_currentSegment);
        else if (_currentSegment.IsAllocated)
            _memoryPool.Return(_currentSegment.Reference, true);
        _currentSegment = Segment.Empty;

        var source = CollectionsMarshal.AsSpan(_segments);
        for (int i = 0; i < _segments.Count; i++)
        {
            ref var memory = ref source[i];
            if (memory.Used.Length > 0)
                _responseStream.Write(memory.Used.Span);
            _memoryPool.Return(memory.Reference, true);
        }
        _segments.Clear();
        _unflushedBytes = 0;
        _responseStream.Flush();
    }

    /// <summary>
    /// Write the frame header into the local <see cref="_buffer"/>
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
        _buffer[0] = _frameType;
        var success = VariableLenghtIntegerDecoder.TryWrite(_buffer.AsSpan(1), length, out var writtenCount);
        Debug.Assert(success);
        return writtenCount + 1;
    }

    private void ClearSegments(Span<Segment> source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            ref var memory = ref source[i];
            if (memory.Reference.Length != 0)
                _memoryPool.Return(memory.Reference, true);
        }
        _unflushedBytes = 0;
        _segments.Clear();
        if (_currentSegment.IsAllocated)
            _memoryPool.Return(_currentSegment.Reference);
        _currentSegment = Segment.Empty;
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
            throw new InvalidOperationException("PipeWriter already completed");
    }
}