using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

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
    private readonly bool _usePriority;
    private readonly string? _altservice;
    private FeatureCollection _featureCollection;
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
        _requestHeaders = new RequestHeaderCollection();
        _requestBodyPipe = new(new PipeOptions(MemoryPool<byte>.Shared));
        _requestBodyPipeReader = new(_requestBodyPipe.Reader, ReleaseServerFlowControl);
        _requestBodyPipeWriter = new(_requestBodyPipe.Writer, flushStartingCallback: ConsumeServerFlowControl);
        _isPathSet = false;
        _pathLatinEncoded = [];

        _responseHeaders = new ResponseHeaderCollection();
        _responseBodyPipe = new(new PipeOptions(MemoryPool<byte>.Shared, pauseWriterThreshold: 0));
        _responseBodyPipeWriter = new(_responseBodyPipe.Writer,
            flushStartingCallback: FlushResponseBodyAsync, pipeCompleted: ApplicationResponseBodyPipeCompleted);
        _cts = new();
        StatusCode = 200;
        _responseWriterBodyFlushCompleted = new ManualResetValueTaskSource<bool>() { RunContinuationsAsynchronously = true };
        _clientFlowControlBarrier = new ManualResetValueTaskSource<bool>() { RunContinuationsAsynchronously = true };

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
        _altservice = _connection.ServerOptions.AltService;
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
        _requestHeaders.ResetHeaderCollection();
        _requestBodyPipe.Reset();
        _requestBodyPipeReader.Reset();
        _requestBodyPipeWriter.Reset();

        _responseBodyPipe.Reset();
        _responseBodyPipeWriter.Reset();

        _cts = new();
        _responseHeaders.ResetHeaderCollection();
        _responseTrailers = null;
        StatusCode = 200;
        ReasonPhrase = null;
        Scheme = string.Empty;
        Method = string.Empty;
        PathBase = string.Empty;
        _isPathSet = false;
        // _pathLatinEncoded not reset
        QueryString = string.Empty;
        _onStartingCallback = null;
        _onStartingState = null;
        _onCompletedCallback = null;
        _onCompletedState = null;

        _clientFlowControlBarrier.Reset();
        _responseWriterBodyFlushCompleted.Reset();
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
                _requestHeaders.Add("Host", value);
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
        if (separatorIndex >= 0)
        {
            QueryString = Encoding.Latin1.GetString(value[separatorIndex..]);
            value = value[..separatorIndex];
        }

        if (!_pathLatinEncoded.SequenceEqual(value))
        {
            _pathLatinEncoded = value.ToArray();
            Path = Encoding.Latin1.GetString(value);
        }
        _isPathSet = true;
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
        var cancelledException = new TaskCanceledException();
        _clientFlowControlBarrier.SetException(cancelledException);
        _responseWriterBodyFlushCompleted.SetException(cancelledException);
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
    private RequestHeaderCollection _requestHeaders;

    private Pipe _requestBodyPipe;
    private Http2StreamPipeReader _requestBodyPipeReader;
    private PreFlushHttp2StreamPipeWriter _requestBodyPipeWriter;
    private bool _isPathSet;
    private byte[] _pathLatinEncoded;

    public string Protocol { get => "HTTP/2"; set => throw new NotSupportedException(); }
    public string Scheme { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get => _isPathSet ? field : string.Empty; set => field = value; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string RawTarget { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    IHeaderDictionary IHttpRequestFeature.Headers { get => _requestHeaders; set => throw new NotSupportedException(); }
    public Stream Body { get => _requestBodyPipeReader.AsStream(); set => throw new NotSupportedException(); }

    public bool CanHaveBody => true;

    public bool IsAborted { get; private set; }

    public PipeWriter RequestPipe => _state <= StreamState.HalfOpenLocal ?
        _requestBodyPipeWriter : throw new Http2ConnectionException("STREAM CLOSED");

    public CancellationToken RequestAborted { get => _cts.Token; set => throw new NotSupportedException(); }

    public RequestHeaderCollection RequestHeaders { get => _requestHeaders; set => throw new NotSupportedException(); }

    public Priority9218 Priority { get; private set; }

    // TestHook
    internal async Task CompleteRequest(ReadOnlyMemory<byte>? responseBody = null)
    {
        _requestBodyPipeReader.Complete();
        _requestBodyPipeWriter.Complete();
        if (responseBody.HasValue)
            await _responseBodyPipeWriter.WriteAsync(responseBody.Value);
    }
}

internal partial class Http2Stream : IHttpResponseFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature
{
    private readonly Pipe _responseBodyPipe;
    private readonly PostFlushHttp2StreamPipeWriter _responseBodyPipeWriter;
    private ManualResetValueTaskSource<bool> _clientFlowControlBarrier;
    private ManualResetValueTaskSource<bool> _responseWriterBodyFlushCompleted;

    private bool _hasStarted = false;
    private readonly ResponseHeaderCollection _responseHeaders;
    private RequestHeaderCollection? _responseTrailers;
    private long _responseBodyBufferLength;

    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }

    public bool HasStarted => _hasStarted;

    public Stream Stream => _responseBodyPipeWriter.AsStream();

    public PipeWriter Writer => _responseBodyPipeWriter;

    public PipeReader ResponseBodyReader => _responseBodyPipe.Reader;

    public long ResponseBodyBufferLength => _responseBodyBufferLength;

    public void OnResponseBodySegmentFlush(long consumed) => Interlocked.Add(ref _responseBodyBufferLength, -consumed);

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

    public ResponseHeaderCollection ResponseHeaders => _responseHeaders;

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
        if (Interlocked.CompareExchange(ref _hasStarted, true, false) || IsAborted)
            return;
        if (_onStartingCallback != null)
            await _onStartingCallback.Invoke(_onStartingState!);
        if(_altservice != null)
            _responseHeaders.AltSvc = _altservice;
        _responseHeaders.SetReadOnly();
        cancellationToken.Register(() => _cts.Cancel());
        _writer.ScheduleWriteHeaders(this);
    }

    public async Task CompleteAsync()
    {
        if (!_hasStarted)
            await StartAsync();
        bool endStreamWritten = false;
        try
        {
            // Wait for the response body to be completly written by the response writer.
            // _responseWriterBodyFlushCompleted, gets cancelled, not converting to Task.
            await new ValueTask(_responseWriterBodyFlushCompleted, _responseWriterBodyFlushCompleted.Version);
            if (_responseTrailers != null)
            {
                _responseTrailers.SetReadOnly();

                // Write trailers and end stream.
                _writer.ScheduleWriteTrailers(this);
            }
            else
                _writer.ScheduleEndStream(this);
            endStreamWritten = true;
        }
        catch (OperationCanceledException)
        {
            // May happen during shutdown.
        }
        finally
        {
            if (IsAborted && !endStreamWritten)
                _writer.ScheduleResetStream(this, Http2ErrorCode.INTERNAL_ERROR);
        }
    }

    private async ValueTask FlushResponseBodyAsync(uint size, CancellationToken token = default)
    {
        if (!_hasStarted)
            await StartAsync();

        while (!IsAborted && !token.IsCancellationRequested && size > 0)
        {
            // Reserve flow control size for the response body.
            _clientFlowControlBarrier.Reset();
            if (!ReserveClientFlowControlSize(size, out var reservedSize)) // Reserve here to block write pipe.
            {
                await new ValueTask(_clientFlowControlBarrier, _clientFlowControlBarrier.Version)
                    .AsTask().WaitAsync(token);
                continue;
            }
            Interlocked.Add(ref _responseBodyBufferLength, reservedSize);
            _writer.ScheduleWriteData(this);
            size -= reservedSize;
        }
    }

    private void ApplicationResponseBodyPipeCompleted()
    {
        if (ResponseBodyBufferLength > 0)
            _writer.ScheduleWriteData(this);
        else
            OnResponseDataFlushed();
    }

    /// <summary>
    /// Called by response writer when write is completed.
    /// </summary>
    public void OnResponseDataFlushed()
    {
        // Release semaphore for the next write.
        _responseWriterBodyFlushCompleted.TrySetResult(true);
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
        _responseBodyPipeWriter.Complete();
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


internal class PreFlushHttp2StreamPipeWriter(PipeWriter writer, Action<int> flushStartingCallback) : PipeWriter
{
    private readonly PipeWriter _writer = writer;
    private readonly Action<int> _flushStartingCallback = flushStartingCallback;
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
        _flushStartingCallback.Invoke(checked((int)_unflushedBytes));
        var result = await _writer.FlushAsync(cancellationToken);
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

internal class PostFlushHttp2StreamPipeWriter(PipeWriter writer,
    Func<uint, CancellationToken, ValueTask> flushStartingCallback,
    Action pipeCompleted) : PipeWriter
{
    private readonly PipeWriter _writer = writer;
    private readonly Func<uint, CancellationToken, ValueTask> _flushStartingCallback = flushStartingCallback;
    private readonly Action _pipeCompleted = pipeCompleted;
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
        _pipeCompleted();
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        var result = await _writer.FlushAsync(cancellationToken);
        await _flushStartingCallback.Invoke(checked((uint)_unflushedBytes), cancellationToken);
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
