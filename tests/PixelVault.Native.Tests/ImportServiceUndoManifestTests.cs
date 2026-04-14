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

    [Fact]
    public void ExecuteUndoImportMoves_RegistersExactRestoredTargetForBackgroundSuppression()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-undo-suppress-" + System.Guid.NewGuid().ToString("n"));
        var sourceDir = Path.Combine(root, "source");
        var destinationDir = Path.Combine(root, "destination");
        var currentPath = Path.Combine(destinationDir, "clip.png");
        var conflictingSourcePath = Path.Combine(sourceDir, "clip.png");
        string suppressedPath = null;
        try
        {
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destinationDir);
            File.WriteAllText(currentPath, "new");
            File.WriteAllText(conflictingSourcePath, "existing");

            var svc = new ImportService(new ImportServiceDependencies
            {
                FileSystem = new FileSystemService(),
                MetadataService = new StubMetadataService(),
                GetFileCreationTime = _ => System.DateTime.MinValue,
                GetFileLastWriteTime = _ => System.DateTime.MinValue,
                CoverService = new StubCoverService(),
                NormalizeGameIndexName = s => s.Trim(),
                GetDestinationRoot = () => destinationDir,
                GetLibraryRoot = () => string.Empty,
                UniquePath = path => Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + " (1)" + Path.GetExtension(path)),
                SuppressBackgroundAutoIntakePathBeforeUndoMove = path => suppressedPath = path
            });

            var result = svc.ExecuteUndoImportMoves(new[]
            {
                new UndoImportEntry
                {
                    SourceDirectory = sourceDir,
                    ImportedFileName = "clip.png",
                    CurrentPath = currentPath
                }
            });

            var expectedTarget = Path.Combine(sourceDir, "clip (1).png");
            Assert.Equal(1, result.Moved);
            Assert.Equal(expectedTarget, suppressedPath, ignoreCase: true);
            Assert.True(File.Exists(expectedTarget));
            Assert.False(File.Exists(currentPath));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
