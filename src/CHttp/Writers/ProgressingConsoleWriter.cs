using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class ProgressingConsoleWriter : IWriter
{
    private Task _progressBarTask;
    private CancellationTokenSource _cts;
    private ProgressBar _progressBar;
    private readonly IBufferedProcessor _contentProcessor;
    private readonly IConsole _console;

    public PipeWriter Buffer => _contentProcessor.Pipe;

    public ProgressingConsoleWriter(IBufferedProcessor contentProcessor, IConsole console)
    {
        _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
        _console = console;
        _progressBarTask = Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _progressBar = new ProgressBar(console ?? new CHttpConsole(), new Awaiter());
    }

    public async Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Version httpVersion, Encoding encoding)
    {
        _contentProcessor.Cancel();
        _cts.Cancel();
        await CompleteAsync(CancellationToken.None);
        _cts = new CancellationTokenSource();
        PrintResponse(responseStatus, headers, httpVersion, encoding);
        _progressBarTask = _progressBar.RunAsync(_cts.Token);
        _ = _contentProcessor.RunAsync(ProcessLine);
    }

    private void PrintResponse(HttpStatusCode responseStatus, HttpResponseHeaders headers, Version httpVersion, Encoding encoding)
    {
        _console.WriteLine($"Status: {responseStatus} Version: {httpVersion} Encoding: {encoding.WebName}");
        foreach (var header in headers)
            _console.WriteLine($"{header.Key}: {string.Join(',', header.Value)}");
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        _progressBar.Set(_contentProcessor.Position);
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
    {
        await _contentProcessor.CompleteAsync(CancellationToken.None);
        _cts.Cancel();
        await _progressBarTask;
        foreach (var trailer in trailers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            _console.WriteLine($"{trailer.Key}: {string.Join(',', trailer.Value)}");
        summary.SetSize(_contentProcessor.Position);
        _console.WriteLine(summary.ToString());
    }

    public async Task CompleteAsync(CancellationToken token) => await Task.WhenAll(_contentProcessor.CompleteAsync(token), _progressBarTask);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
