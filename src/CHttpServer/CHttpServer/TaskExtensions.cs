using System.Runtime.CompilerServices;

namespace CHttpServer;

public static class TaskExtensions
{
    extension(Task task)
    {
        public AllowCancellationAwaitable AllowCancellation() => new AllowCancellationAwaitable(task);
    }
}

public readonly struct AllowCancellationAwaitable(Task task) : ICriticalNotifyCompletion
{
    private readonly Task _task = task ?? throw new ArgumentNullException(nameof(task));

    public bool IsCompleted => _task.IsCompleted;

    public void OnCompleted(Action continuation) => _task.GetAwaiter().OnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation) => _task.GetAwaiter().UnsafeOnCompleted(continuation);

    public void GetResult()
    {
        if (_task.IsCanceled)
            return;
        try
        {
            _task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Do nothing.
        }
    }

    public AllowCancellationAwaitable GetAwaiter() => this;
}