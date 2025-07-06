using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Headers;

namespace CHttpServer;

internal partial class Http2Stream
{
    private enum StreamState : byte
    {
        Open = 0,
        HalfOpenLocal = 1,
        HalfOpenRemote = 2,
        Closed = 4,
    }

    private readonly Http2Connection _connection;
    private readonly IResponseWriter _writer;
    private readonly FeatureCollection _featureCollection;
    private readonly bool _usePriority;
    private FlowControlSize _serverWindowSize; // Controls Data received
    private FlowControlSize _clientWindowSize; // Controls Data sent
    private StreamState _state;
    private CancellationTokenSource _cts;

    public Http2Stream(Http2Connection connection, FeatureCollection featureCollection)
    {
        _connection = connection;
        _writer = connection.ResponseWriter!;
        _state = StreamState.Closed;
        RequestEndHeaders = false;
        _requestHeaders = new HeaderCollection();
        _requestBodyPipe = new(new PipeOptions(MemoryPool<byte>.Shared));
        _requestBodyPipeReader = new(_requestBodyPipe.Reader, ReleaseServerFlowControl);
        _requestBodyPipeWriter = new(_requestBodyPipe.Writer, flushStartingCallback: ConsumeServerFlowControl, flushedCallback: null);

        _responseHeaders = null;
        _responseBodyPipe = new(new PipeOptions(MemoryPool<byte>.Shared));
        _responseBodyPipeWriter = new(_responseBodyPipe.Writer, flushStartingCallback: size =>
        {
            if (!_hasStarted)
                _ = StartAsync();
        });
        _cts = new();
        StatusCode = 200;
        _responseWriterFlushedResponse = new ManualResetValueTaskSource<bool>() { RunContinuationsAsynchronously = true };
        _clientFlowControlBarrier = new ManualResetValueTaskSource<bool>() { RunContinuationsAsynchronously = true };
        _responseWritingTask = new TaskCompletionSource<Task<bool>>();

        _featureCollection = featureCollection.Copy();
        _featureCollection.Add<IHttpRequestFeature>(this);
        _featureCollection.Add<IHttpResponseFeature>(this);
        _featureCollection.Add<IHttpResponseBodyFeature>(this);
        _featureCollection.Add<IHttpResponseTrailersFeature>(this);
        _featureCollection.Add<IHttpRequestBodyDetectionFeature>(this);
        _featureCollection.Add<IHttpRequestLifetimeFeature>(this);
        _featureCollection.Add<IPriority9218Feature>(this);
        _featureCollection.Checkpoint();

        _usePriority = _connection.ServerOptions.UsePriority;
        Priority = Priority9218.Default;
    }

    public void Initialize(uint streamId, uint initialWindowSize, uint serverStreamFlowControlSize)
    {
        if (_state != StreamState.Closed)
            throw new InvalidOperationException("Stream is in use.");
        _state = StreamState.Open;
        StreamId = streamId;
        _clientWindowSize = new(initialWindowSize);
        _serverWindowSize = new(serverStreamFlowControlSize);

        // HasStarted is reset at initialization to avoid race conditions Complete() and StartAsync()
        _hasStarted = false;
    }

    public void Reset()
    {
        if (_state != StreamState.Closed)
            throw new InvalidOperationException("Stream is in use.");
        StreamId = 0;
        RequestEndHeaders = false;
        _requestHeaders = new();
        _requestBodyPipe.Reset();
        _requestBodyPipeReader.Reset();
        _requestBodyPipeWriter.Reset();

        _responseBodyPipe.Reset();
        _responseBodyPipeWriter.Reset();

        _cts = new();
        _responseHeaders = null;
        _responseTrailers = null;
        StatusCode = 200;
        ReasonPhrase = null;
        Scheme = string.Empty;
        Method = string.Empty;
        PathBase = string.Empty;
        Path = string.Empty;
        QueryString = string.Empty;
        _onStartingCallback = null;
        _onStartingState = null;
        _onCompletedCallback = null;
        _onCompletedState = null;
        _responseWritingTask = new TaskCompletionSource<Task<bool>>();

        _clientFlowControlBarrier.Reset();
        _responseWriterFlushedResponse.Reset();
        _featureCollection.ResetCheckpoint();
        Priority = Priority9218.Default;
    }

    public uint StreamId { get; private set; }

    public bool RequestEndHeaders { get; private set; }

    internal void RequestEndHeadersReceived() => RequestEndHeaders = true;

