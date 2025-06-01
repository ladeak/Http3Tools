using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer;

internal class Http2Stream<TContext> : Http2Stream where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;
    private readonly FeatureCollection _featureCollection;

    public Http2Stream(uint streamId, uint initialWindowSize, Http2Connection connection, FeatureCollection features, IHttpApplication<TContext> application)
        : base(streamId, initialWindowSize, connection)
    {
        _application = application;
        _featureCollection = features.Copy();
        _featureCollection.Add<IHttpRequestFeature>(this);
        _featureCollection.Add<IHttpResponseFeature>(this);
        _featureCollection.Add<IHttpResponseBodyFeature>(this);
        _featureCollection.Add<IHttpResponseTrailersFeature>(this);
        _featureCollection.Add<IHttpRequestBodyDetectionFeature>(this);
    }

    protected override Task RunApplicationAsync()
    {
        var context = _application.CreateContext(_featureCollection);
        return _application.ProcessRequestAsync(context);
    }
}

internal abstract partial class Http2Stream : IThreadPoolWorkItem
{
    private enum StreamState : byte
    {
        Open = 0,
        HalfOpenLocal = 1,
        HalfOpenRemote = 2,
        Closed = 4,
    }

    private readonly Http2Connection _connection;
    private readonly Http2ResponseWriter _writer;
    private FlowControlSize _serverWindowSize; // Controls Data received
    private FlowControlSize _clientWindowSize; // Controls Data sent
    private StreamState _state;
    private CancellationTokenSource _cts;

    public Http2Stream(uint streamId, uint initialWindowSize, Http2Connection connection)
    {
        StreamId = streamId;
        _clientWindowSize = new(initialWindowSize);
        _serverWindowSize = new(connection.ServerOptions.ServerStreamFlowControlSize);
        _connection = connection;
        _writer = connection.ResponseWriter!;
        _state = StreamState.Open;
        RequestEndHeaders = false;
        _requestHeaders = new HeaderCollection();
        _requestContentPipe = new(new PipeOptions(MemoryPool<byte>.Shared));
        _requestContentPipeReader = new(_requestContentPipe.Reader, ReleaseServerFlowControl);
        _requestContentPipeWriter = new(_requestContentPipe.Writer, flushStartingCallback: ConsumeServerFlowControl, flushedCallback: null);

        _responseContentPipe = new(new PipeOptions(MemoryPool<byte>.Shared));
        _responseContentPipeWriter = new(_responseContentPipe.Writer, flushStartingCallback: size =>
        {
            if (!_hasStarted)
                _ = StartAsync();
        });
        _cts = new();
    }

    public uint StreamId { get; }

    public bool RequestEndHeaders { get; private set; }

    protected abstract Task RunApplicationAsync();

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

    public async void Execute()
    {
        _requestHeaders.SetReadOnly();
        await RunApplicationAsync();
        _responseContentPipeWriter.Complete();
        await CompleteAsync();
    }

    public void Abort()
    {
        _cts.Cancel();
        _state = StreamState.Closed;
    }

