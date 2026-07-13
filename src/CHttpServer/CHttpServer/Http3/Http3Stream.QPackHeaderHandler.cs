using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Http3;

internal sealed partial class Http3Stream : IQPackHeaderHandler, IHttpRequestFeature, IRequestBodyPipeFeature
{
    private const byte QueryStringSeparator = (byte)'?';
    private byte[] _pathEncoded;
    private bool _isPathSet;

    private string _hostDecoded;
    private byte[] _hostEncoded;
    private Http3DeframingPipeReader _requestDataToAppPipeReader;


    public string Protocol { get => "HTTP/3"; set => throw new PlatformNotSupportedException(); }
    public string PathBase { get => string.Empty; set => throw new PlatformNotSupportedException(); }
    public string RawTarget { get => string.Empty; set => throw new PlatformNotSupportedException(); }
#pragma warning disable CS9266 // Property accessor should use 'field' because the other accessor is using it.
    public string Scheme { get; set; }
    public string Method { get; set; }
    public string Path { get => _isPathSet ? field : string.Empty; set => field = value; }
    public string QueryString { get; set; }
    public Stream Body { get => Reader.AsStream(); set => throw new PlatformNotSupportedException(); }
    IHeaderDictionary IHttpRequestFeature.Headers { get => _requestHeaders; set => throw new PlatformNotSupportedException(); }
#pragma warning restore CS9266 // Property accessor should use 'field' because the other accessor is using it.

    public PipeReader Reader => _requestDataToAppPipeReader;

    private readonly Http3RequestHeaderCollection _requestHeaders;

    private static readonly SearchValues<char> InvalidFieldValueChars = SearchValues.Create(
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000A\u000B\u000C\u000D\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F\u007F");

    private static readonly SearchValues<char> ValidFieldNameChars = SearchValues.Create("!#$%&'*+-.^_`ˇ|~0123456789abcdefghijklmnopqrstuvwxyz");

    public void OnHeader(in KnownHeaderField staticHeader, in ReadOnlySequence<byte> value)
    {
        switch (staticHeader.StaticTableIndex)
        {
            case 0:
                if (!value.IsSingleSegment || !value.FirstSpan.SequenceEqual(_hostEncoded))
                {
                    _hostDecoded = Encoding.Latin1.GetString(value);
                    ValidateFieldValue(_hostDecoded);
                }
                _requestHeaders.Add("Host", _hostDecoded);
                _hostEncoded = value.ToArray();
                break;
            case 1:
                var queryStringSeparatorIndex = value.PositionOf(QueryStringSeparator);
                ReadOnlySequence<byte> pathPart = value;
                if (queryStringSeparatorIndex != null)
                {
                    pathPart = value.Slice(0, queryStringSeparatorIndex.Value);
                    QueryString = Encoding.Latin1.GetString(value.Slice(queryStringSeparatorIndex.Value));
                    ValidateFieldValue(QueryString);
                }
                _isPathSet = true;
                if (pathPart.IsSingleSegment)
                {
                    if (!_pathEncoded.SequenceEqual(pathPart.FirstSpan))
                    {
                        Path = Encoding.Latin1.GetString(pathPart);
                        ValidateFieldValue(Path);
                        _pathEncoded = pathPart.FirstSpan.ToArray();
                    }
                    return;
                }
                Path = Encoding.Latin1.GetString(pathPart);
                ValidateFieldValue(Path);
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
                ValidateFieldValue(Method);
                break;
            case 22:
            case 23:
                Scheme = Encoding.Latin1.GetString(value);
                ValidateFieldValue(Scheme);
                break;
            default:
                var fieldValue = Encoding.Latin1.GetString(value);
                ValidateFieldValue(fieldValue);
                _requestHeaders.Add(staticHeader.Name, fieldValue);
                break;
        }
    }

    public void OnHeader(in KnownHeaderField staticHeader)
    {
        switch (staticHeader.StaticTableIndex)
        {
            case 0:
                _requestHeaders.Add("Host", staticHeader.Value);
                break;
            case 1:
                _isPathSet = true;
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
        var fieldValue = Encoding.Latin1.GetString(value);
        ValidateFieldValue(fieldValue);
        if (AspNetCoreHeadnerNamesLookup.TryGetAspNetCoreHeader(name, out var aspnetDecodedHeaderName))
            _requestHeaders.Add(aspnetDecodedHeaderName, fieldValue);
        else
        {
            var fieldName = Encoding.Latin1.GetString(name);
            ValidateFieldName(fieldName);
            _requestHeaders.Add(fieldName, fieldValue);
        }
    }

    private static void ValidateFieldValue(ReadOnlySpan<char> value)
    {
        if (value.ContainsAny(InvalidFieldValueChars))
            ThrowInvalidChar();
    }

    private static void ValidateFieldName(ReadOnlySpan<char> value)
    {
        if (value.ContainsAnyExcept(ValidFieldNameChars))
            ThrowInvalidChar();
    }

    private static void ThrowInvalidChar() => throw new InvalidOperationException("Invalid character in Header");
}
