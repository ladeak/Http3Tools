using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

namespace CHttp.Writers;

internal sealed class WriterStrategy : IWriter
{
    private readonly IBufferedProcessor _processor;
    private IWriter _strategy;

    public WriterStrategy(LogLevel logLevel) : this(new BufferedProcessor(), new CHttpConsole(), logLevel)
    {
    }

    public WriterStrategy(IBufferedProcessor processor, LogLevel logLevel) : this(processor, new CHttpConsole(), logLevel)
    {
    }

    public WriterStrategy(IBufferedProcessor processor, IConsole console, LogLevel logLevel)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _strategy = logLevel switch
        {
            LogLevel.Quiet => new QuietConsoleWriter(_processor, console),
            LogLevel.Normal => new NormalConsoleWriter(_processor, console),
            LogLevel.Verbose => new NormalConsoleWriter(_processor, console),
            _ => throw new InvalidOperationException("Not supported log level")
        };
    }

    public PipeWriter Pipe => _strategy?.Pipe ?? throw new InvalidOperationException("Cannot access pipe before initialization");

    public Task CompleteAsync(CancellationToken token) => _strategy.CompleteAsync(token);

    public ValueTask DisposeAsync() => _strategy.DisposeAsync();

    public Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding) => _strategy.InitializeResponseAsync(responseStatus, headers, encoding);

    public Task WriteSummaryAsync(Summary summary) => _strategy.WriteSummaryAsync(summary);
}
