using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using CHttp.Abstractions;
using CHttp.Data;

namespace CHttp.Writers;

internal sealed class SilentConsoleWriter(IBufferedProcessor contentProcessor, IConsole console) : IWriter
{
    private readonly IBufferedProcessor _contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
    private readonly IConsole _console = console;

    public PipeWriter Buffer => _contentProcessor.Pipe;

    public async Task InitializeResponseAsync(HttpResponseInitials initials)
    {
        _contentProcessor.Cancel();
        await CompleteAsync(CancellationToken.None);
        PrintResponse(initials);
        _ = _contentProcessor.RunAsync(ProcessLine);
    }

    private void PrintResponse(HttpResponseInitials initials)
    {
        _console.WriteLine($"Status: {initials.ResponseStatus} Version: {initials.HttpVersion} Encoding: {initials.Encoding.WebName}");
    }

    private Task ProcessLine(ReadOnlySequence<byte> line)
    {
        return Task.CompletedTask;
    }

    public async Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
    {
        await _contentProcessor.CompleteAsync(CancellationToken.None);
        summary.Length = _contentProcessor.Position;
        _console.WriteLine(summary.ToString());
    }

    public async Task CompleteAsync(CancellationToken token) => await _contentProcessor.CompleteAsync(token);

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
