namespace CHttpServer.Tests;

public class TaskExtensionsTests
{
    [Fact]
    public async Task ShouldNotThrow()
    {
        await Task.CompletedTask.AllowCancellation();
        await Task.FromCanceled(new CancellationToken(true)).AllowCancellation();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Task.FromException(new InvalidOperationException()).AllowCancellation());
        await Task.FromException(new OperationCanceledException()).AllowCancellation();
        await Task.FromException(new TaskCanceledException()).AllowCancellation();

        var cancelledToken = new CancellationToken(true);
        await Task.Delay(100, cancelledToken).AllowCancellation();

        var cts = new CancellationTokenSource(50);
        await Task.Delay(100, cts.Token).AllowCancellation();

        await Task.Run(() =>
        {
            while (!cancelledToken.IsCancellationRequested)
            {
            }
        }, CancellationToken.None).AllowCancellation();

        await Task.Run(() =>
        {
            while (true)
            {
                cancelledToken.ThrowIfCancellationRequested();
            }
        }, CancellationToken.None).AllowCancellation();

        cts = new CancellationTokenSource(50);
        await Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
            }
        }, CancellationToken.None).AllowCancellation();

        cts = new CancellationTokenSource(50);
        await Task.Run(() =>
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
            }
        }, CancellationToken.None).AllowCancellation();
    }
}
