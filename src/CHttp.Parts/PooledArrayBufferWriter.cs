using System.Buffers;

namespace CHttp.Parts.UriBuilders;

public sealed class PooledArrayBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private const int DefaultInitialBufferSize = 4096 * 2;

    private T[] _buffer;
    private int _index;

    public PooledArrayBufferWriter()
    {
        _buffer = ArrayPool<T>.Shared.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

    public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);

    public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);

    public int WrittenCount => _index;

    public int Capacity => _buffer.Length;

    public int FreeCapacity => _buffer.Length - _index;

    public void Clear()
    {
        ArrayPool<T>.Shared.Return(_buffer);
        _buffer = ArrayPool<T>.Shared.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentException(null, nameof(count));

        if (_index > _buffer.Length - count)
            ThrowInvalidOperationException_AdvancedTooFar();

        _index += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsSpan(_index);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentException(nameof(sizeHint));

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > FreeCapacity)
        {
            int currentLength = _buffer.Length;

            int growBy = Math.Max(sizeHint, currentLength);

            int newSize = currentLength + growBy;

            var temp = ArrayPool<T>.Shared.Rent(newSize);
            Array.Copy(_buffer, temp, _index);
            ArrayPool<T>.Shared.Return(_buffer);
            _buffer = temp;
        }
    }

    private static void ThrowInvalidOperationException_AdvancedTooFar() => throw new InvalidOperationException();

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_buffer);
    }
}