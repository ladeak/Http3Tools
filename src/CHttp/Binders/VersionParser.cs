using System.Net;

namespace CHttp.Binders;

internal class VersionParser
{
    internal const string Version10 = "1.0";
    internal const string Version11 = "1.1";
    internal const string Version20 = "2";
    internal const string Version30 = "3";

    internal static Version Map(string value)
    {
        return value switch
        {
            Version10 => HttpVersion.Version10,
            Version11 => HttpVersion.Version11,
            Version20 => HttpVersion.Version20,
            Version30 => HttpVersion.Version30,
            _ => throw new ArgumentException("Invalid version")
        };
    }
}