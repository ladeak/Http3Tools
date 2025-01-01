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
    private uint _windowSize;
    private StreamState _state;
    private CancellationTokenSource _cts;

    public Http2Stream(uint streamId, uint initialWindowSize, Http2Connection connection)
    {
        StreamId = streamId;
        _windowSize = initialWindowSize;
        _connection = connection;
        _writer = connection.ResponseWriter!;
        _state = StreamState.Open;
        RequestEndHeaders = false;
        _requestHeaders = new HeaderCollection();

        _responseContentPipe = new(new PipeOptions(MemoryPool<byte>.Shared));
        _responseContentPipeWriter = new(_responseContentPipe.Writer, () =>
        {
            if (!_hasStarted)
                _ = StartAsync();
            _applicationStartedResponse.Release(1);
        });
        _cts = new();
    }

    public uint StreamId { get; }

    public bool RequestEndHeaders { get; private set; }

    protected abstract Task RunApplicationAsync();

    public void UpdateWindowSize(uint updateSize)
    {
        if (updateSize == 0)
            throw new Http2ProtocolException(); //Stream error

        var updatedValue = _windowSize + updateSize;
        if (updatedValue > Http2Connection.MaxWindowUpdateSize)
        {
            // RST_STREAM with an error code of FLOW_CONTROL_ERROR
            // Reset instead of throwing?
            throw new Http2FlowControlException();
        }
        _windowSize = updatedValue;
    }

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
                };
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
        _requestContentPipe.Writer.Complete();
        _state = StreamState.HalfOpenRemote;
    }
}

internal partial class Http2Stream : IHttpRequestFeature, IHttpRequestBodyDetectionFeature, IHttpRequestLifetimeFeature
{
    private HeaderCollection _requestHeaders;

    private Pipe _requestContentPipe = new(new PipeOptions(MemoryPool<byte>.Shared));

    public string Protocol { get => "HTTP/2"; set => throw new NotSupportedException(); }
    public string Scheme { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string RawTarget { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    IHeaderDictionary IHttpRequestFeature.Headers { get => _requestHeaders; set => throw new NotSupportedException(); }
    public Stream Body { get => _requestContentPipe.Reader.AsStream(); set => throw new NotSupportedException(); }

    public bool CanHaveBody => true;

    public PipeWriter RequestPipe => _state <= StreamState.HalfOpenLocal ?
        _requestContentPipe.Writer : throw new Http2ConnectionException("STREAM CLOSED");

    public CancellationToken RequestAborted { get => _cts.Token; set => throw new NotSupportedException(); }

    public HeaderCollection RequestHeaders { get => _requestHeaders; set => throw new NotSupportedException(); }
}

internal partial class Http2Stream : IHttpResponseFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature
{
    private readonly Pipe _responseContentPipe;
    private readonly Http2StreamPipeWriter _responseContentPipeWriter;
    private readonly SemaphoreSlim _applicationStartedResponse = new(0);

    private bool _hasStarted = false;
    private Task? _responseWritingTask;
    private HeaderCollection? _responseHeaders;
    private HeaderCollection? _responseTrailers;

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }

    public bool HasStarted => _hasStarted;

    public Stream Stream => throw new NotSupportedException($"Write with the {nameof(IHttpResponseBodyFeature.Writer)}");

    public PipeWriter Writer => _responseContentPipeWriter;

    public PipeReader ResponseContent => _responseContentPipe.Reader;

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

    internal bool HasResponseContent => _responseContentPipeWriter.HasData;

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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasStarted, true, false))
            return;
        await (_onStartingCallback?.Invoke(_onStartingState!) ?? Task.CompletedTask);
        _responseHeaders ??= new();
        _responseHeaders.SetReadOnly();
        cancellationToken.Register(() => _cts.Cancel());
        _responseWritingTask = StartResponseAsync(_cts.Token);
    }

    public Task CompleteAsync() =>
        _responseWritingTask ?? StartResponseAsync();

    private async Task StartResponseAsync(CancellationToken cancellationToken = default)
    {
        _writer.ScheduleWriteHeaders(this);
        do
        {
            await _applicationStartedResponse.WaitAsync(cancellationToken);

            // Send response content on the connection.
            _writer.ScheduleWriteData(this);
        }
        while (!cancellationToken.IsCancellationRequested && !_responseContentPipeWriter.IsCompleted);

        if (_responseTrailers != null)
        {
            _responseTrailers.SetReadOnly();

            // Write trailers and end stream.
            _writer.ScheduleWriteTrailers(this);
        }
        else
            _writer.ScheduleEndStream(this);
    }

    internal async Task OnStreamCompletedAsync()
    {
        await (_onCompletedCallback?.Invoke(_onCompletedState!) ?? Task.CompletedTask);
        _state = StreamState.Closed;
        _connection.OnStreamCompleted(this);
    }
}


public class Http2StreamPipeWriter(PipeWriter writer, Action writeStartedCallback) : PipeWriter
{
    private readonly PipeWriter _writer = writer;
    private readonly Action _writeStartedCallback = writeStartedCallback;
    private volatile bool _hasData;
    private volatile bool _completed;
    private long _unflushedBytes;

    public bool HasData => _hasData;

    public bool IsCompleted => _completed;

    public override void Advance(int bytes)
    {
        _writer.Advance(bytes);
        _hasData = true;
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
        _writeStartedCallback();
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        var result = await _writer.FlushAsync(cancellationToken);
        _writeStartedCallback();
        _unflushedBytes = 0;
        return result;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _writer.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _writer.GetSpan(sizeHint);
}