    internal void SetStaticHeader(HeaderField header, Http2Connection.PseudoHeaderFields pseudoHeader)
    {
        switch (pseudoHeader)
        {
            case Http2Connection.PseudoHeaderFields.Method:
                Method = HttpStaticFieldParser.GetMethod(header.Value);
                break;
            case Http2Connection.PseudoHeaderFields.Scheme:
                Scheme = HttpStaticFieldParser.GetScheme(header.Value);
                break;
        }
    }

    internal void SetStaticHeader(HeaderField header, Http2Connection.PseudoHeaderFields pseudoHeader, ReadOnlySpan<byte> value)
    {
        switch (pseudoHeader)
        {
            case Http2Connection.PseudoHeaderFields.Authority:
                _requestHeaders.Add("Host", value.ToArray());
                break;
            case Http2Connection.PseudoHeaderFields.Path:
                SetPath(value);
                break;
            case Http2Connection.PseudoHeaderFields.None:

                switch (header.StaticTableIndex)
                {
                    case H2StaticTable.ContentType:
                        _requestHeaders.Add(Microsoft.Net.Http.Headers.HeaderNames.ContentType, value);
                        break;
                    case H2StaticTable.Accept:
                        _requestHeaders.Add(Microsoft.Net.Http.Headers.HeaderNames.Accept, value);
                        break;
                }
                ;
                break;
        }
    }

    internal void SetHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        var (key, values) = RequestHeaders.Add(name, value);
        if (_usePriority && key == "priority")
        {
            Priority9218.TryParse(values, out var priority);
            Priority = priority;
        }
    }

    private const byte QuestionMarkByte = (byte)'?';

    private void SetPath(ReadOnlySpan<byte> value)
    {
        var separatorIndex = value.IndexOf(QuestionMarkByte);
        if (separatorIndex < 0)
        {
            Path = Encoding.Latin1.GetString(value);
            return;
        }

        Path = Encoding.Latin1.GetString(value[0..separatorIndex]);
        QueryString = Encoding.Latin1.GetString(value[separatorIndex..]);
    }

    public async void Execute<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        try
        {
            _requestHeaders.SetReadOnly();
            var context = application.CreateContext(_featureCollection);
            await application.ProcessRequestAsync(context);
        }
        catch (Exception)
        {
            Abort();
        }
        _requestBodyPipeReader.Complete();
        _requestBodyPipeWriter.Complete();
        _responseBodyPipeWriter.Complete();
        await CompleteAsync();
    }

    public void Abort()
    {
        _cts.Cancel();
        _state = StreamState.Closed;
        IsAborted = true;
    }

    public void CompleteRequestStream()
    {
        _requestBodyPipeWriter.Complete();
        _state = StreamState.HalfOpenRemote;

        // Reader maybe cancelled or completed, in which the data written to the pipe
        // has been already counted by the request sender. To make sure flowcontrol matches
        // the client, WindowUpdates is written for the remaining unflushed bytes.
        if (_requestBodyPipeWriter.UnflushedBytes > 0)
        {
            ReleaseServerFlowControl((checked((int)_requestBodyPipeWriter.UnflushedBytes)));
        }
    }

    private void ReleaseServerFlowControl(int size)
    {
        if (size > Http2Connection.MaxWindowUpdateSize || size < 0)
            throw new Http2FlowControlException();
        if (size == 0)
            return;
        uint windowSize = (uint)size;
        _serverWindowSize.ReleaseSize(windowSize);

        // Release Read FlowControl Window for the stream and the connection.
        _writer.ScheduleWriteWindowUpdate(this, windowSize);
    }

    private void ConsumeServerFlowControl(int size)
    {
        if (size > Http2Connection.MaxWindowUpdateSize || size < 0)
            throw new Http2FlowControlException();
        uint windowSize = (uint)size;
        _serverWindowSize.TryUse(windowSize);
    }
}

internal partial class Http2Stream : IHttpRequestFeature, IHttpRequestBodyDetectionFeature, IHttpRequestLifetimeFeature, IPriority9218Feature
{
    private HeaderCollection _requestHeaders;

    private Pipe _requestBodyPipe;
    private Http2StreamPipeReader _requestBodyPipeReader;
    private Http2StreamPipeWriter _requestBodyPipeWriter;

