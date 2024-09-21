using CHttp.Abstractions;

namespace CHttpExtension;

public class StateConsole : IConsole
{
    private readonly Action<string> _callback;

    public StateConsole(Action<string> callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public bool CursorVisible { get; set; } = false;

    public string Text => string.Empty;

    public int WindowWidth => 72;

    public ConsoleColor ForegroundColor { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public (int Left, int Top) GetCursorPosition() => (0, 0);

    public void SetCursorPosition(int left, int top)
    {
    }

    public void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer[0] != '[' && buffer[^1] != ']')
            _callback(buffer.ToString());
    }

    public void WriteLine(ReadOnlySpan<char> value) { }

    public void WriteLine() => _callback("Completed");
}
