namespace CHttp.Abstractions;

internal interface IConsole
{
    int WindowWidth { get; }

    bool CursorVisible { set; }

    (int Left, int Top) GetCursorPosition();

    void SetCursorPosition(int left, int top);

    void WriteLine();

    void Write(ReadOnlySpan<char> buffer);

    void WriteLine(ReadOnlySpan<char> value);

    ConsoleColor ForegroundColor { get; set; }
}
