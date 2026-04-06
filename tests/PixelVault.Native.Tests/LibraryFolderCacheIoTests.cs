using System;
using System.Collections.Generic;
using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class LibraryFolderCacheIoTests
{
    [Fact]
    public void InventoryStampDiffersOnlyInDirectoryTicks_DetectsMtimeOnlyChange()
    {
        Assert.True(MainWindow.LibraryFolderCacheInventoryStampDiffersOnlyInDirectoryTicks("3|100|42", "3|200|42"));
        Assert.False(MainWindow.LibraryFolderCacheInventoryStampDiffersOnlyInDirectoryTicks("3|100|42", "3|100|42"));
        Assert.False(MainWindow.LibraryFolderCacheInventoryStampDiffersOnlyInDirectoryTicks("3|100|42", "4|200|42"));
        Assert.False(MainWindow.LibraryFolderCacheInventoryStampDiffersOnlyInDirectoryTicks("3|100|42", "3|200|99"));
    }

    [Fact]
    public void IsLibraryFolderCacheMetadataRevisionLine_RecognizesIndexRevisionFormat()
    {
        Assert.True(MainWindow.IsLibraryFolderCacheMetadataRevisionLine("12345|638547890123456789"));
        Assert.True(MainWindow.IsLibraryFolderCacheMetadataRevisionLine("missing|0"));
        Assert.False(MainWindow.IsLibraryFolderCacheMetadataRevisionLine("a\tb"));
        Assert.False(MainWindow.IsLibraryFolderCacheMetadataRevisionLine("onlyPart"));
    }

    [Fact]
    public void SerializeAndParseLibraryFolderCacheRecord_PreservesCompletionFields()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PixelVault-Native-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var mediaPath = Path.Combine(tempRoot, "capture.png");
        File.WriteAllBytes(mediaPath, new byte[] { 1, 2, 3, 4 });

        try
        {
            var folder = new LibraryFolderInfo
            {
                GameId = "game-123",
                FolderPath = tempRoot,
                Name = "Test Game",
                FileCount = 1,
                PreviewImagePath = mediaPath,
                PlatformLabel = "Steam",
                FilePaths = new[] { mediaPath },
                NewestCaptureUtcTicks = 12345L,
                NewestRecentSortUtcTicks = 67890L,
                SteamAppId = "999",
                SteamGridDbId = "abc",
                IsCompleted100Percent = true,
                CompletedUtcTicks = 777L
            };

            var line = MainWindow.SerializeLibraryFolderCacheRecordLine(folder);
            var parsed = MainWindow.ParseLibraryFolderCacheRecordLine(tempRoot, line, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            Assert.NotNull(parsed);
            Assert.Equal("game-123", parsed.GameId);
            Assert.True(parsed.IsCompleted100Percent);
            Assert.Equal(777L, parsed.CompletedUtcTicks);
            Assert.Equal(12345L, parsed.NewestCaptureUtcTicks);
            Assert.Equal(67890L, parsed.NewestRecentSortUtcTicks);
            Assert.Single(parsed.FilePaths);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ParseLibraryFolderCacheRecord_OldFormat_DefaultsCompletionFields()
    {
        var line = string.Join("\t", new[]
        {
            "game-123",
            @"C:\Games\Test Game",
            "Test Game",
            "4",
            @"C:\Games\Test Game\cover.png",
            "Steam",
            @"C:\Games\Test Game\a.png|C:\Games\Test Game\b.png",
            "999",
            "abc",
            "12345",
            "67890"
        });

        var parsed = MainWindow.ParseLibraryFolderCacheRecordLine(@"C:\Games", line, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.NotNull(parsed);
        Assert.False(parsed.IsCompleted100Percent);
        Assert.Equal(0L, parsed.CompletedUtcTicks);
        Assert.Equal(12345L, parsed.NewestCaptureUtcTicks);
        Assert.Equal(67890L, parsed.NewestRecentSortUtcTicks);
    }
}
