using System.Buffers;

namespace CHttp.Tests;

public class MemorySegment<T> : ReadOnlySequenceSegment<T>
{
    public MemorySegment<T> Head { get; init; }

    public MemorySegment(ReadOnlyMemory<T> memory)
    {
        Memory = memory;
        Head = this;
    }

    public MemorySegment<T> Append(ReadOnlyMemory<T> memory)
    {
        var segment = new MemorySegment<T>(memory)
        {
            Head = this.Head,
            RunningIndex = RunningIndex + Memory.Length
        };
        Next = segment;

        return segment;
    }

    public ReadOnlySequence<T> AsSequence()
    {
        return new ReadOnlySequence<T>(Head, 0, this, Memory.Length);
    }

    public MemorySegment<T>? NextSegment => Next as MemorySegment<T>;
}