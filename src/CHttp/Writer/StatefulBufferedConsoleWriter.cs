using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

internal sealed class StatefulBufferedConsoleWriter : BufferedWriter
{
    private LogLevel _logLevel;
    private Task _progress;
    private CancellationTokenSource _cts;
    private ProgressBar _progressBar;

    public StatefulBufferedConsoleWriter()
    {
        _logLevel = LogLevel.Normal;
        _progress = Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _progressBar = new ProgressBar();
    }

    public override async Task InitializeResponse(string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel)
    {
        await CompleteAsync(CancellationToken.None);
        _logLevel = logLevel;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        if (_logLevel == LogLevel.Normal)
            _progress = _progressBar.Run(_cts.Token);
        else
            _progress = Task.CompletedTask;
        _ = RunAsync();
    }

    protected override async Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);

        if (_logLevel == LogLevel.Verbose)
            Console.Write(buffer, 0, count);

        _progressBar.Add(line.Length);

        ArrayPool<char>.Shared.Return(buffer);
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


internal sealed class ProgressBar
{
    private const int Length = 8;
    private long _responseSize;

    public void Add(long size) => _responseSize += size;

    public async Task Run(CancellationToken token)
    {
        _responseSize = 0;
        char[] buffer = new char[Length];
        int state = 0;
        (int Left, int Top) position;
        Console.WriteLine();
        position = Console.GetCursorPosition();
        Console.CursorVisible = false;
        while (!token.IsCancellationRequested)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = i < (state % Length) ? ':' : ' ';
            }
            Console.SetCursorPosition(position.Left, position.Top);
            Console.Write(buffer);
            Console.Write(FormatSize());
            state++;
            await Task.Delay(50);
        }
        Console.SetCursorPosition(position.Left, position.Top);
        Console.Write("100%".PadRight(Length));
        Console.Write(FormatSize());
        Console.WriteLine();
        Console.CursorVisible = true;
    }

    private const int GigaByte = 1024 * 1024 * 1024;
    private const int MegaByte = 1024 * 1024;
    private const int KiloByte = 1024;
    private const int Alignment = 4;

    private string FormatSize()
    {
        return _responseSize switch
        {
            >= GigaByte => $"{_responseSize / GigaByte,Alignment:D} GB",
            >= MegaByte and < GigaByte => $"{_responseSize / MegaByte,Alignment:D} MB",
            >= KiloByte and < MegaByte => $"{_responseSize / KiloByte,Alignment:D} KB",
            < KiloByte => $"{_responseSize,Alignment:D} B"
        }; ;
    }


}