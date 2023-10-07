using CHttp.Abstractions;

namespace CHttp.Tests;

public class SyncedAwaiter : IAwaiter
{
    private readonly SemaphoreSlim _semaphoreLoop;
    private readonly SemaphoreSlim _semaphoreController;
    private readonly CancellationTokenSource _cancellationTokenSouce;

    public SyncedAwaiter(int loopCount)
    {
        _semaphoreLoop = new SemaphoreSlim(loopCount);
        _semaphoreController = new SemaphoreSlim(0);
        _cancellationTokenSouce = new CancellationTokenSource();
    }

    public CancellationToken Token => _cancellationTokenSouce.Token;

    public async Task WaitLoopAsync()
    {
        await _semaphoreController.WaitAsync();
    }

    public Task LoopAsync(int count = 1)
    {
        _semaphoreLoop.Release(count);
        return Task.CompletedTask;
    }

    public Task LoopOnceAsync() => LoopAsync(1);

    public Task CompleteLoopAsync()
    {
        _cancellationTokenSouce.Cancel();
        _semaphoreLoop.Release(1);
        return Task.CompletedTask;
    }

    public async Task WaitAsync(TimeSpan duration)
    {
        _semaphoreController.Release();
        await _semaphoreLoop.WaitAsync();
    }

    public int RemainingCount => _semaphoreLoop.CurrentCount;
}
