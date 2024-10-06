namespace CHttpServer;

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