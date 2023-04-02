using System.Diagnostics.Tracing;

namespace CHttp.EventListeners;

internal class QuicEventListener : EventListener, INetEventListener
{
    private const string QuicSourceName = "Private.InternalDiagnostics.System.Net.Quic";

    private List<string?> _messages = new();
    private EventSource? _source;

    // "[strm][0x23F82B5DCC0] Stream reading into memory of '64' bytes."
    // https://source.dot.net/#System.Net.Sockets/System/Net/Sockets/SocketsTelemetry.cs,1041276d2c058285

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name != QuicSourceName)
            return;

        EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        if (_source != null)
            throw new InvalidOperationException();
        _source = eventSource;
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.Payload != null
            && eventData.Payload.Count > 2
            && eventData.Payload[1] is string operation
            && operation == "ReadAsync")
            _messages.Add(eventData.Payload[2] as string);
    }

    public Task WaitUpdateAndStopAsync()
    {
        if (_source is not null)
            DisableEvents(_source);
        return Task.CompletedTask;
    }

    public long GetBytesRead()
    {
        long sum = 0;
        if (_messages.Any())
        {
            foreach (var item in _messages)
            {
                if (item != null)
                {
                    var start = item.IndexOf('\'');
                    var end = item.LastIndexOf('\'');
                    if (start >= 0
                        && end > start
                        && int.TryParse(item.AsSpan(start + 1, end - start - 1), out var bytes))
                        sum += bytes;
                }
            }
        }
        return sum;
    }
}
