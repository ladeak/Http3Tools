using System.Net.Quic;

namespace Http3Parts.Tests;

public sealed class QuicSupportedFactAttribute : FactAttribute
{
    public QuicSupportedFactAttribute()
    {
        if (!QuicConnection.IsSupported)
            Skip = "Quic is not supported on this platform.";
    }
}

public sealed class QuicSupportedTheoryAttribute : TheoryAttribute
{
    public QuicSupportedTheoryAttribute()
    {
        if (!QuicConnection.IsSupported)
            Skip = "Quic is not supported on this platform.";
    }
}
