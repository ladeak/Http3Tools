using System.IO.Pipelines;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class Http3FrameWriterTests
{
    [Fact]
    public async Task WriteGoAway_SingleBytePayload()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        Http3FrameWriter.WriteGoAway(pipe, 63);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x07, 0x01, 0x3F }));
    }

    [Fact]
    public async Task WriteGoAway_TwoBytePayload()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        Http3FrameWriter.WriteGoAway(pipe, 64);
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x07, 0x02, 0x40, 0x40 }));
    }

    [Fact]
    public async Task WriteSettings_SingleBytePayload()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        Http3FrameWriter.WriteSettings(pipe, new Http3Settings() { ServerMaxFieldSectionSize = 63 });
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x04, 0x02, 0x06, 0x3F }));
    }

    [Fact]
    public async Task WriteSettings_FourBytePayload()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        Http3FrameWriter.WriteSettings(pipe, new Http3Settings() { ServerMaxFieldSectionSize = 1073741823 });
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x04, 0x05, 0x06, 0xBF, 0xFF, 0xFF, 0xFF }));
    }

    [Fact]
    public async Task WriteSettings_NoPayload_WritesReservedEntry()
    {
        var stream = new MemoryStream();
        var pipe = PipeWriter.Create(stream);
        Http3FrameWriter.WriteSettings(pipe, new Http3Settings() { ServerMaxFieldSectionSize = null });
        await pipe.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(stream.ToArray().SequenceEqual(new byte[] { 0x04, 0x02, 0x21, 0x00 }));
    }
}
