using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace CHttpServer.Http3;

public static class AspNetCoreHeadnerNamesLookup
{
    private class Comparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (x == null || y == null)
                return false;
            return x.SequenceEqual(y);
        }

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

        public int GetHashCode([DisallowNull] byte[] obj) => (int)XxHash3.HashToUInt64(obj, 0);

        public int GetHashCode(ReadOnlySpan<byte> alternate) => (int)XxHash3.HashToUInt64(alternate, 0);
    }

    public static Dictionary<byte[], string> AspNetHeaderNamingLookupTable { get; } = new Dictionary<byte[], string>(new Comparer())
    {
        { Encoding.Latin1.GetBytes("accept"), HeaderNames.Accept },
        { Encoding.Latin1.GetBytes("accept-encoding"), HeaderNames.AcceptEncoding },
        { Encoding.Latin1.GetBytes("accept-ranges"), HeaderNames.AcceptRanges },
        { Encoding.Latin1.GetBytes("accept-language"), HeaderNames.AcceptLanguage },
        { Encoding.Latin1.GetBytes("accept-charset"), HeaderNames.AcceptCharset },
        { Encoding.Latin1.GetBytes("access-control-allow-credentials"), HeaderNames.AccessControlAllowCredentials },
        { Encoding.Latin1.GetBytes("access-control-allow-headers"), HeaderNames.AccessControlAllowHeaders },
        { Encoding.Latin1.GetBytes("access-control-allow-origin"), HeaderNames.AccessControlAllowOrigin },
        { Encoding.Latin1.GetBytes("access-control-expose-headers"), HeaderNames.AccessControlExposeHeaders },
        { Encoding.Latin1.GetBytes("access-control-max-age"), HeaderNames.AccessControlMaxAge },
        { Encoding.Latin1.GetBytes("access-control-request-headers"), HeaderNames.AccessControlRequestHeaders },
        { Encoding.Latin1.GetBytes("access-control-request-method"), HeaderNames.AccessControlRequestMethod },
        { Encoding.Latin1.GetBytes("age"), HeaderNames.Age },
        { Encoding.Latin1.GetBytes("allow"), HeaderNames.Allow },
        { Encoding.Latin1.GetBytes("alt-svc"), HeaderNames.AltSvc },
        { Encoding.Latin1.GetBytes("authorization"), HeaderNames.Authorization },
        { Encoding.Latin1.GetBytes("baggage"), HeaderNames.Baggage },
        { Encoding.Latin1.GetBytes("cache-control"), HeaderNames.CacheControl },
        { Encoding.Latin1.GetBytes("connection"), HeaderNames.Connection },
        { Encoding.Latin1.GetBytes("content-disposition"), HeaderNames.ContentDisposition },
        { Encoding.Latin1.GetBytes("content-encoding"), HeaderNames.ContentEncoding },
        { Encoding.Latin1.GetBytes("content-language"), HeaderNames.ContentLanguage },
        { Encoding.Latin1.GetBytes("content-length"), HeaderNames.ContentLength },
        { Encoding.Latin1.GetBytes("content-location"), HeaderNames.ContentLocation },
        { Encoding.Latin1.GetBytes("content-md5"), HeaderNames.ContentMD5 },
        { Encoding.Latin1.GetBytes("content-range"), HeaderNames.ContentRange },
        { Encoding.Latin1.GetBytes("content-security-policy"), HeaderNames.ContentSecurityPolicy },
        { Encoding.Latin1.GetBytes("content-security-policy-report-only"), HeaderNames.ContentSecurityPolicyReportOnly },
        { Encoding.Latin1.GetBytes("content-type"), HeaderNames.ContentType },
        { Encoding.Latin1.GetBytes("cookie"), HeaderNames.Cookie },
        { Encoding.Latin1.GetBytes("correlation-context"), HeaderNames.CorrelationContext },
        { Encoding.Latin1.GetBytes("date"), HeaderNames.Date },
        { Encoding.Latin1.GetBytes("etag"), HeaderNames.ETag },
        { Encoding.Latin1.GetBytes("expect"), HeaderNames.Expect },
        { Encoding.Latin1.GetBytes("expires"), HeaderNames.Expires },
        { Encoding.Latin1.GetBytes("from"), HeaderNames.From },
        { Encoding.Latin1.GetBytes("grpc-accept-encoding"), HeaderNames.GrpcAcceptEncoding },
        { Encoding.Latin1.GetBytes("grpc-encoding"), HeaderNames.GrpcEncoding },
        { Encoding.Latin1.GetBytes("grpc-message"), HeaderNames.GrpcMessage },
        { Encoding.Latin1.GetBytes("grpc-status"), HeaderNames.GrpcStatus },
        { Encoding.Latin1.GetBytes("grpc-timeout"), HeaderNames.GrpcTimeout },
        { Encoding.Latin1.GetBytes("host"), HeaderNames.Host },
        { Encoding.Latin1.GetBytes("if-modified-since"), HeaderNames.IfModifiedSince },
        { Encoding.Latin1.GetBytes("if-match"), HeaderNames.IfMatch },
        { Encoding.Latin1.GetBytes("if-none-match"), HeaderNames.IfNoneMatch },
        { Encoding.Latin1.GetBytes("if-range"), HeaderNames.IfRange },
        { Encoding.Latin1.GetBytes("if-unmodified-since"), HeaderNames.IfUnmodifiedSince },
        { Encoding.Latin1.GetBytes("keep-alive"), HeaderNames.KeepAlive },
        { Encoding.Latin1.GetBytes("last-modified"), HeaderNames.LastModified },
        { Encoding.Latin1.GetBytes("link"), HeaderNames.Link },
        { Encoding.Latin1.GetBytes("location"), HeaderNames.Location },
        { Encoding.Latin1.GetBytes("max-forwards"), HeaderNames.MaxForwards },
        { Encoding.Latin1.GetBytes("origin"), HeaderNames.Origin },
        { Encoding.Latin1.GetBytes("pragam"), HeaderNames.Pragma },
        { Encoding.Latin1.GetBytes("proxy-authenticate"), HeaderNames.ProxyAuthenticate },
        { Encoding.Latin1.GetBytes("proxy-authoritation"), HeaderNames.ProxyAuthorization },
        { Encoding.Latin1.GetBytes("proxy-connection"), HeaderNames.ProxyConnection },
        { Encoding.Latin1.GetBytes("range"), HeaderNames.Range },
        { Encoding.Latin1.GetBytes("referer"), HeaderNames.Referer },
        { Encoding.Latin1.GetBytes("request-id"), HeaderNames.RequestId },
        { Encoding.Latin1.GetBytes("retry-after"), HeaderNames.RetryAfter },
        { Encoding.Latin1.GetBytes("sec-websocket-accept"), HeaderNames.SecWebSocketAccept },
        { Encoding.Latin1.GetBytes("sec-websocket-extensions"), HeaderNames.SecWebSocketExtensions },
        { Encoding.Latin1.GetBytes("sec-websocket-key"), HeaderNames.SecWebSocketKey },
        { Encoding.Latin1.GetBytes("sec-websocket-protocol"), HeaderNames.SecWebSocketProtocol },
        { Encoding.Latin1.GetBytes("sec-websocket-version"), HeaderNames.SecWebSocketVersion },
        { Encoding.Latin1.GetBytes("server"), HeaderNames.Server },
        { Encoding.Latin1.GetBytes("set-cookie"), HeaderNames.SetCookie },
        { Encoding.Latin1.GetBytes("strict-transport-security"), HeaderNames.StrictTransportSecurity },
        { Encoding.Latin1.GetBytes("trace-parent"), HeaderNames.TraceParent },
        { Encoding.Latin1.GetBytes("trace-state"), HeaderNames.TraceState },
        { Encoding.Latin1.GetBytes("trailer"), HeaderNames.Trailer },
        { Encoding.Latin1.GetBytes("translate"), HeaderNames.Translate },
        { Encoding.Latin1.GetBytes("upgrade"), HeaderNames.Upgrade },
        { Encoding.Latin1.GetBytes("upgrade-insecure-requests"), HeaderNames.UpgradeInsecureRequests },
        { Encoding.Latin1.GetBytes("user-agent"), HeaderNames.UserAgent },
        { Encoding.Latin1.GetBytes("vary"), HeaderNames.Vary },
        { Encoding.Latin1.GetBytes("warning"), HeaderNames.Warning },
        { Encoding.Latin1.GetBytes("www-authenticate"), HeaderNames.WWWAuthenticate  },
        { Encoding.Latin1.GetBytes("x-xss-protection"), HeaderNames.XXSSProtection },
    };

    public static bool TryGetAspNetCoreHeader(in ReadOnlySequence<byte> name, [NotNullWhen(true)] out string? result)
    {
        const int LongestSupportedHeaderName = 35;
        if (name.Length > LongestSupportedHeaderName)
        {
            result = null;
            return false;
        }
        var success = AspNetHeaderNamingLookupTable.TryGetAlternateLookup<ReadOnlySpan<byte>>(out var lookup);
        Debug.Assert(success);

        scoped ReadOnlySpan<byte> queryTerm;
        if (name.IsSingleSegment)
            queryTerm = name.FirstSpan;
        else
        {
            Span<byte> temp = stackalloc byte[LongestSupportedHeaderName];
            name.CopyTo(temp);
            queryTerm = temp;
        }
        return lookup.TryGetValue(queryTerm, out result);
    }
}
