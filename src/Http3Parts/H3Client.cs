using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.QPack;
using System.Net.Quic;
using System.Net.Security;
using System.Text;

namespace Http3Parts;

/// <summary>
/// https://datatracker.ietf.org/doc/rfc9114/
/// </summary>
public class H3Client : IAsyncDisposable
{
    private CancellationTokenSource _inboundCts = new CancellationTokenSource();

    private Task? _inboundConnectionHandler;

    private List<Task> _readingTasks = new();

    public event EventHandler<Frame>? OnFrame;

    public ConnectionContext? ConnectionContext { get; private set; }

    public StreamContext? OutgoingControlStream { get; private set; }

    private Dictionary<long, StreamContext> _requestStreams = new();

    public async ValueTask DisposeAsync()
    {
        _inboundCts.Cancel();
        await (ConnectionContext?.QuicConnection.DisposeAsync() ?? ValueTask.CompletedTask);
        try
        {
            await (_inboundConnectionHandler ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
            // Because _inboundCts is cancelled above.
        }
        try
        {
            await Task.WhenAll(_readingTasks);
        }
        catch (AggregateException aggregate)
        {
            // Because _inboundCts is cancelled above.
            if (aggregate.InnerExceptions.Any(x => x is not OperationCanceledException))
                throw;
        }
    }

    public Task ConnectAsync(IPEndPoint endpoint)
    {
        var options = CreateClientConnectionOptions(endpoint);
        return ConnectAsync(options);
    }

    public async Task ConnectAsync(QuicClientConnectionOptions options)
    {
        var clientConnection = await QuicConnection.ConnectAsync(options);
        ConnectionContext = new ConnectionContext() { QuicConnection = clientConnection };
        if (options.MaxInboundUnidirectionalStreams > 0 || options.MaxInboundBidirectionalStreams > 0)
            _inboundConnectionHandler = Task.Run(() => HandleIncomingStreams(ConnectionContext, _inboundCts.Token), _inboundCts.Token);

    }

    public Task SendSettingsAsync() => SendSettingsAsync(new SettingParameter((long)Http3SettingType.MaxHeaderListSize, 1024));

    public async Task SendSettingsAsync(params SettingParameter[] settings)
    {
        if (ConnectionContext == null)
            throw new InvalidOperationException("Call ConnectAsync() first");

        var connection = ConnectionContext.QuicConnection;
        var clientControl = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional);

        OutgoingControlStream = new StreamContext()
        {
            Connection = ConnectionContext,
            Incoming = false,
            Stream = clientControl
        };

        SendStreamIdentifier(clientControl);
        var settingsFrame = BuildSettingsFrame(settings);
        SendFrameHeader(clientControl, Http3FrameType.Settings, settingsFrame);
        await clientControl.WriteAsync(settingsFrame);
    }

    private void SendStreamIdentifier(QuicStream clientControl)
    {
        Span<byte> streamIdentifier = stackalloc byte[1];
        streamIdentifier[0] = (byte)Http3StreamType.Control;
        clientControl.Write(streamIdentifier);
    }

    private void SendFrameHeader(QuicStream clientControl, Http3FrameType type, ReadOnlyMemory<byte> payload)
    {
        Span<byte> buffer = stackalloc byte[1 + VariableLengthIntegerHelper.GetByteCount(payload.Length)];
        buffer[0] = (byte)type;
        VariableLengthIntegerHelper.WriteInteger(buffer.Slice(1), payload.Length);
        clientControl.Write(buffer);
    }

    private ReadOnlyMemory<byte> BuildSettingsFrame(params SettingParameter[] settings)
    {
        byte[] buffer = new byte[2 * VariableLengthIntegerHelper.MaximumEncodedLength * settings.Length];
        Span<byte> remainingBuffer = buffer;
        int payloadSize = 0;
        foreach (var setting in settings)
        {
            int currentLength = VariableLengthIntegerHelper.WriteInteger(remainingBuffer, setting.Id);
            currentLength += VariableLengthIntegerHelper.WriteInteger(remainingBuffer.Slice(currentLength), setting.Value);
            remainingBuffer = remainingBuffer.Slice(currentLength);
            payloadSize += currentLength;
        }
        return buffer.AsMemory(0, payloadSize);
    }

    public async Task<long> OpenRequestStream()
    {
        if (ConnectionContext == null)
            throw new InvalidOperationException("Call ConnectAsync() first");

        var connection = ConnectionContext.QuicConnection;
        var dataStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);

