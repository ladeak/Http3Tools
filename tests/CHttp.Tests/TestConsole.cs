using System.Text;
using CHttp.Writer;

namespace CHttp.Tests;

public class TestConsole : IConsole
{
    public bool CursorVisible { get; set; }
    private StringBuilder _sb = new StringBuilder();

    public string Text { get => _sb.ToString(); }

    public (int Left, int Top) GetCursorPosition() => (0, 0);

    public void SetCursorPosition(int left, int top)
    {
        // NoOp
    }

    public void Write(char[] buffer)
    {
        _sb.Append(buffer);
    }

    public void Write(string buffer)
    {
        _sb.Append(buffer);
        _sb.AppendLine();
    }

    public void WriteLine()
    {
        _sb.AppendLine();
    }
}
