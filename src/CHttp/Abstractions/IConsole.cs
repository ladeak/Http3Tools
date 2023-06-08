namespace CHttp.Abstractions;

internal interface IConsole
{
    int WindowWidth { get; }

    bool CursorVisible { set; }

    (int Left, int Top) GetCursorPosition();

    void SetCursorPosition(int left, int top);

    void Write(char[] buffer);

    void Write(char[] buffer, int index, int count);

    void Write(string buffer);

    void WriteLine();

    void WriteLine(string value);
}
