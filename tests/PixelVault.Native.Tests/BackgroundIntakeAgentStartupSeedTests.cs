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

    [Fact]
    public void EnumerateExistingTopLevelMediaFiles_WhenRecursiveIncludesNestedMediaAndSkipsHidden()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "pv-bgint-seed-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(baseDir, "root");
        var nested = Path.Combine(root, "Mario Kart World");
        var importDuplicates = Path.Combine(root, "Import Duplicates", "Mario Kart World");

        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(importDuplicates);

        var topLevelMedia = Path.Combine(root, "capture-a.png");
        var nestedMedia = Path.Combine(nested, "2018032120305000_c.jpg");
        var parkedDuplicate = Path.Combine(importDuplicates, "2018032120305000_c.jpg");
        var hiddenMedia = Path.Combine(nested, "hidden.png");

        try
        {
            File.WriteAllText(topLevelMedia, "a");
            File.WriteAllText(nestedMedia, "b");
            File.WriteAllText(parkedDuplicate, "b");
            File.WriteAllText(hiddenMedia, "c");
            File.SetAttributes(hiddenMedia, File.GetAttributes(hiddenMedia) | FileAttributes.Hidden);
            static bool IsMediaPath(string path)
            {
                var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
                return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".jxr" or ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm";
            }

            var results = MainWindow.BackgroundIntakeAgent.EnumerateExistingTopLevelMediaFiles(
                new[] { root },
                IsMediaPath,
                includeSubfolders: true);

            Assert.Equal(
                new[] { Path.GetFullPath(topLevelMedia), Path.GetFullPath(nestedMedia) }.OrderBy(path => path),
                results.OrderBy(path => path));
        }
        finally
        {
            try
            {
                if (File.Exists(hiddenMedia))
                    File.SetAttributes(hiddenMedia, FileAttributes.Normal);
                if (Directory.Exists(baseDir))
                    Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
