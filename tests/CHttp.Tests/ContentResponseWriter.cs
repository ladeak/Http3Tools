using System.Text;

namespace CHttp.Tests;

internal class ContentResponseWriter : IWriter
{
    private StringBuilder _sb;

    public ContentResponseWriter()
    {
        _sb = new StringBuilder();
    }

    public void Write(ReadOnlySpan<char> info)
    {
        foreach (var c in info)
            _sb.Append(c);
    }

    public void WriteInfo(string info)
    { 
    }

    public void WriteSummary(Summary summary)
    { 
    }

    public void WriteUpdate(Update update)
    { 
    }

    public override string ToString() => _sb.ToString();
}