    public string Protocol { get => "HTTP/2"; set => throw new NotSupportedException(); }
    public string Scheme { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string RawTarget { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    IHeaderDictionary IHttpRequestFeature.Headers { get => _requestHeaders; set => throw new NotSupportedException(); }
    public Stream Body { get => _requestBodyPipeReader.AsStream(); set => throw new NotSupportedException(); }

    public bool CanHaveBody => true;

    public bool IsAborted { get; private set; }

    public PipeWriter RequestPipe => _state <= StreamState.HalfOpenLocal ?
        _requestBodyPipeWriter : throw new Http2ConnectionException("STREAM CLOSED");

    public CancellationToken RequestAborted { get => _cts.Token; set => throw new NotSupportedException(); }

    public HeaderCollection RequestHeaders { get => _requestHeaders; set => throw new NotSupportedException(); }

    public Priority9218 Priority { get; private set; }
}

internal partial class Http2Stream : IHttpResponseFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature
{
    private readonly Pipe _responseBodyPipe;
    private readonly Http2StreamPipeWriter _responseBodyPipeWriter;
    private ManualResetValueTaskSource<bool> _clientFlowControlBarrier;
    private ManualResetValueTaskSource<bool> _responseWriterFlushedResponse;

    private bool _hasStarted = false;
    private TaskCompletionSource<Task<bool>> _responseWritingTask;
    private HeaderCollection? _responseHeaders;
    private HeaderCollection? _responseTrailers;

    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }

    public bool HasStarted => _hasStarted;

    public Stream Stream => _responseBodyPipeWriter.AsStream();

    public PipeWriter Writer => _responseBodyPipeWriter;

    public ReadOnlySequence<byte> ResponseBodyBuffer { get; private set; }

    public IHeaderDictionary Trailers
    {
        get
        {
            _responseTrailers ??= new();
            return _responseTrailers;
        }
        set => throw new NotSupportedException();
    }

    IHeaderDictionary IHttpResponseFeature.Headers
    {
        get => ResponseHeaders;
        set => throw new NotSupportedException();
    }

    public HeaderCollection ResponseHeaders => _responseHeaders ??= new();

    public void DisableBuffering()
    {
        throw new NotImplementedException();
    }

    private Func<object, Task>? _onCompletedCallback;
    private object? _onCompletedState;

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        _onCompletedCallback = callback;
        _onCompletedState = state;
    }

    private Func<object, Task>? _onStartingCallback;
    private object? _onStartingState;

    public void OnStarting(Func<object, Task> callback, object state)
    {
        _onStartingCallback = callback;
        _onStartingState = state;
    }

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public void UpdateWindowSize(uint updateSize)
    {
        if (updateSize == 0)
            throw new Http2ProtocolException(); //Stream error

        _clientWindowSize.ReleaseSize(updateSize);
        _clientFlowControlBarrier.TrySetResult(true);
    }

    public void OnConnectionWindowUpdateSize()
    {
        _clientFlowControlBarrier.TrySetResult(true);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasStarted, true, false))
            return;
        await (_onStartingCallback?.Invoke(_onStartingState!) ?? Task.CompletedTask);
        _responseHeaders ??= new();
        _responseHeaders.SetReadOnly();
        cancellationToken.Register(() => _cts.Cancel());
        _responseWritingTask.SetResult(WriteResponseAsync(_cts.Token));
    }

    public async Task CompleteAsync()
    {
        var writingStarted = _responseWritingTask.Task;
        if (!writingStarted.IsCompleted)
            await StartAsync();

        var responseWriting = await writingStarted.WaitAsync(_cts.Token);
        
        bool endStreamWritten;
        try
        {
            endStreamWritten = await responseWriting;
        }
        catch (OperationCanceledException)
        {
            endStreamWritten = false;
        }
        if (IsAborted && !endStreamWritten)
        {
            _writer.ScheduleResetStream(this, Http2ErrorCode.INTERNAL_ERROR);
            return;
        }
    }

    private async Task<bool> WriteResponseAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;
        _writer.ScheduleWriteHeaders(this);
        await WriteBodyResponseAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (_responseTrailers != null)
        {
            _responseTrailers.SetReadOnly();

            // Write trailers and end stream.
            _writer.ScheduleWriteTrailers(this);
        }
        else
            _writer.ScheduleEndStream(this);
        return true;
    }

    private async Task WriteBodyResponseAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            var readResult = await _responseBodyPipe.Reader.ReadAsync(token);
            if (readResult.IsCanceled)
                return;

            var buffer = readResult.Buffer;
            while (!buffer.IsEmpty)
            {
                // Reserve flow control size for the response body.
                var size = buffer.Length > uint.MaxValue ? uint.MaxValue : (uint)buffer.Length;
                _clientFlowControlBarrier.Reset();
                if (!ReserveClientFlowControlSize(size, out size)) // Reserve here to block write pipe.
                {
                    await new ValueTask(_clientFlowControlBarrier, _clientFlowControlBarrier.Version)
                        .AsTask()
                        .WaitAsync(token);
                    continue;
                }

                ResponseBodyBuffer = buffer.Slice(0, size);

                _responseWriterFlushedResponse.Reset();
                _writer.ScheduleWriteData(this);
                await new ValueTask(_responseWriterFlushedResponse, _responseWriterFlushedResponse.Version)
                    .AsTask().WaitAsync(token);
                buffer = buffer.Slice(size);
            }
            _responseBodyPipe.Reader.AdvanceTo(readResult.Buffer.End);
            if (readResult.IsCompleted)
                return;
        }

        // Complete when cancelled or the body is fully written.
        _responseBodyPipe.Reader.Complete();
    }

    /// <summary>
    /// Called by response writer when write is completed.
    /// </summary>
    public void OnResponseDataFlushed()
    {
        // Release semaphore for the next write.
        _responseWriterFlushedResponse.TrySetResult(true);
    }

    private bool ReserveClientFlowControlSize(uint requestedSize, out uint reservedSize)
    {
        _clientWindowSize.TryUseAny(requestedSize, out var streamReservedSize);
        if (streamReservedSize == 0)
        {
            reservedSize = 0;
            return false;
        }
        if (!_connection.ReserveClientFlowControlSize(streamReservedSize, out var connectionReservedSize))
        {
            // Return the difference to the stream.
            _clientWindowSize.ReleaseSize(streamReservedSize - connectionReservedSize);
        }
        reservedSize = connectionReservedSize;
        if (reservedSize == 0)
        {
            return false;
        }

        return true;
    }

    internal async ValueTask OnStreamCompletedAsync()
    {
        if (_onCompletedCallback != null)
            await _onCompletedCallback.Invoke(_onCompletedState!);
        _state = StreamState.Closed;
        _responseBodyPipe.Reader.Complete();
        _connection.OnStreamCompleted(this);
    }

    public void SetPriority(Priority9218 serverPriority)
    {
        if (!_usePriority)
            throw new InvalidOperationException("Priority server option must be disabled.");

        ResponseHeaders["priority"] = serverPriority.ToString();
        Priority = serverPriority;
    }
}


