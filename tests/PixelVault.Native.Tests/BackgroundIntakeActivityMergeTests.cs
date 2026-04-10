using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class BackgroundIntakeActivityMergeTests
{
    [Fact]
    public void MergeManifestAfterPartialUndo_RemovesSucceeded_KeepsSkippedAndUnselected()
    {
        var a = new UndoImportEntry { SourceDirectory = @"C:\up", ImportedFileName = "a.png", CurrentPath = @"D:\lib\a.png" };
        var b = new UndoImportEntry { SourceDirectory = @"C:\up", ImportedFileName = "b.png", CurrentPath = @"D:\lib\b.png" };
        var c = new UndoImportEntry { SourceDirectory = @"C:\up", ImportedFileName = "c.png", CurrentPath = @"D:\lib\c.png" };
        var full = new List<UndoImportEntry> { a, b, c };

        var selected = new List<UndoImportEntry>
        {
            new UndoImportEntry { SourceDirectory = @"C:\up", ImportedFileName = "a.png", CurrentPath = @"D:\lib\a.png" },
            new UndoImportEntry { SourceDirectory = @"C:\up", ImportedFileName = "b.png", CurrentPath = @"D:\lib\b.png" }
        };

        var undoResult = new UndoImportExecutionResult
        {
            Moved = 1,
            Skipped = 1,
            RemainingEntries = new List<UndoImportEntry>
            {
                new UndoImportEntry { SourceDirectory = @"C:\up", ImportedFileName = "b.png", CurrentPath = @"D:\lib\b.png" }
            }
        };

        var merged = BackgroundIntakeActivitySession.MergeManifestAfterPartialUndo(full, selected, undoResult);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, e => e.ImportedFileName == "b.png");
        Assert.Contains(merged, e => e.ImportedFileName == "c.png");
        Assert.DoesNotContain(merged, e => e.ImportedFileName == "a.png");
    }
}
