using System.Buffers;
using System.IO.Pipelines;

internal interface IBufferedProcessor
{
    PipeWriter Pipe { get; }

    Task RunAsync(Func<ReadOnlySequence<byte>, Task> lineProcessor, PipeOptions? options = null);

    Task CompleteAsync(CancellationToken token);

    void Cancel();

    ValueTask DisposeAsync();
}