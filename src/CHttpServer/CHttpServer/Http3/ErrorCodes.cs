namespace CHttpServer.Http3;

internal class ErrorCodes
{
    internal const int H3NoError = 0x100;
    internal const int H3GeneralProtocolError = 0x101;
    internal const int H3InternalError = 0x102;
    internal const int H3StreamCreationError = 0x0103;
    internal const int H3ClosedCriticalStream = 0x104;
    internal const int H3FrameUnexpected = 0x105;
    internal const int H3FrameError = 0x0106;
    internal const int H3ExcessiveLoadError = 0x107;
    internal const int H3IdError = 0x108;
    internal const int H3SettingsError = 0x109;
    internal const int H3MissingSettings = 0x10a;
    internal const int H3RequestRejected = 0x10b;
    internal const int H3RequestCancelled = 0x10c;
    internal const int H3RequestIncomplete = 0x10d;
    internal const int H3MessageError = 0x10e;
    internal const int H3ConnectError = 0x10f;
    internal const int H3VersionFallback = 0x110;
}
