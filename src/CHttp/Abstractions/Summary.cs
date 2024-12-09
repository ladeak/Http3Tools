using System.Diagnostics;
using System.Net;
using CHttp.Data;

namespace CHttp.Abstractions;

public record struct Summary
{
    public Summary(string url)
    {
        Error = string.Empty;
        Url = url;
        StartTime = Stopwatch.GetTimestamp();
    }

    internal Summary(string url, DateTime startTime, TimeSpan duration)
    {
        Error = string.Empty;
        Url = url;
        if (startTime.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException();
        StartTime = startTime.Ticks;
        EndTime = StartTime + duration.Ticks;
    }

    public string Url { get; init; }

    public string Error { get; init; }

    public ErrorType ErrorCode { get; init; }

    public long StartTime { get; init; }

    private long _endTime;
    public long EndTime
    {
        get => _endTime;
        set
        {
            if (_endTime != default || StartTime == default)
                return;
            _endTime = value;
            Duration = TimeSpan.FromTicks(_endTime - StartTime);
        }
    }

    public TimeSpan Duration { get; private set; }

    public long Length { get; set; }

    public int? HttpStatusCode { get; set; }

    public void RequestCompleted(HttpStatusCode statusCode)
    {
        EndTime = Stopwatch.GetTimestamp();
        HttpStatusCode = (int)statusCode;
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Error))
            return Error;

        return string.Create(Url.Length + 7 + 16 + 3, (Length, Url, Duration), static (buffer, inputs) =>
        {
            inputs.Url.CopyTo(buffer);
            buffer = buffer.Slice(inputs.Url.Length);
            buffer[0] = ' ';
            buffer = buffer.Slice(1);
            var responseSize = inputs.Length;
            if (!SizeFormatter<long>.TryFormatSize(responseSize, buffer, out var count))
                ThrowInvalidOperationException();
            buffer = buffer.Slice(count);
            buffer[0] = ' ';
            buffer = buffer.Slice(1);
            if (!inputs.Duration.TryFormat(buffer, out count, "c"))
                ThrowInvalidOperationException();
            buffer = buffer.Slice(count);
            buffer[0] = 's';
            buffer = buffer.Slice(1);
            buffer.Fill(' ');
        });
    }

    private static void ThrowInvalidOperationException() => throw new InvalidOperationException("Formatting results failed.");
}