    public void CompleteRequestStream()
    {
        _requestContentPipeWriter.Complete();
        _state = StreamState.HalfOpenRemote;

        // Reader maybe cancelled or completed, in which the data written to the pipe
        // has been already counted by the request sender. To make sure flowcontrol matches
        // the client, WindowUpdates is written for the remaining unflushed bytes.
        if (_requestContentPipeWriter.UnflushedBytes > 0)
        {
            ReleaseServerFlowControl((checked((int)_requestContentPipeWriter.UnflushedBytes)));
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

internal partial class Http2Stream : IHttpRequestFeature, IHttpRequestBodyDetectionFeature, IHttpRequestLifetimeFeature
{
    private HeaderCollection _requestHeaders;

    private Pipe _requestContentPipe;
    private Http2StreamPipeReader _requestContentPipeReader;
    private Http2StreamPipeWriter _requestContentPipeWriter;

    public string Protocol { get => "HTTP/2"; set => throw new NotSupportedException(); }
    public string Scheme { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string RawTarget { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    IHeaderDictionary IHttpRequestFeature.Headers { get => _requestHeaders; set => throw new NotSupportedException(); }
    public Stream Body { get => _requestContentPipeReader.AsStream(); set => throw new NotSupportedException(); }

    public bool CanHaveBody => true;

    public PipeWriter RequestPipe => _state <= StreamState.HalfOpenLocal ?
        _requestContentPipeWriter : throw new Http2ConnectionException("STREAM CLOSED");

    public CancellationToken RequestAborted { get => _cts.Token; set => throw new NotSupportedException(); }

    public HeaderCollection RequestHeaders { get => _requestHeaders; set => throw new NotSupportedException(); }
}

internal partial class Http2Stream : IHttpResponseFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature
{
    private readonly Pipe _responseContentPipe;
    private readonly Http2StreamPipeWriter _responseContentPipeWriter;
    private readonly SemaphoreSlim _applicationFlushedResponse = new(0);
    private readonly SemaphoreSlim _clientFlowControlBarrier = new(1, 1);

    private bool _hasStarted = false;
    private Task? _responseWritingTask;
    private HeaderCollection? _responseHeaders;
    private HeaderCollection? _responseTrailers;

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }

    public bool HasStarted => _hasStarted;

    public Stream Stream => throw new NotSupportedException($"Write with the {nameof(IHttpResponseBodyFeature.Writer)}");

    public PipeWriter Writer => _responseContentPipeWriter;

    public ReadOnlySequence<byte> ResponseContentBuffer { get; private set; }

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
        if (_clientFlowControlBarrier.CurrentCount == 0)
            _clientFlowControlBarrier.Release(1);
    }

    public void OnConnectionWindowUpdateSize()
    {
        if (_clientFlowControlBarrier.CurrentCount == 0)
            _clientFlowControlBarrier.Release(1);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasStarted, true, false))
            return;
        await (_onStartingCallback?.Invoke(_onStartingState!) ?? Task.CompletedTask);
        _responseHeaders ??= new();
        _responseHeaders.SetReadOnly();
        cancellationToken.Register(() => _cts.Cancel());
        _responseWritingTask = WriteResponseAsync(_cts.Token);
    }

    public async Task CompleteAsync()
    {
        var task = _responseWritingTask;
        if (task == null)
            await StartAsync();

        await _responseWritingTask!;
    }

    private async Task WriteResponseAsync(CancellationToken cancellationToken = default)
    {
        _writer.ScheduleWriteHeaders(this);
        await WriteBodyResponseAsync(cancellationToken);
        if (_responseTrailers != null)
        {
            _responseTrailers.SetReadOnly();

            // Write trailers and end stream.
            _writer.ScheduleWriteTrailers(this);
        }
        else
            _writer.ScheduleEndStream(this);
    }

    private async Task WriteBodyResponseAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            var readResult = await _responseContentPipe.Reader.ReadAsync(token);
            if (readResult.IsCanceled)
                return;

            var buffer = readResult.Buffer;
            while (!buffer.IsEmpty)
            {
                // Reserve flow control size for the response content.
                var size = buffer.Length > uint.MaxValue ? uint.MaxValue : (uint)buffer.Length;
                if (!ReserveClientFlowControlSize(size, out size)) // Reserve here to block write pipe.
                {
                    await _clientFlowControlBarrier.WaitAsync(token);
                    continue;
                }

                ResponseContentBuffer = buffer.Slice(0, size);
                _writer.ScheduleWriteData(this);
                await _applicationFlushedResponse.WaitAsync(token);
                buffer = buffer.Slice(size);
            }
            _responseContentPipe.Reader.AdvanceTo(readResult.Buffer.End);
            if (readResult.IsCompleted)
                return;
        }
    }

    /// <summary>
    /// Called by response writer when write is completed.
    /// </summary>
    public void OnResponseDataFlushed()
    {
        // Release semaphore for the next write.
        _applicationFlushedResponse.Release(1);
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

    internal async Task OnStreamCompletedAsync()
    {
        await (_onCompletedCallback?.Invoke(_onCompletedState!) ?? Task.CompletedTask);
        _state = StreamState.Closed;
        _connection.OnStreamCompleted(this);
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
}
