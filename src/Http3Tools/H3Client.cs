using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.QPack;
using System.Net.Quic;
using System.Net.Security;
using System.Text;

namespace Http3Tools;

/// <summary>
/// https://datatracker.ietf.org/doc/rfc9114/
/// </summary>
public class H3Client
{
    public event EventHandler<Frame>? OnFrame;

    public async Task<Frame> TestAsync()
    {
        // Open connection and send settings frame on control stream.
        var options = CreateClientConnectionOptions(new IPEndPoint(IPAddress.Loopback, 5001));
        await using var clientConnection = await QuicConnection.ConnectAsync(options);
        var connecectionContext = new ConnectionContext() { QuicConnection = clientConnection };

        await OpenControlStreamAsync(connecectionContext);

        var inboundCts = new CancellationTokenSource();
        var inboundTasks = Task.Run(() => HandleIncomingStreams(connecectionContext, inboundCts.Token), inboundCts.Token);

        // Open bidirectional stream to send request.
        var clientStream = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        var getRequestHeader = BuildGetHeaderFrame();
        await clientStream.WriteAsync(getRequestHeader, completeWrites: true);
        await ReadAsync(connecectionContext, clientStream, false, CancellationToken.None);

        var goAwayFrame = BuildGoAwayFrame();
        inboundCts.Cancel();
        await inboundTasks;

        return new Frame() { Data = "test", SourceStream = "test" };
    }

    async Task HandleIncomingStreams(ConnectionContext connectionCtx, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var inbound = await connectionCtx.QuicConnection.AcceptInboundStreamAsync(token);
                _ = Task.Run(() => ProcessIncomingStream(connectionCtx, inbound, token), token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task OpenControlStreamAsync(ConnectionContext connectionContext)
    {
        var clientControl = await connectionContext.QuicConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional);

        var streamIdentifier = new byte[1];
        streamIdentifier[0] = (byte)Http3StreamType.Control;
        await clientControl.WriteAsync(streamIdentifier);
        var settingsFrame = BuildSettingsFrame();
        await clientControl.WriteAsync(settingsFrame);
    }

    private byte[] BuildGetHeaderFrame()
    {
        var frame = ArrayPool<byte>.Shared.Rent(32);
        try
        {
            Span<byte> buffer = frame.AsSpan(1 + VariableLengthIntegerHelper.MaximumEncodedLength);
            var payloadLength = BuildQPACKFrame(buffer);
            int payloadLengthSize = VariableLengthIntegerHelper.GetByteCount(payloadLength);

            buffer = frame.AsSpan(1 + VariableLengthIntegerHelper.MaximumEncodedLength - 1 - payloadLengthSize, payloadLength + 1 + payloadLengthSize);

            // H3 Header frame
            // Header frame type
            buffer[0] = (byte)Http3FrameType.Headers;
            VariableLengthIntegerHelper.WriteInteger(buffer.Slice(1), payloadLength);
            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    /// <summary>
    /// Send GET request header
    /// </summary>
    /// <returns></returns>
    private int BuildQPACKFrame(Span<byte> buffer)
    {
        // Add QPACK header block prefix.
        //https://datatracker.ietf.org/doc/html/draft-ietf-quic-qpack-11#section-4.5.1
        buffer[0] = 0;
        buffer[1] = 0;
        buffer = buffer.Slice(2);
        int payloadLength = 2;

        // Write method
        QPackEncoder.EncodeStaticIndexedHeaderField(H3StaticTable.MethodGet, buffer, out var length);
        payloadLength += length;
        buffer = buffer.Slice(length);

        // Write scheme
        QPackEncoder.EncodeStaticIndexedHeaderField(H3StaticTable.SchemeHttps, buffer, out length);
        payloadLength += length;
        buffer = buffer.Slice(length);

        // Write Host
        QPackEncoder.EncodeLiteralHeaderFieldWithStaticNameReference(H3StaticTable.Authority, "localhost", null, buffer, out length);
        payloadLength += length;
        buffer = buffer.Slice(length);

        // Write Path
        QPackEncoder.EncodeStaticIndexedHeaderField(H3StaticTable.PathSlash, buffer, out length);
        payloadLength += length;
        return payloadLength;
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

    private byte[] BuildSettingsFrame()
    {
        Span<byte> buffer = stackalloc byte[1 + VariableLengthIntegerHelper.MaximumEncodedLength];

        buffer[0] = (byte)Http3SettingType.MaxHeaderListSize;
        int integerLength = VariableLengthIntegerHelper.WriteInteger(buffer.Slice(1), 1024);

        var payloadSize = 1 + integerLength;
        return BuildFrameHeader(Http3FrameType.Settings, buffer.Slice(0, payloadSize));
    }

    private static byte[] BuildFrameHeader(Http3FrameType type, ReadOnlySpan<byte> payload)
    {
        int payloadSize = payload.Length;
        Span<byte> buffer = stackalloc byte[1 + VariableLengthIntegerHelper.MaximumEncodedLength + payloadSize];
        int payloadSizeByteCount = VariableLengthIntegerHelper.GetByteCount(payloadSize); // includes the setting ID and the integer value.
        buffer[0] = (byte)type;
        VariableLengthIntegerHelper.WriteInteger(buffer.Slice(1), payloadSize);
        payload.CopyTo(buffer.Slice(1 + payloadSizeByteCount));
        return buffer.Slice(0, 1 + payloadSizeByteCount + payloadSize).ToArray();
    }

    static QuicClientConnectionOptions CreateClientConnectionOptions(EndPoint remoteEndPoint)
    {
        return new QuicClientConnectionOptions
        {
            MaxInboundBidirectionalStreams = 0,
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
            DefaultStreamErrorCode = (long)Http3ErrorCode.RequestCancelled,
            DefaultCloseErrorCode = (long)Http3ErrorCode.NoError,
        };
    }

    private async Task ProcessIncomingStream(ConnectionContext connectionCtx, QuicStream stream, CancellationToken token)
    {
        await ReadAsync(connectionCtx, stream, true, token);
    }

    private async Task ReadAsync(ConnectionContext connectionCtx, QuicStream clientStream, bool incoming, CancellationToken token)
    {
        var pipe = new Pipe();
        var writer = pipe.Writer;
        var copyTask = clientStream.CopyToAsync(writer, token);
        var context = new StreamContext()
        {
            Reader = pipe.Reader,
            Stream = clientStream,
            Connection = connectionCtx,
            Incoming = incoming
        };
        var pipeTask = ProcessPipeAsync(context, token);

        await copyTask;
        await pipe.Writer.CompleteAsync();
        await pipeTask;
    }

    private async Task ProcessPipeAsync(StreamContext context, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await context.Reader.ReadAsync(token);
                if (read.IsCanceled)
                    break;

                var buffer = read.Buffer;

                if (context.Incoming)
                {
                    // Read HTTP stream type
                    long streamTypeRaw = VariableLengthIntegerHelper.GetInteger(in buffer, out var consumed, out var examined);
                    if (!Enum.IsDefined<Http3StreamType>((Http3StreamType)streamTypeRaw))
                    {
                        // error H3_STREAM_CREATION_ERROR or read data without processing
                    }
                    buffer = buffer.Slice(consumed);
                    context.StreamType = (Http3StreamType)streamTypeRaw; // this is the stream type, not the Id
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
