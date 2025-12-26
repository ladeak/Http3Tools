using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Authentication;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed partial class Http2Connection
{
    private enum ConnectionState : byte
    {
        Open = 0,
        HalfOpenLocal = 1,
        HalfOpenRemote = 2,
        Closed = 4,
    }

    private const int MaxFrameHeaderLength = 9;
    private const uint MaxStreamId = uint.MaxValue >> 1;
    internal const int DefaultServerMaxConcurrentStream = 128;
    internal const uint MaxWindowUpdateSize = 2_147_483_647; // int.MaxValue

    private static ReadOnlySpan<byte> PrefaceBytes => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    private readonly CHttp2ConnectionContext _context;
    private readonly PipeReader _inputRequestReader;
    private uint _streamIdIndex;
    private readonly HPackDecoder _hpackDecoder;
    private readonly LimitedObjectPool<Http2Stream> _streamPool;

    private byte[] _buffer;
    private FrameWriter? _writer;
    private Http2Frame _readFrame;
    private IResponseWriter? _responseWriter;
    private Http2SettingsPayload _h2Settings;
    private ConcurrentDictionary<uint, Http2Stream> _streams;
    private Http2Stream _currentStream;
    private CancellationTokenSource _aborted;
    private volatile bool _gracefulShutdownRequested;
    private FlowControlSize _serverWindow;
    private FlowControlSize _clientWindow;

    public Http2Connection(CHttp2ConnectionContext connectionContext)
    {
        _context = connectionContext;
        connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(OnHeartbeat, this);
        var concurrentStreamsCount = connectionContext.ServerOptions.ConcurrentStreams;
        _streams = new ConcurrentDictionary<uint, Http2Stream>(concurrentStreamsCount, concurrentStreamsCount);
        _h2Settings = new();
        _hpackDecoder = new(maxDynamicTableSize: 0, maxHeadersLength: connectionContext.ServerOptions.Http2MaxRequestHeaderLength);
        _buffer = ArrayPool<byte>.Shared.Rent(checked((int)_h2Settings.ReceiveMaxFrameSize) + MaxFrameHeaderLength);
        _inputRequestReader = connectionContext.TransportPipe!.Input;
        _aborted = _context.ConnectionCancellation;
        _gracefulShutdownRequested = false;
        _readFrame = new();
        _serverWindow = new(_context.ServerOptions.ServerConnectionFlowControlSize + CHttpServerOptions.InitialStreamFlowControlSize);
        _clientWindow = new(_h2Settings.InitialWindowSize);
        _streamPool = new LimitedObjectPool<Http2Stream>();
        _currentStream = _streamPool.Get(CreateConnection, (Connection: this, _context.Features));
    }

    // Setter is atest hook
    internal IResponseWriter? ResponseWriter { get => _responseWriter; init => _responseWriter = value; }

    internal CHttpServerOptions ServerOptions => _context.ServerOptions;

    public async Task ProcessRequestAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        _writer = new FrameWriter(_context);
        var serverOptions = _context.ServerOptions;
        if (!serverOptions.UsePriority)
            _responseWriter = new Http2ResponseWriter(_writer, _h2Settings.SendMaxFrameSize);
        else
            _responseWriter = new ChannelsPriorityResponseWriter(_writer, _h2Settings.SendMaxFrameSize);
        CancellationTokenSource cts = new();
        var responseWriting = _responseWriter.RunAsync(cts.Token);
        Http2ErrorCode errorCode = Http2ErrorCode.NO_ERROR;
        try
        {
            ValidateTlsRequirements();
            var token = _aborted.Token;
            await ReadPreface(token);
            _writer.WriteSettings(new Http2SettingsPayload() { InitialWindowSize = serverOptions.ServerStreamFlowControlSize, MaxConcurrentStream = DefaultServerMaxConcurrentStream, DisableRFC7540Priority = serverOptions.UsePriority });
            _writer.WriteWindowUpdate(0, serverOptions.ServerConnectionFlowControlSize);
            await _writer.FlushAsync();
            while (!token.IsCancellationRequested)
            {
                var dataRead = await ReadFrameHeader(token);
                if (dataRead.IsEmpty) // On End of Stream
                    return;
                await ProcessFrame(dataRead.Slice(MaxFrameHeaderLength), application);
                _inputRequestReader.AdvanceTo(dataRead.GetPosition(MaxFrameHeaderLength + _readFrame.PayloadLength));
            }

        }
        catch (OperationCanceledException) when (_aborted.IsCancellationRequested)
        {
            // Connection was aborted, no action needed
        }
        catch (Http2ConnectionException e)
        {
            errorCode = e.Code ?? Http2ErrorCode.CONNECT_ERROR;
        }
        catch (Http2FlowControlException)
        {
            // GOAWAY frame with an error code of FLOW_CONTROL_ERROR
            errorCode = Http2ErrorCode.FLOW_CONTROL_ERROR;
        }
        catch (Http2ProtocolException)
        {
            errorCode = Http2ErrorCode.PROTOCOL_ERROR;
        }
        catch (IOException)
        {
            errorCode = Http2ErrorCode.CONNECT_ERROR;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Unexpected error in HTTP/2 connection: {e}");
        }
        finally
        {
            if (_streams.Count > 0)
            {
                foreach (var stream in _streams.Values)
                    stream.Abort();
                _streams.Clear();
            }

            if (!responseWriting.IsCompleted)
            {
                cts.Cancel();
                await responseWriting;
            }
            _writer.WriteGoAway(_streamIdIndex, errorCode);
            await _writer.FlushAsync();
            CloseConnection();
        }
    }

    private void CloseConnection()
    {
        _inputRequestReader.Complete();
        _context.Transport!.Close();
        ArrayPool<byte>.Shared.Return(_buffer);
        _context.Features.Get<IConnectionLifetimeNotificationFeature>()?.RequestClose();
    }

    private ValueTask ProcessFrame<TContext>(ReadOnlySequence<byte> dataRead,
        IHttpApplication<TContext> application) where TContext : notnull
    {
        if (_readFrame.Type == Http2FrameType.DATA)
            return ProcessDataFrame(dataRead);
        if (_readFrame.Type == Http2FrameType.WINDOW_UPDATE)
            return ProcessWindowUpdateFrame(dataRead);
        if (_readFrame.Type == Http2FrameType.HEADERS)
            return ProcessHeaderFrame(dataRead, application);
        if (_readFrame.Type == Http2FrameType.SETTINGS)
            return ProcessSettingsFrame(dataRead);
        if (_readFrame.Type == Http2FrameType.PING)
            return ProcessPingFrame(dataRead);
        if (_readFrame.Type == Http2FrameType.GOAWAY)
            return ProcessGoAwayFrame(dataRead);
        if (_readFrame.Type == Http2FrameType.RST_STREAM)
            return ProcessResetStreamFrame(dataRead);
        if (_readFrame.Type == Http2FrameType.CONTINUATION)
            return ProcessContinuationFrame(dataRead, application);
        return ValueTask.CompletedTask;
    }

    // +---------------------------------------------------------------+
    // |                                                               |
    // |                      Opaque Data(64)                         |
    // |                                                               |
    // +---------------------------------------------------------------+
    private ValueTask ProcessPingFrame(in ReadOnlySequence<byte> dataRead)
    {
        // Read Opague Data
        if (_readFrame.PayloadLength != 8)
            throw new Http2ConnectionException(Http2ErrorCode.FRAME_SIZE_ERROR);
        if (_readFrame.StreamId != 0)
            throw new Http2ProtocolException();

        if (_readFrame.Flags == 0)
        {
            var payload = BinaryPrimitives.ReadUInt64LittleEndian(ToSpan(dataRead.Slice(0, 8)));
            _responseWriter!.ScheduleWritePingAck(payload);
        }
        return ValueTask.CompletedTask;
    }

    // +-+-------------------------------------------------------------+
    // |R|                  Last-Stream-ID(31)                        |
    // +-+-------------------------------------------------------------+
    // |                      Error Code(32)                          |
    // +---------------------------------------------------------------+
    // |                  Additional Debug Data(*)                    |
    // +---------------------------------------------------------------+
    private ValueTask ProcessGoAwayFrame(in ReadOnlySequence<byte> dataRead)
    {
        if (_readFrame.StreamId != 0)
            throw new Http2ConnectionException("GOAWAY frame must be sent on connection stream (stream id 0)");
        var buffer = ToSpan(dataRead.Slice(0, _readFrame.PayloadLength));
        var _ = IntegerSerializer.ReadUInt32BigEndian(buffer[0..4]);
        var errorCode = (Http2ErrorCode)IntegerSerializer.ReadUInt32BigEndian(buffer.Slice(4, 4));
        if (errorCode == Http2ErrorCode.NO_ERROR)
        {
            _gracefulShutdownRequested = true;
            TryGracefulShutdown();
        }

        string additionalDebugData = string.Empty;
        if (_readFrame.PayloadLength > 8)
        {
            // Read additional debug data if present
            additionalDebugData = Encoding.Latin1.GetString(buffer[8..]);
        }
        if (_readFrame.GoAwayErrorCode != Http2ErrorCode.NO_ERROR)
            throw new Http2ConnectionException($"GOAWAY frame received {additionalDebugData}");
        return ValueTask.CompletedTask;
    }

    // +---------------+
    // |Pad Length? (8)|
    // +---------------+-----------------------------------------------+
    // |                            Data(*)                         ...
    // +---------------------------------------------------------------+
    // |                           Padding(*)                       ...
    // +---------------------------------------------------------------+
    private async ValueTask ProcessDataFrame(ReadOnlySequence<byte> dataRead)
    {
        var streamId = _readFrame.StreamId;
        if (!_streams.TryGetValue(streamId, out var httpStream))
            throw new Http2ProtocolException();

        int paddingLength = 0;
        if (_readFrame.HasPadding)
        {
            paddingLength = dataRead.FirstSpan[0];
            if (paddingLength > _readFrame.PayloadLength)
                throw new Http2ConnectionException("Invalid data pad length");
            dataRead = dataRead.Slice(1);
        }

        var buffer = httpStream.RequestPipe.GetSpan((int)_h2Settings.ReceiveMaxFrameSize); // framesize max
        dataRead.Slice(0, _readFrame.PayloadLength).CopyTo(buffer);

        httpStream.RequestPipe.Advance((int)_readFrame.PayloadLength - paddingLength); // Padding is read but not advanced.
        await httpStream.RequestPipe.FlushAsync();
        if (_readFrame.EndStream)
        {
            httpStream.CompleteRequestStream();
        }
    }

    private ValueTask ProcessHeaderFrame<TContext>(
        in ReadOnlySequence<byte> dataRead,
        IHttpApplication<TContext> application) where TContext : notnull
    {
        // +---------------+
        // | Pad Length ? (8) |
        // +-+-------------+-----------------------------------------------+
        // | E | Stream Dependency ? (31) |
        // +-+-------------+-----------------------------------------------+
        // | Weight ? (8) |
        // +-+-------------+-----------------------------------------------+
        // | Header Block Fragment(*)...
        // +---------------------------------------------------------------+
        // | Padding(*)...
        // +---------------------------------------------------------------+
        int payloadStart = 0;
        int paddingLength = 0;
        if (_readFrame.HasPadding)
        {
            payloadStart = 1;
            paddingLength = dataRead.FirstSpan[0];
        }
        if (_readFrame.HasPriorty)
            payloadStart += 5;

        if ((_currentStream.StreamId == _readFrame.StreamId && _currentStream.RequestEndHeaders)
            || _streams.ContainsKey(_readFrame.StreamId))
            throw new Http2ProtocolException();

        _currentStream = _streamPool.Get(CreateConnection, (Connection: this, _context.Features));
        _currentStream.Initialize(_readFrame.StreamId, _h2Settings.InitialWindowSize, _context.ServerOptions.ServerStreamFlowControlSize);
        var addResult = _streams.TryAdd(_readFrame.StreamId, _currentStream);
        _streamIdIndex = uint.Max(_readFrame.StreamId, _streamIdIndex);
        Debug.Assert(addResult);
        bool endHeaders = _readFrame.EndHeaders;
        ResetHeadersParsingState();

        _hpackDecoder.Decode(dataRead.Slice(payloadStart, _readFrame.PayloadLength - payloadStart - paddingLength), endHeaders, this);
        if (endHeaders)
        {
            _currentStream.RequestEndHeadersReceived();
            StartStream(_currentStream, application);
        }
        return ValueTask.CompletedTask;
    }

    // +---------------------------------------------------------------+
    // |                   Header Block Fragment(*)                 ...
    // +---------------------------------------------------------------+
    private ValueTask ProcessContinuationFrame<TContext>(
        ReadOnlySequence<byte> dataRead,
        IHttpApplication<TContext> application) where TContext : notnull
    {
        if (_currentStream.StreamId != _readFrame.StreamId || _currentStream.RequestEndHeaders)
            throw new Http2ProtocolException();
        if (!_streams.ContainsKey(_readFrame.StreamId))
            return ValueTask.CompletedTask;

        bool endHeaders = _readFrame.EndHeaders;
        _hpackDecoder.Decode(dataRead.Slice(0, _readFrame.PayloadLength), endHeaders, this);
        if (endHeaders)
        {
            _currentStream.RequestEndHeadersReceived();
            StartStream(_currentStream, application);
        }
        return ValueTask.CompletedTask;
    }

    // +---------------------------------------------------------------+
    // |                        Error Code(32)                        |
    // +---------------------------------------------------------------+
    private ValueTask ProcessResetStreamFrame(in ReadOnlySequence<byte> dataRead)
    {
        var streamId = _readFrame.StreamId;
        if (!_streams.TryGetValue(streamId, out var httpStream))
            throw new Http2ProtocolException();
        if (_readFrame.PayloadLength != 4)
            throw new Http2ConnectionException(Http2ErrorCode.FRAME_SIZE_ERROR);
        var buffer = ToSpan(dataRead.Slice(0, 4));
        Http2ErrorCode errorCode = (Http2ErrorCode)IntegerSerializer.ReadUInt32BigEndian(buffer);

        // Abort stream
        httpStream.Abort();
        OnStreamCompleted(httpStream);
        if (_currentStream.StreamId == streamId)
            _currentStream = _streamPool.Get(CreateConnection, (Connection: this, _context.Features));
        return ValueTask.CompletedTask;
    }

    private static void StartStream<TContext>(Http2Stream stream, IHttpApplication<TContext> application) where TContext : notnull
    {
        ThreadPool.UnsafeQueueUserWorkItem(
            state => state.Stream.Execute(state.App),
            (App: application, Stream: stream),
            preferLocal: false);
    }

    private ValueTask ProcessWindowUpdateFrame(in ReadOnlySequence<byte> dataRead)
    {
        if (_readFrame.PayloadLength != 4)
            throw new Http2ProtocolException();
        var updateSize = IntegerSerializer.ReadUInt32BigEndian(ToSpan(dataRead.Slice(0, 4)));
        if (updateSize > MaxWindowUpdateSize)
            throw new Http2ProtocolException();

        var streamId = _readFrame.StreamId;
        if (streamId == 0)
            UpdateConnectionWindowSize(updateSize);
        else if (_streams.TryGetValue(_readFrame.StreamId, out var stream))
            stream.UpdateWindowSize(updateSize);
        return ValueTask.CompletedTask;
    }

    internal void UpdateConnectionWindowSize(uint updateSize) // Internal as hook
    {
        _clientWindow.ReleaseSize(updateSize);

        // Let all streams know, so that they can schedule data writes (if
        // they were blocked by connection windows earlier).
        foreach (var stream in _streams)
            stream.Value.OnConnectionWindowUpdateSize();
    }

    private async ValueTask<ReadOnlySequence<byte>> ReadFrameHeader(CancellationToken token)
    {
        while (true)
        {
            var readResult = await _inputRequestReader.ReadAsync(token);
            var buffer = readResult.Buffer;
            if (buffer.IsEmpty && readResult.IsCompleted)
                return ReadOnlySequence<byte>.Empty;
            if (buffer.Length < MaxFrameHeaderLength)
            {
                _inputRequestReader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }
            var frameHeader = ToSpan(buffer.Slice(0, MaxFrameHeaderLength));

            _readFrame.PayloadLength = IntegerSerializer.ReadUInt24BigEndian(frameHeader[0..3]);
            if (_readFrame.PayloadLength + MaxFrameHeaderLength > buffer.Length)
            {
                _inputRequestReader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            _readFrame.Type = (Http2FrameType)frameHeader[3];
            _readFrame.Flags = frameHeader[4];
            var streamId = IntegerSerializer.ReadUInt32BigEndian(frameHeader[5..]);
            if (streamId >= MaxStreamId)
                throw new Http2ConnectionException("Reserved bit must be unset");
            _readFrame.StreamId = streamId;

            return buffer;
        }
    }

    public ReadOnlySpan<byte> ToSpan(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.IsSingleSegment)
            return buffer.FirstSpan;
        var result = _buffer.AsSpan();
        buffer.CopyTo(result);
        return result;
    }

    private ValueTask ProcessSettingsFrame(in ReadOnlySequence<byte> dataRead)
    {
        if (_readFrame.StreamId != 0)
            throw new Http2ProtocolException();
        if (_readFrame.Flags == 1) // SETTING ACK
            return ValueTask.CompletedTask;

        var span = ToSpan(dataRead.Slice(0, _readFrame.PayloadLength));
        while (span.Length > 0)
        {
            var settingId = IntegerSerializer.ReadUInt16BigEndian(span);
            span = span[2..];
            var settingValue = IntegerSerializer.ReadUInt32BigEndian(span);
            span = span[4..];
            switch (settingId)
            {
                case 1:
                    _h2Settings.HeaderTableSize = Math.Min(_h2Settings.HeaderTableSize, settingValue);
                    break;
                case 2:
                    _h2Settings.EnablePush = Math.Min(_h2Settings.EnablePush, settingValue);
                    break;
                case 3:
                    _h2Settings.MaxConcurrentStream = Math.Min(_h2Settings.MaxConcurrentStream, settingValue);
                    break;
                case 4:
                    _h2Settings.InitialWindowSize = settingValue;
                    break;
                case 5:
                    _h2Settings.SendMaxFrameSize = settingValue;
                    _responseWriter!.UpdateFrameSize(settingValue);
                    break;

            }
        }
        _writer!.WriteSettingAck();
        return ValueTask.CompletedTask;
        //SETTINGS_HEADER_TABLE_SIZE
        //SETTINGS_ENABLE_PUSH
        //SETTINGS_MAX_CONCURRENT_STREAMS
        //SETTINGS_INITIAL_WINDOW_SIZE
        //SETTINGS_MAX_FRAME_SIZE
        //SETTINGS_MAX_HEADER_LIST_SIZE
    }

    private async Task ReadPreface(CancellationToken token)
    {
        while (true)
        {
            ReadResult dataRead;
            try
            {
                dataRead = await _inputRequestReader.ReadAsync(token);
            }
            catch (OperationCanceledException)
            {
                throw new Http2ConnectionException("Connection aborted while reading preface");
            }
            if (dataRead.IsCanceled || dataRead.IsCompleted)
                throw new Http2ConnectionException("Connection aborted while reading preface");

            var buffer = dataRead.Buffer;
            if (dataRead.Buffer.Length < PrefaceBytes.Length)
            {
                _inputRequestReader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            if (!ToSpan(buffer.Slice(0, PrefaceBytes.Length)).SequenceEqual(PrefaceBytes))
                throw new Http2ConnectionException("Request is not HTTP2");
            _inputRequestReader.AdvanceTo(buffer.GetPosition(PrefaceBytes.Length));
            return;
        }
    }

    private void OnHeartbeat(object state)
    {
        // timeout for settings ack
        TryGracefulShutdown();
    }

    private void ValidateTlsRequirements()
    {
        var tlsFeature = _context.Features.Get<ITlsHandshakeFeature>();
        if (tlsFeature == null || tlsFeature.Protocol < SslProtocols.Tls12)
            throw new Http2ConnectionException("TLS required");
    }

    internal void OnStreamCompleted(Http2Stream stream)
    {
        _streams.TryRemove(stream.StreamId, out _);
        if (!stream.IsAborted)
        {
            stream.Reset();
            _streamPool.Return(stream);
        }
        if (_gracefulShutdownRequested)
            TryGracefulShutdown();
    }

    private bool TryGracefulShutdown()
    {
        if (_streams.Count != 0)
            return false;

        _aborted.Cancel();
        _responseWriter?.Complete();
        return true;
    }

    internal bool ReserveClientFlowControlSize(uint requestedSize, out uint reservedSize)
    {
        return _clientWindow.TryUseAny(requestedSize, out reservedSize);
    }

    private static Http2Stream CreateConnection((Http2Connection Connection, FeatureCollection Features) state) => new Http2Stream(state.Connection, state.Features);
}