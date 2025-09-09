using System.Diagnostics;
using System.Net.Quic;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class Http3Connection
{
    private readonly CHttp3ConnectionContext _context;
    private readonly CancellationTokenSource _cts;
    private long _maxProcessedStreamId = 0;

    public Http3Connection(CHttp3ConnectionContext connectionContext)
    {
        _context = connectionContext;
        _cts = new();
    }

    internal async Task ProcessConnectionAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        Debug.Assert(_context.Transport != null);

        try
        {
            var quickStream = await _context.Transport.AcceptInboundStreamAsync(_cts.Token);
            HandleStream(quickStream, application);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Http3ConnectionException ex)
        {
            // Write GOAWAY frame
            Debug.WriteLine(ex.ErrorCode);
            await _context.Transport.CloseAsync(ex.ErrorCode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
        finally
        {
            // TODO error code
            await _context.Transport.CloseAsync(0);
        }
    }

    private void HandleStream<TContext>(QuicStream quickStream, IHttpApplication<TContext> application) where TContext : notnull
    {
        if (quickStream.Type == QuicStreamType.Bidirectional)
            throw new Http3ConnectionException(ErrorCodes.H3StreamCreationError);
        _maxProcessedStreamId = Math.Max(quickStream.Id, _maxProcessedStreamId);

    }
}

internal class Http3ConnectionException : Exception
{
    public Http3ConnectionException(int errorCode) : base()
    {
        ErrorCode = errorCode;
    }
    public int ErrorCode { get; }
}

internal class ErrorCodes
{
    internal const int H3StreamCreationError = 0x0103;
}
