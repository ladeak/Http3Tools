public class KeyValueDescriptor
{
    private readonly string _header;

    private readonly Range _keyRange;

    private readonly Range _valueRange;

    public KeyValueDescriptor(string header)
    {
        var separatorIndex = header.IndexOf(':');
        if (separatorIndex < 1)
            throw new ArgumentException(nameof(header));
        _header = header;
        _keyRange = new Range(0, separatorIndex);
        _valueRange = new Range(separatorIndex + 1, header.Length - 1);
    }

    public ReadOnlySpan<char> GetKey() => _header[_keyRange];

    public ReadOnlySpan<char> GetValue() => _header[_valueRange];
}
