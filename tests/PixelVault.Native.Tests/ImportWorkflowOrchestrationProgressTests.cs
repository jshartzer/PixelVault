using System.Collections.Generic;
using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class ImportWorkflowOrchestrationProgressTests
{
    [Fact]
    public void StandardTotals_Matches_Move_ManualPaths_Exclusion()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pv-import-totals-" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tmp);
            var a = Path.Combine(tmp, "a.jpg");
            var b = Path.Combine(tmp, "b.jpg");
            File.WriteAllText(a, "");
            File.WriteAllText(b, "");
            var inv = new SourceInventory
            {
                TopLevelMediaFiles = new List<string> { a, b },
                RenameScopeFiles = new List<string> { a }
            };
            var manual = new HashSet<string>(new[] { a }, System.StringComparer.OrdinalIgnoreCase);
            var t = ImportWorkflowOrchestration.ComputeStandardImportWorkTotals(inv, new List<ReviewItem>(), inv, manual);
            Assert.Equal(1, t.RenameTotal);
            Assert.Equal(1, t.MoveTotal);
            Assert.Equal(0, t.DeleteTotal);
            Assert.Equal(0, t.MetadataTotal);
            Assert.Equal(1 + 0 + 0 + 1 + 1, t.TotalWork);
            Assert.Equal(0, t.RenameOffset);
            Assert.Equal(1, t.DeleteOffset);
            Assert.Equal(1, t.MetadataOffset);
            Assert.Equal(1, t.MoveOffset);
            Assert.Equal(2, t.SortOffset);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void UnifiedPlan_Doubles_Rename_Phase_And_Counts_Deletes()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pv-uni-" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tmp);
            var path1 = Path.Combine(tmp, "x.jpg");
            var path2 = Path.Combine(tmp, "y.jpg");
            File.WriteAllText(path1, "");
            File.WriteAllText(path2, "");
            var batch = new List<ManualMetadataItem>
            {
                new ManualMetadataItem { FilePath = path1, DeleteBeforeProcessing = true },
                new ManualMetadataItem { FilePath = path2 },
                new ManualMetadataItem { FilePath = Path.Combine(tmp, "missing.jpg") }
            };
            var p = ImportWorkflowOrchestration.ComputeUnifiedImportProgressPlan(batch);
            Assert.Equal(3, p.SteamRenameTotal);
            Assert.Equal(3, p.ManualRenameTotal);
            Assert.Equal(1, p.DeleteTotal);
            Assert.Equal(3, p.MetadataTotal);
            Assert.Equal(2, p.MoveTotal);
            var expected = 3 + 3 + 1 + 3 + 2 + 1;
            Assert.Equal(expected, p.TotalWork);
            Assert.Equal(0, p.SteamOff);
            Assert.Equal(3, p.ManualRenameOff);
            Assert.Equal(6, p.DeleteOff);
            Assert.Equal(7, p.MetadataOff);
            Assert.Equal(10, p.MoveOff);
            Assert.Equal(12, p.SortOff);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ManualIntakePlan_Aligns_Offsets()
    {
        var batch = new List<ManualMetadataItem> { new ManualMetadataItem { FilePath = "nope" } };
        var p = ImportWorkflowOrchestration.ComputeManualIntakeProgressPlan(batch);
        Assert.Equal(1, p.RenameTotal);
        Assert.Equal(1, p.MetadataTotal);
        Assert.Equal(0, p.MoveTotal);
        Assert.Equal(1 + 1 + 0 + 1, p.TotalWork);
        Assert.Equal(0, p.RenameOffset);
        Assert.Equal(1, p.MetadataOffset);
        Assert.Equal(2, p.MoveOffset);
        Assert.Equal(2, p.SortOffset);
    }
}
