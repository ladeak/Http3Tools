using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer;

internal class FeatureCollectionContext<TContext> : FeatureCollection, IHostContextContainer<TContext> where TContext : notnull
{
    public TContext? HostContext { get; set; }

    internal override void ResetCheckpoint()
    {
        base.ResetCheckpoint();
        HostContext = default;
    }

    protected override FeatureCollectionContext<TContext> Create() => new FeatureCollectionContext<TContext>();
}

public class FeatureCollection : IFeatureCollection
{
    private const int MaxFeatures = 15;
    private readonly Dictionary<Type, object> _features = new();
    private InlineArray15<(Type Key, object Value)> _featuresFrozen = new();
    private volatile int _revision = 0;
    private int _checkpointRevision = 0;

    public object? this[Type key]
    {
        get => Get(key);
        set => Set(key, value);
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

    public void Add<TFeature>(TFeature value) => Set(typeof(TFeature), value);

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        foreach (var pair in _features)
            yield return pair;
    }

    public void Set<TFeature>(TFeature? value) => Set(typeof(TFeature), value);

    internal void Checkpoint()
    {
        int i = 0;
        foreach (var pair in _features)
            _featuresFrozen[i++] = (pair.Key, pair.Value);
        _revision++;
        _checkpointRevision = _revision;
    }

    internal virtual void ResetCheckpoint()
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
        Debug.Assert(_checkpointRevision == 0);
        var copy = Create();
        foreach (var pair in _features)
            copy._features.Add(pair.Key, pair.Value);
        _revision++;
        return copy;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private object? Get(Type key)
    {
        if (!_features.TryGetValue(key, out var value))
            return null;
        return value;
    }

    internal FeatureCollection ToContextAware<TContext>() where TContext : notnull
    {
        Debug.Assert(_checkpointRevision == 0);
        var copy = new FeatureCollectionContext<TContext>();
        foreach (var pair in _features)
            copy._features.Add(pair.Key, pair.Value);
        _revision++;
        return copy;
    }

    protected virtual FeatureCollection Create() => new FeatureCollection();

    private void Set(Type key, object? value)
    {
        if (value is null)
            _features.Remove(key);
        else
            _features[key] = value;
        _revision++;
    }
}