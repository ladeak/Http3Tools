﻿using System.Globalization;
using System.Numerics;
using CHttp.Data;

namespace CHttp.Abstractions;

internal interface INumberFormatter<T>
{
    public static abstract string FormatSize(T value);

    public static abstract (string Formatted, string Qualifier) FormatSizeWithQualifier(T value);

    public static abstract bool TryFormatSize(T value, Span<char> destination, out int count);
}

internal class SizeFormatter<T> : INumberFormatter<T> where T : IBinaryNumber<T>
{
    private const int Alignment = 4;
    private static readonly T KiloByte = T.CreateChecked(1024);
    private static readonly T MegaByte = T.CreateChecked(1024) * T.CreateChecked(1024);
    private static readonly T GigaByte = T.CreateChecked(1024) * T.CreateChecked(1024) * T.CreateChecked(1024);
    private static readonly T TeraByte = T.CreateChecked(1024) * T.CreateChecked(1024) * T.CreateChecked(1024) * T.CreateChecked(1024);

    public static string FormatSize(T value)
    {
        var absValue = T.Abs(value);
        if (absValue >= TeraByte)
            return string.Create(CultureInfo.InvariantCulture, $"{value / TeraByte,Alignment:D} TB");
        if (absValue >= GigaByte)
            return string.Create(CultureInfo.InvariantCulture, $"{value / GigaByte,Alignment:D} GB");
        if (absValue >= MegaByte)
            return string.Create(CultureInfo.InvariantCulture, $"{value / MegaByte,Alignment:D} MB");
        if (absValue >= KiloByte)
            return string.Create(CultureInfo.InvariantCulture, $"{value / KiloByte,Alignment:D} KB");
        return string.Create(CultureInfo.InvariantCulture, $"{value,Alignment:D} B");
    }

    public static (string Formatted, string Qualifier) FormatSizeWithQualifier(T value)
    {
        var absValue = T.Abs(value);
        if (absValue >= TeraByte)
            return (string.Create(CultureInfo.InvariantCulture, $"{value / TeraByte,Alignment:F3}"), "T");
        if (absValue >= GigaByte)
            return (string.Create(CultureInfo.InvariantCulture, $"{value / GigaByte,Alignment:F3}"), "G");
        if (absValue >= MegaByte)
            return (string.Create(CultureInfo.InvariantCulture, $"{value / MegaByte,Alignment:F3}"), "M");
        if (absValue >= KiloByte)
            return (string.Create(CultureInfo.InvariantCulture, $"{value / KiloByte,Alignment:F3}"), "K");
        return (string.Create(CultureInfo.InvariantCulture, $"{value,Alignment:F3}"), " ");
    }

    public static (string Formatted, string Qualifier) FormatSizeWithQualifierWithSign(T value)
    {
        var absValue = T.Abs(value);
        if (absValue >= TeraByte)
            return ($"{value / TeraByte,Alignment:+#0.000;-#0.000;0}", "T");
        if (absValue >= GigaByte)
            return ($"{value / GigaByte,Alignment:+#0.000;-#0.000;0}", "G");
        if (absValue >= MegaByte)
            return ($"{value / MegaByte,Alignment:+#0.000;-#0.000;0}", "M");
        if (absValue >= KiloByte)
            return ($"{value / KiloByte,Alignment:+#0.000;-#0.000;0}", "K");
        return ($"{value,Alignment:+#0.000;-#0.000;0}", " ");
    }

    public static bool TryFormatSize(T value, Span<char> destination, out int count)
    {
        count = 0;
        if (destination.Length < 7)
            return false;

        ReadOnlySpan<char> Size = "";
        ReadOnlySpan<char> Format = "###0";
        bool result = false;
        var absValue = T.Abs(value);
        if (absValue >= TeraByte)
        {
            result = (value / TeraByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "TB";
        }
        if (absValue >= GigaByte)
        {
            result = (value / GigaByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "GB";
        }
        if (absValue >= MegaByte)
        {
            result = (value / MegaByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "MB";
        }
        if (absValue >= KiloByte)
        {
            result = (value / KiloByte).TryFormat(destination, out count, Format, CultureInfo.InvariantCulture);
            Size = "KB";
        }
        if (absValue < KiloByte)
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

internal class CountFormatter<T> : INumberFormatter<T> where T : IBinaryInteger<T>
{
    private const int Alignment = 7;

    public static string FormatSize(T value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value,Alignment:D}");
    }

    public static (string Formatted, string Qualifier) FormatSizeWithQualifier(T value)
    {
        return (string.Create(CultureInfo.InvariantCulture, $"{value,Alignment:F3}"), string.Empty);
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

internal class RatioFormatter<T> : INumberFormatter<Ratio<T>> where T : IBinaryInteger<T>
{
    private const int Alignment = 7;
    private const int AlignmentSec = 5;

    public static string FormatSize(Ratio<T> value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value.Numerator,Alignment:D}/{value.Total:D} {value.RelativeRemaining.TotalSeconds,AlignmentSec:F1}s");
    }

    public static (string Formatted, string Qualifier) FormatSizeWithQualifier(Ratio<T> value)
    {
        return (string.Create(CultureInfo.InvariantCulture, $"{value.Numerator,Alignment:D}/{value.Total:D}"), string.Empty);
    }

    public static bool TryFormatSize(Ratio<T> value, Span<char> destination, out int count)
    {
        count = 0;
        if (destination.Length < 15)
            return false;
        ReadOnlySpan<char> FormatNumerator = "######0";
        ReadOnlySpan<char> FormatTotal = "0";
        if (!value.Numerator.TryFormat(destination, out var currentCount, FormatNumerator, CultureInfo.InvariantCulture))
            return false;
        count += currentCount;
        destination = destination.Slice(count);
        destination[0] = '/';
        count++;
        if (!value.Total.TryFormat(destination.Slice(1), out currentCount, FormatTotal, CultureInfo.InvariantCulture))
            return false;
        count += currentCount;
        return true;
    }
}