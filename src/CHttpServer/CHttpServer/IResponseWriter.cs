
namespace CHttpServer;

internal interface IResponseWriter
{
    // Indicates that there are no further streams to write
    // to the response stream.
    void Complete();

    Task RunAsync(CancellationToken token);

    void ScheduleEndStream(Http2Stream source);

    void ScheduleWriteData(Http2Stream source);

    void ScheduleWriteHeaders(Http2Stream source);

    void ScheduleWritePingAck(ulong value);

    void ScheduleWriteTrailers(Http2Stream source);

    void ScheduleWriteWindowUpdate(Http2Stream source, uint size);

    void UpdateFrameSize(uint size);

    void ScheduleResetStream(Http2Stream source, Http2ErrorCode errorCode);
}