using System.Net.Quic;

public class ConnectionContext
{
    public required QuicConnection QuicConnection { get; init; }
}