using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CHttpServer;

public class ResponseHeaderCollection : IHeaderDictionary, IEnumerator<KeyValuePair<string, StringValues>>
{
    private const string ServerHeaderName = "Server";
    private const string AltSvcHeaderName = "Alt-Svc";

    // Known header keys
    private readonly StringValues _serverValue = "CHttp";
    private StringValues _altSvcValue = StringValues.Empty;
    // Move to bitmap with more known headers
    private bool _isAltSvcValueSet = false;

    private Dictionary<string, StringValues> _headers { get; set; } = new();
    private bool _readonly;
    private long? _contentLength;

    public ResponseHeaderCollection()
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
        return key switch
        {
            ServerHeaderName => true,
            AltSvcHeaderName => _isAltSvcValueSet,
            _ => _headers.ContainsKey(key),
        };
    }

    public bool Remove(string key)
    {
        ValidateReadOnly();
        if (key == AltSvcHeaderName)
        {
            _isAltSvcValueSet = false;
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
        if (key == ServerHeaderName)
        {
            value = _serverValue;
            return true;
        }
        if (key == AltSvcHeaderName)
        {
            value = _isAltSvcValueSet ? _altSvcValue : StringValues.Empty;
            return _isAltSvcValueSet;
        }

        return _headers.TryGetValue(key, out value);
    }

    public void Add(KeyValuePair<string, StringValues> item) => Add(item.Key, item.Value);

    //public void Add(string key, byte[] value) => Add(key, value.AsSpan());

    //public void Add(string key, ReadOnlySpan<byte> rawValue)
    //{
    //    ValidateReadOnly();
    //    if (!TrySetKnownHeader(key, rawValue))
    //    {
    //        var value = new StringValues(Encoding.Latin1.GetString(rawValue));
    //        _headers.TryAdd(key, value);
    //    }
    //    Count++;
    //}

    //public (string, StringValues) Add(ReadOnlySpan<byte> rawKey, ReadOnlySpan<byte> rawValue)
    //{
    //    ValidateReadOnly();
    //    if (!TrySetKnownHeader(rawKey, rawValue, out var key, out var value))
    //    {
    //        key = Encoding.Latin1.GetString(rawKey);
    //        value = new StringValues(Encoding.Latin1.GetString(rawValue));
    //        _headers.TryAdd(key, value);
    //    }
    //    Count++;
    //    return (key, value);
    //}

    //private bool TrySetKnownHeader(ReadOnlySpan<byte> rawKey, ReadOnlySpan<byte> rawValue, [NotNullWhen(true)] out string? key, out StringValues value)
    //{
    //    if (rawKey == "Server"u8)
    //    {
    //        key = ServerHeaderName;
    //        value = _serverValue;
    //        return true;
    //    }
    //    if (rawKey == "Alt-Svc"u8)
    //    {
    //        Span<char> utf16Value = stackalloc char[rawValue.Length];
    //        Encoding.Latin1.GetChars(rawValue, utf16Value);
    //        if (_altSvcValue.Count > 0 && utf16Value.SequenceEqual(_altSvcValue[0].AsSpan()))
    //        {
    //            key = AltSvcHeaderName;
    //            value = _altSvcValue;
    //            return true;
    //        }

    //        _altSvcValue = new StringValues(utf16Value.ToString());
    //        _isAltSvcValueSet = true;
    //        key = AltSvcHeaderName;
    //        value = _altSvcValue;
    //        return true;
    //    }

    //    key = null;
    //    value = StringValues.Empty;
    //    return false;
    //}

    //private bool TrySetKnownHeader(string key, ReadOnlySpan<byte> rawValue)
    //{
    //    if (key == ServerHeaderName)
    //    {
    //        return true;
    //    }
    //    if (key == AltSvcHeaderName)
    //    {
    //        Span<char> utf16Value = stackalloc char[rawValue.Length];
    //        Encoding.Latin1.GetChars(rawValue, utf16Value);
    //        if (_altSvcValue.Count > 0 && utf16Value.SequenceEqual(_altSvcValue[0].AsSpan()))
    //        {
    //            key = AltSvcHeaderName;
    //            return true;
    //        }
    //        _altSvcValue = new StringValues(utf16Value.ToString());
    //        _isAltSvcValueSet = true;
    //        return true;
    //    }
    //    return false;
    //}

    private bool TrySetKnownHeader(string key, StringValues value)
    {
        if (key == ServerHeaderName)
        {
            return true;
        }
        if (key == AltSvcHeaderName)
        {
            _altSvcValue = value;
            _isAltSvcValueSet = true;
            return true;
        }
        return false;
    }

    public void Clear()
    {
        ValidateReadOnly();
        _headers.Clear();
        _isAltSvcValueSet = false;
        Count = 0;
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        TryGetValue(item.Key, out var value);
        return value == item.Value;
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        array[arrayIndex++] = new KeyValuePair<string, StringValues>(ServerHeaderName, _serverValue);
        if (_isAltSvcValueSet)
            array[arrayIndex++] = new KeyValuePair<string, StringValues>(AltSvcHeaderName, _altSvcValue);
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

    public StringValues AltSvc
    {
        get { return _isAltSvcValueSet ? _altSvcValue : StringValues.Empty; }
        set { _altSvcValue = value; _isAltSvcValueSet = true; }
    }

    public bool MoveNext()
    {
        bool hasNext;
        if (_iteratorState == 0)
        {
            _iteratorState = 1;
            Current = new KeyValuePair<string, StringValues>(ServerHeaderName, _serverValue);
            return true;
        }
        if (_iteratorState == 1)
        {
            _iteratorState = 2;
            if (_isAltSvcValueSet)
            {
                Current = new KeyValuePair<string, StringValues>(AltSvcHeaderName, _altSvcValue);
                return true;
            }
        }
        if (_iteratorState == 2)
        {
            hasNext = _enumerator.MoveNext();
            if (hasNext)
            {
                Current = _enumerator.Current;
                return true;
            }
            else
                _iteratorState = 3;
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
        _isAltSvcValueSet = false;
    }

    public void Dispose()
    {
        _enumerator.Dispose();
        _iteratorState = 2;
        Current = default;
    }
}

