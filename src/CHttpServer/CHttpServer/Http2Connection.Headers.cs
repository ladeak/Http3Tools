using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer;

internal sealed partial class Http2Connection : System.Net.Http.HPack.IHttpStreamHeadersHandler
{
    private RequestHeaderParsingState _requestHeaderParsingState = RequestHeaderParsingState.Ready;
    private PseudoHeaderFields _parsedPseudoHeaderFields;

    public void ResetHeadersParsingState()
    {
        _requestHeaderParsingState = RequestHeaderParsingState.Ready;
        _parsedPseudoHeaderFields = PseudoHeaderFields.None;
    }

    public void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
    }

    public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
    }

    public void OnHeadersComplete(bool endStream)
    {
        _currentStream.RequestEndHeadersReceived();
        _requestHeaderParsingState = RequestHeaderParsingState.Ready;
    }

    public void OnStaticIndexedHeader(int index)
    {
        var header = H2StaticTable.Get(index - 1);
        var pseudoHeader = GetPseudoHeaderField(header.StaticTableIndex);
        UpdateHeaderParsingState(pseudoHeader);
        _currentStream.SetStaticHeader(header, pseudoHeader);
    }

    public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
    {
        var header = H2StaticTable.Get(index - 1);
        var pseudoHeader = GetPseudoHeaderField(header.StaticTableIndex);
        UpdateHeaderParsingState(pseudoHeader);
        _currentStream.SetStaticHeader(header, pseudoHeader, value);
    }

    private void UpdateHeaderParsingState(PseudoHeaderFields headerField)
    {
        // http://httpwg.org/specs/rfc7540.html#rfc.section.8.1.2.1
        if (headerField != PseudoHeaderFields.None)
        {
            if (_requestHeaderParsingState == RequestHeaderParsingState.Headers)
            {
                // All pseudo-header fields MUST appear in the header block before regular header fields.
                // Any request or response that contains a pseudo-header field that appears in a header
                // block after a regular header field MUST be treated as malformed (Section 8.1.2.6).
                throw new Http2ConnectionException("Invalid Request Headers");
            }

            if (_requestHeaderParsingState == RequestHeaderParsingState.Trailers)
            {
                // Pseudo-header fields MUST NOT appear in trailers.
                throw new Http2ConnectionException("Invalid Request Headers");
            }

            _requestHeaderParsingState = RequestHeaderParsingState.PseudoHeaderFields;

            if (headerField == PseudoHeaderFields.Unknown)
            {
                // Endpoints MUST treat a request or response that contains undefined or invalid pseudo-header
                // fields as malformed (Section 8.1.2.6).
                throw new Http2ConnectionException("Invalid Request Headers");
            }

            if (headerField == PseudoHeaderFields.Status)
            {
                // Pseudo-header fields defined for requests MUST NOT appear in responses; pseudo-header fields
                // defined for responses MUST NOT appear in requests.
                throw new Http2ConnectionException("Invalid Request Headers");
            }

            if ((_parsedPseudoHeaderFields & headerField) == headerField)
            {
                // http://httpwg.org/specs/rfc7540.html#rfc.section.8.1.2.3
                // All HTTP/2 requests MUST include exactly one valid value for the :method, :scheme, and :path pseudo-header fields
                throw new Http2ConnectionException("Invalid Request Headers");
            }

            _parsedPseudoHeaderFields |= headerField;
        }
        else if (_requestHeaderParsingState != RequestHeaderParsingState.Trailers)
        {
            _requestHeaderParsingState = RequestHeaderParsingState.Headers;
        }
    }


    private enum RequestHeaderParsingState
    {
        Ready,
        PseudoHeaderFields,
        Headers,
        Trailers
    }

    internal enum PseudoHeaderFields
    {
        None = 0x0,
        Authority = 0x1,
        Method = 0x2,
        Path = 0x4,
        Scheme = 0x8,
        Status = 0x10,
        Protocol = 0x20,
        Unknown = 0x40000000
    }

    private static PseudoHeaderFields GetPseudoHeaderField(int staticTableIndex)
    {
        return staticTableIndex switch
        {
            1 => PseudoHeaderFields.Authority,
            2 => PseudoHeaderFields.Method,
            3 => PseudoHeaderFields.Method,
            4 => PseudoHeaderFields.Path,
            5 => PseudoHeaderFields.Path,
            6 => PseudoHeaderFields.Scheme,
            7 => PseudoHeaderFields.Scheme,
            8 => PseudoHeaderFields.Status,
            9 => PseudoHeaderFields.Status,
            10 => PseudoHeaderFields.Status,
            11 => PseudoHeaderFields.Status,
            12 => PseudoHeaderFields.Status,
            13 => PseudoHeaderFields.Status,
            14 => PseudoHeaderFields.Status,
            _ => PseudoHeaderFields.None
        };

    }
}