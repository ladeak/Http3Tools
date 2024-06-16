using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using CHttp.Data;

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

internal abstract class Assertion
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
}

internal abstract class Assertion<T> : Assertion
    where T : INumber<T>
{
    public required T Comperand { get; init; }

    protected abstract T GetValue(Stats stats);

    public override bool Assert(Stats stats, [NotNullWhen(false)] out string? description)
    {
        var value = GetValue(stats);
        if (!AssertValue(value, Comperand, Comperator))
        {
            description = $"assertion error: {value} is not {Comperator} {Comperand}";
            return false;
        }
        description = null;
        return true;
    }
}

internal class MeanAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.Mean;
}

internal class MedianAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Median;
}

internal class StdDevAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.StdDev;
}

internal class ErrorAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.Error;
}

internal class RequestSecAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.RequestSec;
}

internal class ThroughputAssertion : Assertion<double>
{
    protected override double GetValue(Stats stats) => stats.Throughput;
}

internal class MinAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Min;
}

internal class MaxAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Max;
}

internal class Percentile95thAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.Percentile95th;
}

internal class SuccessStatusCodesAssertion : Assertion<long>
{
    protected override long GetValue(Stats stats) => stats.StatusCodes[1];
}