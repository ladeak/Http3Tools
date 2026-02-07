using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using FeatureItem = (System.Type Key, object Value);

namespace CHttpServer;

internal sealed class FeatureCollection<TContext> : FeatureCollection, IHostContextContainer<TContext> where TContext : notnull
{
    public TContext? HostContext { get; set; }

    internal override void ResetCheckpoint()
    {
        base.ResetCheckpoint();
        HostContext = default;
    }
}

public class FeatureCollection : IFeatureCollection
{
    protected record struct CheckpointCollection(FeatureItem[] Features, int Revision)
    {
        public object? Get(Type key)
        {
            foreach (var feature in Features)
                if (feature.Key == key)
                    return feature.Value;
            return null;
        }
    }

    protected CheckpointCollection? _checkpoint = null;
    protected FeatureItem[] _features = [];
    protected int _revision = 0;

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

    public void Add<TFeature>(TFeature value) where TFeature : notnull
    {
        _features = [.. _features, (typeof(TFeature), value)];
        _revision++;
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        foreach (var pair in _features)
            yield return new KeyValuePair<Type, object>(pair.Key, pair.Value);

        if (_checkpoint == null)
            yield break;

        foreach (var pair in _checkpoint.Value.Features)
            yield return new KeyValuePair<Type, object>(pair.Key, pair.Value);
    }

    public void Set<TFeature>(TFeature? value) => Set(typeof(TFeature), value);

    internal void Checkpoint()
    {
        _revision++;
        _checkpoint = new CheckpointCollection(_features, _revision);
        _features = [];
    }

    internal virtual void ResetCheckpoint()
    {
        if (_checkpoint == null)
        {
            _features = [];
            _revision = 0;
        }
        else
        {
            _features = _checkpoint.Value.Features;
            _revision = _checkpoint.Value.Revision;
            _checkpoint = null;
        }
    }

    internal FeatureCollection Copy() => Copy<FeatureCollection>();

    internal T Copy<T>() where T : FeatureCollection, new()
    {
        return new T() { _features = this._features, _revision = this._revision, _checkpoint = this._checkpoint };
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private object? Get(Type key)
    {
        foreach (var feature in _features)
            if (feature.Key == key)
                return feature.Value;
        return _checkpoint?.Get(key);
    }

    internal FeatureCollection ToContextAware<TContext>() where TContext : notnull => Copy<FeatureCollection<TContext>>();

    private void Set(Type key, object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException();
        }
        else
        {
            for (int i = 0; i < _features.Length; i++)
            {
                if (_features[i].Key == key)
                {
                    _features = [.. _features];
                    _features[i] = (key, value);
                    _revision++;
                    return;
                }
            }

            _features = [.. _features, (key, value)];
        }
        _revision++;
    }

    public void AddRange(params ReadOnlySpan<FeatureItem> items)
    {
        _features = [.. _features, .. items];
        _revision++;
    }
}
