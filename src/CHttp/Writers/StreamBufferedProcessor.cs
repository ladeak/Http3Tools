using System.Buffers;
using System.IO.Pipelines;

namespace CHttp.Writers;

internal class StreamBufferedProcessor : IBufferedProcessor
{
    private Pipe _pipe;
    private CancellationTokenSource _cts;
    private Task _pipeReaderTask;
    private long _position;
    private readonly Stream _output;

    public StreamBufferedProcessor(Stream output)
    {
        _pipe = new Pipe();
        _cts = new CancellationTokenSource();
        _pipeReaderTask = Task.CompletedTask;
        _output = output;
    }

    public PipeWriter Pipe => _pipe.Writer;

    public long Position => _position;

    public Task RunAsync(Func<ReadOnlySequence<byte>, Task> lineProcessor, PipeOptions? options = null)
    {
        _pipe = new Pipe(options ?? PipeOptions.Default);
        _cts = new CancellationTokenSource();
        _pipeReaderTask = Task.Run(() => ReadPipeAsync(lineProcessor, _cts.Token));
        return _pipeReaderTask;
    }

    private async Task ReadPipeAsync(Func<ReadOnlySequence<byte>, Task> lineProcessor, CancellationToken token)
    {
        try
        {
            _position = 0;
            while (!token.IsCancellationRequested)
            {
                var result = await _pipe.Reader.ReadAsync(token);

                if (result.IsCanceled)
                    break;

                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    unchecked
                    {
                        _position += line.Length;
                    }
                    await CopyToOutput(line);
                    await lineProcessor(line);
                }

                // Tell the PipeReader how much of the buffer has been consumed.
                _pipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                // Stop reading if there's no more data coming.
                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing to do, we got cancelled.
        }
        finally
        {
            await _output.FlushAsync();
            await _pipe.Reader.CompleteAsync();
        }
    }

    private async Task CopyToOutput(ReadOnlySequence<byte> line)
    {
        foreach (ReadOnlyMemory<byte> segment in line)
            await _output.WriteAsync(segment);
    }

    internal bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        if (buffer.Length == 0)
        {
            line = buffer;
            return false;
        }
        line = buffer.Slice(0, buffer.End);
        buffer = buffer.Slice(buffer.End);
        return true;
    }

    public async Task CompleteAsync(CancellationToken token)
    {
        await _pipeReaderTask.WaitAsync(token);
    }

    public void Cancel() => _cts.Cancel();

    public async ValueTask DisposeAsync() => await CompleteAsync(CancellationToken.None);
}
