using CHttp.Writers;

namespace CHttp.Tests.Formatters;

public class RatioFormatterTests
{
    [Fact]
    public void IntRatioFormatterTest()
    {
        var result = RatioFormatter<int>.FormatSize(new Ratio<int>(1, 2, TimeSpan.FromSeconds(1)));
        Assert.Equal("      1/2   1.0s", result);
    }

    [Fact]
    public void LongRatioFormatterTest()
    {
        var result = RatioFormatter<long>.FormatSize(new Ratio<long>(110, 230, TimeSpan.Zero));
        Assert.Equal("    110/230   0.0s", result);
    }

    [Fact]
    public void LongTotalRatioFormatterTest()
    {
        var result = RatioFormatter<long>.FormatSize(new Ratio<long>(110, int.MaxValue + 1L, TimeSpan.FromSeconds(100)));
        Assert.Equal("    110/2147483648 100.0s", result);
    }

    [Fact]
    public void LongNumeratorRatioFormatterTest()
    {
        var result = RatioFormatter<long>.FormatSize(new Ratio<long>(int.MaxValue + 1L, 1, TimeSpan.FromSeconds(10)));
        Assert.Equal("2147483648/1  10.0s", result);
    }

    [Fact]
    public void ShortRatioFormatterTest()
    {
        var result = RatioFormatter<short>.FormatSize(new Ratio<short>(1, 10, TimeSpan.Zero));
        Assert.Equal("      1/10   0.0s", result);
    }
}
