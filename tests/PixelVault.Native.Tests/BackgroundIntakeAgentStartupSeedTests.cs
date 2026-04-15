using System.IO;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class BackgroundIntakeAgentStartupSeedTests
{
    [Fact]
    public void EnumerateExistingTopLevelMediaFiles_IncludesOnlyTopLevelMediaAndDedupesRoots()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "pv-bgint-seed-" + Guid.NewGuid().ToString("N"));
        var rootA = Path.Combine(baseDir, "rootA");
        var rootB = Path.Combine(baseDir, "rootB");
        var nested = Path.Combine(rootA, "nested");

        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        Directory.CreateDirectory(nested);

        var topLevelMediaA = Path.Combine(rootA, "capture-a.png");
        var topLevelTextA = Path.Combine(rootA, "notes.txt");
        var nestedMedia = Path.Combine(nested, "nested-capture.png");
        var topLevelMediaB = Path.Combine(rootB, "capture-b.jpg");

        try
        {
            File.WriteAllText(topLevelMediaA, "a");
            File.WriteAllText(topLevelTextA, "b");
            File.WriteAllText(nestedMedia, "c");
            File.WriteAllText(topLevelMediaB, "d");
            static bool IsMediaPath(string path)
            {
                var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
                return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".jxr" or ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm";
            }

            var results = MainWindow.BackgroundIntakeAgent.EnumerateExistingTopLevelMediaFiles(
                new[] { rootA, rootA, rootB, Path.Combine(baseDir, "missing") },
                IsMediaPath);

            Assert.Equal(
                new[] { Path.GetFullPath(topLevelMediaA), Path.GetFullPath(topLevelMediaB) }.OrderBy(path => path),
                results.OrderBy(path => path));
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
