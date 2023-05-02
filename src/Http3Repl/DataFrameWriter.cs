using System.Buffers;

public sealed class DataFrameWriter : IBufferWriter<char>
{
    private const int DefaultInitialBufferSize = 4096 * 2;

    private char[] _buffer;
    private int _index;

    public DataFrameWriter()
    {
        _buffer = ArrayPool<char>.Shared.Rent(DefaultInitialBufferSize);
        _index = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentException(null, nameof(count));

        if (_index > _buffer.Length - count)
            ThrowInvalidOperationException_AdvancedTooFar();

        Console.WriteLine(_buffer, _index, count);
        _index += count;
    }

    public Memory<char> GetMemory(int sizeHint = 0)
    {
        _index = 0;
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<char> GetSpan(int sizeHint = 0)
    {
        _index = 0;
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

        if (sizeHint > _buffer.Length)
        {
            var temp = ArrayPool<char>.Shared.Rent(sizeHint);
            ArrayPool<char>.Shared.Return(_buffer);
            _buffer = temp;
        }
    }

    private static void ThrowInvalidOperationException_AdvancedTooFar() => throw new InvalidOperationException();
}