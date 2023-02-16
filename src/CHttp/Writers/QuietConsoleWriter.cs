using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class QuietConsoleWriter : IWriter
{
    private Task _progressBarTask;
    private CancellationTokenSource _cts;
    private ProgressBar _progressBar;
    private readonly IBufferedProcessor _contentProcessor;
    private readonly IConsole _console;

    public PipeWriter Buffer => _contentProcessor.Pipe;

    public QuietConsoleWriter(IBufferedProcessor contentProcessor, IConsole console)
    {
        _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
        _console = console;
        _progressBarTask = Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _progressBar = new ProgressBar(console ?? new CHttpConsole(), new Awaiter());
    }

    public async Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        _contentProcessor.Cancel();
        _cts.Cancel();
        await CompleteAsync(CancellationToken.None);
        _cts = new CancellationTokenSource();
        PrintResponse(responseStatus, headers);
        _progressBarTask = _progressBar.RunAsync(_cts.Token);
        _ = _contentProcessor.RunAsync(ProcessLine);
    }

    private void PrintResponse(HttpStatusCode responseStatus, HttpResponseHeaders headers)
    {
        _console.WriteLine($"Status: {responseStatus}");
        foreach (var header in headers)
            _console.WriteLine($"{header.Key}: {header.Value}");
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {   
        _progressBar.Set(_contentProcessor.Position);
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(Summary summary)
    {
        await _contentProcessor.CompleteAsync(CancellationToken.None);
        _cts.Cancel();
        await _progressBarTask;
        _console.WriteLine(summary.ToString());
    }

    public async Task CompleteAsync(CancellationToken token) => await Task.WhenAll(_contentProcessor.CompleteAsync(token), _progressBarTask);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
