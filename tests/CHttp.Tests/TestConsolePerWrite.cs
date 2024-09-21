using System.Text;
using CHttp.Abstractions;

namespace CHttp.Tests;

public class TestConsolePerWrite : IConsole
{
    private readonly StringBuilder _sb = new StringBuilder();
    private readonly string _filterDate = string.Empty;

    public TestConsolePerWrite()
    {
        WindowWidth = 8;
    }

    public TestConsolePerWrite(int windowWidth)
    {
        WindowWidth = windowWidth;
    }

    public TestConsolePerWrite(string filterDate) : this()
    {
        _filterDate = filterDate;
    }

    public bool CursorVisible { get; set; }

    public string Text { get => _sb.ToString(); }

    public int WindowWidth { get; }

    public ConsoleColor ForegroundColor { get; set; }

    public (int Left, int Top) GetCursorPosition() => (0, 0);

    public void SetCursorPosition(int left, int top)
    {
        _sb.AppendLine();
    }

    public void Write(ReadOnlySpan<char> buffer) => _sb.Append(FilterDate(buffer));

    public void WriteLine() => _sb.AppendLine();

    public void WriteLine(ReadOnlySpan<char> value)
    {
        _sb.Append(FilterDate(value));
        _sb.AppendLine();
    }

    private ReadOnlySpan<char> FilterDate(ReadOnlySpan<char> line)
    {
        if (DateTime.TryParse(line, out _))
            return _filterDate.AsSpan();
        return line;
    }
}
