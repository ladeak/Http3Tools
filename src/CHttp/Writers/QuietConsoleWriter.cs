using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class QuietConsoleWriter : IWriter
{
    private Task _progress;
    private CancellationTokenSource _cts;
    private ProgressBar _progressBar;
    private long _responseSize;
    private readonly IBufferedProcessor _processor;

    public PipeWriter Pipe => throw new NotImplementedException();

    public QuietConsoleWriter(IBufferedProcessor processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _progress = Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _progressBar = new ProgressBar(new CHttpConsole(), new Awaiter());
    }

    public async Task InitializeResponseAsync(string responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        await CompleteAsync(CancellationToken.None);
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _responseSize = 0;
        _progress = _progressBar.Run(_cts.Token);
        _ = _processor.RunAsync(ProcessLine);
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        _progressBar.Set(_responseSize += line.Length);
        return Task.CompletedTask;
    }

    public void WriteSummary(Summary summary)
    {
        _cts.Cancel();
        Console.WriteLine(summary.ToString());
    }

    public async Task CompleteAsync(CancellationToken token) => await Task.WhenAll(_processor.CompleteAsync(token), _progress);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
