using System.Numerics;
using System.Runtime;
using System.Security.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed class Http2Connection
{
    private const int MaxFrameHeaderLength = 9;
    private const uint MaxStreamId = uint.MaxValue >> 1;

    private static ReadOnlySpan<byte> PrefaceBytes => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    private readonly CHttpConnectionContext _context;
    private readonly Stream _inputStream;
    private readonly int _streamIdIndex;
    private readonly bool _aborted;

    private byte[] _buffer;
    private FrameWriter? _writer;
    private Http2Frame _readFrame;
    private Http2SettingsPayload _h2Settings;

    public Http2Connection(CHttpConnectionContext connectionContext)
    {
        _context = connectionContext;
        connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(OnHeartbeat, this);
        _h2Settings = new();
        _buffer = new byte[_h2Settings.MaxFrameSize];
        _inputStream = connectionContext.Transport!;
        _aborted = false;
        _readFrame = new();
    }

    public async Task ProcessRequestAsync<TContext>(IHttpApplication<TContext> application)
    {
        _writer = new FrameWriter(_context);
        Http2ErrorCode errorCode = Http2ErrorCode.NO_ERROR;
        try
        {
            ValidateTlsRequirements();
            await ReadPreface();
            _writer.WriteSettings(_h2Settings);

            while (!_aborted)
            {
                await ReadFrameHeader();
                await ProcessFrame();
            }

        }
        catch (Http2ConnectionException)
        {
            errorCode = Http2ErrorCode.CONNECT_ERROR;
        }
        catch (Http2ProtocolException)
        {
            errorCode = Http2ErrorCode.PROTOCOL_ERROR;
        }
        catch (Exception e)
        {
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

    private Task ProcessFrame()
    {
        if (_readFrame.Type == Http2FrameType.SETTINGS)
            return ProcessSettingsFrame();
        return Task.CompletedTask;
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

    private async Task ProcessSettingsFrame()
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
        {
            throw new Http2ConnectionException("TLS required");
        }
    }
}

internal struct Http2SettingsPayload
{
    public Http2SettingsPayload()
    {
        HeaderTableSize = 0;
        EnablePush = 0;
        MaxConcurrentStream = 100;
        InitialWindowSize = 65_535 * 2;
        MaxFrameSize = 16_384 * 2;
        SettingsReceived = false;
    }

    public uint HeaderTableSize { get; set; }

    public uint EnablePush { get; set; }

    public uint MaxConcurrentStream { get; set; }

    public uint InitialWindowSize { get; set; } 

    public uint MaxFrameSize { get; set; }

    public uint MaxHeaderListSize { get; set; }

    public bool SettingsReceived { get; set; }
}