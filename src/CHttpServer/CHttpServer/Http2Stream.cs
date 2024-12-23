using System.Buffers;
using System.IO.Pipelines;
using System.Reflection.PortableExecutable;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static CHttpServer.System.Net.Http.HPack.H2StaticTable;

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
        await StartAsync(_cts.Token);
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
}

internal partial class Http2Stream : IHttpResponseFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature
{
    private HeaderCollection? _responseHeaders;
    private HeaderCollection? _responseTrailers;
    private Pipe _responseContentPipe = new(new PipeOptions(MemoryPool<byte>.Shared));

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }

    public bool HasStarted { get; private set; }

    public Stream Stream => Writer.AsStream();

    public PipeWriter Writer => _responseContentPipe.Writer;

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
        get => Headers;
        set => throw new NotSupportedException();
    }

    public HeaderCollection Headers => _responseHeaders ??= new();

    public Task CompleteAsync()
    {
        return _onCompletedCallback?.Invoke(_onCompletedState!) ?? Task.CompletedTask;
    }

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

    private Func<object, Task>? _onStartignCallback;
    private object? _onStartingState;

    public void OnStarting(Func<object, Task> callback, object state)
    {
        _onStartignCallback = callback;
        _onStartingState = state;
    }

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await (_onStartignCallback?.Invoke(_onStartingState!) ?? Task.CompletedTask);
        HasStarted = true;
        _responseHeaders ??= new();
        _responseHeaders.SetReadOnly();

        // send headers
        _writer.ScheduleWriteHeaders(this);

        _onCompletedCallback?.Invoke(_onCompletedState!);
    }
}
