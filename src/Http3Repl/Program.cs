using System.Net;

var client = new HttpClient();
var response = await client.SendAsync(new HttpRequestMessage()
{
    Method = HttpMethod.Get,
    RequestUri = new Uri("https://localhost:5001"),
    VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
    Version = HttpVersion.Version30
});
response.EnsureSuccessStatusCode();
var content = await response.Content.ReadAsStringAsync();
Console.WriteLine(content);
Console.ReadLine();