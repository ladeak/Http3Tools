using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace CHttpServer;

internal class Http2Stream<TContext> : Http2Stream where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;
    private readonly FeatureCollection _featureCollection;

    public Http2Stream(uint streamId, uint initialWindowSize, Http2Connection connection, FeatureCollection features, IHttpApplication<TContext> application)
        : base(streamId, initialWindowSize, connection)
    {
        _application = application;
        _featureCollection = features;
    }

    protected override Task RunApplicationAsync()
    {
        var context = _application.CreateContext(_featureCollection);
        return _application.ProcessRequestAsync(context);
    }
}

internal abstract partial class Http2Stream : IThreadPoolWorkItem
{
    private enum StreamState
    {
        Open,
        HalfOpenRemote,
        HalfOpenLocal,
        Closed,
    }

    private readonly Http2Connection _connection;
    private readonly FeatureCollection _features;
    private uint _windowSize;
    private StreamState _state;

    public Http2Stream(uint streamId, uint initialWindowSize, Http2Connection connection)
    {
        _windowSize = initialWindowSize;
        _connection = connection;
        _state = StreamState.Open;
        RequestEndHeaders = false;
        _requestHeaders = new HeaderCollection();
    }

    public uint StreamId { get; }

    public bool RequestEndHeaders { get; private set; }

    protected abstract Task RunApplicationAsync();

    public void UpdateWindowSize(uint updateSize)
    {
        if (updateSize == 0)
            throw new Http2ProtocolException(); //Stream error

        var updatedValue = _windowSize + updateSize;
        if (updatedValue > Http2Connection.MaxWindowUpdateSize)
        {
            // RST_STREAM with an error code of FLOW_CONTROL_ERROR
            // Reset instead of throwing?
            throw new Http2FlowControlException();
        }
        _windowSize = updatedValue;
    }

    internal void RequestEndHeadersReceived() => RequestEndHeaders = true;

    internal void SetStaticHeader(HeaderField header, Http2Connection.PseudoHeaderFields pseudoHeader)
    {
        switch (pseudoHeader)
        {
            case Http2Connection.PseudoHeaderFields.Method:
                Method = HttpStaticFieldParser.GetMethod(header.Value);
                break;
            case Http2Connection.PseudoHeaderFields.Scheme:
                Scheme = HttpStaticFieldParser.GetScheme(header.Value);
                break;
        }

    }

    internal void SetStaticHeader(HeaderField header, Http2Connection.PseudoHeaderFields pseudoHeader, ReadOnlySpan<byte> value)
    {
        switch (pseudoHeader)
        {
            case Http2Connection.PseudoHeaderFields.Authority:
                _requestHeaders.Add("Authority", value.ToArray());
                break;
            case Http2Connection.PseudoHeaderFields.Path:
                Path = Encoding.Latin1.GetString(value);
                break;
        }

    }

    public async void Execute()
    {
        await RunApplicationAsync();
    }
}

internal partial class Http2Stream : IHttpRequestFeature
{
    private HeaderCollection _requestHeaders;

    public string Protocol { get => "HTTP/2"; set => throw new NotSupportedException(); }
    public string Scheme { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string RawTarget { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public IHeaderDictionary Headers { get => _requestHeaders; set => throw new NotSupportedException(); }
    public Stream Body { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}


public static class HttpStaticFieldParser
{
    private const string Get = "GET";
    private const string Put = "PUT";
    private const string Post = "POST";
    private const string Delete = "DELETE";
    private const string Head = "HEAD";
    private const string Options = "OPTIONS";
    private const string Connect = "CONNECT";
    private const string Trace = "TRACE";
    private const string Patch = "PATCH";
    private const string Https = "https://";

    public static string GetMethod(ReadOnlySpan<byte> method)
    {
        switch (method.Length)
        {
            case 3:
                if ("GET"u8.SequenceEqual(method))
                    return Get;
                if ("PUT"u8.SequenceEqual(method))
                    return Put;
                break;
            case 4:
                if ("POST"u8.SequenceEqual(method))
                    return Post;
                if ("HEAD"u8.SequenceEqual(method))
                    return Head;
                break;
            case 5:
                if ("PATCH"u8.SequenceEqual(method))
                    return Patch;
                if ("TRACE"u8.SequenceEqual(method))
                    return Trace;
                break;
            case 6:
                if ("DELETE"u8.SequenceEqual(method))
                    return Delete;
                break;
            case 7:
                if ("OPTIONS"u8.SequenceEqual(method))
                    return Options;
                if ("CONNECT"u8.SequenceEqual(method))
                    return Connect;
                break;
        }
        return Encoding.Latin1.GetString(method);
    }

    public static string GetScheme(ReadOnlySpan<byte> scheme)
    {
        if ("https"u8.SequenceEqual(scheme))
            return Https;
        return Encoding.Latin1.GetString(scheme);
    }
}

public class HeaderCollection : IHeaderDictionary, IEnumerator<KeyValuePair<string, StringValues>>
{
    private Dictionary<string, byte[]> _headersRaw { get; set; } = new();
    private Dictionary<string, StringValues> _headers { get; set; } = new();
    private bool _readonly;
    private long? _contentLength;

    public long? ContentLength { get => _contentLength; set => _contentLength = value; }

    public ICollection<string> Keys => new List<string>(this.Select(x => x.Key));

    public ICollection<StringValues> Values => new List<StringValues>(this.Select(x => x.Value));

    public int Count => _headersRaw.Count;

    public bool IsReadOnly => _readonly;

    public StringValues this[string key]
    {
        get
        {
            if (TryGetValue(key, out var value))
                return value;
            throw new KeyNotFoundException();
        }
        set
        {
            ValidateReadOnly();
            _headers.Add(key, value);
        }
    }

    public void SetReadOnly() => _readonly = true;

    public void Add(string key, StringValues value)
    {
        ValidateReadOnly();
        _headers.Add(key, value);
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
        result &= _headersRaw.Remove(key);
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
    }

    public void Add(string key, ReadOnlySpan<byte> rawValue)
    {
        ValidateReadOnly();
        var value = new StringValues(Encoding.Latin1.GetString(rawValue));
        _headers.TryAdd(key, value);
    }

    public void Clear()
    {
        ValidateReadOnly();
        _headers.Clear();
        _headersRaw.Clear();
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
        _currentIndex = -1;
        _passedParsed = false;
        return this;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int _currentIndex = -1;
    private bool _passedParsed = false;
    private Dictionary<string, StringValues>.Enumerator _enumerator;
    private Dictionary<string, byte[]>.Enumerator _enumeratorRaw;

    public KeyValuePair<string, StringValues> Current { get; private set; }

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        _currentIndex++;
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

