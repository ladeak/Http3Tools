using System.Diagnostics;
using System.Numerics;

namespace CHttp.Data;

internal record struct Ratio<T> where T : IBinaryNumber<T>
{
    private readonly long _createdTimestamp;
    private readonly TimeSpan _remaining;
    private readonly long? _relativeRemainingTimestamp;

    public Ratio(T numerator, T total, TimeSpan remaining) : this(numerator, total, remaining, Stopwatch.GetTimestamp(), null)
    {
    }

    public Ratio(T numerator, T total, TimeSpan remaining, long createdTimestamp, long? relativeRemainingTimestamp)
    {
        Numerator = numerator;
        Total = total;
        _remaining = remaining;
        _createdTimestamp = createdTimestamp;
        _relativeRemainingTimestamp = relativeRemainingTimestamp;
    }

    public T Numerator { get; }

    public T Total { get; }

    public TimeSpan RelativeRemaining
    {
        get
        {
            var now = _relativeRemainingTimestamp ?? Stopwatch.GetTimestamp();
            var value = _remaining - TimeSpan.FromTicks(now - _createdTimestamp);
            return value > TimeSpan.Zero ? value : TimeSpan.Zero;
        }
    }
}

