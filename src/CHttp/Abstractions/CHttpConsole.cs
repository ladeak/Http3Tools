namespace CHttp.Abstractions;

internal class CHttpConsole : IConsole
{
    public bool CursorVisible { set => Console.CursorVisible = value; }

    public int WindowWidth => Console.WindowWidth;

    public ConsoleColor ForegroundColor { get => Console.ForegroundColor; set => Console.ForegroundColor = value; }

    public (int Left, int Top) GetCursorPosition() => Console.GetCursorPosition();

    public void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    public void Write(char[] buffer) => Console.Write(buffer);

    public void Write(string buffer) => Console.Write(buffer);

    public void WriteLine() => Console.WriteLine();

    public void WriteLine(string value) => Console.WriteLine(value);
}
