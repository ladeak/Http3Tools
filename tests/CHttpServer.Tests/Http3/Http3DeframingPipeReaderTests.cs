using System.ComponentModel.DataAnnotations;
using System.IO.Pipelines;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class Http3DeframingPipeReaderTests
{
    [Fact]
    public async Task HeadersFrameThrows()
    {
        var pipe = new Pipe();
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetHeadersFrame(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<Http3ConnectionException>(async () => await sut.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(32)]
    [InlineData(1024)]
    [InlineData(32384)]
    [InlineData(32385)]
    public async Task ReservedFrameIgnored(int reservedFrameSize)
    {
        var pipe = new Pipe();
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(reservedFrameSize), TestContext.Current.CancellationToken);
        pipe.Writer.Complete();
        var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsCompleted);
        Assert.True(result.Buffer.IsEmpty);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(32)]
    [InlineData(1024)]
    [InlineData(32384)]
    [InlineData(32385)]
    public async Task DataFrame(int frameSize)
    {
        var pipe = new Pipe();
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        pipe.Writer.Complete();
        var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsCompleted);
        Assert.Equal(frameSize, result.Buffer.Length);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(16192)]
    [InlineData(32385)]
    public async Task MixedFrames_Write_Read(int frameSize)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4096), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4096), TestContext.Current.CancellationToken);
        pipe.Writer.Complete();
        long totalDataLength = 0;
        while (true)
        {
            var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
            totalDataLength += result.Buffer.Length;
            sut.AdvanceTo(result.Buffer.End);
            if ((result.IsCompleted && result.Buffer.IsEmpty) || result.IsCanceled)
                break;
        }
        Assert.Equal(frameSize * 2, totalDataLength);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(16192)]
    [InlineData(32385)]
    public async Task MixedFrames_Parallel_ReadWrite(int frameSize)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        var readingTask = ReadAsync(sut);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4097), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4095), TestContext.Current.CancellationToken);
        pipe.Writer.Complete();
        long totalDataLength = await readingTask;
        Assert.Equal(frameSize * 2, totalDataLength);

        static async Task<long> ReadAsync(Http3DeframingPipeReader sut)
        {
            long totalDataLength = 0;
            while (true)
            {
                var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
                totalDataLength += result.Buffer.Length;
                sut.AdvanceTo(result.Buffer.End);
                if ((result.IsCompleted && result.Buffer.IsEmpty) || result.IsCanceled)
                    break;
            }

            return totalDataLength;
        }
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(4096)]
    [InlineData(8095)]
    public async Task CopyToAsyncStream_Parallel_ReadWrite(int frameSize)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        var readingTask = ReadToStreamAsync(sut);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4097), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4095), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);

        pipe.Writer.Complete();
        long totalDataLength = await readingTask;
        Assert.Equal(frameSize * 2, totalDataLength);

        static async Task<long> ReadToStreamAsync(Http3DeframingPipeReader sut)
        {
            var ms = new MemoryStream();
            await sut.CopyToAsync(ms);
            return ms.Length;
        }
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(4096)]
    [InlineData(8095)]
    public async Task CopyToAsyncPipe_Parallel_ReadWrite(int frameSize)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        var readingTask = ReadToPipeAsync(sut);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4097), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4095), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);

        pipe.Writer.Complete();
        long totalDataLength = await readingTask;
        Assert.Equal(frameSize * 2, totalDataLength);

        static async Task<long> ReadToPipeAsync(Http3DeframingPipeReader sut)
        {
            var ms = new MemoryStream();
            var copyPipe = PipeWriter.Create(ms, new(leaveOpen: true));
            await sut.CopyToAsync(copyPipe);
            return ms.Length;
        }
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(4096)]
    [InlineData(8095)]
    public async Task Reset(int frameSize)
    {
        var sut = new Http3DeframingPipeReader(PipeReader.Create(Stream.Null));
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        sut.Reset(pipe.Reader);
        var readingTask = ReadToStreamAsync(sut);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4097), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4095), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);

        pipe.Writer.Complete();
        long totalDataLength = await readingTask;
        Assert.Equal(frameSize * 2, totalDataLength);

        static async Task<long> ReadToStreamAsync(Http3DeframingPipeReader sut)
        {
            var ms = new MemoryStream();
            await sut.CopyToAsync(ms);
            return ms.Length;
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(4096)]
    [InlineData(8095)]
    public async Task AdvanceTo_Small(int frameSize)
    {
        var sut = new Http3DeframingPipeReader(PipeReader.Create(Stream.Null));
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        sut.Reset(pipe.Reader);
        var readingTask = ReadAsync(sut);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4097), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4095), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);

        pipe.Writer.Complete();
        long totalDataLength = await readingTask;
        Assert.Equal(frameSize * 2, totalDataLength);

        static async Task<long> ReadAsync(Http3DeframingPipeReader sut)
        {
            long totalLength = 0;
            while (true)
            {
                var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
                var offset = result.Buffer.Length > 128 ? 128 : result.Buffer.Length;
                sut.AdvanceTo(result.Buffer.GetPosition(offset));
                totalLength += offset;
                if ((result.IsCompleted && result.Buffer.IsEmpty) || result.IsCanceled)
                    break;
            }
            return totalLength;
        }
    }

    [Theory]
    [InlineData(16001)]
    [InlineData(32022)]
    [InlineData(64500)]
    public async Task AdvanceTo_Large(int frameSize)
    {
        var sut = new Http3DeframingPipeReader(PipeReader.Create(Stream.Null));
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        sut.Reset(pipe.Reader);
        var readingTask = ReadAsync(sut);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4097), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4095), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);

        pipe.Writer.Complete();
        long totalDataLength = await readingTask;
        Assert.Equal(frameSize * 2, totalDataLength);

        static async Task<long> ReadAsync(Http3DeframingPipeReader sut)
        {
            long totalLength = 0;
            while (true)
            {
                var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
                var offset = result.Buffer.Length > 8192 ? 8192 : result.Buffer.Length;
                sut.AdvanceTo(result.Buffer.GetPosition(offset));
                totalLength += offset;
                if ((result.IsCompleted && result.Buffer.IsEmpty) || result.IsCanceled)
                    break;
            }
            return totalLength;
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(16192)]
    [InlineData(32385)]
    public async Task MixedFrames_Write_TryRead(int frameSize)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4096), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetDataFrame(frameSize), TestContext.Current.CancellationToken);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(4096), TestContext.Current.CancellationToken);
        pipe.Writer.Complete();
        long totalDataLength = 0;
        while (sut.TryRead(out var result))
        {
            totalDataLength += result.Buffer.Length;
            sut.AdvanceTo(result.Buffer.End);
            if ((result.IsCompleted && result.Buffer.IsEmpty) || result.IsCanceled)
                break;
        }
        Assert.Equal(frameSize * 2, totalDataLength);
    }
}
