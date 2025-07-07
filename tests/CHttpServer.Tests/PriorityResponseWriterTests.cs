using static CHttpServer.Tests.TestBase;

namespace CHttpServer.Tests;

public class PriorityResponseWriterTests
{
    [Fact]
    public async Task ScheduleHeader_WritesToResponse()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        var stream = new Http2Stream(new Http2Connection(ctx), ctx.Features);
        stream.SetPriority(new(1, false));
        sut.ScheduleWriteHeaders(stream);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        await AssertResponseHeaders(pipe);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task ScheduleHeaderUrgency()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        Http2Stream streamHigh = CreateStream(ctx, id: 1, new(1, false));
        Http2Stream streamLow = CreateStream(ctx, id: 2, new(2, false));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertResponseHeaders(pipe);
        var secondFrame = await AssertResponseHeaders(pipe);
        Assert.Equal(streamHigh.StreamId, firstFrame.StreamId);
        Assert.Equal(streamLow.StreamId, secondFrame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task ScheduleHeaderIncremental()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        Http2Stream streamHigh = CreateStream(ctx, id: 1, new(1, true));
        Http2Stream streamLow = CreateStream(ctx, id: 2, new(1, false));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertResponseHeaders(pipe);
        var secondFrame = await AssertResponseHeaders(pipe);
        Assert.Equal(streamHigh.StreamId, firstFrame.StreamId);
        Assert.Equal(streamLow.StreamId, secondFrame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task ScheduleHeaderUrgencyOverIncremental()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        Http2Stream streamHigh = CreateStream(ctx, id: 1, new(1, false));
        Http2Stream streamLow = CreateStream(ctx, id: 2, new(2, true));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertResponseHeaders(pipe);
        var secondFrame = await AssertResponseHeaders(pipe);
        Assert.Equal(streamHigh.StreamId, firstFrame.StreamId);
        Assert.Equal(streamLow.StreamId, secondFrame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task ScheduleHeaderSameLevelInOrder()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        Http2Stream streamHigh = CreateStream(ctx, id: 1, new(2, true));
        Http2Stream streamLow = CreateStream(ctx, id: 2, new(2, true));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertResponseHeaders(pipe);
        var secondFrame = await AssertResponseHeaders(pipe);
        Assert.Equal(streamLow.StreamId, firstFrame.StreamId);
        Assert.Equal(streamHigh.StreamId, secondFrame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task SchedulePingAsHighestPriority()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        Http2Stream streamHigh = CreateStream(ctx, id: 1, new(2, false));
        sut.ScheduleWritePingAck(10);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertPingAck(pipe);
        var secondFrame = await AssertResponseHeaders(pipe);
        Assert.Equal(streamHigh.StreamId, secondFrame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task AfterComplete_Schedule_DoesNotThrow()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        Http2Stream streamHigh = CreateStream(ctx, id: 1, new(2, false));
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);
        cts.Cancel();
        await writerTask;

        sut.ScheduleWritePingAck(10);
        sut.ScheduleWriteHeaders(streamHigh);
        sut.ScheduleWriteData(streamHigh);
        sut.ScheduleWriteTrailers(streamHigh);
        sut.ScheduleWriteWindowUpdate(streamHigh, 100);
        sut.ScheduleEndStream(streamHigh);
        sut.ScheduleResetStream(streamHigh, Http2ErrorCode.REFUSED_STREAM);
    }

    [Fact]
    public async Task EndStream_Invokes_OnCompleted()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        var stream = CreateStream(ctx, id: 1);
        stream.CompleteRequestBodyPipe();
        stream.Writer.Complete();
        var tcs = new TaskCompletionSource();
        stream.OnCompleted(_ => { tcs.SetResult(); return Task.CompletedTask; }, null!);
        sut.ScheduleEndStream(stream);

        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);
        await tcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task EndWriteTrailers_Invokes_OnCompleted()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        var stream = CreateStream(ctx, id: 1);
        stream.CompleteRequestBodyPipe();
        stream.Writer.Complete();
        var tcs = new TaskCompletionSource();
        stream.OnCompleted(_ => { tcs.SetResult(); return Task.CompletedTask; }, null!);
        sut.ScheduleWriteTrailers(stream);

        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);
        await tcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task ResetStream_Invokes_OnCompleted()
    {
        var (pipe, ctx, sut) = CreateResponseWriter();
        var stream = CreateStream(ctx, 1);
        stream.CompleteRequestBodyPipe();
        stream.Writer.Complete();
        var tcs = new TaskCompletionSource();
        stream.OnCompleted(_ => { tcs.SetResult(); return Task.CompletedTask; }, null!);
        sut.ScheduleResetStream(stream, Http2ErrorCode.CANCEL);

        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);
        await tcs.Task.WaitAsync(TestContext.Current.CancellationToken);

        cts.Cancel();
        await writerTask;
    }

    // Test continuations
    // Test DATA frames preempted
    // Test Update_framesize

    private static Http2Stream CreateStream(CHttpConnectionContext ctx, int id, Priority9218 priority = default)
    {
        var stream = new Http2Stream(new Http2Connection(ctx), ctx.Features);
        stream.Initialize((uint)id, 100, 100);
        stream.SetPriority(priority);
        return stream;
    }

    private static (TestDuplexPipe Pipe, CHttpConnectionContext Context, PriorityResponseWriter Writer) CreateResponseWriter()
    {
        var pipe = new TestDuplexPipe();
        var context = new CHttpConnectionContext()
        {
            ConnectionId = 1,
            ServerOptions = new CHttpServerOptions() { UsePriority = true },
            Features = new FeatureCollection(),
            TransportPipe = pipe,
        };
        var writer = new PriorityResponseWriter(new FrameWriter(context), 100);
        return (pipe, context, writer);
    }

    private static async Task<Http2Frame> AssertResponseHeaders(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.HEADERS, frame.Type);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData.AsMemory());
        return frame;
    }

    private static async Task<Http2Frame> AssertPingAck(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.PING, frame.Type);
        Assert.Equal(1, frame.Flags);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData.AsMemory());
        return frame;
    }

    private static async Task<Http2Frame> AssertDataStream(TestDuplexPipe pipe, Memory<byte> destination)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.DATA, frame.Type);
        Assert.Equal((uint)destination.Length, frame.PayloadLength);
        Assert.False(frame.EndStream);
        await pipe.Response.ReadExactlyAsync(destination, TestContext.Current.CancellationToken);
        return frame;
    }

}
