using System.Buffers;
using System.Diagnostics;
using System.IO;
using Xunit.Sdk;
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
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(1, false));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, false));
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
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(1, true));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(1, false));
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
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(1, false));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, true));
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
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(2, true));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, true));
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
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(2, false));
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
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(2, false));
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
        stream.CompleteRequest();
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
        stream.CompleteRequest();
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
        stream.CompleteRequest();
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

    [Fact]
    public async Task ScheduleHeaderWithContinuations()
    {
        var frameSize = 30_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(1, false));
        streamHigh.ResponseHeaders.Add("header1", new string('a', (int)frameSize * 15));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, true));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertResponseHeaders(pipe);
        for (int i = 0; i < 15; i++)
            await AssertContinuationHeaders(pipe);

        var secondFrame = await AssertResponseHeaders(pipe);
        Assert.Equal(streamHigh.StreamId, firstFrame.StreamId);
        Assert.Equal(streamLow.StreamId, secondFrame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task ScheduleMultiple_HeaderWithContinuations()
    {
        var frameSize = 30_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        Http2Stream streamMiddle = CreateStream(ctx, id: 1, priority: new(1, false));
        streamMiddle.ResponseHeaders.Add("header", new string('a', (int)frameSize * 16));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, true));
        Http2Stream streamHigh = CreateStream(ctx, id: 3, priority: new(1, true));
        streamHigh.ResponseHeaders.Add("header", new string('a', (int)frameSize));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamMiddle);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var frame = await AssertResponseHeaders(pipe);
        Assert.Equal(3UL, frame.StreamId);
        var continuation = await AssertContinuationHeaders(pipe);
        Assert.Equal(3UL, continuation.StreamId);

        frame = await AssertResponseHeaders(pipe);
        Assert.Equal(1UL, frame.StreamId);
        for (int i = 0; i < 16; i++)
        {
            frame = await AssertContinuationHeaders(pipe);
            Assert.Equal(1UL, frame.StreamId);
        }

        frame = await AssertResponseHeaders(pipe);
        Assert.Equal(2UL, frame.StreamId);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task UpdateFrameSize_ScheduleHeaderWithContinuations()
    {
        var frameSize = 30_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(1, false));
        streamHigh.ResponseHeaders.Add("header1", new string('a', (int)frameSize * 15));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, true));
        sut.ScheduleWriteHeaders(streamLow);
        sut.ScheduleWriteHeaders(streamHigh);
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        var firstFrame = await AssertResponseHeaders(pipe);
        for (int i = 0; i < 15; i++)
            await AssertContinuationHeaders(pipe);
        var secondFrame = await AssertResponseHeaders(pipe);

        frameSize = frameSize * 2;
        sut.UpdateFrameSize(frameSize);
        sut.ScheduleWriteHeaders(streamHigh);
        firstFrame = await AssertResponseHeaders(pipe);
        for (int i = 0; i < 7; i++)
            await AssertContinuationHeaders(pipe);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task SingleStream_UpdateFrameSize_ScheduleData()
    {
        var frameSize = 20_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        var connection = new Http2Connection(ctx) { ResponseWriter = sut };
        var h2Stream = new Http2Stream(connection, ctx.Features);
        h2Stream.Initialize(1U, 100, 100);
        h2Stream.UpdateWindowSize(frameSize * 15);
        connection.UpdateConnectionWindowSize(frameSize * 15);

        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        await h2Stream.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);

        await AssertResponseHeaders(pipe);
        for (int i = 0; i < 15; i++)
            await AssertDataFrame(pipe, streamId: 1);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task UpdateFrameSize_ScheduleData()
    {
        var frameSize = 20_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);

        var connection = new Http2Connection(ctx) { ResponseWriter = sut };
        var streamHigh = new Http2Stream(connection, ctx.Features);
        streamHigh.Initialize(1, 100, 100);
        streamHigh.SetPriority(new(1, false));
        streamHigh.UpdateWindowSize(frameSize * 15 * 2);
        Http2Stream streamLow = new Http2Stream(connection, ctx.Features);
        streamLow.Initialize(2, 100, 100);
        streamLow.SetPriority(new(2, true));
        streamLow.UpdateWindowSize(frameSize * 15);
        connection.UpdateConnectionWindowSize(frameSize * 15 * 3);

        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        await streamHigh.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);
        await streamLow.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);

        await AssertResponseHeaders(pipe);
        for (int i = 0; i < 15; i++)
            await AssertDataFrame(pipe, streamId: 1);

        await AssertResponseHeaders(pipe);
        for (int i = 0; i < 15; i++)
            await AssertDataFrame(pipe, streamId: 2);

        sut.UpdateFrameSize(frameSize * 2);
        await streamHigh.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);
        for (int i = 0; i < 8; i++)
            await AssertDataFrame(pipe, streamId: 1);

        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task Preempted_Mixes_ScheduleData_AssertFrames()
    {
        var frameSize = 50_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        var connection = new Http2Connection(ctx) { ResponseWriter = sut };
        connection.UpdateConnectionWindowSize(frameSize * 15 * 3);

        Http2Stream streamHigh = new Http2Stream(connection, ctx.Features);
        streamHigh.Initialize(streamId: 1, 0, 0);
        streamHigh.SetPriority(new(1, false));
        streamHigh.UpdateWindowSize(frameSize * 15);
        streamHigh.CompleteRequest();
        var streamHighWriting = streamHigh.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);
        Http2Stream streamLow = new Http2Stream(connection, ctx.Features);
        streamLow.Initialize(streamId: 2, 0, 0);
        streamLow.SetPriority(new(2, true));
        streamLow.UpdateWindowSize(frameSize * 15);
        streamLow.CompleteRequest();
        var streamLowWriting = streamLow.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);
        Http2Stream streamMiddle = new Http2Stream(connection, ctx.Features);
        streamMiddle.Initialize(streamId: 3, 0, 0);
        streamMiddle.SetPriority(new(1, false));
        streamMiddle.UpdateWindowSize(frameSize * 15);
        streamMiddle.CompleteRequest();
        var streamMiddleWriting = streamMiddle.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);

        // Start output writer
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        await streamHighWriting;
        await streamMiddleWriting;
        await streamLowWriting;
        streamHigh.Writer.Complete();
        streamMiddle.Writer.Complete();
        streamLow.Writer.Complete();

        List<Http2Frame> frames = new();
        int endStreamCount = 0;
        while (endStreamCount < 3)
        {
            var frame = await ReadFrame(pipe);
            if (frame.EndStream)
                endStreamCount++;
            frames.Add(frame);
        }

        Assert.Equal(3, frames.Count(x => x.Type == Http2FrameType.HEADERS));
        Assert.Equal(3, frames.Count(x => x.Type == Http2FrameType.DATA && x.EndStream));
        Assert.Equal(45, frames.Count(x => x.Type == Http2FrameType.DATA && !x.EndStream));

        // Stop output writer
        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task Preempted_Mixes_ScheduleData()
    {
        var frameSize = 50_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        var connection = new Http2Connection(ctx) { ResponseWriter = sut };
        connection.UpdateConnectionWindowSize(frameSize * 15 * 3);
        Http2Stream streamHigh = CreateStream(ctx, id: 1, priority: new(1, false));
        streamHigh.CompleteRequest(new ReadOnlySequence<byte>(new byte[frameSize * 15]));
        Http2Stream streamLow = CreateStream(ctx, id: 2, priority: new(2, true));
        streamLow.CompleteRequest(new ReadOnlySequence<byte>(new byte[frameSize * 15]));
        Http2Stream streamMiddle = CreateStream(ctx, id: 3, priority: new(1, false));
        streamMiddle.CompleteRequest(new ReadOnlySequence<byte>(new byte[frameSize * 15]));
        sut.ScheduleWriteData(streamHigh);
        sut.ScheduleWriteData(streamMiddle);
        sut.ScheduleWriteData(streamLow);

        // Start output writer
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        List<Http2Frame> frames = new();
        while (frames.Count < 45)
        {
            var frame = await ReadFrame(pipe);
            frames.Add(frame);
        }

        // All stream 2 DATA frames are after stream 1 and stream 3 DATA frames
        var stream2StartIndex = frames.FindIndex(x => x.Type == Http2FrameType.DATA && x.StreamId == 2);
        Assert.Equal(30, stream2StartIndex);
        Assert.DoesNotContain(frames.Index(), x => x.Item.Type == Http2FrameType.DATA && !x.Item.EndStream && x.Item.StreamId == 1 && x.Index > stream2StartIndex);
        Assert.DoesNotContain(frames.Index(), x => x.Item.Type == Http2FrameType.DATA && !x.Item.EndStream && x.Item.StreamId == 3 && x.Index > stream2StartIndex);

        // Stream 1 and stream 3 DATA frames are interleaved
        int contiuousStreakOfFramesForSingleStream = 0;
        for (int i = 1; i < stream2StartIndex; i++)
        {
            var currentFrame = frames[i];
            if (currentFrame.Type == Http2FrameType.DATA && !currentFrame.EndStream && frames[i - 1].StreamId == currentFrame.StreamId)
                contiuousStreakOfFramesForSingleStream++;
            else
                contiuousStreakOfFramesForSingleStream = 0;
        }

        Assert.True(contiuousStreakOfFramesForSingleStream < 8);

        // Stop output writer
        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task DataChunked_By_ClientWindowSize()
    {
        var frameSize = 50_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        var connection = new Http2Connection(ctx) { ResponseWriter = sut };
        connection.UpdateConnectionWindowSize(frameSize * 15 * 3);

        Http2Stream streamHigh = new Http2Stream(connection, ctx.Features);
        streamHigh.Initialize(streamId: 1, 0, 0);
        streamHigh.SetPriority(new(1, false));
        streamHigh.CompleteRequest();
        var streamHighWriting = streamHigh.Writer.WriteAsync(new byte[frameSize * 3], TestContext.Current.CancellationToken);

        // Start output writer
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        await AssertResponseHeaders(pipe);

        // Writes one half frame
        streamHigh.UpdateWindowSize(frameSize / 2);
        var frame = await ReadFrame(pipe);
        Assert.Equal(frame.PayloadLength, frameSize / 2);

        // Writes 2 frames, one full frame and one half frame
        streamHigh.UpdateWindowSize(frameSize * 3 / 2);
        frame = await ReadFrame(pipe);
        Assert.Equal(frame.PayloadLength, frameSize);
        frame = await ReadFrame(pipe);
        Assert.Equal(frame.PayloadLength, frameSize / 2);

        // Writes one full frame
        streamHigh.UpdateWindowSize(frameSize);
        frame = await ReadFrame(pipe);
        Assert.Equal(frame.PayloadLength, frameSize);

        await streamHighWriting;
        streamHigh.Writer.Complete();

        // Stop output writer
        cts.Cancel();
        await writerTask;
    }

    [Fact]
    public async Task DataChunkedAndPreempted_By_ClientWindowSize()
    {
        var frameSize = 20_000U;
        var (pipe, ctx, sut) = CreateResponseWriter();
        sut.UpdateFrameSize(frameSize);
        var connection = new Http2Connection(ctx) { ResponseWriter = sut };
        connection.UpdateConnectionWindowSize(frameSize * 15 * 3);

        Http2Stream streamHigh = new Http2Stream(connection, ctx.Features);
        streamHigh.Initialize(streamId: 1, 0, 0);
        streamHigh.SetPriority(new(1, false));
        streamHigh.CompleteRequest();
        var streamHighWriting = streamHigh.Writer.WriteAsync(new byte[frameSize * 15], TestContext.Current.CancellationToken);

        // Start output writer
        CancellationTokenSource cts = new();
        var writerTask = sut.RunAsync(cts.Token);

        await AssertResponseHeaders(pipe);

        // Writes one half frame
        streamHigh.UpdateWindowSize(frameSize / 2);
        var frame = await ReadFrame(pipe);
        Assert.Equal(frame.PayloadLength, frameSize / 2);

        // Writes 2 frames, one full frame and one half frame
        streamHigh.UpdateWindowSize(frameSize * 14);
        for (int i = 0; i < 14; i++)
        {
            frame = await ReadFrame(pipe);
            Assert.Equal(frame.PayloadLength, frameSize);
        }

        // Writes one half frame
        streamHigh.UpdateWindowSize(frameSize / 2);
        frame = await ReadFrame(pipe);
        Assert.Equal(frame.PayloadLength, frameSize / 2);

        await streamHighWriting;
        streamHigh.Writer.Complete();

        // Stop output writer
        cts.Cancel();
        await writerTask;
    }

    private static Http2Stream CreateStream(CHttpConnectionContext ctx,
        int id, PriorityResponseWriter? writer = null, Priority9218 priority = default)
    {
        var stream = new Http2Stream(new Http2Connection(ctx) { ResponseWriter = writer }, ctx.Features);
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
        var writer = new PriorityResponseWriter(new FrameWriter(context), 20_000);
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

    private static async Task<Http2Frame> AssertContinuationHeaders(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.CONTINUATION, frame.Type);
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

    private static async Task<Http2Frame> AssertDataFrame(TestDuplexPipe pipe, Memory<byte> destination)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.DATA, frame.Type);
        Assert.Equal((uint)destination.Length, frame.PayloadLength);
        Assert.False(frame.EndStream);
        await pipe.Response.ReadExactlyAsync(destination, TestContext.Current.CancellationToken);
        return frame;
    }

    private static async Task<Http2Frame> AssertDataFrame(TestDuplexPipe pipe, uint streamId)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.DATA, frame.Type);
        Assert.Equal(streamId, frame.StreamId);
        Assert.False(frame.EndStream);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData, TestContext.Current.CancellationToken);
        return frame;
    }

    private static async Task<Http2Frame> ReadFrame(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData, TestContext.Current.CancellationToken);
        return frame;
    }
}
