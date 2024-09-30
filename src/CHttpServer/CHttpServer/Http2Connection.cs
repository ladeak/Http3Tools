using System.Security.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

public sealed class Http2Connection
{
    private static ReadOnlySpan<byte> PrefaceBytes => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;
    private readonly CHttpConnectionContext _context;
    private readonly FrameWriter _writer;
    private readonly int _streamIdIndex;

    public Http2Connection(CHttpConnectionContext connectionContext)
    {
        _context = connectionContext;
        connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(OnHeartbeat, this);
        _writer = new FrameWriter(_context);
    }

    public async Task ProcessRequestAsync<TContext>(IHttpApplication<TContext> application)
    {
        Http2ErrorCode errorCode = Http2ErrorCode.NO_ERROR;
        try
        {
            ValidateTlsRequirements();

        }
        catch (Http2ConnectionException)
        {
            errorCode = Http2ErrorCode.CONNECT_ERROR;
        }
        catch (Http2ProtocolException)
        {
            errorCode = Http2ErrorCode.PROTOCOL_ERROR;
        }
        finally
        {
            if (errorCode != Http2ErrorCode.NO_ERROR)
            {
                // todo close stream
                // todo write goaway
                _writer.WriteGoAway(_streamIdIndex, errorCode);
            }
        }
    }

    private static void OnHeartbeat(object state)
    {
        // timeout for settings ack
    }

    private void ValidateTlsRequirements()
    {
        var tlsFeature = _context.Features.Get<ITlsHandshakeFeature>();
        if (tlsFeature == null || tlsFeature.Protocol < SslProtocols.Tls12)
        {
            throw new Http2ConnectionException("TLS required");
        }
    }
}

public sealed class Http2ConnectionException : Exception
{
    public Http2ConnectionException(string message) : base(message)
    {
    }
}

public sealed class Http2ProtocolException : Exception
{
}
