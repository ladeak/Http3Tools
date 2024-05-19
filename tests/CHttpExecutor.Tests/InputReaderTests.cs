namespace CHttpExecutor.Tests;

public class InputReaderTests
{
    private const int Port = 5020;

    private byte[] _singleRequest = @"###
GET https://localhost:5020/ HTTP/2"u8.ToArray();

    [Fact]
    public async Task SingleRequestInvokesEndpoint()
    {
        var stream = new MemoryStream(_singleRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        
    }
}