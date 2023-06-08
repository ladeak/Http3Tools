namespace CHttp.Abstractions;

internal sealed class Awaiter : IAwaiter
{
    public Task WaitAsync() => Task.Delay(50);
}