        var requestContext = new StreamContext()
        {
            Connection = ConnectionContext,
            Incoming = true,
            Stream = dataStream,
        };
        _requestStreams.Add(dataStream.Id, requestContext);
        return dataStream.Id;
    }

    private ReadOnlyMemory<byte> BuildHeaderFrame(Uri url, HttpMethod method, IEnumerable<KeyValuePair<string, string>> headers)
    {
        using var writer = new PooledArrayBufferWriter<byte>();
        BuildQpackHeaders(writer, url, method, headers);
        var frameHeader = BuildFrameHeader(Http3FrameType.Headers, writer.WrittenMemory);
        byte[] result = new byte[frameHeader.Length + writer.WrittenCount];
        frameHeader.CopyTo(result.AsMemory());
        writer.WrittenSpan.CopyTo(result.AsSpan(frameHeader.Length));
        return result;
    }

    public ValueTask SendFrameAsync(long streamId, ReadOnlyMemory<byte> data, CancellationToken token)
    {
        // This only sends data
        if (!_requestStreams.TryGetValue(streamId, out var streamContext))
            throw new ArgumentException("Given streamId has no corresponding stream opened");
        return streamContext.Stream.WriteAsync(data, token);
    }

    private void BuildQpackHeaders(IBufferWriter<byte> writer, Uri url, HttpMethod method, IEnumerable<KeyValuePair<string, string>> headers)
    {
        int currentRequestSize = 128;
        var buffer = writer.GetSpan(currentRequestSize);

        // Add QPACK header block prefix.
        //https://datatracker.ietf.org/doc/html/draft-ietf-quic-qpack-11#section-4.5.1
        buffer[0] = 0;
        buffer[1] = 0;
        writer.Advance(2);

        int length;

        // Write method
        var staticTableMethod = H3StaticTable.MethodIndex[method];
        buffer = writer.GetSpan(currentRequestSize);
        while (!QPackEncoder.EncodeStaticIndexedHeaderField(staticTableMethod, buffer, out length))
        {
            currentRequestSize = currentRequestSize << 1;
            buffer = writer.GetSpan(currentRequestSize);
        }
        writer.Advance(length);

        // Write scheme
        var scheme = url.Scheme.ToLowerInvariant() switch
        {
            "http" => H3StaticTable.SchemeHttp,
            "https" => H3StaticTable.SchemeHttps,
            _ => throw new NotSupportedException("Scheme not supported")
        };
        buffer = writer.GetSpan(currentRequestSize);
        while (!QPackEncoder.EncodeStaticIndexedHeaderField(scheme, buffer, out length))
        {
            currentRequestSize = currentRequestSize << 1;
            buffer = writer.GetSpan(currentRequestSize);
        }
        writer.Advance(length);

        // Write Host
        buffer = writer.GetSpan(currentRequestSize);
        while (!QPackEncoder.EncodeLiteralHeaderFieldWithStaticNameReference(H3StaticTable.Authority, url.Host, null, buffer, out length))
        {
            currentRequestSize = currentRequestSize << 1;
            buffer = writer.GetSpan(currentRequestSize);
        }
        writer.Advance(length);

        // Write Path
        buffer = writer.GetSpan(currentRequestSize);
        while (!QPackEncoder.EncodeLiteralHeaderFieldWithStaticNameReference(H3StaticTable.PathSlash, url.AbsolutePath, buffer, out length))
        {
            currentRequestSize = currentRequestSize << 1;
            buffer = writer.GetSpan(currentRequestSize);
        }
        writer.Advance(length);

        // Other Headers
        foreach (var header in headers)
        {
            if (HeaderMap.TryGetStaticRequestHeader(header.Key, out var headerKey))
            {
                buffer = writer.GetSpan(currentRequestSize);
                while (!QPackEncoder.EncodeLiteralHeaderFieldWithStaticNameReference(headerKey, header.Value, buffer, out length))
                {
                    currentRequestSize = currentRequestSize << 1;
                    buffer = writer.GetSpan(currentRequestSize);
                }
                writer.Advance(length);
            }
            else
            {
                buffer = writer.GetSpan(currentRequestSize);
                while (!QPackEncoder.EncodeLiteralHeaderFieldWithoutNameReference(header.Key, header.Value, buffer, out length))
                {
                    currentRequestSize = currentRequestSize << 1;
                    buffer = writer.GetSpan(currentRequestSize);
                }
                writer.Advance(length);
            }
        }
    }

    private ReadOnlyMemory<byte> BuildFrameHeader(Http3FrameType type, ReadOnlyMemory<byte> payload)
    {
        var buffer = new byte[VariableLengthIntegerHelper.GetByteCount((long)type) + VariableLengthIntegerHelper.GetByteCount(payload.Length)];
        int count = VariableLengthIntegerHelper.WriteInteger(buffer.AsSpan(0), (long)type);
        VariableLengthIntegerHelper.WriteInteger(buffer.AsSpan(count), payload.Length);
        return buffer;
    }

    public async Task TestAsync(Uri address)
    {
        // Open connection and send settings frame on control stream.
        await ConnectAsync(new IPEndPoint(IPAddress.Loopback, address.Port));
        await SendSettingsAsync();

        // Open bidirectional stream to send request.
        var connectionId = await OpenRequestStream();
        var frame = BuildHeaderFrame(address, HttpMethod.Get, Enumerable.Empty<KeyValuePair<string, string>>());
        await SendFrameAsync(connectionId, frame, CancellationToken.None);

        await ReadAsync(ConnectionContext, _requestStreams[connectionId].Stream, false, CancellationToken.None);

        var goAwayFrame = BuildGoAwayFrame();
        await OutgoingControlStream.Stream.WriteAsync(goAwayFrame);
    }

    private async Task HandleIncomingStreams(ConnectionContext connectionCtx, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var inbound = await connectionCtx.QuicConnection.AcceptInboundStreamAsync(token);
                _ = Task.Run(() => ProcessIncomingStream(connectionCtx, inbound, token), token);
            }
            catch (QuicException ex) when (_inboundCts.IsCancellationRequested)
            {
                throw new Exception($"{ex.Message}, {ex.TransportErrorCode}, {ex.QuicError}, {ex.ApplicationErrorCode}, {ex.HelpLink}, {ex.Data}, {ex.HResult}");
                // Shutting down connection due to dispose.
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private byte[] BuildGoAwayFrame()
    {
        //GOAWAY Frame {
        //    Type(i) = 0x07,
        //    Length(i),
        //   Stream ID/ Push ID(i),
        //}
        var frame = ArrayPool<byte>.Shared.Rent(32);
        try
        {
            Span<byte> buffer = frame.AsSpan(1 + VariableLengthIntegerHelper.MaximumEncodedLength);
            var payloadLength = 1;
            int payloadLengthSize = VariableLengthIntegerHelper.GetByteCount(payloadLength);

            buffer = frame.AsSpan(1 + VariableLengthIntegerHelper.MaximumEncodedLength - 1 - payloadLengthSize, payloadLength + 1 + payloadLengthSize);

            buffer[0] = (byte)Http3FrameType.GoAway;
            VariableLengthIntegerHelper.WriteInteger(buffer.Slice(1), payloadLength);
            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    private QuicClientConnectionOptions CreateClientConnectionOptions(EndPoint remoteEndPoint)
    {
        return new QuicClientConnectionOptions
        {
            MaxInboundBidirectionalStreams = 5,
            MaxInboundUnidirectionalStreams = 5,
            RemoteEndPoint = remoteEndPoint,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    SslApplicationProtocol.Http3
                },
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            DefaultStreamErrorCode = (long)Http3ErrorCode.ConnectError,
            DefaultCloseErrorCode = (long)Http3ErrorCode.ProtocolError,
        };
    }

    private async Task ProcessIncomingStream(ConnectionContext connectionCtx, QuicStream stream, CancellationToken token)
    {
        Task? readingTask = null;
        try
        {
            readingTask = ReadAsync(connectionCtx, stream, true, token);
            _readingTasks.Add(readingTask);
            await readingTask;
        }
        finally
        {
            if (readingTask != null)
                _readingTasks.Remove(readingTask);
        }
    }

    private async Task ReadAsync(ConnectionContext connectionCtx, QuicStream clientStream, bool incoming, CancellationToken token)
    {
        try
        {
            var pipe = new Pipe();
            Task? pipeTask = null;
            try
            {
                var writer = pipe.Writer;
                var copyTask = clientStream.CopyToAsync(writer, token);
                var context = new StreamContext()
                {
                    Reader = pipe.Reader,
                    Stream = clientStream,
                    Connection = connectionCtx,
                    Incoming = incoming
                };
                pipeTask = ProcessPipeAsync(context, token);

                await copyTask;
                await pipe.Writer.CompleteAsync();

                await pipeTask;
            }
            catch (Exception ex)
            {
                await pipe.Writer.CompleteAsync(ex);
            }
            await (pipeTask ?? Task.CompletedTask);

        }
        finally
        {
            _requestStreams.Remove(clientStream.Id);
        }
    }

    private async Task ProcessPipeAsync(StreamContext context, CancellationToken token)
    {
        if (context.Reader == null)
            return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await context.Reader.ReadAsync(token);
                if (read.IsCanceled)
                    break;

                var buffer = read.Buffer;

                if (context.Incoming && context.StreamType is null)
                {
                    // Read HTTP stream type
                    long streamTypeRaw = VariableLengthIntegerHelper.GetInteger(in buffer, out var consumed, out var examined);
                    if (!Enum.IsDefined<Http3StreamType>((Http3StreamType)streamTypeRaw))
                    {
                        // error H3_STREAM_CREATION_ERROR or read data without processing
                        await ConnectionErrorAsync(context, Http3ErrorCode.StreamCreationError);
                    }
                    buffer = buffer.Slice(consumed);
                    context.StreamType = (Http3StreamType)streamTypeRaw;
                }

                while (TryReadFrame(ref buffer, out var frame))
                {
                    await ProcessFrameAsync(frame, context);
                }

                context.Reader.AdvanceTo(buffer.Start, buffer.End);

                if (read.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        finally
        {
            await context.Reader.CompleteAsync();
        }
    }

    private async Task ProcessFrameAsync(DataFrame frame, StreamContext context)
    {
        if (context.Incoming && !context.InitialFrameProcessed && frame.Type != Http3FrameType.Settings)
        {
            // H3_MISSING_SETTINGS error
            await ConnectionErrorAsync(context, Http3ErrorCode.MissingSettings);
            return;
        }

        // TODO: CANCEL_PUSH, PUSH_PROMISE, GOAWAY, MAX_PUSH_ID
        switch (frame.Type)
        {
            case Http3FrameType.Data:
                await ProcessDataFrameAsync(frame);
                break;
            case Http3FrameType.Headers:
                await ProcessHeaderFrameAsync(frame, context);
                break;
            case Http3FrameType.Settings:
                await ProcessSettingsFrameAsync(frame, context);
                break;
        };
        context.InitialFrameProcessed = true;
    }

    private ValueTask ProcessSettingsFrameAsync(DataFrame frame, StreamContext context)
    {
        if (context.InitialFrameProcessed)
        {
            // H3_FRAME_UNEXPECTED error
            return ConnectionErrorAsync(context, Http3ErrorCode.UnexpectedFrame);
        }

        var buffer = frame.Payload;
        while (buffer.Length > 0)
        {
            var settingIdentifier = (Http3SettingType)buffer.FirstSpan[0];
            buffer = buffer.Slice(1);
            var value = VariableLengthIntegerHelper.GetInteger(buffer, out var consumed, out var examined);
            if (buffer.GetOffset(consumed) > 0)
            {
                context.Settings.Add(settingIdentifier, value);
                buffer = buffer.Slice(consumed);
            }
            else
            {
                return ConnectionErrorAsync(context, Http3ErrorCode.SettingsError);
            }
        }
        return ValueTask.CompletedTask;
    }

    private Task ProcessHeaderFrameAsync(DataFrame frame, StreamContext context)
    {
        context.HeaderDecoder ??= new QPackDecoder((int)frame.Payload.Length);
        context.HeaderDecoder.Decode(frame.Payload, true, new HeadersHandler());
        return Task.CompletedTask;
    }

    private Task ProcessDataFrameAsync(DataFrame frame)
    {
        long count = Encoding.UTF8.GetChars(frame.Payload, new ConsoleBufferWriter());
        return Task.CompletedTask;
    }

    private bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out DataFrame frame)
    {
        frame = default;
        if (buffer.Length == 0)
            return false;

        var frameType = (Http3FrameType)buffer.FirstSpan[0];
        var reader = new SequenceReader<byte>(buffer.Slice(1L));
        var frameTypelessBuffer = buffer.Slice(1L);
        if (!VariableLengthIntegerHelper.TryRead(ref reader, out long length))
            return false;
        int bytesCount = (int)reader.Consumed;
        // Failed to read length
        if (length == -1)
            return false;
        var totalFrameLength = 1L + bytesCount + length;
        if (buffer.Length < totalFrameLength)
            return false;

        frame = new DataFrame() { Type = frameType, Payload = frameTypelessBuffer.Slice(bytesCount, length) };
        buffer = buffer.Slice(totalFrameLength);
        return true;
    }

    private ValueTask ConnectionErrorAsync(StreamContext context, Http3ErrorCode error)
    {
        return context.Connection.QuicConnection.CloseAsync((long)error);
    }
}
