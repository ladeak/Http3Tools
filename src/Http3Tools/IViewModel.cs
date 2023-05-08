namespace Http3Tools;

public interface IViewModel
{
    public IEnumerable<string> Commands { get; }

    public Task ExecuteCommandAsync(string command);

    public void AddFrame(Frame frame);

    public Task<IEnumerable<Frame>> AwaitUpdateAsync(CancellationToken token);
}
