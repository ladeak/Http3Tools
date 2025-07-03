using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.Extensions.ObjectPool;

namespace CHttpServer;

internal class PriorityResponseWriter : IResponseWriter
{
    private class PrioritySchedule
    {
        private readonly Queue<StreamWriteRequest> _queue;
        private readonly HashSet<uint> _scheduledDataStreams;
        private readonly Lock _lock;

        public PrioritySchedule(int maxConcurrentStreams)
        {
            _queue = new Queue<StreamWriteRequest>(maxConcurrentStreams);
            _scheduledDataStreams = new HashSet<uint>(maxConcurrentStreams);
            _lock = new Lock();
        }

        public void Enqueue(StreamWriteRequest request)
        {
            lock (_lock)
            {
                if (request.OperationName == WriteData)
                {
                    if (_scheduledDataStreams.Add(request.Stream.StreamId))
                        _queue.Enqueue(request);
                }
                else
                {
                    _queue.Enqueue(request);
                }
            }
        }

        public bool TryDequeue([MaybeNullWhen(false)] out StreamWriteRequest request)
        {
            lock (_lock)
            {
                var hasItem = _queue.TryDequeue(out request);
                if (hasItem && request!.OperationName == WriteData)
                    _scheduledDataStreams.Remove(request.Stream.StreamId);
                return hasItem;
            }
        }

        public void Clear()
        {
            // Should not be invoked in race condition.
            lock (_lock)
            {
                _queue.Clear();
                _scheduledDataStreams.Clear();
            }
        }

        public int Capacity => Math.Max(_scheduledDataStreams.Capacity, _queue.Capacity);
    }

    private class PrioritySchedulePolicy : IPooledObjectPolicy<PrioritySchedule>
    {
        public PrioritySchedule Create() => new PrioritySchedule(Http2Connection.DefaultServerMaxConcurrentStream);

        public bool Return(PrioritySchedule obj)
        {
            if (obj.Capacity > Http2Connection.DefaultServerMaxConcurrentStream * 2)
                return false;
            obj.Clear();
            return true;
        }
    }

    private const string WriteHeaders = nameof(WriteHeaders);
    private const string WriteData = nameof(WriteData);
    private const string WriteEndStream = nameof(WriteEndStream);
    private const string WritePingFrame = nameof(WritePingFrame);
    private const string WriteTrailers = nameof(WriteTrailers);
    private const string WriteWindowUpdate = nameof(WriteWindowUpdate);
    private const int PriortyLevels = 16;
    private const int MaxDataFrames = 6;

    /// <summary>
    /// Each entry corresponds to a level of priority (urgency and incremental)
    /// The beginning of the collection corresponds to higher priority.
    /// </summary>
    private readonly InlineArray16<PrioritySchedule> _queues;
    private static readonly ObjectPool<PrioritySchedule> _scheduleLevelPools;

    private readonly FrameWriter _frameWriter;
    private DynamicHPackEncoder? _hpackEncoder;
    private int _maxFrameSize;
    private byte[] _buffer;
    private volatile bool _isCompleted;
    private ManualResetValueTaskSource<bool> _semaphore;

    static PriorityResponseWriter()
    {
        var poolProvider = new DefaultObjectPoolProvider();
        _scheduleLevelPools = poolProvider.Create(new PrioritySchedulePolicy());
    }

    public PriorityResponseWriter(FrameWriter writer, uint maxFrameSize, uint maxConcurrentStreams)
    {
        _frameWriter = writer;
        _maxFrameSize = (int)maxFrameSize;
        _hpackEncoder = new DynamicHPackEncoder();
        _buffer = [];
        for (int i = 0; i < PriortyLevels; i++)
            _queues[i] = _scheduleLevelPools.Get();
        _semaphore = new();
    }

