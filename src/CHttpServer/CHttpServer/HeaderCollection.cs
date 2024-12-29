using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CHttpServer;

public class HeaderCollection : IHeaderDictionary, IEnumerator<KeyValuePair<string, StringValues>>
{
    private Dictionary<string, byte[]> _headersRaw { get; set; } = new();
    private Dictionary<string, StringValues> _headers { get; set; } = new();
    private bool _readonly;
    private long? _contentLength;

    public HeaderCollection()
    {
        
    }

    public long? ContentLength { get => _contentLength; set => _contentLength = value; }

    public ICollection<string> Keys => new List<string>(this.Select(x => x.Key));

    public ICollection<StringValues> Values => new List<StringValues>(this.Select(x => x.Value));

    public int Count { get; private set; }

    public bool IsReadOnly => _readonly;

    public StringValues this[string key]
    {
        get
        {
            if (TryGetValue(key, out var value))
                return value;
            return default;
        }
        set
        {
            ValidateReadOnly();
            if (_headers.TryAdd(key, value))
                Count++;
            else
                _headers[key] = value;
        }
    }

    public void SetReadOnly() => _readonly = true;

    public void Add(string key, StringValues value)
    {
        ValidateReadOnly();
        _headers.Add(key, value);
        Count++;
    }

    private void ValidateReadOnly()
    {
        if (_readonly)
            ThrowReadOnlyException();
    }

    private static void ThrowReadOnlyException() => throw new InvalidOperationException("HeaderCollection is readonly");

    public bool ContainsKey(string key)
    {
        return _headers.ContainsKey(key) || _headersRaw.ContainsKey(key);
    }

    public bool Remove(string key)
    {
        ValidateReadOnly();
        var result = _headers.Remove(key);
        result |= _headersRaw.Remove(key);
        if (result)
            Count--;
        return result;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value)
    {
        if (_headers.TryGetValue(key, out value))
            return true;

        if (!_headersRaw.TryGetValue(key, out var rawValue))
            return false;

        value = new StringValues(Encoding.Latin1.GetString(rawValue));
        _headers.TryAdd(key, value);
        return true;
    }

    public void Add(KeyValuePair<string, StringValues> item) => Add(item.Key, item.Value);

    public void Add(string key, byte[] value)
    {
        ValidateReadOnly();
        _headersRaw.Add(key, value);
        Count++;
    }

    public void Add(string key, ReadOnlySpan<byte> rawValue)
    {
        ValidateReadOnly();
        var value = new StringValues(Encoding.Latin1.GetString(rawValue));
        _headers.TryAdd(key, value);
        Count++;
    }

    public void Add(ReadOnlySpan<byte> key, ReadOnlySpan<byte> rawValue)
    {
        ValidateReadOnly();
        var value = new StringValues(Encoding.Latin1.GetString(rawValue));
        _headers.TryAdd(Encoding.Latin1.GetString(key), value);
        Count++;
    }

    public void Clear()
    {
        ValidateReadOnly();
        _headers.Clear();
        _headersRaw.Clear();
        Count = 0;
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        TryGetValue(item.Key, out var value);
        return value == item.Value;
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        foreach (var item in this)
            array[arrayIndex++] = item;
    }

    public bool Remove(KeyValuePair<string, StringValues> item)
    {
        if (TryGetValue(item.Key, out var value) && value == item.Value)
        {
            Remove(item.Key);
            return true;
        }
        return false;

    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        _enumerator = _headers.GetEnumerator();
        _enumeratorRaw = _headersRaw.GetEnumerator();
        _passedParsed = false;
        return this;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool _passedParsed = false;
    private Dictionary<string, StringValues>.Enumerator _enumerator;
    private Dictionary<string, byte[]>.Enumerator _enumeratorRaw;

    public KeyValuePair<string, StringValues> Current { get; private set; }

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        bool hasNext;
        if (!_passedParsed)
        {
            hasNext = _enumerator.MoveNext();
            if (hasNext)
            {
                Current = _enumerator.Current;
                return true;
            }
            else
            {
                _passedParsed = true;
            }
        }

        do
        {
            hasNext = _enumeratorRaw.MoveNext();
        } while (hasNext && _headers.ContainsKey(_enumeratorRaw.Current.Key));

        if (!hasNext)
            return false;

        var currentRaw = _enumeratorRaw.Current;
        var value = new StringValues(Encoding.Latin1.GetString(currentRaw.Value));
        _headers.TryAdd(currentRaw.Key, value);
        Current = new(currentRaw.Key, value);
        return true;
    }

    public void Reset()
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
        _enumerator.Dispose();
        _enumeratorRaw.Dispose();
        Current = default;
    }
}

