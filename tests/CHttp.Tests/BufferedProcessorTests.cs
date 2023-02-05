using System.Buffers;

namespace CHttp.Tests;

public class BufferedProcessorTests
{
    [Theory]
    [MemberData(nameof(InputData))]
    public void GiveSingleSegment_ReturnsFullText(byte[] input)
    {
        ReadOnlyMemory<byte> data = input.AsMemory();
        var segment = new ReadOnlySequence<byte>(data);
        var sut = new BufferedProcessor();
        var result = sut.TryReadLine(ref segment, out var line);
        Assert.True(result);
        Assert.True(data.Span.SequenceEqual(line.ToArray()));
    }

    [Theory]
    [MemberData(nameof(InputData))]
    public void GiveTwoSegment_ReturnsFullText(byte[] input)
    {
        ReadOnlyMemory<byte> data = input.AsMemory();
        for (int offset = 0; offset < data.Length; offset++)
        {
            var segment = new MemorySegment<byte>(data.Slice(0, offset))
                .Append(data.Slice(offset, data.Length - offset))
                .AsSequence();
            var sut = new BufferedProcessor();
            var result = sut.TryReadLine(ref segment, out var line);
            Assert.True(result);
            Assert.True(data.Span.SequenceEqual(line.ToArray()));
        }
    }

    [Theory]
    [MemberData(nameof(InputData))]
    public void GiveThreeSegment_ReturnsFullText(byte[] input)
    {
        ReadOnlyMemory<byte> data = input.AsMemory();
        for (int innerSegmentLength = 1; innerSegmentLength < 4; innerSegmentLength++)
        {
            for (int offset = 0; offset < data.Length - innerSegmentLength; offset++)
            {
                var segment = new MemorySegment<byte>(data.Slice(0, offset))
                  .Append(data.Slice(offset, innerSegmentLength))
                  .Append(data.Slice(offset + innerSegmentLength, data.Length - offset - innerSegmentLength))
                  .AsSequence();
                var sut = new BufferedProcessor();
                var result = sut.TryReadLine(ref segment, out var line);
                Assert.True(result);
                Assert.True(data.Span.SequenceEqual(line.ToArray()));
            }
        }
    }

    public static IEnumerable<object[]> InputData()
    {
        yield return new[] { "hello"u8.ToArray() };
        yield return new object[] { "hello€there"u8.ToArray() };
        yield return new object[] { "€there€"u8.ToArray() };
        yield return new object[] { "€there한"u8.ToArray() };
        yield return new object[] { "한there한"u8.ToArray() };
        yield return new object[] { "𐍈there"u8.ToArray() };
        yield return new object[] { "hi𐍈there"u8.ToArray() };
        yield return new object[] { "there𐍈"u8.ToArray() };
        yield return new object[] { "𐍈한𐍈€€𐍈한𐍈𐍈"u8.ToArray() };
    }
}
