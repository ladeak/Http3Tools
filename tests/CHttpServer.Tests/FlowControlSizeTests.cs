namespace CHttpServer.Tests;

public class FlowControlSizeTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void TryUse_Available_True(uint requestedSize)
    {
        var flowControl = new FlowControlSize(100);
        Assert.True(flowControl.TryUse(requestedSize));
    }

    [Fact]
    public void TryUse_NotAvailable_False()
    {
        var flowControl = new FlowControlSize(1);
        Assert.False(flowControl.TryUse(2));
    }

    [Fact]
    public void MultipleTryUse()
    {
        var flowControl = new FlowControlSize(10);
        Assert.True(flowControl.TryUse(4));
        Assert.True(flowControl.TryUse(6));
        Assert.False(flowControl.TryUse(1));
    }

    [Fact]
    public void ReleaseSize()
    {
        var flowControl = new FlowControlSize(10);
        flowControl.ReleaseSize(10);
        Assert.True(flowControl.TryUse(20));
    }

    [Fact]
    public void TryUseAndRelease_Available_True()
    {
        var flowControl = new FlowControlSize(10);
        Assert.True(flowControl.TryUse(4));
        flowControl.ReleaseSize(1);
        Assert.True(flowControl.TryUse(6));
        Assert.True(flowControl.TryUse(1));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void TryUseAny_Available_True(uint requestedSize)
    {
        var flowControl = new FlowControlSize(100);
        Assert.True(flowControl.TryUseAny(requestedSize, out var received));
        Assert.Equal(requestedSize, received);
    }

    [Fact]
    public void TryUseAny_NotAvailable_False()
    {
        var flowControl = new FlowControlSize(1);
        Assert.False(flowControl.TryUseAny(2, out var received));
        Assert.Equal(1U, received);
    }

    [Fact]
    public void MultipleTryUseAny()
    {
        var flowControl = new FlowControlSize(10);
        Assert.True(flowControl.TryUseAny(4, out var received));
        Assert.Equal(4U, received);
        Assert.True(flowControl.TryUseAny(5, out received));
        Assert.Equal(5U, received);
        Assert.False(flowControl.TryUseAny(2, out received));
        Assert.Equal(1U, received);
    }

    [Fact]
    public void TryUseAnyAndRelease_Available_True()
    {
        var flowControl = new FlowControlSize(10);
        Assert.True(flowControl.TryUseAny(4, out var received));
        Assert.Equal(4U, received);
        flowControl.ReleaseSize(1);
        Assert.True(flowControl.TryUseAny(6, out received));
        Assert.Equal(6U, received);
        Assert.True(flowControl.TryUseAny(1, out received));
        Assert.Equal(1U, received);
    }
}