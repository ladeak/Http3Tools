using System.Net.Quic;

namespace Http3Parts.Tests;

public sealed class QuicSupported : FactAttribute
{
    public QuicSupported()
    {
        if (!QuicConnection.IsSupported)
            Skip = "Quic is not supported on this platform.";
    }
}
