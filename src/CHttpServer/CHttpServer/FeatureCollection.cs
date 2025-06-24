using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer;

public class FeatureCollection : IFeatureCollection
{
    private const int MaxFeatures = 14;
    private readonly Dictionary<Type, object> _features = new();
    private InlineArray14<(Type Key, object Value)> _featuresFrozen = new();
    private volatile int _revision = 0;
    private int _checkpointRevision = 0;

    public object? this[Type key]
    {
        get => Get(key);
        set => throw new NotSupportedException();
    }

    public bool IsReadOnly => false;

    public int Revision => _revision;

    [return: MaybeNull]
    public TFeature? Get<TFeature>()
    {
        var feature = Get(typeof(TFeature));
        if (feature is null)
            return default;
        return (TFeature?)feature;
    }

    public void Add<TFeature>(TFeature value)
    {
        var key = typeof(TFeature);
        if (value is null)

            _features.Remove(key);
        else
            _features[key] = value;
        _revision++;
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        foreach (var pair in _features)
            yield return pair;
    }

    public void Set<TFeature>(TFeature? value)
    {
        var key = typeof(TFeature);
        if (value is null)
            _features.Remove(key);
        else
            _features[key] = value;
        _revision++;
    }

    internal void Checkpoint()
    {
        int i = 0;
        foreach (var pair in _features)
            _featuresFrozen[i++] = (pair.Key, pair.Value);
        _revision++;
        _checkpointRevision = _revision;
    }

    internal void ResetCheckpoint()
    {
        _features.Clear();
        for (int i = 0; i < MaxFeatures; i++)
        {
            var current = _featuresFrozen[i];
            if (current == default)
                break;
            _features.Add(current.Key, current.Value);
        }
        _revision = _checkpointRevision;
    }

    internal FeatureCollection Copy()
    {
        var copy = new FeatureCollection();
        foreach (var pair in _features)
            copy._features.Add(pair.Key, pair.Value);
        for (int i = 0; i < MaxFeatures; i++)
            copy._featuresFrozen[i] = _featuresFrozen[i];
        _revision++;
        _checkpointRevision = _revision;
        return copy;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private object? Get(Type key)
    {
        if (!_features.TryGetValue(key, out var value))
            return null;
        return value;
    }
}