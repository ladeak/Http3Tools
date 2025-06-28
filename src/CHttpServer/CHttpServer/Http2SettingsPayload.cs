namespace CHttpServer;

internal struct Http2SettingsPayload
{
    public Http2SettingsPayload()
    {
        HeaderTableSize = 0;
        EnablePush = 0;
        InitialWindowSize = 65_535;
        SendMaxFrameSize = 16_384; // Receiver can change it settings
        ReceiveMaxFrameSize = 16_384 * 2; // Advertised by the server
        SettingsReceived = false;
        DisableRFC7540Priority = false;
    }

    public uint HeaderTableSize { get; set; }

    public uint EnablePush { get; set; }

    public uint MaxConcurrentStream { get; set; }

    public uint InitialWindowSize { get; set; }

    public uint SendMaxFrameSize { get; set; }

    public uint ReceiveMaxFrameSize { get; set; }

    public uint MaxHeaderListSize { get; set; }

    public bool SettingsReceived { get; set; }

    public bool DisableRFC7540Priority { get; init; }
}