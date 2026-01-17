using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class Http3Stream
{
    private QuicStream? _quicStream;
    private PipeReader _dataReader;
    private QPackDecoder _qpackDecoder;
    private FeatureCollection _features;
    private TaskCompletionSource _streamCompletion;
    private Http3Connection? _connection;

    public Http3Stream(FeatureCollection features)
    {
        Id = 0;
        _quicStream = null;
        _dataReader = PipeReader.Create(new ReadOnlySequence<byte>());
        _responseDataWriter = new(Stream.Null, frameType: 0);
        _responseHeaderWriter = new(Stream.Null, frameType: 1);
        _qpackDecoder = new QPackDecoder();
        _pathEncoded = [];
        Scheme = string.Empty;
        Method = string.Empty;
        Path = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false;
        _streamCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _requestHeaders = [];
        _responseHeaders = [];

        _features = features;
        _features.Add<IHttpRequestFeature>(this);
        _features.Add<IHttpResponseFeature>(this);
        _features.Add<IHttpResponseBodyFeature>(this);
        //_features.Add<IHttpResponseTrailersFeature>(this);
        //_features.Add<IHttpRequestBodyDetectionFeature>(this);
        //_features.Add<IHttpRequestLifetimeFeature>(this);
        //_features.Add<IPriority9218Feature>(this);
        _features.Checkpoint();
    }

    internal long Id { get; private set; }

    internal Task StreamCompletion => _streamCompletion.Task;

    public void Initialize(Http3Connection connection, QuicStream quicStream)
    {
        Id = quicStream.Id;
        _connection = connection;
        _quicStream = quicStream;
        _dataReader = PipeReader.Create(quicStream);
        _responseDataWriter.Reset(quicStream, StartAsync);
        _responseHeaderWriter.Reset(quicStream);
        Scheme = string.Empty;
        Method = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false; // The actual Path is not reset.
        _features.ResetCheckpoint();
        _requestHeaders.ResetHeaderCollection();
        _responseHeaders.ResetHeaderCollection();
        StatusCode = 200;
        _hasStarted = 0;
        _onStartingCallback = null;
        _onCompletedCallback = null;
        _streamCompletion.TrySetResult(); // Complete previous instance.
        _streamCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async void ProcessStream<TContext>(IHttpApplication<TContext> application, CancellationToken token)
        where TContext : notnull => await ProcessStreamAsync(application, token);

    public async Task ProcessStreamAsync<TContext>(IHttpApplication<TContext> application, CancellationToken token)
        where TContext : notnull
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var readResult = await _dataReader.ReadAsync(token);
                if (readResult.IsCanceled || readResult.IsCompleted)
                    break;

                var buffer = readResult.Buffer;

                // FrameType is a single byte.
                if (!VariableLenghtIntegerDecoder.TryRead(buffer.FirstSpan, out ulong frameType, out int bytesRead))
                    throw new Http3ConnectionException(ErrorCodes.H3FrameError);

                buffer = buffer.Slice(bytesRead);
                if (!VariableLenghtIntegerDecoder.TryRead(buffer, out ulong payloadLength, out bytesRead))
                {
                    // Not enough data to read payload length.
                    _dataReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    continue;
                }
                buffer = buffer.Slice(bytesRead);
                long processed = 1 + bytesRead; // 1 for the frame type. Should be always one byte by spec.
                switch (frameType)
                {
                    case 0x0: // DATA
                        processed += ProcessDataFrame(buffer);
                        break;
                    case 0x1: // HEADERS
                        if (payloadLength > (ulong)buffer.Length)
                        {
                            // Not enough data to read payload length.
                            _dataReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                            continue;
                        }
                        processed += ProcessHeaderFrame(buffer);
                        await StartApplicationProcessing(application, token);
                        break;
                }

                _dataReader.AdvanceTo(readResult.Buffer.GetPosition(processed), readResult.Buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            await CloseStreamAsync();
        }
    }

    private async Task CloseStreamAsync()
    {
        _quicStream?.Abort(QuicAbortDirection.Both, ErrorCodes.H3NoError);
        await _dataReader.CompleteAsync();
        await _responseDataWriter.CompleteAsync();
        _quicStream?.Dispose();
        _streamCompletion.TrySetResult();
        _connection?.StreamClosed(this);
    }

    private long ProcessDataFrame(ReadOnlySequence<byte> buffer)
    {
        // TODO: send to application
        return 0;
    }

    private long ProcessHeaderFrame(ReadOnlySequence<byte> buffer)
    {
        _qpackDecoder.DecodeHeader(buffer, this, out long consumed);
        return consumed;
    }
}


internal partial class Http3Stream : IHttpResponseFeature
{
    private (Func<object, Task> Callback, object State)? _onStartingCallback;
    private (Func<object, Task> Callback, object State)? _onCompletedCallback;

    public int StatusCode { get; set; }

    public string? ReasonPhrase { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    public byte _hasStarted = 0;
    public bool HasStarted => _hasStarted == 0;

    public void OnStarting(Func<object, Task> callback, object state) => _onStartingCallback = (callback, state);

    public void OnCompleted(Func<object, Task> callback, object state) => _onCompletedCallback = (callback, state);
}

internal partial class Http3Stream : IHttpResponseBodyFeature
{
    private readonly Http3ResponseHeaderCollection _responseHeaders;
    public IHeaderDictionary ResponseHeaders { get => _responseHeaders; set => throw new PlatformNotSupportedException(); }
    private Http3FramingStreamWriter _responseHeaderWriter;

    private readonly Http3FramingStreamWriter _responseDataWriter;

    public Stream Stream => throw new PlatformNotSupportedException();

    public PipeWriter Writer => _responseDataWriter;

    public Task CompleteAsync()
    {
        // TODO
        return Task.CompletedTask;
    }

    public void DisableBuffering()
    {
    }

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException();

    // Called by Http3DataFramingStreamWriter before the first write or the application
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasStarted, 1, 0) != 0)
            return;
        if (_onStartingCallback.HasValue)
            await _onStartingCallback.Value.Callback(_onStartingCallback.Value.State);
        _responseHeaders.SetReadOnly();
        await WriteHeadersAsync(_responseHeaders, StatusCode);
    }

    // StartApplicationProcessing start application processing
    // Before writing reponse DATA StartAsync invoked
    // DATA response written
    // Trailers written

    private async Task StartApplicationProcessing<TContext>(IHttpApplication<TContext> application, CancellationToken token) where TContext : notnull
    {
        try
        {
            _requestHeaders.SetReadOnly();
            var context = application.CreateContext(_features);
            if (_features is IHostContextContainer<TContext> contextAwareFeatureCollection)
                contextAwareFeatureCollection.HostContext = context;
            var applicationPrcessing = application.ProcessRequestAsync(context);

            await applicationPrcessing;
            await _responseDataWriter.CompleteAsync();

            // Invoke start to make sure headers written when no DATA in the response.
            await StartAsync(token);

            // Write trailers
            await WriteHeadersAsync(null); // todo trailers features
        }
        catch (Exception)
        {
            // TODO, close stream, ...
        }
    }

    private async Task WriteHeadersAsync(int statusCode, IHeaderDictionary headers)
    {
        _qpackDecoder.Encode(statusCode, headers, _responseHeaderWriter);
        await _responseHeaderWriter.FlushAsync();
    }

    private async Task WriteHeadersAsync(IHeaderDictionary headers)
    {
        _qpackDecoder.Encode(headers, _responseHeaderWriter);
        await _responseHeaderWriter.FlushAsync();
    }

}
