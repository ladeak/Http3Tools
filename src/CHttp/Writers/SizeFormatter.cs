using System.Globalization;
using System.Numerics;

namespace CHttp.Writers;

public interface INumberFormatter<T> where T : IBinaryNumber<T>
{
    public static abstract string FormatSize(T value);

    public static abstract (string Formatted, string Qualifier) FormatSizeWithQualifier(T value);

    public static abstract bool TryFormatSize(T value, Span<char> destination, out int count);
}

public class SizeFormatter<T> : INumberFormatter<T> where T : IBinaryNumber<T>
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

    public static (string Formatted, string Qualifier) FormatSizeWithQualifier(T value)
    {
        if (value >= TeraByte)
            return ($"{value / TeraByte,Alignment:F3}", "T");
        if (value >= GigaByte)
            return ($"{value / GigaByte,Alignment:F3}", "G");
        if (value >= MegaByte)
            return ($"{value / MegaByte,Alignment:F3}", "M");
        if (value >= KiloByte)
            return ($"{value / KiloByte,Alignment:F3}", "K");
        return ($"{value,Alignment:F3}", "");
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

public class CountFormatter<T> : INumberFormatter<T> where T : IBinaryInteger<T>
{
    private const int Alignment = 7;

    public static string FormatSize(T value)
    {
        return $"{value,Alignment:D}";
    }

    public static (string Formatted, string Qualifier) FormatSizeWithQualifier(T value)
    {
        return ($"{value,Alignment:F3}", "");
    }

    public static bool TryFormatSize(T value, Span<char> destination, out int count)
    {
        count = 0;
        if (destination.Length < 7)
            return false;
        ReadOnlySpan<char> Format = "######0";
        return value.TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
    }
}