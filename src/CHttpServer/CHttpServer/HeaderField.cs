using System.Diagnostics;
using System.Text;

namespace CHttpServer;

internal readonly struct HeaderField
{
    // http://httpwg.org/specs/rfc7541.html#rfc.section.4.1
    public const int RfcOverhead = 32;

    public HeaderField(int staticTableIndex, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        StaticTableIndex = staticTableIndex;

        Debug.Assert(name.Length > 0);

        Name = name.ToArray();
        Value = value.ToArray();
    }

    public int StaticTableIndex { get; }

    public byte[] Name { get; }

    public byte[] Value { get; }

    public int Length => GetLength(Name.Length, Value.Length);

    public static int GetLength(int nameLength, int valueLength) => nameLength + valueLength + RfcOverhead;

    public override string ToString()
    {
        if (Name != null)
        {
            return Encoding.Latin1.GetString(Name) + ": " + Encoding.Latin1.GetString(Value);
        }
        else
        {
            return "<empty>";
        }
    }
}