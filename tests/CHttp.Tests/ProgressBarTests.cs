using CHttp.Abstractions;
using CHttp.Writers;

namespace CHttp.Tests;

public class ProgressBarTests
{
    [Fact]
    public async Task SingleLoop_Writes100PercentAndZero()
    {
        var testConsole = new TestConsolePerWrite();
        var loopHandle = new SyncedAwaiter(0);
        var sut = new ProgressBar<long>(testConsole, loopHandle);
        var sutTask = sut.RunAsync<SizeFormatter<long>>(loopHandle.Token);
        await loopHandle.CompleteLoopAsync();
        await sutTask;
        Assert.Equal(@"
[=-----]   0 B
100%       0 B

", testConsole.Text);
    }

    [Fact]
    public async Task Run_Resets_Size()
    {
        var testConsole = new TestConsolePerWrite();
        var loopHandle = new SyncedAwaiter(0);
        var sut = new ProgressBar<long>(testConsole, loopHandle);
        sut.Set(100);
        var sutTask = sut.RunAsync<SizeFormatter<long>>(loopHandle.Token);
        await loopHandle.CompleteLoopAsync();
        await sutTask;
        Assert.Equal(@"
[=-----]   0 B
100%       0 B

", testConsole.Text);
    }

    [Fact]
    public async Task WhenEnds_Writes100PercentAndSize()
    {
        var testConsole = new TestConsolePerWrite();
        var loopHandle = new SyncedAwaiter(0);
        var sut = new ProgressBar<long>(testConsole, loopHandle);
        var sutTask = sut.RunAsync<SizeFormatter<long>>(loopHandle.Token);
        await loopHandle.WaitLoopAsync();
        sut.Set(100);
        await loopHandle.LoopAsync();
        await loopHandle.WaitLoopAsync();
        await loopHandle.CompleteLoopAsync();
        await sutTask;
        Assert.Equal(@"
[=-----]   0 B
[-=----] 100 B
100%     100 B

", testConsole.Text);
    }

    [Fact]
    public async Task MultipleLoops_IncrasesSize()
    {
        var testConsole = new TestConsolePerWrite();
        var loopHandle = new SyncedAwaiter(0);
        var sut = new ProgressBar<long>(testConsole, loopHandle);
        var sutTask = sut.RunAsync<SizeFormatter<long>>(loopHandle.Token);
        for (int i = 1; i < 4; i++)
        {
            await loopHandle.WaitLoopAsync();
            sut.Set(i * 100);
            await loopHandle.LoopAsync();
        }
        await loopHandle.WaitLoopAsync();
        await loopHandle.CompleteLoopAsync();
        await sutTask;
        Assert.Equal(@"
[=-----]   0 B
[-=----] 100 B
[--=---] 200 B
[---=--] 300 B
100%     300 B

", testConsole.Text);
    }

    [Fact]
    public async Task Complete_Cycle_StartsOverSemicolons()
    {
        var testConsole = new TestConsolePerWrite();
        var loopHandle = new SyncedAwaiter(0);
        var sut = new ProgressBar<long>(testConsole, loopHandle);
        var sutTask = sut.RunAsync<SizeFormatter<long>>(loopHandle.Token);
        for (int i = 1; i < 12; i++)
        {
            await loopHandle.WaitLoopAsync();
            sut.Set(i * 100);
            await loopHandle.LoopAsync();
        }
        await loopHandle.WaitLoopAsync();
        await loopHandle.CompleteLoopAsync();
        await sutTask;
        Assert.Equal(@"
[=-----]   0 B
[-=----] 100 B
[--=---] 200 B
[---=--] 300 B
[----=-] 400 B
[-----=] 500 B
[=-----] 600 B
[-=----] 700 B
[--=---] 800 B
[---=--] 900 B
[----=-]1000 B
[-----=]   1 KB
100%       1 KB

", testConsole.Text);
    }

    [Fact]
    public async Task DisplayAllSizes()
    {
        var testConsole = new TestConsolePerWrite();
        var loopHandle = new SyncedAwaiter(0);
        var sut = new ProgressBar<long>(testConsole, loopHandle);
        var sutTask = sut.RunAsync<SizeFormatter<long>>(loopHandle.Token);
        for (int i = 1; i < 6; i++)
        {
            await loopHandle.WaitLoopAsync();
            sut.Set((long)Math.Pow(1024, i));
            await loopHandle.LoopAsync();
        }
        await loopHandle.WaitLoopAsync();
        await loopHandle.CompleteLoopAsync();
        await sutTask;
        Assert.Equal(@"
[=-----]   0 B
[-=----]   1 KB
[--=---]   1 MB
[---=--]   1 GB
[----=-]   1 TB
[-----=]1024 TB
100%    1024 TB

", testConsole.Text);
    }
}