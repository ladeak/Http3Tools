using System.Buffers;
using System.IO.Pipelines;

namespace CHttp.Writers;

internal class SizeMeasuringPipeWriter : PipeWriter
{
    public long Size { get; private set; }

    public override void CancelPendingFlush()
    {
    }

    public override void Complete(Exception? exception = null)
    {
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new FlushResult(false, false));
    }

    protected override Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
    {
        int count;
        var temp = ArrayPool<byte>.Shared.Rent(4096);
        var memory = temp.AsSpan();
        while ((count = source.Read(memory)) > 0)
            Size += count;
        ArrayPool<byte>.Shared.Return(temp);
        return Task.CompletedTask;
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        throw new NotSupportedException();
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        throw new NotSupportedException();
    }

    public void Reset()
    {
        Size = 0;
    }

    public override void Advance(int bytes)
    {
    }
}