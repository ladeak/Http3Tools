using System.Text;
using CHttp.Abstractions;

namespace CHttpExtension;

public class StringConsole : IConsole
{
	public bool CursorVisible { get; set; } = false;

	private StringBuilder _sb = new StringBuilder();

	public string Text { get => _sb.ToString(); }

	public int WindowWidth => throw new NotImplementedException();

	public ConsoleColor ForegroundColor { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

	public (int Left, int Top) GetCursorPosition() => (0, 0);

	public void SetCursorPosition(int left, int top)
	{
		_sb.Clear();
	}

	public void Write(char[] buffer) => _sb.Append(buffer);

	public void Write(string buffer)
	{
		_sb.Append(buffer);
		_sb.AppendLine();
	}

	public void WriteLine() => _sb.AppendLine();

	public void Write(char[] buffer, int index, int count) => _sb.Append(buffer, index, count);

	public void WriteLine(string value)
	{
		_sb.AppendLine(value);
	}
}
