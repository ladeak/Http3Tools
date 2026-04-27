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
    private enum StreamReadingStatus
    {
        ReadingFrameHeader,
        ReadingPayloadHeader,
        HeaderReadCompleted,
    }

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
        _dataReader = PipeReader.Create(ReadOnlySequence<byte>.Empty);
        _requestDataToAppPipeReader = new(PipeReader.Create(ReadOnlySequence<byte>.Empty));
        _requestDataToAppPipeReader.Complete();
        _responseDataWriter = new(Stream.Null, frameType: 0);
        _responseHeaderWriter = new(Stream.Null, frameType: 1);
        _qpackDecoder = new QPackDecoder();
        _pathEncoded = [];
        _hostDecoded = "";
        _hostEncoded = [];
        Scheme = string.Empty;
        Method = string.Empty;
        Path = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false;
        _streamCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _requestHeaders = [];
        _responseHeaders = [];
        _responseTrailers = [];

        _features = features;
        _features.AddRange(
            (typeof(IHttpRequestFeature), this),
            (typeof(IHttpResponseFeature), this),
            (typeof(IHttpResponseBodyFeature), this),
            (typeof(IHttpResponseTrailersFeature), this),
            (typeof(IRequestBodyPipeFeature), this),
            (typeof(IHttpRequestBodyDetectionFeature), this));
        //_features.Add<IHttpRequestLifetimeFeature>(this);
        //_features.Add<IPriority9218Feature>(this);
        _features.Checkpoint();
    }

    internal long Id { get; private set; }

    internal Task StreamCompletion => _streamCompletion.Task;

    public void Initialize(Http3Connection? connection, QuicStream quicStream)
    {
        _connection = connection;
        Id = quicStream.Id;
        _dataReader = PipeReader.Create(quicStream);
        _responseDataWriter.Reset(quicStream, async ct => await StartImplAsync(ct));
        _responseHeaderWriter.Reset(quicStream);
        Scheme = string.Empty;
        Method = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false; // The actual Path is not reset.
        StatusCode = 200;
        _hasStarted = 0;
        _streamCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void ReleaseState()
    {
        _quicStream = null;
        _features.ResetCheckpoint();
        _requestHeaders.ResetHeaderCollection();
        _responseHeaders.ResetHeaderCollection();
        _responseTrailers.ResetHeaderCollection();
        _onStartingCallback = null;
        _onCompletedCallback = null;
        _qpackDecoder.Reset();
        _streamCompletion.TrySetResult(); // Complete previous instance.
    }

    public async void ProcessStream<TContext>(IHttpApplication<TContext> application, CancellationToken token)
        where TContext : notnull => await ProcessStreamAsync(application, token);

    public async Task ProcessStreamAsync<TContext>(IHttpApplication<TContext> application, CancellationToken token)
        where TContext : notnull
    {
        long payloadRemainingLength = 0;
        var streamReadingState = StreamReadingStatus.ReadingFrameHeader;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var readResult = await _dataReader.ReadAsync(token);
                if (readResult.IsCanceled || (readResult.IsCompleted && readResult.Buffer.IsEmpty))
                    break;
                var buffer = readResult.Buffer;
                long totalBufferConsumed = 0;

                while (!buffer.IsEmpty)
                {
                    long bufferConsumed = 0;
                    if (streamReadingState == StreamReadingStatus.ReadingFrameHeader)
                        bufferConsumed = ReadFrameHeader(ref payloadRemainingLength, ref streamReadingState, buffer);
                    else if (streamReadingState == StreamReadingStatus.ReadingPayloadHeader)
                    {
                        bufferConsumed = ReadHeaderFrame(ref payloadRemainingLength, ref streamReadingState, buffer);
                        if (payloadRemainingLength == 0)
                        {
                            streamReadingState = StreamReadingStatus.HeaderReadCompleted;
                            if (!readResult.IsCompleted)
                                CanHaveBody = true;
                        }
                    }
                    else if (streamReadingState == StreamReadingStatus.HeaderReadCompleted)
                        break;

                    // Could not further process. Break the inner loop to read more data
                    if (bufferConsumed == 0)
                        break;

                    totalBufferConsumed += bufferConsumed;
                    buffer = buffer.Slice(bufferConsumed);
                }
                _dataReader.AdvanceTo(readResult.Buffer.GetPosition(totalBufferConsumed), readResult.Buffer.End);
                if (streamReadingState == StreamReadingStatus.HeaderReadCompleted)
                    break;
            }

            Debug.Assert(streamReadingState == StreamReadingStatus.HeaderReadCompleted);
            _requestDataToAppPipeReader.Reset(_dataReader);
            await StartApplicationProcessing(application, token);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Http3ConnectionException connectionError)
        {
            _connection?.StreamError(connectionError.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            await _dataReader.CompleteAsync();
            CloseStream();
        }
    }

    private long ReadFrameHeader(ref long payloadRemainingLength, ref StreamReadingStatus streamReadingState, ReadOnlySequence<byte> buffer)
    {
        if (!VariableLenghtIntegerDecoder.TryRead(buffer, out var frameType, out int bytesReadFrameType))
            return 0; // Not enough data to read payload length.

        buffer = buffer.Slice(bytesReadFrameType);
        if (!VariableLenghtIntegerDecoder.TryRead(buffer, out var payloadLength, out int bytesReadPayloadLength))
            return 0; // Not enough data to read payload length.

        payloadRemainingLength = checked((long)payloadLength);
        streamReadingState = NextRequestReadingState(frameType);
        var bufferConsumed = bytesReadFrameType + bytesReadPayloadLength;
        return bufferConsumed;
    }

    private long ReadHeaderFrame(ref long payloadRemainingLength, ref StreamReadingStatus streamReadingState, ReadOnlySequence<byte> buffer)
    {
        if (payloadRemainingLength < buffer.Length)
            buffer = buffer.Slice(0, payloadRemainingLength);
        var bufferConsumed = ProcessHeaderFrame(buffer);
        payloadRemainingLength -= bufferConsumed;
        return bufferConsumed;
    }

    private StreamReadingStatus NextRequestReadingState(ulong frameType)
    {
        if (frameType == 0x01)
            return StreamReadingStatus.ReadingPayloadHeader;
        throw new Http3ConnectionException(ErrorCodes.H3FrameUnexpected);
    }

    private void CloseStream()
    {
        _quicStream?.Dispose();
        //await _requestDataToAppPipeReader.CompleteAsync();
        _responseHeaderWriter.Complete();
        _responseDataWriter.Complete();
        _streamCompletion.TrySetResult();
        ReleaseState();
        _connection?.StreamClosed(this);
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

    IHeaderDictionary IHttpResponseFeature.Headers { get => ResponseHeaders; set => throw new PlatformNotSupportedException(); }

    public void OnStarting(Func<object, Task> callback, object state) => _onStartingCallback = (callback, state);

    public void OnCompleted(Func<object, Task> callback, object state) => _onCompletedCallback = (callback, state);
}

