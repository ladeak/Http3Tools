
namespace CHttpServer;

internal interface IResponseWriter
{
    void Complete();

    Task RunAsync(CancellationToken token);

    void ScheduleEndStream(Http2Stream source);

    void ScheduleWriteData(Http2Stream source);

    void ScheduleWriteGoAway(uint streamId);

    void ScheduleWriteHeaders(Http2Stream source);

    void ScheduleWritePingAck(ulong value);

    void ScheduleWriteTrailers(Http2Stream source);

    void ScheduleWriteWindowUpdate(Http2Stream source, uint size);

    void UpdateFrameSize(uint size);
}