internal class Http2StreamPipeWriter(PipeWriter writer, Action<int>? flushStartingCallback = null, Action<int>? flushedCallback = null) : PipeWriter
{
    private readonly PipeWriter _writer = writer;
    private readonly Action<int>? _flushStartingCallback = flushStartingCallback;
    private readonly Action<int>? _flushedCallback = flushedCallback;
    private volatile bool _completed;
    private long _unflushedBytes;

    public bool IsCompleted => _completed;

    public override void Advance(int bytes)
    {
        _writer.Advance(bytes);
        _unflushedBytes += bytes;
    }

    public override void CancelPendingFlush() =>
        _writer.CancelPendingFlush();

    public override bool CanGetUnflushedBytes => true;

    public override long UnflushedBytes => _unflushedBytes;

    public override void Complete(Exception? exception = null)
    {
        _writer.Complete(exception);
        _completed = true;
        _unflushedBytes = 0;
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        _flushStartingCallback?.Invoke(checked((int)_unflushedBytes));
        var result = await _writer.FlushAsync(cancellationToken);
        _flushedCallback?.Invoke(checked((int)_unflushedBytes));
        if (!result.IsCanceled && !result.IsCompleted)
            _unflushedBytes = 0;
        return result;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _writer.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _writer.GetSpan(sizeHint);

    public void Reset()
    {
        _unflushedBytes = 0;
        _completed = false;
    }
}

internal class Http2StreamPipeReader(PipeReader reader, Action<int> onReadCallback) : PipeReader
{
    private readonly PipeReader _reader = reader;
    private readonly Action<int> _onReadCallback = onReadCallback;
    private SequencePosition _lastReadStart;

    public override void AdvanceTo(SequencePosition consumed)
    {
        _reader.AdvanceTo(consumed);
        _onReadCallback(consumed.GetInteger() - _lastReadStart.GetInteger());
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        _reader.AdvanceTo(consumed, examined);
        _onReadCallback(consumed.GetInteger());
    }

    public override void CancelPendingRead()
    {
        _reader.CancelPendingRead();
    }

    public override void Complete(Exception? exception = null)
    {
        _reader?.Complete(exception);
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _reader.ReadAsync(cancellationToken);
        if (!result.IsCanceled)
            _lastReadStart = result.Buffer.Start;
        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        var hasRead = _reader.TryRead(out result);
        if (!result.IsCanceled)
            _lastReadStart = result.Buffer.Start;
        return hasRead;

    }

    public void Reset()
    {
        _lastReadStart = default;
    }
}
