using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Abstractions;
using CHttp.Data;

namespace CHttp.Writers;

internal sealed class WriterStrategy : IWriter
{
    private readonly IBufferedProcessor _contentProcessor;
    private IWriter _strategy;

    public WriterStrategy(OutputBehavior behavior, IBufferedProcessor? contentProcessor = null, IConsole? console = null)
    {
        contentProcessor ??= !string.IsNullOrWhiteSpace(behavior.FilePath) ?
           new StreamBufferedProcessor(File.Open(behavior.FilePath, new FileStreamOptions() { Access = FileAccess.Write, Mode = FileMode.Create, Options = FileOptions.Asynchronous })) : new TextBufferedProcessor();
        console ??= new CHttpConsole();
        _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
        _strategy = behavior switch
        {
            { LogLevel: LogLevel.Quiet } => new QuietConsoleWriter(_contentProcessor, console),
            { LogLevel: LogLevel.Normal } => new ProgressingConsoleWriter(_contentProcessor, console),
            { LogLevel: LogLevel.Verbose, FilePath: string { Length: > 0 } } => new ProgressingConsoleWriter(_contentProcessor, console),
            { LogLevel: LogLevel.Verbose } => new VerboseConsoleWriter(_contentProcessor, console),
            _ => throw new InvalidOperationException("Not supported log level")
        };
    }

    public PipeWriter Buffer => _strategy?.Buffer ?? throw new InvalidOperationException("Cannot access pipe before initialization");

    public Task CompleteAsync(CancellationToken token) => _strategy.CompleteAsync(token);

    public ValueTask DisposeAsync() => _strategy.DisposeAsync();

    public Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Version httpVersion, Encoding encoding) => _strategy.InitializeResponseAsync(responseStatus, headers, httpVersion, encoding);

    public Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary) => _strategy.WriteSummaryAsync(trailers, summary);
}
