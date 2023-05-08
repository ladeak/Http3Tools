using System.Net.Quic;

internal class ConnectionContext
{
    public required QuicConnection QuicConnection { get; init; }
}