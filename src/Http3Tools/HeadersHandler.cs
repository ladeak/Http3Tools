using System.Collections.Concurrent;
using System.Net.Http.QPack;
using System.Text;

namespace Http3Tools;

internal class HeadersHandler : IHttpStreamHeadersHandler
{
    private readonly ConcurrentDictionary<int, string> _dynamicHeaders = new ConcurrentDictionary<int, string>();

    public void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        string headerName = string.Empty;
        if (index.HasValue)
            if (!_dynamicHeaders.TryGetValue(index.Value, out headerName))
            {
                headerName = Encoding.ASCII.GetString(name);
                _dynamicHeaders.TryAdd(index.Value, headerName);
            }
        Console.WriteLine($"{headerName}{Encoding.ASCII.GetString(value)}");
    }

    public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        Console.WriteLine($"{Encoding.ASCII.GetString(name)}{Encoding.ASCII.GetString(value)}");
    }

    public void OnHeadersComplete(bool endStream)
    {
    }

    public void OnStaticIndexedHeader(int index)
    {
        var field = H3StaticTable.Get(index);
        Console.WriteLine(field.ToString());
    }

    public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
    {
        var field = H3StaticTable.Get(index);
        Console.WriteLine($"{field}{Encoding.ASCII.GetString(value)}");
    }

}
