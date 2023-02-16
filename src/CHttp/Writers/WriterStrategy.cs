using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

namespace CHttp.Writers;

internal sealed class WriterStrategy : IWriter
{
    private readonly IBufferedProcessor _contentProcessor;
    private IWriter _strategy;

    public WriterStrategy(LogLevel logLevel) : this(new BufferedProcessor(), new CHttpConsole(), logLevel)
    {
    }

    public WriterStrategy(IBufferedProcessor contentProcessor, LogLevel logLevel) : this(contentProcessor, new CHttpConsole(), logLevel)
    {
    }

    public WriterStrategy(IBufferedProcessor contentProcessor, IConsole console, LogLevel logLevel)
    {
        _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
        _strategy = logLevel switch
        {
            LogLevel.Quiet => new QuietConsoleWriter(_contentProcessor, console),
            LogLevel.Normal => new NormalConsoleWriter(_contentProcessor, console),
            LogLevel.Verbose => new NormalConsoleWriter(_contentProcessor, console),
            _ => throw new InvalidOperationException("Not supported log level")
        };
    }

    public PipeWriter Buffer => _strategy?.Buffer ?? throw new InvalidOperationException("Cannot access pipe before initialization");

    public Task CompleteAsync(CancellationToken token) => _strategy.CompleteAsync(token);

    public ValueTask DisposeAsync() => _strategy.DisposeAsync();

    public Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Version httpVersion, Encoding encoding) => _strategy.InitializeResponseAsync(responseStatus, headers, httpVersion, encoding);

    public Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary) => _strategy.WriteSummaryAsync(trailers, summary);
}
