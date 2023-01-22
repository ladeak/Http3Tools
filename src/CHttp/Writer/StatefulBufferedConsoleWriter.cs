using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

internal sealed class StatefulBufferedConsoleWriter : BufferedWriter
{
    private long _processedSize;
    private (int Left, int Top) _initialPosition;
    private LogLevel _logLevel;

    public StatefulBufferedConsoleWriter()
    {
        _logLevel = LogLevel.Info;
    }

    public override async Task InitializeResponse(string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel)
    {
        await CompleteAsync(CancellationToken.None);
        _initialPosition = Console.GetCursorPosition();
        _processedSize = 0;
        _logLevel = logLevel;
        _ = RunAsync();
    }

    protected override void ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);

        var currentPosition = Console.GetCursorPosition();
        //Console.SetCursorPosition(_initialPosition.Left, _initialPosition.Top);
        int lineLength = Console.WindowWidth;

        // TODO write + update

        if (_logLevel != LogLevel.Quiet)
            Console.Write(buffer, 0, count);

        ArrayPool<char>.Shared.Return(buffer);
    }

    public override void WriteUpdate(Update update) => Console.WriteLine(update.ToString());

    public override void WriteSummary(Summary summary) => Console.WriteLine(summary.ToString());

}
