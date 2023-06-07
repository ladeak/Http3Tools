using System.Diagnostics;
using System.Numerics;

namespace CHttp.Writers;

public record struct Ratio<T> where T : IBinaryNumber<T>
{
    private long _createdTimestamp;
    private TimeSpan _remaining;

    public Ratio(T numerator, T total, TimeSpan remaining)
    {
        Numerator = numerator;
        Total = total;
        _remaining = remaining;
        _createdTimestamp = Stopwatch.GetTimestamp();
    }

    public T Numerator { get; }

    public T Total { get; }

    public TimeSpan Remaining
    {
        get
        {
            var value = _remaining - TimeSpan.FromTicks(Stopwatch.GetTimestamp() - _createdTimestamp); 
            return value > TimeSpan.Zero ? value : TimeSpan.Zero;
        }
    }
}

