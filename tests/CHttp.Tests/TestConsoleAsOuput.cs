﻿using System.Text;
using CHttp.Abstractions;

namespace CHttp.Tests;

public class TestConsoleAsOuput : IConsole
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

    public string Text { get => _sb.ToString(); }

    public int WindowWidth { get; }

    public ConsoleColor ForegroundColor { get; set; }

    public (int Left, int Top) GetCursorPosition() => (0, 0);

    public void SetCursorPosition(int left, int top)
    {
        // NoOp
    }

    public void Write(char[] buffer) => _sb.Append(buffer);

    public void Write(string buffer)
    {
        _sb.Append(buffer);
    }

    public void WriteLine() => _sb.AppendLine();

    public void WriteLine(string value)
    {
        _sb.AppendLine(value);
    }
}
