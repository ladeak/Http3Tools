using System.Linq;
using System.Net.Http.Headers;
using CHttp.Abstractions;

namespace CHttp.Writers;

internal static class HeaderWriter
{
    internal static void Write(HttpHeaders headers, IConsole console)
    {
        foreach (var header in headers)
            Write(header, console);
    }

    internal static void Write(KeyValuePair<string, IEnumerable<string>> header, IConsole console)
    {
        console.Write(header.Key);
        console.Write(":");
        if (!header.Value.Any())
            return;
        console.Write(header.Value.First());
        var separator = GetSeparatorChar(header.Key).ToString();
        foreach (var value in header.Value.Skip(1))
        {
            console.Write(separator);
            console.Write(value);
        }
        console.WriteLine();
    }

    internal static string WriteValue(string headerName, IEnumerable<string> headerValues) => string.Join(GetSeparatorChar(headerName), headerValues);

    private static char GetSeparatorChar(string headerName)
    {
        return headerName switch
        {
            "User-Agent" => ';',
            "Cookie" => ';',
            "Server" => ';',
            _ => ',',
        };
    }
}