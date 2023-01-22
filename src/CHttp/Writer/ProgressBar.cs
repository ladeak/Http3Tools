internal sealed class ProgressBar
{
    private const int GigaByte = 1000 * 1000 * 1000;
    private const int MegaByte = 1000 * 1000;
    private const int KiloByte = 1000;
    private const int Alignment = 4;
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