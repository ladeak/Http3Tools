using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CHttpServer.Http3;

public class Http3ResponseHeaderCollection : IHeaderDictionary, IEnumerator<KeyValuePair<string, StringValues>>
{
    // Known header keys
    private StringValues _serverValue = StringValues.Empty;
    // Move to bitmap with more known headers
    private bool _isServerValueSet = false;

    private Dictionary<string, StringValues> _headers { get; set; } = new();
    private bool _readonly;

    public Http3ResponseHeaderCollection()
    {
    }

    public long? ContentLength { get; set; }

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
            if (!_headers.TryAdd(key, value))
                return;
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
        if (key == "Server")
            return _isServerValueSet;
        return _headers.ContainsKey(key);
    }

    public bool Remove(string key)
    {
        ValidateReadOnly();
        if (key == "Server")
        {
            _isServerValueSet = false;
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
        if (key == "Server")
        {
            value = _isServerValueSet ? _serverValue : StringValues.Empty;
            return _isServerValueSet;
        }

        return _headers.TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<string, StringValues> item) => Add(item.Key, item.Value);

    private bool TrySetKnownHeader(ReadOnlySpan<byte> rawKey, ReadOnlySpan<byte> rawValue, [NotNullWhen(true)] out string? key)
    {
        if (rawKey == "Server"u8)
        {
            Span<char> utf16Value = stackalloc char[rawValue.Length];
            Encoding.Latin1.GetChars(rawValue, utf16Value);
            if (!utf16Value.SequenceEqual(_serverValue[0].AsSpan()))
                _serverValue = new StringValues(utf16Value.ToString());
            _isServerValueSet = true;
            key = "Server";
            return true;
        }

        key = null;
        return false;
    }

    private bool TrySetKnownHeader(string key, in ReadOnlySpan<byte> rawValue)
    {
        if (key == "Server")
        {
            Span<char> utf16Value = stackalloc char[rawValue.Length];
            Encoding.Latin1.GetChars(rawValue, utf16Value);
            if (!utf16Value.SequenceEqual(_serverValue[0].AsSpan()))
                _serverValue = new StringValues(utf16Value.ToString());
            _isServerValueSet = true;
            return true;
        }
        return false;
    }

    private bool TrySetKnownHeader(string key, ReadOnlySequence<byte> rawValue)
    {
        if (key == "Server")
        {
            Span<char> utf16Value = stackalloc char[checked((int)rawValue.Length)];
            Encoding.Latin1.GetChars(rawValue, utf16Value);
            if (!utf16Value.SequenceEqual(_serverValue[0].AsSpan()))
                _serverValue = new StringValues(utf16Value.ToString());
            _isServerValueSet = true;
            return true;
        }
        return false;
    }

    private bool TrySetKnownHeader(string key, StringValues value)
    {
        if (key == "Server")
        {
            _serverValue = value;
            _isServerValueSet = true;
            return true;
        }
        return false;
    }

    public void Clear()
    {
        ValidateReadOnly();
        _headers.Clear();
        _isServerValueSet = false;
        Count = 0;
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        TryGetValue(item.Key, out var value);
        return value == item.Value;
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        if (_isServerValueSet)
            array[arrayIndex++] = new KeyValuePair<string, StringValues>("Server", _serverValue);
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
            if (_isServerValueSet)
            {
                Current = new KeyValuePair<string, StringValues>("Server", _serverValue);
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
        throw new NotSupportedException("This method resets the iterator, which is not supported. User ResetHeaderCollection() method instead.");
    }

    public void ResetHeaderCollection()
    {
        _readonly = false;
        _headers.Clear();
        Count = 0;
        ContentLength = null;
        _iteratorState = 0;
        _enumerator = default;
        _isServerValueSet = false;
    }

    public void Dispose()
    {
        _enumerator.Dispose();
        _iteratorState = 2;
        Current = default;
    }
}

