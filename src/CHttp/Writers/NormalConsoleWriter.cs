using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CHttp.Writers;

internal sealed class NormalConsoleWriter : IWriter
{
    private readonly IBufferedProcessor _contentProcessor;
    private readonly IConsole _console;

    public PipeWriter Buffer => _contentProcessor.Pipe;

    public NormalConsoleWriter(IBufferedProcessor contentProcessor, IConsole console)
    {
        _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
        _console = console;
    }

    public async Task InitializeResponseAsync(HttpStatusCode responseStatus, HttpResponseHeaders headers, Encoding encoding)
    {
        _contentProcessor.Cancel();
        await _contentProcessor.CompleteAsync(CancellationToken.None);

        PrintResponse(responseStatus, headers);
        _ = _contentProcessor.RunAsync(ProcessLine);
    }

    private void PrintResponse(HttpStatusCode responseStatus, HttpResponseHeaders headers)
    {
        _console.WriteLine($"Status: {responseStatus}");
        foreach (var header in headers)
            _console.WriteLine($"{header.Key}: {string.Join(',', header.Value)}");
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)line.Length);
        int count = Encoding.UTF8.GetChars(line, buffer);
        _console.Write(buffer, 0, count);
        ArrayPool<char>.Shared.Return(buffer);
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(Summary summary)
    {
        await _contentProcessor.CompleteAsync(CancellationToken.None);
        _console.WriteLine(summary.ToString());
    }

    public Task CompleteAsync(CancellationToken token) => _contentProcessor.CompleteAsync(token);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
