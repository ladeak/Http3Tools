using System.Text;
using CHttpServer.Http3;
using static CHttpServer.Tests.Http3.QPackDecoderTests;

namespace CHttpServer.Tests.Http3;

public class AspNetCoreHeadnerNamesLookupTests
{
    [Theory]
    [InlineData("accept", "Accept")]
    [InlineData("content-security-policy-report-only", "Content-Security-Policy-Report-Only")]
    [InlineData("content-type", "Content-Type")]
    [InlineData("if-unmodified-since", "If-Unmodified-Since")]
    [InlineData("strict-transport-security", "Strict-Transport-Security")]
    [InlineData("etag", "ETag")]
    public void TestSuccess(string inputHeaderName, string expected)
    {
        var input = new MemorySegment<byte>(Encoding.Latin1.GetBytes(inputHeaderName).AsMemory());
        var result =AspNetCoreHeadnerNamesLookup.TryGetAspNetCoreHeader(input.AsSequence(), out var resultHeaderName);
        Assert.True(result);
        Assert.Equal(expected, resultHeaderName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("content-security-policy-report-only-very-very-long")]
    [InlineData("nonse")]
    [InlineData("content-type2")]
    [InlineData("content-type-")]
    [InlineData(";content-type-")]
    [InlineData("-content-type")]
    public void TestFailures(string inputHeaderName)
    {
        var input = new MemorySegment<byte>(Encoding.Latin1.GetBytes(inputHeaderName).AsMemory());
        var result = AspNetCoreHeadnerNamesLookup.TryGetAspNetCoreHeader(input.AsSequence(), out var resultHeaderName);
        Assert.False(result);
    }
}
