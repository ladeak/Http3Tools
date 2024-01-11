using System.Buffers;
using System.IO.Pipelines;

namespace CHttp.Writers;

internal interface IBufferedProcessor
{
    PipeWriter Pipe { get; }

    long Position { get; }

    Task RunAsync(Func<ReadOnlySequence<byte>, Task> lineProcessor, PipeOptions? options = null);

    Task CompleteAsync(CancellationToken token);

    void Cancel();

    ValueTask DisposeAsync();
}