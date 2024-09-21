
namespace CHttp.Abstractions;

internal class NoOpConsole : IConsole
{
    public int WindowWidth => 0;

    public bool CursorVisible { get; set; }
    public ConsoleColor ForegroundColor { get; set; }
    public (int Left, int Top) GetCursorPosition() => (0, 0);

    public void SetCursorPosition(int left, int top)
    {
    }

    public void Write(ReadOnlySpan<char> buffer)
    {
    }

    public void WriteLine()
    {
    }

    public void WriteLine(ReadOnlySpan<char> value)
    {
    }
}
