using PixelVault.Companion.Connector;
using Xunit;

public sealed class StableSourceTests
{
    [Fact] public void ChangingFilesWaitForStabilityAndAcknowledgedChangesSurviveRestart()
    {
        var now=DateTime.UtcNow;var monitor=new StableSourceMonitor();Assert.False(monitor.Ready("source","A",now));Assert.False(monitor.Ready("source","A",now.AddSeconds(2)));
        Assert.False(monitor.Ready("source","B",now.AddSeconds(3)));Assert.True(monitor.Ready("source","B",now.AddSeconds(7)));
        monitor.Acknowledge("source","B");Assert.False(monitor.Ready("source","B",now.AddHours(1)));
        var restarted=new StableSourceMonitor(new(monitor.Handled));Assert.False(restarted.Ready("source","B",now.AddHours(2)));Assert.False(restarted.Ready("source","C",now.AddHours(2)));Assert.True(restarted.Ready("source","C",now.AddHours(2).AddSeconds(4)));
    }
    [Fact] public void PendingReviewAndOfflineRetriesCanBeBackedOffWithoutLosingAChange()
    {
        var now=DateTime.UtcNow;var monitor=new StableSourceMonitor();monitor.Ready("source","A",now);monitor.Delay("source",now,TimeSpan.FromMinutes(1));
        Assert.False(monitor.Ready("source","A",now.AddSeconds(10)));Assert.True(monitor.Ready("source","A",now.AddMinutes(1)));Assert.Empty(monitor.Handled);
    }
}
