using System.Text;
using CHttp.Abstractions;

namespace CHttpExtension;

public class StateConsole : IConsole
{
	private StringBuilder _sb = new StringBuilder();
	private readonly Action<string> _callback;

	public StateConsole(Action<string> callback)
    {
		_callback = callback ?? throw new ArgumentNullException(nameof(callback));
	}

    public bool CursorVisible { get; set; } = false;

	public string Text { get => _sb.ToString(); }

	public int WindowWidth => 72;

	public ConsoleColor ForegroundColor { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

	public (int Left, int Top) GetCursorPosition() => (0, 0);

	public void SetCursorPosition(int left, int top)
	{
		_callback(Text);
		_sb.Clear();
	}

	public void Write(char[] buffer) => _sb.Append(buffer);

	public void Write(string buffer) => _sb.Append(buffer);

	public void WriteLine() => _callback("Completed");

	public void Write(char[] buffer, int index, int count) => _sb.Append(buffer, index, count);

	public void WriteLine(string value) => _sb.AppendLine(value);
}
