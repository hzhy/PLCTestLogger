using PlcTestLogger.Core.Polling;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class RisingEdgeDetectorTests
{
    [Fact]
    public void ZeroThenOne_TriggersOnce()
    {
        var d = new RisingEdgeDetector();
        Assert.False(d.Feed(0));
        Assert.True(d.Feed(1));
        Assert.False(d.Feed(1));
    }

    [Fact]
    public void ConstantOne_NeverTriggers()
    {
        var d = new RisingEdgeDetector();
        Assert.False(d.Feed(1));
        Assert.False(d.Feed(1));
        Assert.False(d.Feed(1));
    }

    [Fact]
    public void StartsAtOne_TriggersOnlyAfterFallAndRise()
    {
        var d = new RisingEdgeDetector();
        Assert.False(d.Feed(1));
        Assert.False(d.Feed(0));
        Assert.True(d.Feed(1));
    }

    [Fact]
    public void Reset_MakesNextValueBaseline()
    {
        var d = new RisingEdgeDetector();
        Assert.False(d.Feed(0));
        d.Reset();
        Assert.False(d.Feed(1));   // 恢复后首值仅作基线
        Assert.False(d.Feed(0));
        Assert.True(d.Feed(1));
    }
}
