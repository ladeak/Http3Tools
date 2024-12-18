namespace CHttp.Data;

internal class KeyValueDescriptor
{
    private readonly string _name;

    private readonly string _value;

    public KeyValueDescriptor(string rawHeader)
    {
        var header = rawHeader.AsSpan();
        var separatorIndex = header.IndexOf(':');
        if (separatorIndex < 1)
            throw new ArgumentException(nameof(header));
        _name = header[..separatorIndex].Trim().ToString();
        _value = header[(separatorIndex + 1)..].Trim().ToString();
    }

    public KeyValueDescriptor(string name, string value)
    {
        _name = name;
        _value = value;
    }

    public KeyValueDescriptor(ReadOnlySpan<char> name, ReadOnlySpan<char> value) : this(name.ToString(), value.ToString())
    {
    }

    public string GetKey() => _name;

    public string GetValue() => _value;
}
