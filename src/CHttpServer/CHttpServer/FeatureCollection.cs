using System.Collections;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer;

public class FeatureCollection : IFeatureCollection
{
    private readonly Dictionary<Type, object> _features = new();
    private volatile int _revision = 0;

    public object? this[Type key]
    {
        get
        {
            if(!_features.TryGetValue(key, out var value))
                return null;
            return value;
        }
        set
        {
            if (value is null)

                _features.Remove(key);
            else
                _features[key] = value;
            _revision++;
        }
    }

    public bool IsReadOnly => false;

    public int Revision => _revision;

    public TFeature? Get<TFeature>()
    {
        return (TFeature?)this[typeof(TFeature)];
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        foreach (var pair in _features)
        {
            yield return pair;
        }
    }

    public void Set<TFeature>(TFeature? instance)
    {
        this[typeof(TFeature)] = instance;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}