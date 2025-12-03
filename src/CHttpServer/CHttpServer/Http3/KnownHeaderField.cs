namespace CHttpServer.Http3;

internal readonly record struct KnownHeaderField(
    int StaticTableIndex,
    string Name,
    ReadOnlyMemory<byte> NameEncoded,
    string Value,
    ReadOnlyMemory<byte> ValueEncoded)
{
    // http://httpwg.org/specs/rfc7541.html#rfc.section.4.1
    public const int RfcOverhead = 32;

    public int Length => GetLength(Name.Length, Value.Length);

    public static int GetLength(int nameLength, int valueLength) => nameLength + valueLength + RfcOverhead;

    public override string ToString()
    {
        if (Name != null)
            return Name + ": " + Value;
        else
            return "<empty>";
    }
}