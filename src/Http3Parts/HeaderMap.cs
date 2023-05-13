using System.Net.Http.QPack;

namespace Http3Parts;

public static class HeaderMap
{
    public static bool TryGetStaticRequestHeader(string headerName, out int h3StaticValue)
    {
        h3StaticValue = headerName switch
        {
            "Content-Disposition" =>H3StaticTable.ContentDisposition,
            "Content-Length" =>H3StaticTable.ContentLength0,
            "Cookie" =>H3StaticTable.Cookie,
            "Date" =>H3StaticTable.Date,
            "If-Modified-Since" =>H3StaticTable.IfModifiedSince,
            "If-None-Match" =>H3StaticTable.IfNoneMatch,
            "Last-Modified" => H3StaticTable.LastModified,
            "Link" => H3StaticTable.Link,
            "Referer" => H3StaticTable.Referer,
            "Accept-Language" => H3StaticTable.AcceptLanguage,
            "Authorization" => H3StaticTable.Authorization,
            "If-Range" => H3StaticTable.IfRange,
            "Origin" => H3StaticTable.Origin,
            "Upgrade-Insecure-Requests" => H3StaticTable.UpgradeInsecureRequests1,
            "User-Agent" => H3StaticTable.UserAgent,
            _ => -1
        };
        return h3StaticValue != -1;
    }
}