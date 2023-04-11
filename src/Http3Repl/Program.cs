using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.QPack;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using Microsoft.IO;

#pragma warning disable CA1416 // Validate platform compatibility

//var client = new HttpClient();
//var response = await client.SendAsync(new HttpRequestMessage()
//{
//    Method = HttpMethod.Get,
//    RequestUri = new Uri("https://localhost:5001"),
//    VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
//    Version = HttpVersion.Version30
//});
//response.EnsureSuccessStatusCode();
//var content = await response.Content.ReadAsStringAsync();
//Console.WriteLine(content);
//Console.ReadLine();


//https://datatracker.ietf.org/doc/rfc9114/

RecyclableMemoryStreamManager _manager = new RecyclableMemoryStreamManager();
await TestAsync();

async Task TestAsync()
{
    // Open connection and send settings frame on control stream.
    var options = CreateClientConnectionOptions(new IPEndPoint(IPAddress.Loopback, 5001));
    await using var clientConnection = await QuicConnection.ConnectAsync(options);
    var connecectionContext = new ConnectionContext() { QuicConnection = clientConnection };

    var settingsFrame = BuildSettingsFrame();
    var clientControl = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional);
    await clientControl.WriteAsync(settingsFrame);

    var inboundCts = new CancellationTokenSource();
    var inboundTasks = Task.Run(() => HandleIncomingStreams(connecectionContext, inboundCts.Token), inboundCts.Token);

    // Open bidirectional stream to send request.
    var clientStream = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
    var getRequestHeader = BuildGetHeaderFrame();
    await clientStream.WriteAsync(getRequestHeader, completeWrites: true);
    await ReadAsync(connecectionContext, clientStream, CancellationToken.None);

    var goAwayFrame = BuildGoAwayFrame();
    inboundCts.Cancel();
    await inboundTasks;



}

async Task HandleIncomingStreams(ConnectionContext connectionCtx,CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try
        {
            var inbound = await connectionCtx.QuicConnection.AcceptInboundStreamAsync(token);
            _ = Task.Run(() => ReadAsync(connectionCtx, inbound, token), token);
        }
        catch (OperationCanceledException)
        { 
        }
    }
}

static byte[] BuildGetHeaderFrame()
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
static int BuildQPACKFrame(Span<byte> buffer)
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

static byte[] BuildGoAwayFrame()
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

static byte[] BuildSettingsFrame()
{
    Span<byte> buffer = stackalloc byte[4 + VariableLengthIntegerHelper.MaximumEncodedLength];

    int integerLength = VariableLengthIntegerHelper.WriteInteger(buffer.Slice(4), 1024);
    int payloadLength = 1 + integerLength; // includes the setting ID and the integer value.

    buffer[0] = (byte)Http3StreamType.Control;
    buffer[1] = (byte)Http3FrameType.Settings;
    buffer[2] = (byte)payloadLength;
    buffer[3] = (byte)Http3SettingType.MaxHeaderListSize;

    return buffer.Slice(0, 4 + integerLength).ToArray();
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

async Task ReadAsync(ConnectionContext connectionCtx, QuicStream clientStream, CancellationToken token)
{
    var pipe = new Pipe();
    var writer = pipe.Writer;
    var copyTask = clientStream.CopyToAsync(writer, token);
    var context = new StreamContext() { StreamType = clientStream.Type, Reader = pipe.Reader, Stream = clientStream, Connection = connectionCtx };
    var pipeTask = ProcessPipeAsync(context, token);

    await copyTask;
    await pipe.Writer.CompleteAsync();
    await pipeTask;
}

async Task ProcessPipeAsync(StreamContext context, CancellationToken token)
{
    try
    {
        while (!token.IsCancellationRequested)
        {
            var read = await context.Reader.ReadAsync(token);
            if (read.IsCanceled)
                break;

            var buffer = read.Buffer;
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

Task ProcessFrameAsync(DataFrame frame, StreamContext context)
{
    return frame.Type switch
    {
        Http3FrameType.Data => ProcessDataFrame(frame),
        Http3FrameType.Headers => ProcessHeaderFrame(frame, context),
        _ => Task.CompletedTask
    };
}

Task ProcessHeaderFrame(DataFrame frame, StreamContext context)
{
    context.HeaderDecoder ??= new QPackDecoder((int)frame.Payload.Length);
    context.HeaderDecoder.Decode(frame.Payload, true, new HeadersHandler());
    return Task.CompletedTask;
}

Task ProcessDataFrame(DataFrame frame)
{
    long count = Encoding.UTF8.GetChars(frame.Payload, new ConsoleBufferWriter());
    return Task.CompletedTask;
}

static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out DataFrame frame)
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

internal class HeadersHandler : IHttpStreamHeadersHandler
{
    public void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
    }

    public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
    }

    public void OnHeadersComplete(bool endStream)
    {
    }

    public void OnStaticIndexedHeader(int index)
    {
        var field = H3StaticTable.Get(index);
        Console.WriteLine(field.ToString());
    }

    public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
    {
        var field = H3StaticTable.Get(index);
        Console.WriteLine($"{field}{Encoding.ASCII.GetString(value)}");
    }
}



#pragma warning restore CA1416 // Validate platform compatibility