internal partial class Http3Stream : IHttpResponseBodyFeature
{
    private readonly Http3ResponseHeaderCollection _responseHeaders;
    public IHeaderDictionary ResponseHeaders { get => _responseHeaders; set => throw new PlatformNotSupportedException(); }
    private readonly Http3FramingStreamWriter _responseHeaderWriter;
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
    public Task StartAsync(CancellationToken cancellationToken = default) => StartImplAsync(cancellationToken).AsTask();

    private ValueTask<FlushResult> StartImplAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasStarted, 1, 0) != 0)
            return ValueTask.FromResult(new FlushResult(false, true));
        _requestHeaders.SetReadOnly();

        if (_onStartingCallback.HasValue)
            return StatingCallbackWithWriteHeaders();
        else
            return WriteHeaders();

        async ValueTask<FlushResult> StatingCallbackWithWriteHeaders()
        {
            if (_onStartingCallback.HasValue)
                await _onStartingCallback.Value.Callback(_onStartingCallback.Value.State);
            _responseHeaders.SetReadOnly();
            return await WriteHeadersAsync(StatusCode, _responseHeaders);
        }

        ValueTask<FlushResult> WriteHeaders()
        {
            _responseHeaders.SetReadOnly();
            return WriteHeadersAsync(StatusCode, _responseHeaders);
        }
    }

    // StartApplicationProcessing start application processing
    // Before writing reponse DATA StartAsync invoked
    // DATA response written
    // Trailers written
    private async Task StartApplicationProcessing<TContext>(IHttpApplication<TContext> application, CancellationToken token) where TContext : notnull
    {
        try
        {
            var context = application.CreateContext(_features);
            if (_features is IHostContextContainer<TContext> contextAwareFeatureCollection)
                contextAwareFeatureCollection.HostContext = context;
            var applicationProcessing = application.ProcessRequestAsync(context);

            await applicationProcessing;
            await _responseDataWriter.CompleteAsync();

            // Invoke start to make sure headers written when no DATA in the response.
            // When DATA frames are written, the DATA writer invokes it before the first write.
            if (_hasStarted == 0)
                await StartImplAsync(token);

            // Write trailers
            if (_responseTrailers.Count > 0)
            {
                _responseTrailers.SetReadOnly();
                await WriteHeadersAsync(_responseTrailers);
            }
            _quicStream?.CompleteWrites();
        }
        catch (Exception e)
        {
            _quicStream?.Dispose();
            //_quicStream = null;
        }
    }

    private ValueTask<FlushResult> WriteHeadersAsync(int statusCode, Http3ResponseHeaderCollection headers)
    {
        _qpackDecoder.Encode(statusCode, headers, _responseHeaderWriter);
        return _responseHeaderWriter.FlushAsync();
    }

    private ValueTask<FlushResult> WriteHeadersAsync(Http3ResponseHeaderCollection headers)
    {
        _qpackDecoder.Encode(headers, _responseHeaderWriter);
        return _responseHeaderWriter.FlushAsync();
    }
}

internal partial class Http3Stream : IHttpResponseTrailersFeature
{
    private readonly Http3ResponseHeaderCollection _responseTrailers;
    public IHeaderDictionary Trailers { get => _responseTrailers; set => throw new PlatformNotSupportedException(); }
}

internal partial class Http3Stream : IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody { get; set; }
}