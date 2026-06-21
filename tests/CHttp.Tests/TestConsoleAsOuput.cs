using System.Text;
using CHttp.Abstractions;

namespace CHttp.Tests;

public sealed class TestConsoleAsOuput : IConsole
{
    public bool CursorVisible { get; set; }
    private StringBuilder _sb = new StringBuilder();

    public TestConsoleAsOuput()
    {
        WindowWidth = 8;
    }

    public TestConsoleAsOuput(int windowWidth)
    {
        WindowWidth = windowWidth;
    }

    public string Text => field ??= _sb.ToString();

    public int WindowWidth { get; }

    public ConsoleColor ForegroundColor
    {
        get;
        set
        {
            field = value;
            _sb.Append($"[color:{value}]");
        }
    }

    public (int Left, int Top) GetCursorPosition() => (0, 0);

    public void SetCursorPosition(int left, int top)
    {
        // NoOp
    }

    public void WriteLine() => _sb.AppendLine();

    public void Write(ReadOnlySpan<char> buffer) => _sb.Append(buffer);

    public void WriteLine(ReadOnlySpan<char> value) { _sb.Append(value); _sb.AppendLine(); }
}
