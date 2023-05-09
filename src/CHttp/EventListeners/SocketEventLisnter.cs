using System.Diagnostics.Tracing;

namespace CHttp.EventListeners;

internal class SocketEventListener : EventListener, INetEventListener
{
    private const string SocketSourceName = "System.Net.Sockets";

    private EventSource? _source;
    private TaskCompletionSource? _tcs;
    private double _sum;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name != SocketSourceName)
            return;

        EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All, new Dictionary<string, string?>()
        {
            ["EventCounterIntervalSec"] = "1"
        });
        if (_source != null)
            throw new InvalidOperationException();
        _source = eventSource;
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName != "EventCounters" || eventData.Payload == null || eventData.Payload.Count == 0)
        {
            return;
        }
        if (eventData.Payload[0] is not IDictionary<string, object> eventPayload)
        {
            return;
        }
        var keyName = eventPayload["Name"] as string;
        if (keyName == "bytes-sent")
        {
            _sum = (double)eventPayload["Max"];

            if (_tcs != null)
                _tcs.TrySetResult();
        }
    }

    public async Task WaitUpdateAndStopAsync()
    {
        if (_source is null)
            return;
        if (_source.Name == SocketSourceName)
        {
            _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await _tcs.Task;
        }
        DisableEvents(_source);
    }

    public long GetBytesRead()
    {
        return (long)_sum;
    }
}
