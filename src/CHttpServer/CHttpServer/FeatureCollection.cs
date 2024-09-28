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
            return _features[key];
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
        return (TFeature?)_features[typeof(TFeature)];
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