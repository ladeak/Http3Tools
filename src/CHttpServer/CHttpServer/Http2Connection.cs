﻿using System.Security.Authentication;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed partial class Http2Connection
{
    private const int MaxFrameHeaderLength = 9;
    private const uint MaxStreamId = uint.MaxValue >> 1;
    internal const uint MaxWindowUpdateSize = 2_147_483_647;

    private static ReadOnlySpan<byte> PrefaceBytes => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    private readonly CHttpConnectionContext _context;
    private readonly Stream _inputStream;
    private readonly int _streamIdIndex;
    private readonly bool _aborted;
    private readonly HPackDecoder _hpackDecoder;

    private byte[] _buffer;
    private FrameWriter? _writer;
    private Http2Frame _readFrame;
    private Http2ResponseWriter? _responseWriter;
    private Http2SettingsPayload _h2Settings;
    private Dictionary<uint, Http2Stream> _streams;
    private Http2Stream _currentStream;

    public Http2Connection(CHttpConnectionContext connectionContext)
    {
        _context = connectionContext;
        connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(OnHeartbeat, this);
        _streams = [];
        _currentStream = new Http2Stream<object>(0, 0, this, _context.Features, null!);
        _h2Settings = new();
        _hpackDecoder = new(maxDynamicTableSize: 0, maxHeadersLength: (int)_h2Settings.MaxFrameSize);
        _buffer = new byte[_h2Settings.MaxFrameSize];
        _inputStream = connectionContext.Transport!;
        _aborted = false;
        _readFrame = new();
    }

    internal Http2ResponseWriter? ResponseWriter => _responseWriter;

    public async Task ProcessRequestAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        _writer = new FrameWriter(_context, _h2Settings.InitialWindowSize);
        _responseWriter = new Http2ResponseWriter(_writer);
        Http2ErrorCode errorCode = Http2ErrorCode.NO_ERROR;
        try
        {
            ValidateTlsRequirements();
            await ReadPreface();
            _writer.WriteSettings(_h2Settings);

            while (!_aborted)
            {
                await ReadFrameHeader();
                await ProcessFrame(application);
            }

        }
        catch (Http2ConnectionException)
        {
            errorCode = Http2ErrorCode.CONNECT_ERROR;
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
        catch (Exception e)
        {
            // TODO cancel streams
        }
        finally
        {
            if (errorCode != Http2ErrorCode.NO_ERROR)
            {
                // todo close stream
                _writer.WriteGoAway(_streamIdIndex, errorCode);
            }
        }
    }

    private ValueTask ProcessFrame<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        if (_readFrame.Type == Http2FrameType.SETTINGS)
            return ProcessSettingsFrame();
        if (_readFrame.Type == Http2FrameType.WINDOW_UPDATE)
            return ProcessWindowUpdateFrame();
        if (_readFrame.Type == Http2FrameType.HEADERS)
            return ProcessHeaderFrame(application);
        if (_readFrame.Type == Http2FrameType.DATA)
            return ProcessDataFrame();
        return ValueTask.CompletedTask;
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

        var buffer = httpStream.RequestPipe.GetMemory((int)_h2Settings.MaxFrameSize)
            .Slice(0, (int)_readFrame.PayloadLength); // framesize max
        await _inputStream.ReadExactlyAsync(buffer);
        httpStream.RequestPipe.Advance(buffer.Length - paddingLength); // Padding is read but not advanced.

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

        if ((_currentStream.StreamId == _readFrame.StreamId || _currentStream.RequestEndHeaders)
            || _streams.ContainsKey(_readFrame.StreamId))
        {
            throw new Http2ProtocolException();
        }

        _currentStream = new Http2Stream<TContext>(_readFrame.StreamId, _h2Settings.InitialWindowSize, this, _context.Features, application);
        _streams.Add(_readFrame.StreamId, _currentStream);
        bool endHeaders = _readFrame.EndHeaders;
        _hpackDecoder.Decode(memory.Span.Slice(payloadStart, (int)_readFrame.PayloadLength - payloadStart - paddingLength), endHeaders, this);
        if (endHeaders)
            _currentStream.RequestEndHeadersReceived();
        StartStream();
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
            _writer!.UpdateConnectionWindowSize(updateSize);
    }

    private async Task ReadFrameHeader()
    {
        var frameHeader = _buffer.AsMemory(0, MaxFrameHeaderLength);
        await _inputStream.ReadExactlyAsync(frameHeader);
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
        if (_h2Settings.SettingsReceived)
            throw new Http2ConnectionException("Don't allow settings to change mid-connection");

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
                    _h2Settings.InitialWindowSize = Math.Min(_h2Settings.InitialWindowSize, settingValue);
                    break;
                case 5:
                    _h2Settings.MaxFrameSize = Math.Min(_h2Settings.MaxFrameSize, settingValue);
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

    private async Task ReadPreface()
    {
        var preface = _buffer.AsMemory(0, PrefaceBytes.Length);
        await _inputStream.ReadExactlyAsync(preface);
        if (!preface.Span.SequenceEqual(PrefaceBytes))
            throw new Http2ConnectionException("Request is not HTTP2");
    }

    private static void OnHeartbeat(object state)
    {
        // timeout for settings ack
    }

    private void ValidateTlsRequirements()
    {
        var tlsFeature = _context.Features.Get<ITlsHandshakeFeature>();
        if (tlsFeature == null || tlsFeature.Protocol < SslProtocols.Tls12)
            throw new Http2ConnectionException("TLS required");
    }
}