using System.Numerics;

namespace CHttp.Writers;

internal sealed class ProgressBar<T> where T : struct, IBinaryInteger<T>
{
    private readonly int _length;
    private readonly char[] _complete;
    private readonly IConsole _console;
    private readonly IAwaiter _awaiter;
    private T _responseSize;

    public void Set(T size) => _responseSize = size;

    public ProgressBar(IConsole console, IAwaiter awaiter)
    {
        _console = console ?? new CHttpConsole();
        _awaiter = awaiter ?? new Awaiter();
        _length = Math.Min(50, _console.WindowWidth);
        _complete = "100%".PadRight(_length).ToArray();
    }

    public async Task RunAsync<U>(CancellationToken token = default) where U : INumberFormatter<T>
    {
        _responseSize = T.Zero;
        char[] buffer = new char[_length];
        int state = 0;
        (int Left, int Top) position;
        _console.WriteLine();
        position = _console.GetCursorPosition();
        _console.CursorVisible = false;
        do
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = i < (state % _length) ? '=' : ' ';
            }
            _console.SetCursorPosition(position.Left, position.Top);
            _console.Write(buffer);
            _console.WriteLine(U.FormatSize(_responseSize));
            state++;
            await _awaiter.WaitAsync();
        } while (!token.IsCancellationRequested);
        _console.SetCursorPosition(position.Left, position.Top);
        _console.Write(_complete);
        _console.WriteLine(U.FormatSize(_responseSize));
        _console.WriteLine();
        _console.CursorVisible = true;
    }
}
