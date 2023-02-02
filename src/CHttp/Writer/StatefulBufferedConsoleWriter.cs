using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

namespace CHttp.Writer;

internal sealed class StatefulBufferedConsoleWriter : BufferedWriter
{
    private LogLevel _logLevel;
    private Task _progress;
    private CancellationTokenSource _cts;
    private ProgressBar _progressBar;
    private long _responseSize;

    public StatefulBufferedConsoleWriter()
    {
        _logLevel = LogLevel.Normal;
        _progress = Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _progressBar = new ProgressBar(new CHttpConsole(), new Awaiter());
    }

    public override async Task InitializeResponse(string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel)
    {
        await CompleteAsync(CancellationToken.None);
        _logLevel = logLevel;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _responseSize = 0;
        if (_logLevel == LogLevel.Normal)
            _progress = _progressBar.Run(_cts.Token);
        else
            _progress = Task.CompletedTask;
        _ = RunAsync();
    }

    protected override async Task ProcessLine(ReadOnlySequence<byte> line)
    {
        _progressBar.Set(_responseSize += line.Length);
        if (_logLevel == LogLevel.Verbose)
        {
            var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
            int count = Encoding.UTF8.GetChars(line, buffer);
            Console.Write(buffer, 0, count);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public override void WriteUpdate(Update update) => Console.WriteLine(update.ToString());

    public override void WriteSummary(Summary summary)
    {
        _cts.Cancel();
        Console.WriteLine(summary.ToString());
    }

    public override async Task CompleteAsync(CancellationToken token)
    {
        await Task.WhenAll(base.CompleteAsync(token), _progress);
    }
}
