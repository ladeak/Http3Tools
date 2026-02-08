using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using FeatureItem = (System.Type Key, object Value);

namespace CHttpServer;

// FeatureCollection is a type over a mutable and an immutable data structure.
// After instantiating a FeatureCollection, Add and AddRange methods update the
// immutable data structure. This makes copies relatively cheap. Copies are
// created by the server, the connection and the stream. After the last copy
// a checkpoint can be created (no copies allowed after the checkpoint). After
// the checkpoint, updates mutate CheckpointCollection. The 'ASP.NET application'
// only adds features / reads features after the checkpoint. When the application
// completes, the checkpoint is reset.
// This collection does not allow feature removal and does not allow amending
// features after the checkpoint created.

internal sealed class FeatureCollection<TContext> : FeatureCollection, IHostContextContainer<TContext> where TContext : notnull
{
    public TContext? HostContext { get; set; }

    internal override FeatureCollection<TContext> Copy()
    {
        var instance = Copy<FeatureCollection<TContext>>();
        instance.HostContext = HostContext;
        return instance;
    }

    internal override void ResetCheckpoint()
    {
        base.ResetCheckpoint();
        HostContext = default;
    }
}

public class FeatureCollection : IFeatureCollection
{
    private class CheckpointCollection(int revision)
    {
        private readonly List<FeatureItem> _features = [];
        public int Revision { get; private set; } = revision;

        public object? Get(Type key)
        {
            foreach (var (Key, Value) in _features)
                if (Key == key)
                    return Value;
            return null;
        }

        public void Set(Type key, object value)
        {
            for (int i = 0; i < _features.Count; i++)
            {
                var currentFeatureKey = _features[i].Key;
                if (currentFeatureKey == key)
                {
                    _features[i] = (key, value);
                    Revision++;
                    return;
                }
            }
            Add((key, value));
        }

        public void Add(FeatureItem item)
        {
            Revision++;
            _features.Add(item);
        }

        public void Reset(int revision)
        {
            _features.Clear();
            Revision = revision;
        }

        public IEnumerator<FeatureItem> GetEnumerator() => _features.GetEnumerator();
    }

    private CheckpointCollection? _checkpoint = null;
    private FeatureItem[] _features = [];
    private int _revision = 0;

    public object? this[Type key]
    {
        get => Get(key);
        set => Set(key, value);
    }

    public bool IsReadOnly => false;

    public int Revision => _checkpoint?.Revision ?? _revision;

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
        if (_checkpoint != null)
        {
            _checkpoint.Add((typeof(TFeature), value));
            return;
        }
        else
        {
            _features = [.. _features, (typeof(TFeature), value)];
            _revision++;
        }
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        if (_checkpoint != null)
        {
            foreach (var (Key, Value) in _checkpoint)
                yield return new KeyValuePair<Type, object>(Key, Value);
        }

        foreach (var (Key, Value) in _features)
            yield return new KeyValuePair<Type, object>(Key, Value);

    }

    public void Set<TFeature>(TFeature? value) => Set(typeof(TFeature), value);

    internal void Checkpoint()
    {
        _revision++;
        _checkpoint = new CheckpointCollection(_revision);
    }

    internal virtual void ResetCheckpoint()
    {
        _checkpoint?.Reset(_revision);
    }

    internal virtual FeatureCollection Copy() => Copy<FeatureCollection>();

    internal T Copy<T>() where T : FeatureCollection, new()
    {
        // Copy not supported after checkpoint.
        if (_checkpoint != null)
            throw new InvalidOperationException();
        return new T() { _features = this._features, _revision = this._revision };
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private object? Get(Type key)
    {
        var item = _checkpoint?.Get(key);
        if (item is not null)
            return item;
        foreach (var (Key, Value) in _features)
            if (Key == key)
                return Value;
        return null;
    }

    internal FeatureCollection ToContextAware<TContext>() where TContext : notnull => Copy<FeatureCollection<TContext>>();

    private void Set(Type key, object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_checkpoint != null)
        {
            _checkpoint.Set(key, value);
            return;
        }

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
        _revision++;
    }

    public void AddRange(params ReadOnlySpan<FeatureItem> items)
    {
        if (_checkpoint != null)
            throw new InvalidOperationException("Only use AddRange before checkpoint.");
        _features = [.. _features, .. items];
        _revision++;
    }
}
