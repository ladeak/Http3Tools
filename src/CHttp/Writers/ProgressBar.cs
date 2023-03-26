using System.Numerics;

namespace CHttp.Writers;

internal sealed class ProgressBar<T> where T : struct, IBinaryInteger<T>
{
    private const int Length = 8;
    private readonly char[] Complete = "100%".PadRight(Length).ToArray();
    private readonly IConsole _console;
    private readonly IAwaiter _awaiter;
    private T _responseSize;

    public void Set(T size) => _responseSize = size;

    public ProgressBar(IConsole console, IAwaiter awaiter)
    {
        _console = console ?? new CHttpConsole();
        _awaiter = awaiter ?? new Awaiter();
    }

    public async Task RunAsync<U>(CancellationToken token = default) where U : INumberFormatter<T>
    {
        _responseSize = T.Zero;
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
            _console.WriteLine(U.FormatSize(_responseSize));
            state++;
            await _awaiter.WaitAsync();
        } while (!token.IsCancellationRequested);
        _console.SetCursorPosition(position.Left, position.Top);
        _console.Write(Complete);
        _console.WriteLine(U.FormatSize(_responseSize));
        _console.WriteLine();
        _console.CursorVisible = true;
    }
}
