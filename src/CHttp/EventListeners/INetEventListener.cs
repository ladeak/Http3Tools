namespace CHttp.EventListeners;

internal interface INetEventListener
{
    public Task WaitUpdateAndStopAsync();

    public long GetBytesRead();
}
