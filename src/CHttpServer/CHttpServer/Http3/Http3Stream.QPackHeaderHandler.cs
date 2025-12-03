using System.Buffers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Http3;

internal sealed partial class Http3Stream : IQPackHeaderHandler, IHttpRequestFeature
{
    private const byte QueryStringSeparator = (byte)'?';
    private byte[] _pathEncoded;
    private bool _isPathSet;

    public string Protocol { get => "HTTP/3"; set => throw new PlatformNotSupportedException(); }
    public string PathBase { get => string.Empty; set => throw new PlatformNotSupportedException(); }
    public string RawTarget { get => string.Empty; set => throw new PlatformNotSupportedException(); }
#pragma warning disable CS9266 // Property accessor should use 'field' because the other accessor is using it.
    public string Scheme { get; set; }
    public string Method { get; set; }
    public string Path { get => _isPathSet ? field : string.Empty; set => field = value; }
    public string QueryString { get; set; }
    public Stream Body { get; set => throw new PlatformNotSupportedException(); }
    public IHeaderDictionary Headers { get => _requestHeaders; set => throw new PlatformNotSupportedException(); }
#pragma warning restore CS9266 // Property accessor should use 'field' because the other accessor is using it.

    private Http3RequestHeaderCollection _requestHeaders = new();

    public void OnHeader(in KnownHeaderField staticHeader, in ReadOnlySequence<byte> value)
    {
        var decodedValue = Encoding.Latin1.GetString(value);
        switch (staticHeader.StaticTableIndex)
        {
            case 1:
                var queryStringSeparatorIndex = value.PositionOf(QueryStringSeparator);
                ReadOnlySequence<byte> pathPart = value;
                if (queryStringSeparatorIndex != null)
                {
                    pathPart = value.Slice(0, queryStringSeparatorIndex.Value);
                    QueryString = Encoding.Latin1.GetString(value.Slice(queryStringSeparatorIndex.Value));
                }
                _isPathSet = true;
                if (pathPart.IsSingleSegment)
                {
                    if (!_pathEncoded.SequenceEqual(pathPart.FirstSpan))
                    {
                        Path = Encoding.Latin1.GetString(pathPart);
                        _pathEncoded = pathPart.FirstSpan.ToArray();
                    }
                    return;
                }
                Path = Encoding.Latin1.GetString(pathPart);
                _pathEncoded = [];
                break;
            case 15:
            case 16:
            case 17:
            case 18:
            case 19:
            case 20:
            case 21:
                Method = Encoding.Latin1.GetString(value);
                break;
            case 22:
            case 23:
                Scheme = Encoding.Latin1.GetString(value);
                break;
            default:
                _requestHeaders.Add(staticHeader.Name, Encoding.Latin1.GetString(value));
                break;
        }
    }

    public void OnHeader(in KnownHeaderField staticHeader)
    {
        switch (staticHeader.StaticTableIndex)
        {
            case 1:
                Path = staticHeader.Value;
                break;
            case 15:
            case 16:
            case 17:
            case 18:
            case 19:
            case 20:
            case 21:
                Method = staticHeader.Value;
                break;
            case 22:
            case 23:
                Scheme = staticHeader.Value;
                break;
            default:
                _requestHeaders.Add(staticHeader.Name, staticHeader.Value);
                break;
        }
    }

    public void OnHeader(in ReadOnlySequence<byte> name, in ReadOnlySequence<byte> value)
    {
        var decodedHeaderName = Encoding.Latin1.GetString(name);
        var decodedValue = Encoding.Latin1.GetString(value);
        _requestHeaders.Add(decodedHeaderName, decodedValue);
    }
}

//IHttpRequestFeature, IHttpRequestBodyDetectionFeature, IHttpRequestLifetimeFeature
//IHttpResponseFeature, IHttpResponseBodyFeature, IHttpResponseTrailersFeature