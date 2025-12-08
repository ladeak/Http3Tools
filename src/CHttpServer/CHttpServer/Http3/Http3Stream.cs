using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Http3;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed partial class Http3Stream
{
    private int _id;
    private QuicStream? _quicStream;
    private PipeReader _dataReader;
    private PipeWriter _dataWriter;
    private QPackDecoder _qpackDecoder;
    private Task _applicationProcessing;
    private FeatureCollection _features;

    public Http3Stream(FeatureCollection features)
    {
        _id = 0;
        _quicStream = null;
        _dataReader = PipeReader.Create(new ReadOnlySequence<byte>());
        _dataWriter = PipeWriter.Create(Stream.Null);
        _qpackDecoder = new QPackDecoder();
        _pathEncoded = [];
        Scheme = string.Empty;
        Method = string.Empty;
        Path = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false;
        _applicationProcessing = Task.CompletedTask;

        _features = features;
        _features.Add<IHttpRequestFeature>(this);
        //_features.Add<IHttpResponseFeature>(this);
        //_features.Add<IHttpResponseBodyFeature>(this);
        //_features.Add<IHttpResponseTrailersFeature>(this);
        //_features.Add<IHttpRequestBodyDetectionFeature>(this);
        //_features.Add<IHttpRequestLifetimeFeature>(this);
        //_features.Add<IPriority9218Feature>(this);
        _features.Checkpoint();
    }

    public void Initialize(int id, QuicStream quicStream)
    {
        _id = id;
        _quicStream = quicStream;
        _dataReader = PipeReader.Create(quicStream);
        _dataWriter = PipeWriter.Create(quicStream);
        Scheme = string.Empty;
        Method = string.Empty;
        QueryString = string.Empty;
        _isPathSet = false;
        _applicationProcessing = Task.CompletedTask;
        _features.ResetCheckpoint();
        // Path not reset
    }

    public async void ProcessStream<TContext>(IHttpApplication<TContext> application, CancellationToken token)
        where TContext : notnull => await ProcessStreamAsync(application, token);

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
                long processed = 1 + bytesRead; // 1 for the frame type. Should be always one byte by spec.
                switch (frameType)
                {
                    case 0x0: // DATA
                        processed += ProcessDataFrame(buffer);
                        break;
                    case 0x1: // HEADERS
                        processed += ProcessHeaderFrame(buffer);
                        var context = application.CreateContext(_features);
                        if (_features is IHostContextContainer<TContext> contextAwareFeatureCollection)
                            contextAwareFeatureCollection.HostContext = context;
                        _applicationProcessing = application.ProcessRequestAsync(context);
                        break;
                }

                _dataReader.AdvanceTo(readResult.Buffer.GetPosition(processed), readResult.Buffer.End);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _quicStream?.Abort(QuicAbortDirection.Both, ErrorCodes.H3NoError);
            await _dataReader.CompleteAsync();
            await _dataWriter.CompleteAsync();
            _quicStream?.Dispose();
        }
    }

    private long ProcessDataFrame(ReadOnlySequence<byte> buffer)
    {
        return 0;
    }

    private long ProcessHeaderFrame(ReadOnlySequence<byte> buffer)
    {
        _qpackDecoder.DecodeHeader(buffer, this, out long consumed);
        return consumed;
    }
}
