using System.Collections;

namespace CHttp;

internal class KnowSizeEnumerableCollection<T> : IReadOnlyCollection<T>
{
    private readonly IEnumerable<T> _values;

    public KnowSizeEnumerableCollection(IEnumerable<T> values, int count)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        Count = count;
    }

    public int Count { get; }

    public IEnumerator<T> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}