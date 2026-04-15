using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryMetadataTargetedBatchTests
{
    [Fact]
    public void BuildLibraryMetadataItemsForPathsCore_ReadsOnlyRequestedExistingFiles()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "pv-libmeta-targeted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);

        var wantedA = Path.Combine(baseDir, "wanted-a.png");
        var wantedB = Path.Combine(baseDir, "wanted-b.png");
        var unrequested = Path.Combine(baseDir, "unrequested.png");
        var missing = Path.Combine(baseDir, "missing.png");

        try
        {
            File.WriteAllText(wantedA, "a");
            File.WriteAllText(wantedB, "b");
            File.WriteAllText(unrequested, "c");

            var batchReads = new List<string>();
            var result = MainWindow.BuildLibraryMetadataItemsForPathsCore(
                new[] { wantedA, missing, wantedB, wantedA },
                File.Exists,
                files =>
                {
                    batchReads = files.ToList();
                    return batchReads.ToDictionary(
                        path => path,
                        path => new EmbeddedMetadataSnapshot { Comment = Path.GetFileNameWithoutExtension(path) },
                        StringComparer.OrdinalIgnoreCase);
                },
                (file, snapshot) => snapshot.Comment,
                file => string.Equals(file, wantedA, StringComparison.OrdinalIgnoreCase)
                    ? new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc)
                    : new DateTime(2026, 4, 14, 11, 0, 0, DateTimeKind.Utc));

            Assert.Equal(new[] { wantedA, wantedB }, batchReads);
            Assert.Equal(new[] { "wanted-a", "wanted-b" }, result);
            Assert.DoesNotContain(unrequested, batchReads, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
