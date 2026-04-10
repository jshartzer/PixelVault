using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class ImportServiceUndoManifestTests
{
    [Fact]
    public void AppendUndoManifestEntries_MergesWithExisting()
    {
        var undoPath = Path.Combine(Path.GetTempPath(), "pv_undo_" + System.Guid.NewGuid().ToString("n") + ".txt");
        try
        {
            var svc = new ImportService(new ImportServiceDependencies
            {
                FileSystem = new FileSystemService(),
                MetadataService = new StubMetadataService(),
                GetFileCreationTime = _ => System.DateTime.MinValue,
                GetFileLastWriteTime = _ => System.DateTime.MinValue,
                CoverService = new StubCoverService(),
                NormalizeGameIndexName = s => s.Trim(),
                UndoManifestPath = () => undoPath
            });

            svc.SaveUndoManifest(new List<UndoImportEntry>
            {
                new() { SourceDirectory = @"C:\src", ImportedFileName = "a.png", CurrentPath = @"D:\dst\a.png" }
            });

            svc.AppendUndoManifestEntries(new[]
            {
                new UndoImportEntry { SourceDirectory = @"C:\src2", ImportedFileName = "b.png", CurrentPath = @"D:\dst\b.png" }
            });

            var loaded = svc.LoadUndoManifest();
            Assert.Equal(2, loaded.Count);
            Assert.Equal("a.png", loaded[0].ImportedFileName);
            Assert.Equal("b.png", loaded[1].ImportedFileName);
        }
        finally
        {
            try
            {
                if (File.Exists(undoPath)) File.Delete(undoPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AppendUndoManifestEntries_NoOpWhenEmpty()
    {
        var undoPath = Path.Combine(Path.GetTempPath(), "pv_undo_" + System.Guid.NewGuid().ToString("n") + ".txt");
        try
        {
            var svc = new ImportService(new ImportServiceDependencies
            {
                FileSystem = new FileSystemService(),
                MetadataService = new StubMetadataService(),
                GetFileCreationTime = _ => System.DateTime.MinValue,
                GetFileLastWriteTime = _ => System.DateTime.MinValue,
                CoverService = new StubCoverService(),
                NormalizeGameIndexName = s => s.Trim(),
                UndoManifestPath = () => undoPath
            });

            svc.SaveUndoManifest(new List<UndoImportEntry>
            {
                new() { SourceDirectory = @"C:\src", ImportedFileName = "a.png", CurrentPath = @"D:\dst\a.png" }
            });

            svc.AppendUndoManifestEntries(Enumerable.Empty<UndoImportEntry>());
            svc.AppendUndoManifestEntries(null);

            var loaded = svc.LoadUndoManifest();
            Assert.Single(loaded);
        }
        finally
        {
            try
            {
                if (File.Exists(undoPath)) File.Delete(undoPath);
            }
            catch
            {
                // ignore
            }
        }
    }
}
