using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class ForegroundIntakeBusyGateTests
{
    [Fact]
    public void EnterLeave_IsBusy_MatchesDepth()
    {
        var g = new ForegroundIntakeBusyGate();
        Assert.False(g.IsBusy);
        g.Enter();
        Assert.True(g.IsBusy);
        g.Enter();
        Assert.True(g.IsBusy);
        g.Leave();
        Assert.True(g.IsBusy);
        g.Leave();
        Assert.False(g.IsBusy);
    }
}
