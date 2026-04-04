using PixelVault.LibraryAssets.Health;
using PixelVault.LibraryAssets.Reconciliation;
using Xunit;

namespace PixelVault.LibraryAssets.Tests;

public class LibraryRootHealthCheckerTests
{
    [Fact]
    public void Check_MissingDirectory_NotHealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), "pixelvault-health-", Guid.NewGuid().ToString("N"), "nope");
        var result = LibraryRootHealthChecker.Check(path, new LibraryRootHealthOptions(), null, null);
        Assert.False(result.IsHealthy);
        Assert.Equal(LibraryRootHealthFailureCode.Offline, result.FailureCode);
    }

    [Fact]
    public void Check_ExistingTempDir_Healthy()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pv-lib-assets-", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var result = LibraryRootHealthChecker.Check(dir, new LibraryRootHealthOptions(), null, null);
            Assert.True(result.IsHealthy);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Reconciliation_Unhealthy_DefersSoftMissing()
    {
        var health = new LibraryRootHealthResult
        {
            IsHealthy = false,
            FailureCode = LibraryRootHealthFailureCode.Offline,
            Messages = new[] { "Library offline or path missing." }
        };
        var plan = ScanReconciliationPlan.Create(health, destructiveMissingAndDeletesRequested: true);
        Assert.True(plan.ApplyAdditions);
        Assert.True(plan.ApplyUpdates);
        Assert.False(plan.ApplySoftMissing);
        Assert.False(plan.ApplyConfirmedDeletes);
        Assert.True(plan.AbortedDueToUnhealthyRoot);
        Assert.Contains("Library offline/inaccessible", plan.DiagnosticLogLine ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_Healthy_AllowsMissingWhenRequested()
    {
        var health = new LibraryRootHealthResult { IsHealthy = true, Messages = Array.Empty<string>() };
        var plan = ScanReconciliationPlan.Create(health, destructiveMissingAndDeletesRequested: false);
        Assert.True(plan.ApplySoftMissing);
        Assert.False(plan.ApplyConfirmedDeletes);
    }
}
