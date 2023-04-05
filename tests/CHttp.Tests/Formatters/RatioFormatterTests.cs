using CHttp.Writers;

namespace CHttp.Tests.Formatters;

public class RatioFormatterTests
{
    [Fact]
    public void IntRatioFormatterTest()
    {
        var result = RatioFormatter<int>.FormatSize(new Ratio<int>(1, 2));
        Assert.Equal("      1/2", result);
    }

    [Fact]
    public void LongRatioFormatterTest()
    {
        var result = RatioFormatter<long>.FormatSize(new Ratio<long>(110, 230));
        Assert.Equal("    110/230", result);
    }

    [Fact]
    public void LongTotalRatioFormatterTest()
    {
        var result = RatioFormatter<long>.FormatSize(new Ratio<long>(110, int.MaxValue + 1l));
        Assert.Equal("    110/2147483648", result);
    }

    [Fact]
    public void LongNumeratorRatioFormatterTest()
    {
        var result = RatioFormatter<long>.FormatSize(new Ratio<long>(int.MaxValue + 1l, 1));
        Assert.Equal("2147483648/1", result);
    }

    [Fact]
    public void ShortRatioFormatterTest()
    {
        var result = RatioFormatter<short>.FormatSize(new Ratio<short>(1, 10));
        Assert.Equal("      1/10", result);
    }
}
