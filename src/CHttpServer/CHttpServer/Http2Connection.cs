using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed class StreamPool<T> where T : class?
{
    private const int Size = 10;

    [InlineArray(Size)]
    public struct Storage<T1> where T1 : class?
    {
        private T1? _item;
    }

    private Storage<T> _storage;
    private T? _field;

    public T Get<TState>(Func<TState, T> factory, TState state) where TState : struct
    {
        var item = _field;
        if (item is not null && item == Interlocked.CompareExchange(ref _field, null, item))
            return item;

        for (int i = 0; i < Size; i++)
        {
            item = _storage[i];
            if (item is null)
                continue;
            if (item == Interlocked.CompareExchange(ref _storage[i], null, item))
                return item;
        }
        return factory(state);
    }

    public void Return(T item)
    {
        if (_field is null)
        {
            _field = item;
            return;
        }

        for (int i = 0; i < Size; i++)
        {
            var current = _storage[i];
            if (current is not null)
                continue;
            _storage[i] = item;
            return;
        }
    }
}

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
    internal const uint MaxWindowUpdateSize = 2_147_483_647; // int.MaxValue

    private static ReadOnlySpan<byte> PrefaceBytes => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    private readonly CHttpConnectionContext _context;
    private readonly Stream _inputStream;
    private uint _streamIdIndex;
    private readonly HPackDecoder _hpackDecoder;
    private readonly Http2Stream _defaultStream;
    private readonly StreamPool<Http2Stream> _streamPool;

    private byte[] _buffer;
    private FrameWriter? _writer;
    private Http2Frame _readFrame;
    private Http2ResponseWriter? _responseWriter;
    private Http2SettingsPayload _h2Settings;
    private ConcurrentDictionary<uint, Http2Stream> _streams;
    private Http2Stream _currentStream;
    private CancellationTokenSource _aborted;
    private volatile bool _gracefulShutdownRequested;
    private FlowControlSize _serverWindow;
    private FlowControlSize _clientWindow;

    public Http2Connection(CHttpConnectionContext connectionContext)
    {
        _context = connectionContext;
        connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(OnHeartbeat, this);
        _streams = [];
        _h2Settings = new();
        _hpackDecoder = new(maxDynamicTableSize: 0, maxHeadersLength: connectionContext.ServerOptions.MaxRequestHeaderLength);
        _buffer = ArrayPool<byte>.Shared.Rent(checked((int)_h2Settings.ReceiveMaxFrameSize));
        _inputStream = connectionContext.Transport!;
        _aborted = new();
        _gracefulShutdownRequested = false;
        _readFrame = new();
        _serverWindow = new(_context.ServerOptions.ServerConnectionFlowControlSize + CHttpServerOptions.InitialStreamFlowControlSize);
        _clientWindow = new(_h2Settings.InitialWindowSize);
        _streamPool = new StreamPool<Http2Stream>();
        _defaultStream = new Http2Stream<object>(this, new FeatureCollection(), null!);
        _currentStream = _defaultStream;
    }

    // Setter is atest hook
    internal Http2ResponseWriter? ResponseWriter { get => _responseWriter; init => _responseWriter = value; }

    internal CHttpServerOptions ServerOptions => _context.ServerOptions;

    public async Task ProcessRequestAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        _writer = new FrameWriter(_context);
        _responseWriter = new Http2ResponseWriter(_writer, _h2Settings.SendMaxFrameSize);
        CancellationTokenSource cts = new();
        var responseWriting = _responseWriter.RunAsync(cts.Token);
        Http2ErrorCode errorCode = Http2ErrorCode.NO_ERROR;
        try
        {
            ValidateTlsRequirements();
            var token = _aborted.Token;
            await ReadPreface(token);
            _writer.WriteSettings(new Http2SettingsPayload() { InitialWindowSize = _context.ServerOptions.ServerStreamFlowControlSize });
            _writer.WriteWindowUpdate(0, _context.ServerOptions.ServerConnectionFlowControlSize);
            await _writer.FlushAsync();
            while (!token.IsCancellationRequested)
            {
                await ReadFrameHeader(token);
                await ProcessFrame(application);
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
            errorCode = Http2ErrorCode.STREAM_CLOSED;
        }
        catch (Exception e)
        {
        }
        finally
        {
            foreach (var stream in _streams.Values)
                stream.Abort();
            _streams.Clear();

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
        _inputStream.Close();
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    private ValueTask ProcessFrame<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        if (_readFrame.Type == Http2FrameType.DATA)
            return ProcessDataFrame();
        if (_readFrame.Type == Http2FrameType.WINDOW_UPDATE)
            return ProcessWindowUpdateFrame();
        if (_readFrame.Type == Http2FrameType.HEADERS)
            return ProcessHeaderFrame(application);
        if (_readFrame.Type == Http2FrameType.SETTINGS)
            return ProcessSettingsFrame();
        if (_readFrame.Type == Http2FrameType.PING)
            return ProcessPingFrame();
        if (_readFrame.Type == Http2FrameType.GOAWAY)
            return ProcessGoAwayFrame();
        if (_readFrame.Type == Http2FrameType.RST_STREAM)
            return ProcessResetStreamFrame();
        if (_readFrame.Type == Http2FrameType.CONTINUATION)
            return ProcessContinuationFrame();
        return ValueTask.CompletedTask;
    }

    // +---------------------------------------------------------------+
    // |                                                               |
    // |                      Opaque Data(64)                         |
    // |                                                               |
    // +---------------------------------------------------------------+
    private async ValueTask ProcessPingFrame()
    {
        // Read Opague Data
        if (_readFrame.PayloadLength != 8)
            throw new Http2ConnectionException(Http2ErrorCode.FRAME_SIZE_ERROR);
        if (_readFrame.StreamId != 0)
            throw new Http2ProtocolException();
        await _inputStream.ReadExactlyAsync(_buffer.AsMemory(0, 8));
        if (_readFrame.Flags == 0)
        {
            var payload = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.AsSpan(0, 8));
            _responseWriter!.ScheduleWritePingAck(payload);
        }
    }

    // +-+-------------------------------------------------------------+
    // |R|                  Last-Stream-ID(31)                        |
    // +-+-------------------------------------------------------------+
    // |                      Error Code(32)                          |
    // +---------------------------------------------------------------+
    // |                  Additional Debug Data(*)                    |
    // +---------------------------------------------------------------+
    private async ValueTask ProcessGoAwayFrame()
    {
        try
        {
            if (_readFrame.StreamId != 0)
                throw new Http2ConnectionException("GOAWAY frame must be sent on connection stream (stream id 0)");

            var payloadLength = (int)_readFrame.PayloadLength;
            var memory = _buffer.AsMemory(0, payloadLength);
            await _inputStream.ReadExactlyAsync(memory);
            var lastStreamId = IntegerSerializer.ReadUInt32BigEndian(memory.Span[..4]);
            var errorCode = (Http2ErrorCode)IntegerSerializer.ReadUInt32BigEndian(memory.Span.Slice(4, 4));
            if (errorCode == Http2ErrorCode.NO_ERROR)
            {
                _gracefulShutdownRequested = true;
                TryGracefulShutdown();
            }

            string additionalDebugData = string.Empty;
            if (_readFrame.PayloadLength > 8)
            {
                // Read additional debug data if present
                additionalDebugData = Encoding.Latin1.GetString(memory.Span[8..]);
            }
            if (_readFrame.GoAwayErrorCode != Http2ErrorCode.NO_ERROR)
                throw new Http2ConnectionException($"GOAWAY frame received {additionalDebugData}");
        }
        catch (Exception e)
        {
        }
    }

    // +---------------+
    // |Pad Length? (8)|
    // +---------------+-----------------------------------------------+
    // |                            Data(*)                         ...
    // +---------------------------------------------------------------+
    // |                           Padding(*)                       ...
    // +---------------------------------------------------------------+
    private async ValueTask ProcessDataFrame()
    {
        var streamId = _readFrame.StreamId;
        if (!_streams.TryGetValue(streamId, out var httpStream))
            throw new Http2ProtocolException();

        int paddingLength = 0;
        if (_readFrame.HasPadding)
        {
            await _inputStream.ReadExactlyAsync(_buffer.AsMemory(0, 1));
            paddingLength = _buffer[0];
            if (paddingLength > _readFrame.PayloadLength)
                throw new Http2ConnectionException("Invalid data pad length");
        }

        var buffer = httpStream.RequestPipe.GetMemory((int)_h2Settings.ReceiveMaxFrameSize)
            .Slice(0, (int)_readFrame.PayloadLength); // framesize max
        await _inputStream.ReadExactlyAsync(buffer);
        httpStream.RequestPipe.Advance(buffer.Length - paddingLength); // Padding is read but not advanced.
        await httpStream.RequestPipe.FlushAsync();

        if (_readFrame.EndStream)
        {
            httpStream.CompleteRequestStream();
            return;
        }
    }

    private async ValueTask ProcessHeaderFrame<TContext>(IHttpApplication<TContext> application) where TContext : notnull
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
        var memory = _buffer.AsMemory(0, (int)_readFrame.PayloadLength);
        await _inputStream.ReadExactlyAsync(memory);
        int payloadStart = 0;
        int paddingLength = 0;
        if (_readFrame.HasPadding)
        {
            payloadStart = 1;
            paddingLength = memory.Span[0];
        }
        if (_readFrame.HasPriorty)
            payloadStart += 5;

        if ((_currentStream.StreamId == _readFrame.StreamId && _currentStream.RequestEndHeaders)
            || _streams.ContainsKey(_readFrame.StreamId))
            throw new Http2ProtocolException();

        _currentStream = _streamPool.Get(state => new Http2Stream<TContext>(state.Item1, state.Features, state.application), (this, _context.Features, application));
        _currentStream.Initialize(_readFrame.StreamId, _h2Settings.InitialWindowSize, _context.ServerOptions.ServerStreamFlowControlSize);
        var addResult = _streams.TryAdd(_readFrame.StreamId, _currentStream);
        _streamIdIndex = uint.Max(_readFrame.StreamId, _streamIdIndex);
        Debug.Assert(addResult);
        bool endHeaders = _readFrame.EndHeaders;
        ResetHeadersParsingState();
        _hpackDecoder.Decode(memory.Span.Slice(payloadStart, (int)_readFrame.PayloadLength - payloadStart - paddingLength), endHeaders, this);
        if (endHeaders)
        {
            _currentStream.RequestEndHeadersReceived();
            StartStream();
        }
    }

    // +---------------------------------------------------------------+
    // |                   Header Block Fragment(*)                 ...
    // +---------------------------------------------------------------+
    private async ValueTask ProcessContinuationFrame()
    {
        if (_currentStream.StreamId != _readFrame.StreamId || _currentStream.RequestEndHeaders)
            throw new Http2ProtocolException();
        if (!_streams.ContainsKey(_readFrame.StreamId))
            return;

        var memory = _buffer.AsMemory(0, (int)_readFrame.PayloadLength);
        await _inputStream.ReadExactlyAsync(memory);
        bool endHeaders = _readFrame.EndHeaders;
        _hpackDecoder.Decode(memory.Span, endHeaders, this);
        if (endHeaders)
        {
            _currentStream.RequestEndHeadersReceived();
            StartStream();
        }
    }

    // +---------------------------------------------------------------+
    // |                        Error Code(32)                        |
    // +---------------------------------------------------------------+
    private async ValueTask ProcessResetStreamFrame()
    {
        var streamId = _readFrame.StreamId;
        if (!_streams.TryGetValue(streamId, out var httpStream))
            throw new Http2ProtocolException();
        if (_readFrame.PayloadLength != 4)
            throw new Http2ConnectionException(Http2ErrorCode.FRAME_SIZE_ERROR);
        await _inputStream.ReadExactlyAsync(_buffer.AsMemory(0, 4));
        Http2ErrorCode errorCode = (Http2ErrorCode)IntegerSerializer.ReadUInt32BigEndian(_buffer.AsSpan(0, 4));

        // Abort stream
        httpStream.Abort();
        OnStreamCompleted(httpStream);
        if (_currentStream.StreamId == streamId)
            _currentStream = _defaultStream;
    }

    private void StartStream()
    {
        ThreadPool.UnsafeQueueUserWorkItem(_currentStream, preferLocal: false);
    }

    private async ValueTask ProcessWindowUpdateFrame()
    {
        if (_readFrame.PayloadLength != 4)
            throw new Http2ProtocolException();
        var memory = _buffer.AsMemory(0, 4);
        await _inputStream.ReadExactlyAsync(memory);
        var updateSize = IntegerSerializer.ReadUInt32BigEndian(memory.Span);
        if (updateSize > MaxWindowUpdateSize)
            throw new Http2ProtocolException();

        var streamId = _readFrame.StreamId;
        if (streamId > 0)
            _streams[_readFrame.StreamId].UpdateWindowSize(updateSize);
        else
        {
            _clientWindow.ReleaseSize(updateSize);

            // Let all streams know, so that they can schedule data writes (if
            // they were blocked by connection windows earlier).
            foreach (var stream in _streams.Values)
                stream.OnConnectionWindowUpdateSize();
        }
    }

    private async Task ReadFrameHeader(CancellationToken token)
    {
        var frameHeader = _buffer.AsMemory(0, MaxFrameHeaderLength);
        await _inputStream.ReadExactlyAsync(frameHeader, token);
        _readFrame.PayloadLength = IntegerSerializer.ReadUInt24BigEndian(frameHeader.Span[0..3]);
        _readFrame.Type = (Http2FrameType)frameHeader.Span[3];
        _readFrame.Flags = frameHeader.Span[4];
        var streamId = IntegerSerializer.ReadUInt32BigEndian(frameHeader.Span[5..]);
        if (streamId >= MaxStreamId)
            throw new Http2ConnectionException("Reserved bit must be unset");
        _readFrame.StreamId = streamId;
    }

    private async ValueTask ProcessSettingsFrame()
    {
        if (_readFrame.StreamId != 0)
            throw new Http2ProtocolException();
        if (_readFrame.Flags == 1) // SETTING ACK
            return;

        var payloadLength = (int)_readFrame.PayloadLength;
        var memory = _buffer.AsMemory(0, payloadLength);
        await _inputStream.ReadExactlyAsync(memory);

        var span = memory.Span;
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

        //SETTINGS_HEADER_TABLE_SIZE
        //SETTINGS_ENABLE_PUSH
        //SETTINGS_MAX_CONCURRENT_STREAMS
        //SETTINGS_INITIAL_WINDOW_SIZE
        //SETTINGS_MAX_FRAME_SIZE
        //SETTINGS_MAX_HEADER_LIST_SIZE
    }

    private async Task ReadPreface(CancellationToken token)
    {
        var preface = _buffer.AsMemory(0, PrefaceBytes.Length);
        try
        {
            await _inputStream.ReadExactlyAsync(preface, token);
        }
        catch (OperationCanceledException)
        {
            throw new Http2ConnectionException("Connection aborted while reading preface");
        }
        if (!preface.Span.SequenceEqual(PrefaceBytes))
            throw new Http2ConnectionException("Request is not HTTP2");
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
}