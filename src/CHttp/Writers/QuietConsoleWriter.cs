using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class QuietConsoleWriter : IWriter
{
    private Task _progressBarTask;
    private CancellationTokenSource _cts;
    private ProgressBar _progressBar;
    private long _responseSize;
    private readonly IBufferedProcessor _processor;
    private readonly IConsole _console;

    public PipeWriter Pipe => _processor.Pipe;

    public QuietConsoleWriter(IBufferedProcessor processor, IConsole console)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _console = console;
        _progressBarTask = Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _progressBar = new ProgressBar(console ?? new CHttpConsole(), new Awaiter());
    }

    public async Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        _processor.Cancel();
        _cts.Cancel();
        await CompleteAsync(CancellationToken.None);
        _cts = new CancellationTokenSource();
        _responseSize = 0;
        _progressBarTask = _progressBar.RunAsync(_cts.Token);
        _ = _processor.RunAsync(ProcessLine);
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        _progressBar.Set(_responseSize += line.Length);
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(Summary summary)
    {
        await _processor.CompleteAsync(CancellationToken.None);
        _cts.Cancel();
        await _progressBarTask;
        _console.WriteLine(summary.ToString());
    }

    public async Task CompleteAsync(CancellationToken token) => await Task.WhenAll(_processor.CompleteAsync(token), _progressBarTask);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
