using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using System.Text;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class QPackDecoder
{
    private QuicStream? _qpackEncodingStream;
    private QuicStream? _qpackDecodingStream;
    private Task? _reading;

    public void SetEncodingStream(QuicStream stream, PipeReader reader)
    {
        _qpackEncodingStream = stream;
    }

    public void SetDecodingStream(QuicStream stream, CancellationToken token)
    {
        _qpackDecodingStream = stream;
        _reading = RunAsync(token);
    }

    public async Task RunAsync(CancellationToken token)
    {
        Debug.Assert(_qpackDecodingStream != null);
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!token.IsCancellationRequested)
                await _qpackDecodingStream.ReadExactlyAsync(buffer.AsMemory(), token);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task Reset()
    {
        _qpackEncodingStream?.Abort(QuicAbortDirection.Read, ErrorCodes.H3StreamCreationError);
        _qpackEncodingStream = null;
        _qpackDecodingStream?.Abort(QuicAbortDirection.Write, ErrorCodes.H3StreamCreationError);
        _qpackDecodingStream = null;
        await (_reading ?? Task.CompletedTask);
    }

    public string? DecodeHeader(ReadOnlyMemory<byte> line)
    {
        // TODO
        throw new NotImplementedException();
    }

    private static readonly HeaderField[] _staticDecoderTable =
    [
        CreateHeaderField(0, ":authority", ""),
        CreateHeaderField(1, ":path", "/"),
        CreateHeaderField(2, "age", "0"),
        CreateHeaderField(3, "content-disposition", ""),
        CreateHeaderField(4, "content-length", "0"),
        CreateHeaderField(5, "cookie", ""),
        CreateHeaderField(6, "date", ""),
        CreateHeaderField(7, "etag", ""),
        CreateHeaderField(8, "if-modified-since", ""),
        CreateHeaderField(9, "if-none-match", ""),
        CreateHeaderField(10, "last-modified", ""),
        CreateHeaderField(11, "link", ""),
        CreateHeaderField(12, "location", ""),
        CreateHeaderField(13, "referer", ""),
        CreateHeaderField(14, "set-cookie", ""),
        CreateHeaderField(15, ":method", "CONNECT"),
        CreateHeaderField(16, ":method", "DELETE"),
        CreateHeaderField(17, ":method", "GET"),
        CreateHeaderField(18, ":method", "HEAD"),
        CreateHeaderField(19, ":method", "OPTIONS"),
        CreateHeaderField(20, ":method", "POST"),
        CreateHeaderField(21, ":method", "PUT"),
        CreateHeaderField(22, ":scheme", "http"),
        CreateHeaderField(23, ":scheme", "https"),
        CreateHeaderField(24, ":status", "103"),
        CreateHeaderField(25, ":status", "200"),
        CreateHeaderField(26, ":status", "304"),
        CreateHeaderField(27, ":status", "404"),
        CreateHeaderField(28, ":status", "503"),
        CreateHeaderField(29, "accept", "*/*"),
        CreateHeaderField(30, "accept", "application/dns-message"),
        CreateHeaderField(31, "accept-encoding", "gzip, deflate, br"),
        CreateHeaderField(32, "accept-ranges", "bytes"),
        CreateHeaderField(33, "access-control-allow-headers", "cache-control"),
        CreateHeaderField(34, "access-control-allow-headers", "content-type"),
        CreateHeaderField(35, "access-control-allow-origin", "*"),
        CreateHeaderField(36, "cache-control", "max-age=0"),
        CreateHeaderField(37, "cache-control", "max-age=2592000"),
        CreateHeaderField(38, "cache-control", "max-age=604800"),
        CreateHeaderField(39, "cache-control", "no-cache"),
        CreateHeaderField(40, "cache-control", "no-store"),
        CreateHeaderField(41, "cache-control", "public, max-age=31536000"),
        CreateHeaderField(42, "content-encoding", "br"),
        CreateHeaderField(43, "content-encoding", "gzip"),
        CreateHeaderField(44, "content-type", "application/dns-message"),
        CreateHeaderField(45, "content-type", "application/javascript"),
        CreateHeaderField(46, "content-type", "application/json"),
        CreateHeaderField(47, "content-type", "application/x-www-form-urlencoded"),
        CreateHeaderField(48, "content-type", "image/gif"),
        CreateHeaderField(49, "content-type", "image/jpeg"),
        CreateHeaderField(50, "content-type", "image/png"),
        CreateHeaderField(51, "content-type", "text/css"),
        CreateHeaderField(52, "content-type", "text/html; charset=utf-8"),
        CreateHeaderField(53, "content-type", "text/plain"),
        CreateHeaderField(54, "content-type", "text/plain;charset=utf-8"),
        CreateHeaderField(55, "range", "bytes=0-"),
        CreateHeaderField(56, "strict-transport-security", "max-age=31536000"),
        CreateHeaderField(57, "strict-transport-security", "max-age=31536000; includesubdomains"),
        CreateHeaderField(58, "strict-transport-security", "max-age=31536000; includesubdomains; preload"),
        CreateHeaderField(59, "vary", "accept-encoding"),
        CreateHeaderField(60, "vary", "origin"),
        CreateHeaderField(61, "x-content-type-options", "nosniff"),
        CreateHeaderField(62, "x-xss-protection", "1; mode=block"),
        CreateHeaderField(63, ":status", "100"),
        CreateHeaderField(64, ":status", "204"),
        CreateHeaderField(65, ":status", "206"),
        CreateHeaderField(66, ":status", "302"),
        CreateHeaderField(67, ":status", "400"),
        CreateHeaderField(68, ":status", "403"),
        CreateHeaderField(69, ":status", "421"),
        CreateHeaderField(70, ":status", "425"),
        CreateHeaderField(71, ":status", "500"),
        CreateHeaderField(72, "accept-language", ""),
        CreateHeaderField(73, "access-control-allow-credentials", "FALSE"),
        CreateHeaderField(74, "access-control-allow-credentials", "TRUE"),
        CreateHeaderField(75, "access-control-allow-headers", "*"),
        CreateHeaderField(76, "access-control-allow-methods", "get"),
        CreateHeaderField(77, "access-control-allow-methods", "get, post, options"),
        CreateHeaderField(78, "access-control-allow-methods", "options"),
        CreateHeaderField(79, "access-control-expose-headers", "content-length"),
        CreateHeaderField(80, "access-control-request-headers", "content-type"),
        CreateHeaderField(81, "access-control-request-method", "get"),
        CreateHeaderField(82, "access-control-request-method", "post"),
        CreateHeaderField(83, "alt-svc", ""),
        CreateHeaderField(84, "authorization", ""),
        CreateHeaderField(85, "content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"),
        CreateHeaderField(86, "early-data", "1"),
        CreateHeaderField(87, "expect-ct", ""),
        CreateHeaderField(88, "forwarded", ""),
        CreateHeaderField(89, "if-range", ""),
        CreateHeaderField(90, "origin", ""),
        CreateHeaderField(91, "purpose", "prefetch"),
        CreateHeaderField(92, "server", ""),
        CreateHeaderField(93, "timing-allow-origin", ""),
        CreateHeaderField(94, "upgrade-insecure-requests", "1"),
        CreateHeaderField(95, "user-agent", ""),
        CreateHeaderField(96, "x-forwarded-for", ""),
        CreateHeaderField(97, "x-frame-options", "deny"),
        CreateHeaderField(98, "x-frame-options", "sameorigin")
    ];

    private static readonly FrozenDictionary<string, int> _staticEncoderTable = new Dictionary<string, int>(
        _staticDecoderTable
        .Select(x => new KeyValuePair<string, int>(Encoding.ASCII.GetString(x.Name), x.StaticTableIndex)))
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static HeaderField CreateHeaderField(int index, string name, string value)
    {
        return new HeaderField(index, Encoding.ASCII.GetBytes(name), Encoding.ASCII.GetBytes(value));
    }
}