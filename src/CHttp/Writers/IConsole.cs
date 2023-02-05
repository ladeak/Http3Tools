namespace CHttp.Writers;

internal interface IConsole
{
    bool CursorVisible { set; }

    (int Left, int Top) GetCursorPosition();

    void SetCursorPosition(int left, int top);

    void Write(char[] buffer);

    void Write(string buffer);

    void WriteLine();
}
