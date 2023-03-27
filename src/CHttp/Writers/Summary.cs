using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using CHttp.Writers;

public record struct Summary
{
    private const string Request = nameof(Request);
    private const string Length = nameof(Length);
    private const string Url = nameof(Url);
    private const string Trailers = nameof(Trailers);
    private const string StatusCode = nameof(StatusCode);
    private static ActivitySource ActivitySource = new ActivitySource(nameof(Summary));

    public Summary(string url)
    {
        Error = string.Empty;
        RequestActivity = ActivitySource.StartActivity(Request, ActivityKind.Client) ?? new Activity(Request).Start();
        RequestActivity.AddTag(Url, url);
    }

    public Activity RequestActivity { get; }

    public string Error { get; init; }

    public ErrorType ErrorCode { get; init; }

    public void RequestCompleted(HttpStatusCode statusCode)
    {
        RequestActivity.Stop();
        RequestActivity.AddTag(StatusCode, (int)statusCode);
    }

    public void SetSize(long length)
    {
        RequestActivity.AddTag(Length, length);
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Error))
            return Error;
        var url = RequestActivity.GetTagItem(Url) as string ?? string.Empty;
        var trailers = RequestActivity.GetTagItem(Trailers) as HttpResponseHeaders;

        return string.Create(url.Length + 7 + 16 + 3, (RequestActivity, url), static (buffer, inputs) =>
        {
            inputs.url.CopyTo(buffer);
            buffer = buffer.Slice(inputs.url.Length);
            buffer[0] = ' ';
            buffer = buffer.Slice(1);
            var responseSize = (long)(inputs.RequestActivity.GetTagItem(Length) ?? 0L);
            if (!SizeFormatter<long>.TryFormatSize(responseSize, buffer, out var count))
                ThrowInvalidOperationException();
            buffer = buffer.Slice(count);
            buffer[0] = ' ';
            buffer = buffer.Slice(1);
            if (!inputs.RequestActivity.Duration.TryFormat(buffer, out count, "c"))
                ThrowInvalidOperationException();
            buffer = buffer.Slice(count);
            buffer[0] = 's';
        });
    }

    private static void ThrowInvalidOperationException() => throw new InvalidOperationException("Formatting results failed.");
}
