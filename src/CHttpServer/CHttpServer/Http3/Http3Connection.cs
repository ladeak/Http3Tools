using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Connections.Features;
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

    private PipeReader? _clientControlStreamReader;
    private QuicStream? _clientControlStream;
    private Task? _controlStreamProcessing;
    private bool _settingsFrameReceived;

    private QuicStream? _serverControlStream;
    private PipeWriter? _serverControlStreamWriter;

    private QPackDecoder _qPackDecoder = new();
    private int _closingErrorCode;

    public Http3Connection(CHttp3ConnectionContext connectionContext)
    {
        _context = connectionContext;
        _context.Features.Add<IHttp3ConnectionSettings>(this);
        _cts = new();
    }

    internal async Task ProcessConnectionAsync<TContext>(
        IHttpApplication<TContext> application,
        CancellationToken token) where TContext : notnull
    {
        token.Register(_cts.Cancel); // These two tokens have the same lifetime as the connection.

        Debug.Assert(_context.Transport != null);
        try
        {
            // Initiate control stream.
            // Push is unsupported, hence GOAWAY and SETTINGS frames are sent here.
            _serverControlStream = await _context.Transport.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, _cts.Token);
            _serverControlStreamWriter = PipeWriter.Create(_serverControlStream);
            InitiateServerControlStream();
            await _serverControlStreamWriter.FlushAsync();

            _closingErrorCode = ErrorCodes.H3NoError;

            while (!_cts.IsCancellationRequested)
            {
                var quickStream = await _context.Transport.AcceptInboundStreamAsync(_cts.Token);
                await HandleStreamAsync(quickStream, application);
            }
        }
        catch (OperationCanceledException)
        {
            await TryWriteGoAwayAsync();
        }
        catch (Http3ConnectionException ex)
        {
            // Write GOAWAY frame
            Debug.WriteLine(ex.ErrorCode);
            _closingErrorCode = ex.ErrorCode;
            await TryWriteGoAwayAsync();
        }
        catch (QuicException quicException)
        {
            if (quicException.QuicError == QuicError.ConnectionTimeout)
            {
                // Shutdown processing tasks before closing connection.
                _cts.Cancel();
            }
            Debug.WriteLine(quicException.ToString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            await TryWriteGoAwayAsync();
        }
        finally
        {
            var controlStreamProcessing = _controlStreamProcessing;
            if (controlStreamProcessing != null)
                await controlStreamProcessing;
            _serverControlStream?.Close();
            await _context.Transport.CloseAsync(_closingErrorCode);
            _context.Features.Get<IConnectionLifetimeNotificationFeature>()?.RequestClose();
        }
    }

    private async Task TryWriteGoAwayAsync()
    {
        if (_serverControlStreamWriter != null)
        {
            Http3FrameWriter.WriteGoAway(_serverControlStreamWriter, _maxProcessedStreamId);
            await _serverControlStreamWriter.FlushAsync();
        }
    }

    private async Task HandleStreamAsync<TContext>(
        QuicStream quicStream,
        IHttpApplication<TContext> application) where TContext : notnull
    {
        _maxProcessedStreamId = Math.Max(quicStream.Id, _maxProcessedStreamId);
        if (quicStream.Type == QuicStreamType.Bidirectional)
        {
            // Create Http3Stream DATA stream
            var http3Stream = new Http3Stream(_context.Features.Copy());
            http3Stream.Initialize((int)quicStream.Id, quicStream);
            ThreadPool.UnsafeQueueUserWorkItem(
                state => state.Stream.ProcessStream(state.App, state.Cancellation),
                (App: application, Stream: http3Stream, Cancellation: _cts.Token),
                preferLocal: false);
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
                _clientControlStream = quicStream;
                _clientControlStreamReader = reader;
                _controlStreamProcessing = Task.Run(ProcessControlStreamAsync);
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

    private void InitiateServerControlStream()
    {
        Debug.Assert(_serverControlStreamWriter != null);
        var settings = new Http3Settings()
        {
            ServerMaxFieldSectionSize = checked((ulong?)_context.ServerOptions.Http3MaxRequestHeaderLength)
        };
        Http3FrameWriter.WriteControlStreamHeader(_serverControlStreamWriter);
        Http3FrameWriter.WriteSettings(_serverControlStreamWriter, settings);
    }

    private void Abort(int errorCode)
    {
        _closingErrorCode = errorCode;
        _cts.Cancel();
        // close connection and send error
    }

    private async Task ProcessControlStreamAsync()
    {
        Debug.Assert(_clientControlStreamReader != null);
        while (!_cts.IsCancellationRequested)
        {
            var readResult = await _clientControlStreamReader.ReadAsync(_cts.Token);
            if (readResult.IsCompleted || readResult.IsCanceled)
                break;

            var buffer = readResult.Buffer;

            if (!VariableLenghtIntegerDecoder.TryRead(buffer.FirstSpan, out ulong frameType, out int bytesRead))
                Abort(ErrorCodes.H3FrameError);

            buffer = buffer.Slice(bytesRead);
            if (!VariableLenghtIntegerDecoder.TryRead(buffer.FirstSpan, out ulong payloadLength, out bytesRead))
            {
                // Not enough data.
                _clientControlStreamReader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            long processed = 1 + bytesRead; // 1 for the frame type. Should be always one byte by spec.
            switch (frameType)
            {
                case 0x03: // CANCEL_PUSH
                    Abort(ErrorCodes.H3IdError);
                    break;
                case 0x4: // SETTINGS
                    if (_settingsFrameReceived)
                    {
                        Abort(ErrorCodes.H3FrameUnexpected);
                        break;
                    }
                    if (payloadLength + (ulong)bytesRead > (ulong)buffer.Length)
                    {
                        // Not enough data.
                        _clientControlStreamReader.AdvanceTo(buffer.Start, buffer.End);
                        continue;
                    }
                    if (ProcessSettingsFrame(buffer.Slice(bytesRead), out bytesRead))
                    {
                        _settingsFrameReceived = true;
                        processed += bytesRead;
                    }
                    else
                        Abort(ErrorCodes.H3SettingsError);
                    break;
                case 0x7: // GO_AWAY
                    Abort(ErrorCodes.H3NoError);
                    break;
                case 0xd: // MAX_PUSH_ID
                    processed += checked((long)payloadLength);
                    break;
                default:
                    // Reserved frame types
                    if ((frameType - 32) % 31 == 0)
                    {
                        processed += checked((long)payloadLength);
                        break;
                    }
                    Abort(ErrorCodes.H3FrameUnexpected);
                    break;
            }

            _clientControlStreamReader.AdvanceTo(readResult.Buffer.GetPosition(processed), readResult.Buffer.End);
        }
    }

    /// <summary>
    /// Returns <see langword="false" /> to indicate error.
    /// </summary>
    private bool ProcessSettingsFrame(ReadOnlySequence<byte> data, out int consumed)
    {
        consumed = 0;
        while (data.Length > 0)
        {
            if (!VariableLenghtIntegerDecoder.TryRead(data, out ulong settingId, out var bytesRead))
                return false;
            consumed += bytesRead;

            if (data.Length - bytesRead <= 0)
                return false;

            if (!VariableLenghtIntegerDecoder.TryRead(data.Slice(bytesRead), out ulong value, out bytesRead))
                return false;
            consumed += bytesRead;

            // Handle SETTINGS_MAX_FIELD_SECTION_SIZE and ignore unknown settings
            if (settingId == 6)
                if (ClientMaxFieldSectionSize is null)
                    ClientMaxFieldSectionSize = value;
                else
                    return false; // Duplicate setting returns false to indicate error.

            // Only negative values are invalid (otherwise it can be end of stream)
            if (data.Length - bytesRead < 0)
                return false;
            data = data.Slice(consumed);
        }
        return true;
    }
}
