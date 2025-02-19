namespace CHttpServer;

internal struct FlowControlSize
{
    private uint _size;

    public FlowControlSize(uint size)
    {
        _size = size;
    }

    public bool TryUse(uint size)
    {
        uint current, newSize;
        do
        {
            current = _size;
            if (current < size)
                return false;
            newSize = current - _size;
        }
        while (Interlocked.CompareExchange(ref _size, newSize, current) != current);
        return true;
    }

    public void ReleaseSize(uint size)
    {
        var result = Interlocked.Add(ref _size, size);
        if (result > Http2Connection.MaxWindowUpdateSize)
            throw new Http2FlowControlException();
    }
}
