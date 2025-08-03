using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CHttpServer;

public class HeaderCollection : IHeaderDictionary, IEnumerator<KeyValuePair<string, StringValues>>
{
    // Known header keys
    private StringValues _hostValue = StringValues.Empty;
    // Move to bitmap with more known headers
    private bool _isHostValueSet = false;

    private Dictionary<string, StringValues> _headers { get; set; } = new();
    private bool _readonly;
    private long? _contentLength;

    public HeaderCollection()
    {
    }

    public long? ContentLength { get => _contentLength; set => _contentLength = value; }

    public ICollection<string> Keys => [.. this.Select(x => x.Key)];

    public ICollection<StringValues> Values => [.. this.Select(x => x.Value)];

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

            bool valueSet = TrySetKnownHeader(key, value);
            if (!valueSet)
                valueSet = _headers.TryAdd(key, value);
            if (valueSet)
                Count++;
            else
                _headers[key] = value;
        }
    }

    public void SetReadOnly() => _readonly = true;

    public void Add(string key, StringValues value)
    {
        ValidateReadOnly();
        if (!TrySetKnownHeader(key, value))
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
        if (key == "Host")
            return _isHostValueSet;
        return _headers.ContainsKey(key);
    }

    public bool Remove(string key)
    {
        ValidateReadOnly();
        if (key == "Host")
        {
            _isHostValueSet = false;
            Count--;
            return true;
        }

        var result = _headers.Remove(key);
        if (result)
            Count--;
        return result;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value)
    {
        if (key == "Host")
        {
            value = _isHostValueSet ? _hostValue : StringValues.Empty;
            return _isHostValueSet;
        }

        return _headers.TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<string, StringValues> item) => Add(item.Key, item.Value);

    public void Add(string key, byte[] value) => Add(key, value.AsSpan());

    public void Add(string key, ReadOnlySpan<byte> rawValue)
    {
        ValidateReadOnly();
        if (!TrySetKnownHeader(key, rawValue))
        {
            var value = new StringValues(Encoding.Latin1.GetString(rawValue));
            _headers.TryAdd(key, value);
        }
        Count++;
    }

    public (string, StringValues) Add(ReadOnlySpan<byte> rawKey, ReadOnlySpan<byte> rawValue)
    {
        ValidateReadOnly();
        if (!TrySetKnownHeader(rawKey, rawValue, out var key, out var value))
        {
            key = Encoding.Latin1.GetString(rawKey);
            value = new StringValues(Encoding.Latin1.GetString(rawValue));
            _headers.TryAdd(key, value);
        }
        Count++;
        return (key, value);
    }

    private bool TrySetKnownHeader(ReadOnlySpan<byte> rawKey, ReadOnlySpan<byte> rawValue, [NotNullWhen(true)] out string? key, out StringValues value)
    {
        if (rawKey == "Host"u8)
        {
            Span<char> utf16Value = stackalloc char[rawValue.Length];
            Encoding.Latin1.GetChars(rawValue, utf16Value);
            if (_hostValue.Count > 0 && utf16Value.SequenceEqual(_hostValue[0].AsSpan()))
            {
                key = "Host";
                value = _hostValue;
                return true;
            }

            _hostValue = new StringValues(utf16Value.ToString());
            _isHostValueSet = true;
            key = "Host";
            value = _hostValue;
            return true;
        }

        key = null;
        value = StringValues.Empty;
        return false;
    }

    private bool TrySetKnownHeader(string key, ReadOnlySpan<byte> rawValue)
    {
        if (key == "Host")
        {
            Span<char> utf16Value = stackalloc char[rawValue.Length];
            Encoding.Latin1.GetChars(rawValue, utf16Value);
            if (_hostValue.Count > 0 && utf16Value.SequenceEqual(_hostValue[0].AsSpan()))
            {
                key = "Host";
                return true;
            }
            _hostValue = new StringValues(utf16Value.ToString());
            _isHostValueSet = true;
            return true;
        }
        return false;
    }

    private bool TrySetKnownHeader(string key, StringValues value)
    {
        if (key == "Host")
        {
            _hostValue = value;
            _isHostValueSet = true;
            return true;
        }
        return false;
    }

    public void Clear()
    {
        ValidateReadOnly();
        _headers.Clear();
        _isHostValueSet = false;
        Count = 0;
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        TryGetValue(item.Key, out var value);
        return value == item.Value;
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        if (_isHostValueSet)
            array[arrayIndex++] = new KeyValuePair<string, StringValues>("Host", _hostValue);
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
        _iteratorState = 0;
        return this;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int _iteratorState = 0;
    private Dictionary<string, StringValues>.Enumerator _enumerator;

    public KeyValuePair<string, StringValues> Current { get; private set; }

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        bool hasNext;
        if (_iteratorState == 0)
        {
            _iteratorState = 1;
            if (_isHostValueSet)
            {
                Current = new KeyValuePair<string, StringValues>("Host", _hostValue);
                return true;
            }
        }
        if (_iteratorState == 1)
        {
            hasNext = _enumerator.MoveNext();
            if (hasNext)
            {
                Current = _enumerator.Current;
                return true;
            }
            else
                _iteratorState = 2;
        }
        return false;
    }

    public void Reset()
    {
        throw new NotSupportedException();
    }

    public void ResetHeaderCollection()
    {
        _readonly = false;
        _headers.Clear();
        Count = 0;
        _contentLength = null;
        _iteratorState = 0;
        _enumerator = default;
        _isHostValueSet = false;
    }

    public void Dispose()
    {
        _enumerator.Dispose();
        _iteratorState = 2;
        Current = default;
    }
}

