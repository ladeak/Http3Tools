using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace CHttpServer;

internal struct FlowControlSize
{
    private uint _size;

    public FlowControlSize(uint size)
    {
        _size = size;
    }

    public bool TryUse(uint requestedSize)
    {
        uint current, newSize;
        do
        {
            current = _size;
            if (current < requestedSize)
                return false;
            newSize = current - requestedSize;
        }
        while (Interlocked.CompareExchange(ref _size, newSize, current) != current);
        return true;
    }

    public bool TryUseAny(uint requestedSize, out uint reservedSize)
    {
        uint current, newSize;
        var originalRequestedSize = requestedSize;
        do
        {
            current = _size;
            if (current < requestedSize)
                requestedSize = current;
            newSize = current - requestedSize;
        }
        while (Interlocked.CompareExchange(ref _size, newSize, current) != current);
        reservedSize = current - newSize;
        return originalRequestedSize == reservedSize;
    }

    public void ReleaseSize(uint size)
    {
        var result = Interlocked.Add(ref _size, size);
        if (result > Http2Connection.MaxWindowUpdateSize)
            throw new Http2FlowControlException();
    }
}
