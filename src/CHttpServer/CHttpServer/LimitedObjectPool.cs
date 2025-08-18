using System.Runtime.CompilerServices;

namespace CHttpServer;

internal sealed class LimitedObjectPool<T> where T : class?
{
    private const int Size = 20;

    [InlineArray(Size)]
    private struct Storage<T1> where T1 : class?
    {
        private T1? _item;
    }

    private Storage<T> _storage;
    private T? _field;

    public T Get<TState>(Func<TState, T> factory, TState state) where TState : struct
    {
        var item = _field;
        if (item is not null && item == Interlocked.CompareExchange(ref _field, null, item))
            return item;

        for (int i = 0; i < Size; i++)
        {
            item = _storage[i];
            if (item is null)
                continue;
            if (item == Interlocked.CompareExchange(ref _storage[i], null, item))
                return item;
        }
        return factory(state);
    }

    public void Return(T item)
    {
        if (_field is null)
        {
            _field = item;
            return;
        }

        for (int i = 0; i < Size; i++)
        {
            var current = _storage[i];
            if (current is not null)
                continue;
            _storage[i] = item;
            return;
        }
    }
}
