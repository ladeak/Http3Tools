using CHttp.Writers;

namespace CHttp.Tests.Writers;

public class StreamBufferedProcessorTests
{
    [Fact]
    public void StreamBufferedProcessor_CanBeCreated()
    {
        _ = new StreamBufferedProcessor(new MemoryStream());
    }

    [Fact]
    public async Task Run_CopiesDataTo_OutputStream()
    {
        var outputStream = new MemoryStream();
        var input = Enumerable.Range(0, 100).Select(x => (byte)x).ToArray();
        var sut = new StreamBufferedProcessor(outputStream);
        var sutRun = sut.RunAsync(_ => Task.CompletedTask);
        await sut.Pipe.WriteAsync(input);
        sut.Pipe.Complete();
        await sutRun;
        Assert.True(input.SequenceEqual(outputStream.ToArray()));
        Assert.Equal(100, sut.Position);
    }

    [Fact]
    public async Task EmptyArrayRun_CopiesDataTo_OutputStream()
    {
        var outputStream = new MemoryStream();
        var input = new byte[0];
        var sut = new StreamBufferedProcessor(outputStream);
        var sutRun = sut.RunAsync(_ => Task.CompletedTask);
        await sut.Pipe.WriteAsync(input);
        sut.Pipe.Complete();
        await sutRun;
        Assert.True(input.SequenceEqual(outputStream.ToArray()));
    }

    [Fact]
    public async Task LargeArrayRun_CopiesDataTo_OutputStream()
    {
        var outputStream = new MemoryStream();
        var input = Enumerable.Range(0, 100000).Select(x => (byte)x).ToArray();
        var sut = new StreamBufferedProcessor(outputStream);
        var sutRun = sut.RunAsync(_ => Task.CompletedTask);
        await sut.Pipe.WriteAsync(input);
        sut.Pipe.Complete();
        await sutRun;
        Assert.True(input.SequenceEqual(outputStream.ToArray()));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(71)]
    public async Task MultiWrittenArrayRun_CopiesDataTo_OutputStream(int segmentLength)
    {
        var outputStream = new MemoryStream();
        var input = Enumerable.Range(0, 99 * segmentLength).Select(x => (byte)x).ToArray();
        var remainderToWrite = input.AsMemory();
        var sut = new StreamBufferedProcessor(outputStream);
        var sutRun = sut.RunAsync(_ => Task.CompletedTask);

        while (remainderToWrite.Length > 0)
        {
            await sut.Pipe.WriteAsync(remainderToWrite.Slice(0, segmentLength));
            remainderToWrite = remainderToWrite.Slice(segmentLength);
        }

        sut.Pipe.Complete();
        await sutRun;
        Assert.True(input.SequenceEqual(outputStream.ToArray()));
    }

    [Fact]
    public async Task WhenCancelled_Run_Completes()
    {
        var outputStream = new MemoryStream();
        var sut = new StreamBufferedProcessor(outputStream);
        var sutRun = sut.RunAsync(_ => Task.CompletedTask);
        sut.Cancel();
        await sutRun.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, outputStream.Length);
        Assert.Equal(0, sut.Position);
    }
}
