using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CHttp.Data;
using CHttp.Performance.Statitics;
using Quantified = (double DisplayValue, string Qualifier);

namespace CHttpExecutor;

internal enum ComparingOperation
{
    LessThen,
    LessThenOrEquals,
    MoreThen,
    Equals,
    MoreThenOrEquals,
    NotEquals,
}

internal abstract record class Assertion
{
    public required ComparingOperation Comperator { get; init; }

    public abstract bool Assert(Stats stats, [NotNullWhen(false)] out string? description);

    protected static bool AssertValue<T>(T value, T comperand, ComparingOperation op) where T : INumber<T> =>
        op switch
        {
            ComparingOperation.Equals => value == comperand,
            ComparingOperation.NotEquals => value != comperand,
            ComparingOperation.LessThenOrEquals => value <= comperand,
            ComparingOperation.LessThen => value < comperand,
            ComparingOperation.MoreThenOrEquals => value >= comperand,
            ComparingOperation.MoreThen => value >= comperand,
            _ => throw new NotSupportedException("Operator not supported"),
        };

    protected static ReadOnlySpan<char> ComparatorDisplay(ComparingOperation op) =>
        op switch
        {
            ComparingOperation.Equals => "==",
            ComparingOperation.NotEquals => "!=",
            ComparingOperation.LessThenOrEquals => "<=",
            ComparingOperation.LessThen => "<",
            ComparingOperation.MoreThenOrEquals => ">=",
            ComparingOperation.MoreThen => ">=",
            _ => throw new NotSupportedException("Operator not supported"),
        };
}

internal abstract record class Assertion<T> : Assertion, ISpanFormattable
    where T : INumber<T>
{
    public required T Comperand { get; init; }

    protected abstract T GetValue(Stats stats);

    protected abstract Quantified GetComperandDisplay();

    public override bool Assert(Stats stats, [NotNullWhen(false)] out string? description)
    {
        var value = GetValue(stats);
        if (!AssertValue(value, Comperand, Comperator))
        {
            ReadOnlySpan<char> typeName = this.GetType().Name.AsSpan()[0..^9]; // Cut 'Assertion' suffix
            description = $"error: {typeName} is not {ComparatorDisplay(Comperator)} {this}";
            return false;
        }
        description = null;
        return true;
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var comperand = GetComperandDisplay();
        var qualifierLength = comperand.Qualifier.Length;
        if (!comperand.DisplayValue.TryFormat(destination[0..^(qualifierLength + 1)], out charsWritten, "F3", CultureInfo.InvariantCulture)
            || !comperand.Qualifier.TryCopyTo(destination[charsWritten..]))
            return false;
        charsWritten += qualifierLength;
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var comperand = GetComperandDisplay();
        var digits = CountDigits((uint)comperand.DisplayValue);
        return string.Create(digits + 4 + comperand.Qualifier.Length, comperand, static (destination, state) =>
        {
            var qualifierLength = state.Qualifier.Length;
            var formatted = state.DisplayValue.TryFormat(destination[0..^(qualifierLength + 1)], out var charsWritten, "F3", CultureInfo.InvariantCulture);
            formatted &= state.Qualifier.TryCopyTo(destination[charsWritten..]);
            Debug.Assert(formatted);
        });
    }

    private static int CountDigits(uint value)
    {
        // Algorithm based on https://lemire.me/blog/2021/06/03/computing-the-number-of-digits-of-an-integer-even-faster.
        ReadOnlySpan<long> table = new long[]
        {
                4294967296,
                8589934582,
                8589934582,
                8589934582,
                12884901788,
                12884901788,
                12884901788,
                17179868184,
                17179868184,
                17179868184,
                21474826480,
                21474826480,
                21474826480,
                21474826480,
                25769703776,
                25769703776,
                25769703776,
                30063771072,
                30063771072,
                30063771072,
                34349738368,
                34349738368,
                34349738368,
                34349738368,
                38554705664,
                38554705664,
                38554705664,
                41949672960,
                41949672960,
                41949672960,
                42949672960,
                42949672960,
        };
        long tableValue = Unsafe.Add(ref MemoryMarshal.GetReference(table), uint.Log2(value));
        return (int)((value + tableValue) >> 32);
    }
}

internal record class MeanAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.Mean;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class MedianAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Median;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class StdDevAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.StdDev;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class ErrorAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.Error;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class RequestSecAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.RequestSec;
    protected override Quantified GetComperandDisplay() => (Comperand, string.Empty);
}

internal record class ThroughputAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.Throughput;
    protected override Quantified GetComperandDisplay() => (Comperand, string.Empty);
}

internal record class MinAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Min;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class MaxAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Max;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class Percentile95thAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Percentile95th;
    protected override Quantified GetComperandDisplay() => StatisticsCalculator.Display(Comperand);
}

internal record class SuccessStatusCodesAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.StatusCodes[1];
    protected override Quantified GetComperandDisplay() => (Comperand, string.Empty);
}