using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class Http3Stream
{
    private int _id;
    private QuicStream _quicStream;
    private PipeReader _dataReader;
    private PipeWriter _dataWriter;
    private QPackDecoder _qpackDecoder;

    public Http3Stream(
        int id,
        QuicStream quicStream)
    {
        _id = id;
        _quicStream = quicStream;
        _dataReader = PipeReader.Create(quicStream);
        _dataWriter = PipeWriter.Create(quicStream);
        _qpackDecoder = new QPackDecoder();
    }

    public async Task ProcessStreamAsync<TContext>(IHttpApplication<TContext> application, CancellationToken token) 
        where TContext : notnull
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var readResult = await _dataReader.ReadAsync(token);
                if (readResult.IsCanceled || readResult.IsCompleted)
                    break;

                var buffer = readResult.Buffer;

                // FrameType is a single byte.
                if (!VariableLenghtIntegerDecoder.TryRead(buffer.FirstSpan, out ulong frameType, out int bytesRead))
                    throw new Http3ConnectionException(ErrorCodes.H3FrameError);

                buffer = buffer.Slice(bytesRead);
                if (!VariableLenghtIntegerDecoder.TryRead(buffer, out ulong payloadLength, out bytesRead))
                {
                    // Not enough data to read payload length.
                    _dataReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    continue;
                }
                buffer = buffer.Slice(bytesRead);
                long processed = 1 + bytesRead;
                switch (frameType)
                {
                    case 0x0: // DATA
                        break;
                    case 0x1: // HEADERS
                        processed += ProcessHeaderFrame(buffer);
                        break;
                }

                _dataReader.AdvanceTo(readResult.Buffer.GetPosition(processed), readResult.Buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch(Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _quicStream.Abort(QuicAbortDirection.Both, ErrorCodes.H3NoError);
            await _dataReader.CompleteAsync();
            await _dataWriter.CompleteAsync();
            _quicStream.Dispose();
        }
    }

    private long ProcessHeaderFrame(ReadOnlySequence<byte> buffer)
    {
        _qpackDecoder.DecodeHeader(buffer, this, out long consumed);
        return consumed;
    }
}
