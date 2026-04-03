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
        ReadingPayloadData,
        ReadingPayloadReserved,
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
        _dataReader = PipeReader.Create(new ReadOnlySequence<byte>());
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
            (typeof(IHttpResponseTrailersFeature), this));
        //_features.Add<IHttpRequestBodyDetectionFeature>(this);
        //_features.Add<IHttpRequestLifetimeFeature>(this);
        //_features.Add<IPriority9218Feature>(this);
        _features.Checkpoint();
    }

    internal long Id { get; private set; }

    internal Task StreamCompletion => _streamCompletion.Task;

    public void Initialize(Http3Connection? connection, QuicStream quicStream)
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
        _isHostSet = false; // The actual Host is not reset.
        _features.ResetCheckpoint();
        _requestHeaders.ResetHeaderCollection();
        _responseHeaders.ResetHeaderCollection();
        _responseTrailers.ResetHeaderCollection();
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
        long payloadRemainingLength = 0;
        Task? applicationProcessing = null;
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
                    long bufferConsumed = ProcessStreamAsync(application, ref payloadRemainingLength, ref applicationProcessing, ref streamReadingState, buffer, token);

                    // Could not further process. Break the inner loop to read more data
                    if (bufferConsumed == 0)
                        break;

                    totalBufferConsumed += bufferConsumed;
                    buffer = buffer.Slice(bufferConsumed);
                }
                _dataReader.AdvanceTo(readResult.Buffer.GetPosition(totalBufferConsumed), readResult.Buffer.End);
            }
            if (applicationProcessing != null)
                await applicationProcessing;
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
            await CloseStreamAsync();
        }
    }

    private long ProcessStreamAsync<TContext>(IHttpApplication<TContext> application,
        ref long payloadRemainingLength,
        ref Task? applicationProcessing,
        ref StreamReadingStatus streamReadingState,
        ReadOnlySequence<byte> buffer,
        CancellationToken token) where TContext : notnull
    {
        long bufferConsumed = 0;

        if (streamReadingState == StreamReadingStatus.ReadingFrameHeader)
        {
            // FrameType is a single byte.
            if (!VariableLenghtIntegerDecoder.TryRead(buffer.FirstSpan, out var frameType, out int bytesReadFrameType))
            {
                // Not enough data to read payload length.
                return 0;
            }
            buffer = buffer.Slice(bytesReadFrameType);
            if (!VariableLenghtIntegerDecoder.TryRead(buffer, out var payloadLength, out int bytesReadPayloadLength))
            {
                // Not enough data to read payload length.
                return 0;
            }
            payloadRemainingLength = checked((long)payloadLength);
            streamReadingState = NextRequestReadingState(applicationProcessing, frameType);
            buffer = buffer.Slice(bytesReadPayloadLength);
            bufferConsumed += bytesReadFrameType + bytesReadPayloadLength;
            return bufferConsumed;
        }

        if (payloadRemainingLength < buffer.Length)
            buffer = buffer.Slice(0, payloadRemainingLength);

        if (streamReadingState == StreamReadingStatus.ReadingPayloadHeader)
        {
            bufferConsumed = ProcessHeaderFrame(buffer);
            if (payloadRemainingLength == bufferConsumed)
                applicationProcessing = Task.Run(() => StartApplicationProcessing(application, token), token);
        }

        if (streamReadingState == StreamReadingStatus.ReadingPayloadData)
            bufferConsumed = ProcessDataFrame(buffer);

        if (streamReadingState == StreamReadingStatus.ReadingPayloadReserved)
            bufferConsumed = buffer.Length; // Read the complete reserved frame.

        payloadRemainingLength -= bufferConsumed;
        if (payloadRemainingLength == 0)
            streamReadingState = StreamReadingStatus.ReadingFrameHeader;
        return bufferConsumed;
    }

    private StreamReadingStatus NextRequestReadingState(Task? applicationProcessing, ulong frameType)
    {
        StreamReadingStatus streamReadingState;
        switch (frameType)
        {
            case 0x0: // DATA
                if (applicationProcessing == null)
                    throw new Http3ConnectionException(ErrorCodes.H3FrameUnexpected);
                streamReadingState = StreamReadingStatus.ReadingPayloadData;
                break;
            case 0x1: // HEADERS
                if (applicationProcessing != null)
                    throw new Http3ConnectionException(ErrorCodes.H3FrameUnexpected);
                streamReadingState = StreamReadingStatus.ReadingPayloadHeader;
                break;
            default:
                if ((frameType - 0x21) % 0x1f != 0)
                    throw new Http3ConnectionException(ErrorCodes.H3FrameUnexpected);
                streamReadingState = StreamReadingStatus.ReadingPayloadReserved;
                break;
        }

        return streamReadingState;
    }

    private async Task CloseStreamAsync()
    {
        await _dataReader.CompleteAsync();
        await _responseHeaderWriter.CompleteAsync();
        await _responseDataWriter.CompleteAsync();
        _quicStream?.Dispose();
        _streamCompletion.TrySetResult();
        _connection?.StreamClosed(this);
    }

    private long ProcessDataFrame(ReadOnlySequence<byte> buffer)
    {
        // 'Copy' to Body
        return buffer.Length;

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
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasStarted, 1, 0) != 0)
            return;
        _requestHeaders.SetReadOnly();
        if (_onStartingCallback.HasValue)
            await _onStartingCallback.Value.Callback(_onStartingCallback.Value.State);
        _responseHeaders.SetReadOnly();
        await WriteHeadersAsync(StatusCode, _responseHeaders);
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
            await StartAsync(token);

            // Write trailers
            _responseTrailers.SetReadOnly();
            await WriteHeadersAsync(_responseTrailers); // todo trailers features
            _quicStream?.CompleteWrites();
        }
        catch (Exception)
        {
            _quicStream?.Dispose();
            _quicStream = null;
        }
    }

    private async Task WriteHeadersAsync(int statusCode, Http3ResponseHeaderCollection headers)
    {
        _qpackDecoder.Encode(statusCode, headers, _responseHeaderWriter);
        await _responseHeaderWriter.FlushAsync();
    }

    private async Task WriteHeadersAsync(Http3ResponseHeaderCollection headers)
    {
        _qpackDecoder.Encode(headers, _responseHeaderWriter);
        await _responseHeaderWriter.FlushAsync();
    }
}

internal partial class Http3Stream : IHttpResponseTrailersFeature
{
    private readonly Http3ResponseHeaderCollection _responseTrailers;
    public IHeaderDictionary Trailers { get => _responseTrailers; set => throw new PlatformNotSupportedException(); }
}
