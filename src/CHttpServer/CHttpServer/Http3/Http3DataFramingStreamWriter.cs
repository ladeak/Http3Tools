using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace CHttpServer.Http3;

internal class Http3DataFramingStreamWriter(Stream responseStream, ArrayPool<byte>? memoryPool = null, Func<CancellationToken, Task>? onResponseStartingCallback = null) : PipeWriter
{
    private readonly struct Segment()
    {
        public static Segment Empty { get; } = new Segment();

        public byte[] Reference { get; init; } = [];
        public Memory<byte> Used { get; init; } = Memory<byte>.Empty;
    }

    private readonly byte[] _buffer = new byte[9];
    private readonly Lock _lockObject = new();
    private readonly List<Segment> _segments = new List<Segment>(128) { new Segment() };
    private readonly ArrayPool<byte> _memoryPool = memoryPool ?? ArrayPool<byte>.Shared;
    private Stream _responseStream = responseStream;
    private CancellationTokenSource? _cts;
    private bool _isCompleted = false;
    private long _unflushedBytes;
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
        var current = _segments[^1];
        var usedLength = current.Used.Length;
        var available = current.Reference.Length - usedLength;
        ArgumentOutOfRangeException.ThrowIfLessThan(available, bytes);
        current = current with { Used = current.Reference.AsMemory(0, usedLength + bytes) };
        _segments[^1] = current;
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
        var current = _segments[^1];
        var available = current.Reference.Length - current.Used.Length;
        if (sizeHint <= available)
            return current.Reference.AsMemory(current.Used.Length);

        var segment = new Segment() { Reference = _memoryPool.Rent(sizeHint) }; // Minimum size left for ArrayPool.

        // If it is unused segment, but too small, return the currently rented array and replace the segment.
        if (current.Used.Length == 0)
        {
            if (current.Reference.Length != 0)
                _memoryPool.Return(current.Reference, true);
            _segments[^1] = segment;
        }
        else
            _segments.Add(segment);
        return segment.Reference.AsMemory();
    }

    public override Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        if (source.Length == 0)
            return new FlushResult(false, false);
        var frameHeaderLength = PrepareDataFrameHeader(source.Length);
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
            using var _ = cancellationToken.Register(static (object? state) => { ((Http3DataFramingStreamWriter)state!).InternalCancellation.Cancel(); }, this);

            if (_onResponseStartingCallback != null)
            {
                await _onResponseStartingCallback.Invoke(localToken);
                _onResponseStartingCallback = null;
            }
            var dataFrameHeaderLength = PrepareDataFrameHeader(_unflushedBytes);
            await _responseStream.WriteAsync(_buffer.AsMemory(0, dataFrameHeaderLength), localToken);

            int i = 0;
            var emptySegment = Segment.Empty;
            for (; i < _segments.Count; i++)
            {
                var memory = _segments[i];
                if (memory.Reference.Length == 0)
                    break;
                if (memory.Used.Length > 0)
                    await _responseStream.WriteAsync(memory.Used, localToken);
                _memoryPool.Return(memory.Reference, true);
                _segments[i] = emptySegment;
            }
            _unflushedBytes = 0;
            await _responseStream.FlushAsync(localToken);
            return new FlushResult(isCanceled: false, isCompleted: false);
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

    private void Flush()
    {
        if (_unflushedBytes == 0)
            return;

        if (_onResponseStartingCallback != null)
        {
            _onResponseStartingCallback.Invoke(CancellationToken.None).GetAwaiter().GetResult();
            _onResponseStartingCallback = null;
        }
        var dataFrameHeaderLength = PrepareDataFrameHeader(_unflushedBytes);
        _responseStream.Write(_buffer.AsSpan(0, dataFrameHeaderLength));
        var source = CollectionsMarshal.AsSpan(_segments);
        int i = 0;
        var emptySegment = Segment.Empty;
        for (; i < _segments.Count; i++)
        {
            ref var memory = ref source[i];
            if (memory.Reference.Length == 0)
                break;
            if (memory.Used.Length > 0)
                _responseStream.Write(memory.Used.Span);
            _memoryPool.Return(memory.Reference, true);
            source[i] = emptySegment;
        }
        _unflushedBytes = 0;
        _responseStream.Flush();
    }

    /// <summary>
    /// Write the DATA frame header into the local <see cref="_buffer"/>
    /// DATA Frame {
    /// Type(i) = 0x00,
    ///   Length(i),
    ///   Data(..),
    /// }
    /// </summary>
    /// <param name="length">The length of the DATA frame payload.</param>
    /// <returns>The length of the frame header in bytes.</returns>
    private int PrepareDataFrameHeader(long length)
    {
        _buffer[0] = 0;
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
        _segments.Add(Segment.Empty);
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
            throw new InvalidOperationException("PipeWriter already completed");
    }
}