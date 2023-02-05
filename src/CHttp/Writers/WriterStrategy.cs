using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

namespace CHttp.Writers;

internal sealed class WriterStrategy : IWriter
{
    private readonly IBufferedProcessor _processor;
    private IWriter _strategy;

    public WriterStrategy(IBufferedProcessor processor, LogLevel logLevel)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _strategy = logLevel switch
        {
            LogLevel.Quiet => new QuietConsoleWriter(_processor),
            LogLevel.Normal => new NormalConsoleWriter(_processor),
            LogLevel.Verbose => new NormalConsoleWriter(_processor),
            _ => throw new InvalidOperationException("Not supported log level")
        };
    }

    public PipeWriter Pipe => _strategy?.Pipe ?? throw new InvalidOperationException("Cannot access pipe before initialization");

    public Task CompleteAsync(CancellationToken token) => _strategy.CompleteAsync(token);

    public ValueTask DisposeAsync() => _strategy.DisposeAsync();

    public Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding) => _strategy.InitializeResponseAsync(responseStatus, headers, encoding);

    public void WriteSummary(Summary summary) => _strategy.WriteSummary(summary);
}
