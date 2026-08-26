using System.Security.Authentication;

namespace CHttp.Binders;

internal class TlsVersionParser
{
    internal const string Tls12 = "Tls12";
    internal const string Tls13 = "Tls13";

    internal static SslProtocols Map(SslProtocols? value)
    {
        if (value == null)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
                return SslProtocols.Tls12;
            else
                return SslProtocols.Tls12 | SslProtocols.Tls13;
        }
        return value.Value;
    }

    internal static SslProtocols Map(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
                return SslProtocols.Tls12;
            else
                return SslProtocols.Tls12 | SslProtocols.Tls13;
        }
        SslProtocols parsedProtocols = SslProtocols.None;
        foreach (var protocol in value.AsSpan().Split(";, "))
        {
            parsedProtocols |= value.AsSpan(protocol) switch
            {
                Tls13 => SslProtocols.Tls13,
                Tls12 => SslProtocols.Tls12,
                _ => SslProtocols.None,
            };
        }
        return parsedProtocols;
    }
}