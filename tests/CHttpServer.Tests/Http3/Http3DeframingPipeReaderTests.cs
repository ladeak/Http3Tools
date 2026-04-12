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

    [Fact]
    public async Task ReservedFrameIgnored()
    {
        var pipe = new Pipe();
        var sut = new Http3DeframingPipeReader(pipe.Reader);
        await pipe.Writer.WriteAsync(Http3FrameFixture.GetReservedFrame(100), TestContext.Current.CancellationToken);
        pipe.Writer.Complete();
        var result = await sut.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsCompleted);
        Assert.True(result.Buffer.IsEmpty);
    }
}
