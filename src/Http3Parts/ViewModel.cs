using System.Threading.Channels;
using Http3Parts;

public class Frame
{
    public static Frame Default { get; } = new Frame() { Data = string.Empty, SourceStream = string.Empty };

    public required string SourceStream { get; init; }

    public required string Data { get; init; }
}

public class ViewModel : IViewModel
{
    private readonly H3Client _client;

    private readonly Channel<Frame> _channel;

    public ViewModel(H3Client client)
    {
        _client = client;
        _client.OnFrame += FrameReceived;
        _channel = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions()
        {
            AllowSynchronousContinuations = true,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public IEnumerable<string> Commands { get; } = new List<string>() { "TestAsync" };

    private void FrameReceived(object? sender, Frame e) => AddFrame(e);

    public void AddFrame(Frame frame)
    {
        if (!_channel.Writer.TryWrite(frame))
            throw new InvalidOperationException("Channel cannot be writtern");
    }

    public async Task ExecuteCommandAsync(string command)
    {
        var frameTask = command switch
        {
            "TestAsync" => _client.TestAsync(),
            _ => throw new NotImplementedException()
        };
        var frame = await frameTask;
        if (frame != Frame.Default)
            AddFrame(frame);
    }

    public async Task<IEnumerable<Frame>> AwaitUpdateAsync(CancellationToken token)
    {
        await _channel.Reader.WaitToReadAsync(token);
        List<Frame> updates = new(1);
        while (_channel.Reader.TryRead(out Frame? frame) && frame is { })
            updates.Add(frame);

        return updates;
    }
}
