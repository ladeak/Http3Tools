using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class Http3Stream
{
    private QuicStream? _quicStream;
    private PipeReader _dataReader;
    private PipeWriter _dataWriter;
    private QPackDecoder _qpackDecoder;
    private Task _applicationProcessing;
    private FeatureCollection _features;
    private TaskCompletionSource _streamCompletion;
    private Http3Connection? _connection;

    public Http3Stream(FeatureCollection features)
    {
        Id = 0;
        _quicStream = null;
        _dataReader = PipeReader.Create(new ReadOnlySequence<byte>());
        _dataWriter = PipeWriter.Create(Stream.Null);
        _qpackDecoder = new QPackDecoder();
        _pathEncoded = [];
        Scheme = string.Empty;
        Method = string.Empty;
        Path = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false;
        _applicationProcessing = Task.CompletedTask;
        _streamCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _features = features;
        _features.Add<IHttpRequestFeature>(this);
        _features.Add<IHttpResponseFeature>(this);
        //_features.Add<IHttpResponseBodyFeature>(this);
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
        _dataWriter = PipeWriter.Create(quicStream);
        Scheme = string.Empty;
        Method = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false; // The actual Path is not reset.
        _applicationProcessing = Task.CompletedTask;
        _features.ResetCheckpoint();

        StatusCode = 200;
        ReasonPhrase = null;
        HasStarted = false;
        _onStartingCallback = null;
        _onStartingCallbackState = null;
        _onCompletedCallback = null;
        _onCompletedCallbackState = null;
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
                        var context = application.CreateContext(_features);
                        if (_features is IHostContextContainer<TContext> contextAwareFeatureCollection)
                            contextAwareFeatureCollection.HostContext = context;
                        _applicationProcessing = application.ProcessRequestAsync(context);
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
            _quicStream?.Abort(QuicAbortDirection.Both, ErrorCodes.H3NoError);
            await _dataReader.CompleteAsync();
            await _dataWriter.CompleteAsync();
            _quicStream?.Dispose();
            _streamCompletion.TrySetResult();
            _connection?.StreamClosed(this);
        }
    }

    private long ProcessDataFrame(ReadOnlySequence<byte> buffer)
    {
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
    private Func<object, Task>? _onStartingCallback;
    private object? _onStartingCallbackState;

    private Func<object, Task>? _onCompletedCallback;
    private object? _onCompletedCallbackState;

    public int StatusCode { get; set; }

    public string? ReasonPhrase { get; set; }

    public bool HasStarted { get; private set; }

    // TODO invoke onStarting and completed
    // TODO manage streams in collection with a Dic
    public void OnStarting(Func<object, Task> callback, object state)
    {
        _onStartingCallback = callback;
        _onStartingCallbackState = state;
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        _onCompletedCallback = callback;
        _onCompletedCallbackState = state;
    }
}