using CHttp.Abstractions;

namespace CHttp.Writers;

internal sealed class ProgressBar<T> where T : struct
{
    private readonly int _length;
    private readonly char[] _complete;
    private readonly IConsole _console;
    private readonly IAwaiter _awaiter;
    private T _value;

    public void Set(T value) => _value = value;

    public ProgressBar(IConsole console, IAwaiter awaiter)
    {
        _console = console ?? new CHttpConsole();
        _awaiter = awaiter ?? new Awaiter();
        _length = Math.Min(50, _console.WindowWidth);
        _complete = "100%".PadRight(_length).ToArray();
    }

    public async Task RunAsync<U>(CancellationToken token = default) where U : INumberFormatter<T>
    {
        _value = default;
        char[] buffer = new char[_length];
        buffer[0] = '[';
        buffer[^1] = ']';
        int state = 0;
        (int Left, int Top) position;
        _console.WriteLine();
        position = _console.GetCursorPosition();
        _console.CursorVisible = false;
        for (int i = 1; i < buffer.Length - 1; i++)
        {
            buffer[i] = '-';
        }
        int prevIndex = 1;
        do
        {
            buffer[prevIndex] = '-';
            var index = state % (_length - 2) + 1;
            buffer[index] = '=';
            prevIndex = index;
            _console.SetCursorPosition(position.Left, position.Top);
            _console.Write(buffer);
            _console.Write(U.FormatSize(_value));
            state++;
            await _awaiter.WaitAsync(TimeSpan.FromMilliseconds(50));
        } while (!token.IsCancellationRequested);
        _console.SetCursorPosition(position.Left, position.Top);
        _console.Write(_complete);
        _console.Write(U.FormatSize(_value));
        _console.WriteLine();
        _console.CursorVisible = true;
    }
}
