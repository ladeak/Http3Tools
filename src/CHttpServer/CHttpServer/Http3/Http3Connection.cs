using System.Diagnostics;
using System.Formats.Asn1;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer.Http3;

/// <summary>
/// Handles control stream.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class Http3Connection
{
    private readonly CHttp3ConnectionContext _context;
    private readonly CancellationTokenSource _cts;
    private long _maxProcessedStreamId = 0;

    private PipeReader? _controlStreamReader;
    private PipeWriter? _controlStreamWriter;
    private QuicStream? _controlStream;

    private QPackDecoder _qPackDecoder = new();

    public Http3Connection(CHttp3ConnectionContext connectionContext)
    {
        _context = connectionContext;
        _cts = new();
    }

    internal async Task ProcessConnectionAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        Debug.Assert(_context.Transport != null);
        var closingError = ErrorCodes.H3NoError;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var quickStream = await _context.Transport.AcceptInboundStreamAsync(_cts.Token);
                await HandleStreamAsync(quickStream, application);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Http3ConnectionException ex)
        {
            // Write GOAWAY frame
            Debug.WriteLine(ex.ErrorCode);
            closingError = ex.ErrorCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
        finally
        {
            await _context.Transport.CloseAsync(closingError);
        }
    }

    private async Task HandleStreamAsync<TContext>(QuicStream quicStream, IHttpApplication<TContext> application) where TContext : notnull
    {
        _maxProcessedStreamId = Math.Max(quicStream.Id, _maxProcessedStreamId);
        if (quicStream.Type == QuicStreamType.Bidirectional)
        {
            // Create Http3Stream DATA stream
            var http3Stream = new Http3Stream((int)quicStream.Id, quicStream);
        }
        else
        {
            var reader = PipeReader.Create(quicStream);
            ReadResult initialRead;
            initialRead = await reader.ReadAtLeastAsync(8);
            if (initialRead.IsCanceled
                || initialRead.IsCompleted
                || !VariableLenghtIntegerDecoder.TryRead(initialRead.Buffer.FirstSpan, out ulong streamType, out int bytesRead))
                throw new Http3ConnectionException(ErrorCodes.H3StreamCreationError);
            reader.AdvanceTo(initialRead.Buffer.GetPosition(bytesRead));

            // Control stream
            if (streamType == 0)
            {
                _controlStream = quicStream;
                _controlStreamReader = reader;
                _controlStreamWriter = PipeWriter.Create(quicStream);
            }

            // QPack Encoding stream
            else if (streamType == 2)
            {
                _qPackDecoder.SetEncodingStream(quicStream, reader);
            }

            // QPack Decoding stream
            else if (streamType == 3)
            {
                _qPackDecoder.SetDecodingStream(quicStream, _cts.Token);
            }
            else
            {
                quicStream.Abort(QuicAbortDirection.Both, ErrorCodes.H3StreamCreationError);
            }
        }
    }
}

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class Http3Stream
{
    private int _id;
    private QuicStream _quicStream;
    private PipeReader _dataReader;
    private PipeWriter _dataWriter;

    public Http3Stream(
        int id,
        QuicStream quicStream)
    {
        _id = id;
        _quicStream = quicStream;
        _dataReader = PipeReader.Create(quicStream);
        _dataWriter = PipeWriter.Create(quicStream);
    }

    public async Task ProcessStreamAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var readResult = await _dataReader.ReadAsync(token);
                if (readResult.IsCanceled || readResult.IsCompleted)
                    break;
                _dataReader.AdvanceTo(readResult.Buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        finally
        {
            _quicStream.Abort(QuicAbortDirection.Both, ErrorCodes.H3NoError);
            await _dataReader.CompleteAsync();
            await _dataWriter.CompleteAsync();
            _quicStream.Dispose();
        }
    }
}