    public void Complete() => _isCompleted = true;

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            _hpackEncoder = new();
            _buffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
            while (!_isCompleted && !token.IsCancellationRequested)
            {
                await WriteAllLevels(null);
            }
        }
        catch (OperationCanceledException)
        {
            // Channel is closed by the connection.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_buffer);

            // Return schedule levels to the pool
            for (int i = 0; i < PriortyLevels; i++)
                _scheduleLevelPools.Return(_queues[i]);
        }
    }

    private async Task WriteAllLevels(object? p)
    {
        bool allLevelsEmpty = true;
        _semaphore.Reset();
        for (int i = 0; i < PriortyLevels; i++)
        {
            var queueLevels = _queues[i];
            var isEmpty = await WriteLevel(queueLevels);
            allLevelsEmpty &= isEmpty;

            // If a level had no items, handle the next priority level.
            // Otherwise, the current level is handle, restart with the
            // highest priority level.
            if (!isEmpty)
                break;
        }
        if (allLevelsEmpty)
            await new ValueTask(_semaphore, _semaphore.Version);
    }

    private async Task<bool> WriteLevel(PrioritySchedule queueLevels)
    {
        bool isEmpty = true;
        while (queueLevels.TryDequeue(out var request))
        {
            isEmpty = false;
            if (request.OperationName == WriteData)
            {
                // If writes are preempted, requeue data writes for the stream.
                if (!await WriteDataAsync(request))
                    queueLevels.Enqueue(request);
            }
            else if (request.OperationName == WriteHeaders)
                await WriteHeadersAsync(request.Stream);
            else if (request.OperationName == WriteEndStream)
                await WriteEndStreamAsync(request.Stream);
            else if (request.OperationName == WriteTrailers)
                await WriteTrailersAsync(request.Stream);
            else if (request.OperationName == WriteWindowUpdate)
                await WriteWindowUpdateAsync(request.Stream, request.Data);
            if (request.OperationName == WritePingFrame)
                await WritePingAckAsync(request.Data);
        }
        return isEmpty;
    }

    public void ScheduleEndStream(Http2Stream source)
    {
        if (_isCompleted)
            return;
        var level = GetLevel(source);
        _queues[level].Enqueue(new StreamWriteRequest(source, WriteEndStream));
        _semaphore.TrySetResult(true);
    }

    public void ScheduleWriteData(Http2Stream source)
    {
        if (_isCompleted)
            return;
        var level = GetLevel(source);
        _queues[level].Enqueue(new StreamWriteRequest(source, WriteData));
        _semaphore.TrySetResult(true);
    }

    public void ScheduleWriteHeaders(Http2Stream source)
    {
        if (_isCompleted)
            return;
        var level = GetLevel(source);
        _queues[level].Enqueue(new StreamWriteRequest(source, WriteHeaders));
        _semaphore.TrySetResult(true);
    }

    public void ScheduleWritePingAck(ulong value)
    {
        if (_isCompleted)
            return;
        _queues[0].Enqueue(new StreamWriteRequest(null!, WritePingFrame, value));
        _semaphore.TrySetResult(true);
    }

    public void ScheduleWriteTrailers(Http2Stream source)
    {
        if (_isCompleted)
            return;
        var level = GetLevel(source);
        _queues[level].Enqueue(new StreamWriteRequest(source, WriteTrailers));
        _semaphore.TrySetResult(true);
    }

    public void ScheduleWriteWindowUpdate(Http2Stream source, uint size)
    {
        if (_isCompleted)
            return;
        var level = GetLevel(source);
        _queues[level].Enqueue(new StreamWriteRequest(source, WriteWindowUpdate, size));
        _semaphore.TrySetResult(true);
    }

    public void UpdateFrameSize(uint size)
    {
        if (size < 16384 || size > 16777215)
            throw new Http2ProtocolException(); // Invalid frame size
        _maxFrameSize = (int)size;
    }

    private static int GetLevel(Http2Stream source)
    {
        var priorty = source.Priority;
        var level = priorty.Urgency;
        if (source.Priority.Incremental)
            return level;
        return level + 1;
    }

    private async ValueTask<bool> WriteDataAsync(StreamWriteRequest writeRequest)
    {
        var stream = writeRequest.Stream;
        var responseContent = stream.ResponseBodyBuffer;
        var totalFramesWritten = 0;
        do
        {
            var maxFrameSize = _maxFrameSize; // Capture to avoid changing during data writes.
            var currentSize = responseContent.Length > maxFrameSize ? maxFrameSize : responseContent.Length;
            _frameWriter.WriteData(stream.StreamId, responseContent.Slice(0, currentSize));
            await _frameWriter.FlushAsync();
            responseContent = responseContent.Slice(currentSize);
            totalFramesWritten++;
        } while (!responseContent.IsEmpty || totalFramesWritten <= MaxDataFrames);
        stream.OnResponseDataFlushed();
        return responseContent.IsEmpty;
    }

    private async Task WriteEndStreamAsync(Http2Stream stream)
    {
        _frameWriter.WriteEndStream(stream.StreamId);
        await _frameWriter.FlushAsync();
        await stream.OnStreamCompletedAsync();
    }

    private async ValueTask WriteHeadersAsync(Http2Stream stream)
    {
        var currentMaxFrameSize = _maxFrameSize;
        var encodingBuffer = _buffer.AsSpan(0, currentMaxFrameSize);
        HPackEncoder.EncodeStatusHeader(stream.StatusCode, encodingBuffer, out var writtenLength);
        int totalLength = writtenLength;
        foreach (var header in stream.ResponseHeaders)
        {
            var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

            // This is stateful
            while (!_hpackEncoder.EncodeHeader(encodingBuffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                header.Key, header.Value.ToString(), Encoding.Latin1, out writtenLength))
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(encodingBuffer.Length * 2);
                encodingBuffer.CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
                encodingBuffer = _buffer.AsSpan();
            }
            totalLength += writtenLength;
        }

        // Fast path when all headers fit in a single frame.
        var flushingBuffer = _buffer.AsMemory(0, totalLength);
        if (flushingBuffer.Length <= currentMaxFrameSize)
        {
            _frameWriter.WriteHeader(stream.StreamId, flushingBuffer, endHeaders: true, endStream: false);
            await _frameWriter.FlushAsync();
            return;
        }

        // Slow path for large headers, write a single HEADER frame followed by CONTINUATION frames.
        _frameWriter.WriteHeader(stream.StreamId, flushingBuffer.Slice(0, currentMaxFrameSize), endHeaders: false, endStream: false);
        await _frameWriter.FlushAsync();
        flushingBuffer = flushingBuffer.Slice(currentMaxFrameSize);

        while (flushingBuffer.Length > currentMaxFrameSize)
        {
            _frameWriter.WriteContinuation(stream.StreamId, flushingBuffer.Slice(0, currentMaxFrameSize), endHeaders: false, endStream: false);
            await _frameWriter.FlushAsync();
            flushingBuffer = flushingBuffer.Slice(currentMaxFrameSize);
        }

        // Flush the remaining part and set endHeaders.
        _frameWriter.WriteContinuation(stream.StreamId, flushingBuffer, endHeaders: true, endStream: false);
        await _frameWriter.FlushAsync();

        if (totalLength > currentMaxFrameSize)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
        }
    }

    private async ValueTask WritePingAckAsync(ulong data)
    {
        _frameWriter.WritePingAck(data);
        await _frameWriter.FlushAsync();
    }

    private async Task WriteTrailersAsync(Http2Stream stream)
    {
        var buffer = _buffer.AsSpan(0, _maxFrameSize);
        int totalLength = 0;
        foreach (var header in stream.Trailers)
        {
            var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

            // This is stateful
            if (!_hpackEncoder.EncodeHeader(buffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                header.Key, header.Value.ToString(), Encoding.Latin1, out var writtenLength))
                throw new InvalidOperationException("Trailer too large");
            totalLength += writtenLength;
        }

        _frameWriter.WriteHeader(stream.StreamId, _buffer.AsMemory(0, totalLength), endHeaders: true, endStream: true);
        await _frameWriter.FlushAsync();
        await stream.OnStreamCompletedAsync();
    }

    private async ValueTask WriteWindowUpdateAsync(Http2Stream stream, ulong data)
    {
        var size = (uint)data;
        _frameWriter.WriteWindowUpdate(stream.StreamId, size);
        _frameWriter.WriteWindowUpdate(0, size);
        await _frameWriter.FlushAsync();
    }

    private HeaderEncodingHint GetHeaderEncodingHint(int headerIndex)
    {
        return headerIndex switch
        {
            55 => HeaderEncodingHint.NeverIndex, // SetCookie
            25 => HeaderEncodingHint.NeverIndex, // Content-Disposition
            28 => HeaderEncodingHint.IgnoreIndex, // Content-Length
            _ => HeaderEncodingHint.Index
        };
    }

}