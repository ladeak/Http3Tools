namespace CHttp.Writer;

internal interface IAwaiter
{
    // await 
    public Task WaitAsync();
}

internal sealed class Awaiter : IAwaiter
{
    public Task WaitAsync() => Task.Delay(50);
}

internal sealed class ProgressBar
{
    private const long TerraByte = GigaByte * 1000;
    private const long GigaByte = MegaByte * 1000;
    private const long MegaByte = KiloByte * 1000;
    private const long KiloByte = 1000;
    private const int Alignment = 4;
    private const int Length = 8;
    private readonly char[] Complete = "100%".PadRight(Length).ToArray();
    private readonly IConsole _console;
    private readonly IAwaiter _awaiter;
    private long _responseSize;

    public void Set(long size) => _responseSize = size;

    public ProgressBar(IConsole console, IAwaiter awaiter)
    {
        _console = console ?? new CHttpConsole();
        _awaiter = awaiter ?? new Awaiter();
    }

    public async Task Run(CancellationToken token = default)
    {
        _responseSize = 0;
        char[] buffer = new char[Length];
        int state = 0;
        (int Left, int Top) position;
        _console.WriteLine();
        position = _console.GetCursorPosition();
        _console.CursorVisible = false;
        do
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = i < (state % Length) ? ':' : ' ';
            }
            _console.SetCursorPosition(position.Left, position.Top);
            _console.Write(buffer);
            _console.Write(FormatSize());
            state++;
            await _awaiter.WaitAsync();
        } while (!token.IsCancellationRequested);
        _console.SetCursorPosition(position.Left, position.Top);
        _console.Write(Complete);
        _console.Write(FormatSize());
        _console.WriteLine();
        _console.CursorVisible = true;
    }

    private string FormatSize()
    {
        return _responseSize switch
        {
            >= TerraByte => $"{_responseSize / TerraByte,Alignment:D} TB",
            >= GigaByte => $"{_responseSize / GigaByte,Alignment:D} GB",
            >= MegaByte and < GigaByte => $"{_responseSize / MegaByte,Alignment:D} MB",
            >= KiloByte and < MegaByte => $"{_responseSize / KiloByte,Alignment:D} KB",
            < KiloByte => $"{_responseSize,Alignment:D} B"
        }; ;
    }
}