using System.Drawing;
using System.Text;
using CHttp.Abstractions;

namespace CHttpExtension;

public class StringConsole : IConsole
{
	private ConsoleColor _color = ConsoleColor.Black;
	private bool _colorize;

	public StringConsole()
	{
	}

	public StringConsole(string color)
	{
		if (!Enum.TryParse<ConsoleColor>(color, out _color))
			_color = ConsoleColor.Black;
	}

	public bool CursorVisible { get; set; } = false;

	private StringBuilder _sb = new StringBuilder();

	public string Text { get => _sb.ToString(); }

	public int WindowWidth => 72;

	public ConsoleColor ForegroundColor
	{
		get => _color;
		set
		{
			_color = value;
			_colorize = !_colorize;
			if (_colorize)
				Write($"<div style=\"color:{_color};\">");
			else
				Write("</div>");
		}
	}

	public (int Left, int Top) GetCursorPosition() => (0, 0);

	public void SetCursorPosition(int left, int top)
	{
		_sb.Clear();
	}

	public void Write(char[] buffer) => _sb.Append(buffer);

	public void Write(string buffer) => _sb.Append(buffer);

	public void WriteLine() => _sb.AppendLine();

	public void Write(char[] buffer, int index, int count) => _sb.Append(buffer, index, count);

	public void WriteLine(string value) => _sb.AppendLine(value);
}
