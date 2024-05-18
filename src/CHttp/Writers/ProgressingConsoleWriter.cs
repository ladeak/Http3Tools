using System.Buffers;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using CHttp.Abstractions;

namespace CHttp.Writers;

internal sealed class ProgressingConsoleWriter : IWriter
{
	private Task _progressBarTask;
	private CancellationTokenSource _cts;
	private ProgressBar<long> _progressBar;
	private readonly IBufferedProcessor _contentProcessor;
	private readonly IConsole _console;

	public PipeWriter Buffer => _contentProcessor.Pipe;

	public ProgressingConsoleWriter(IBufferedProcessor contentProcessor, IConsole console)
	{
		_contentProcessor = contentProcessor ?? throw new ArgumentNullException(nameof(contentProcessor));
		_console = console;
		_progressBarTask = Task.CompletedTask;
		_cts = new CancellationTokenSource();
		_progressBar = new ProgressBar<long>(console ?? new CHttpConsole(), new Awaiter());
	}

	public async Task InitializeResponseAsync(HttpResponseInitials initials)
	{
		_contentProcessor.Cancel();
		_cts.Cancel();
		await CompleteAsync(CancellationToken.None);
		_cts = new CancellationTokenSource();
		PrintResponse(initials);
		_progressBarTask = _progressBar.RunAsync<SizeFormatter<long>>(_cts.Token);
		_ = _contentProcessor.RunAsync(ProcessLine);
	}

	private void PrintResponse(HttpResponseInitials initials)
	{
		_console.WriteLine($"Status: {initials.ResponseStatus} Version: {initials.HttpVersion} Encoding: {initials.Encoding.WebName}");
        HeaderWriter.Write(initials.Headers, _console);
        HeaderWriter.Write(initials.ContentHeaders, _console);
    }

	private Task ProcessLine(ReadOnlySequence<byte> line)
	{
		_progressBar.Set(_contentProcessor.Position);
		return Task.CompletedTask;
	}

	public async Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
	{
		await _contentProcessor.CompleteAsync(CancellationToken.None);
		_cts.Cancel();
		await _progressBarTask;
        if (trailers is { })
            HeaderWriter.Write(trailers, _console);
        summary.Length = _contentProcessor.Position;
		_console.WriteLine(summary.ToString());
	}

	public async Task CompleteAsync(CancellationToken token) => await Task.WhenAll(_contentProcessor.CompleteAsync(token), _progressBarTask);

	public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
