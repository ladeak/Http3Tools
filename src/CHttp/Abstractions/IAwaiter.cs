namespace CHttp.Abstractions;

internal interface IAwaiter
{
    public Task WaitAsync(TimeSpan duration);
}
