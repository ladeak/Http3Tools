using System.Globalization;
using System.Numerics;

namespace CHttp.Writers;

public static class SizeFormatter<T> where T : IBinaryInteger<T>
{
    private const int Alignment = 4;
    private static readonly T KiloByte = T.CreateChecked(1024);
    private static readonly T MegaByte = T.CreateChecked(1024) * T.CreateChecked(1024);
    private static readonly T GigaByte = T.CreateChecked(1024) * T.CreateChecked(1024) * T.CreateChecked(1024);
    private static readonly T TeraByte = T.CreateChecked(1024) * T.CreateChecked(1024) * T.CreateChecked(1024) * T.CreateChecked(1024);

    public static string FormatSize(T value)
    {
        if (value >= TeraByte)
            return $"{value / TeraByte,Alignment:D} TB";
        if (value >= GigaByte)
            return $"{value / GigaByte,Alignment:D} GB";
        if (value >= MegaByte)
            return $"{value / MegaByte,Alignment:D} MB";
        if (value >= KiloByte)
            return $"{value / KiloByte,Alignment:D} KB";
        return $"{value,Alignment:D} B";
    }

    public static bool TryFormatSize(T value, Span<char> destination, out int count)
    {
        count = 0;
        if (destination.Length < 7)
            return false;

        ReadOnlySpan<char> Size = "";
        ReadOnlySpan<char> Format = "###0";
        bool result = false;
        if (value >= TeraByte)
        {
            result = (value / TeraByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "TB";
        }
        if (value >= GigaByte)
        {
            result = (value / GigaByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "GB";
        }
        if (value >= MegaByte)
        {
            result = (value / MegaByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "MB";
        }
        if (value >= KiloByte)
        {
            result = (value / KiloByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "KB";
        }
        if (value < KiloByte)
        {
            result = value.TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = " B";
        }
        if (count > 4 || !result)
            return false;

        result = Size.TryCopyTo(destination.Slice(count));
        count += Size.Length;
        return result;
    }
}