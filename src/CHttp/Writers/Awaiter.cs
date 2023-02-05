namespace CHttp.Writers;

internal sealed class Awaiter : IAwaiter
{
    public Task WaitAsync() => Task.Delay(50);
}
