using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using CHttp.Data;

namespace CHttp.Tests;

internal class TestContentResponseWriter : BufferedWriter
{
    public Task ReadCompleted => _pipeReader;
    private StringBuilder _sb = new StringBuilder();

    public override async Task InitializeResponse(string responseStatus, HttpResponseHeaders headers, Encoding encoding, LogLevel logLevel)
    {
        await CompleteAsync(CancellationToken.None);
        _ = RunAsync();
    }

    protected override Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);
        _sb.Append(buffer.AsSpan(0, count));
        ArrayPool<char>.Shared.Return(buffer);
    }

    public override string ToString() => _sb.ToString();

    public override void WriteUpdate(Update update) => Console.WriteLine(update.ToString());

    public override void WriteSummary(Summary summary) => Console.WriteLine(summary.ToString());

}
