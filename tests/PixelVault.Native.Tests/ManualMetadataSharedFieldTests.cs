using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class ManualMetadataSharedFieldTests
{
    [Fact]
    public void BuildManualMetadataGameTitleChoicesFromGameIndex_UsesOnlyCurrentIndexRows()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { Name = "Portal", FolderPath = @"E:\Game Captures\Portal" },
            new() { Name = "Disco Elysium: The Final Cut", FolderPath = @"E:\Game Captures\Disco Elysium The Final Cut" },
            new() { Name = "portal", FolderPath = @"E:\Game Captures\Old Portal Duplicate" },
            new() { Name = "  ", FolderPath = @"E:\Game Captures\Deleted Game" }
        };

        var choices = MainWindow.BuildManualMetadataGameTitleChoicesFromGameIndex(
            rows,
            (name, _) => (name ?? string.Empty).Trim());

        Assert.Equal(new[] { "Disco Elysium: The Final Cut", "Portal" }, choices);
        Assert.DoesNotContain("Deleted Game", choices);
    }

    [Fact]
    public void BuildManualMetadataFolderGroups_GroupsImportItemsBySourceFolder()
    {
        var items = new List<ManualMetadataItem>
        {
            new() { FilePath = @"E:\Uploads\Steam\shot-1.png", FileName = "shot-1.png" },
            new() { FilePath = @"E:\Uploads\PC\shot-2.png", FileName = "shot-2.png" },
            new() { FilePath = @"E:\Uploads\Steam\shot-3.png", FileName = "shot-3.png" }
        };

        var groups = MainWindow.BuildManualMetadataFolderGroups(items);

        Assert.Equal(2, groups.Count);
        Assert.Equal(@"E:\Uploads\Steam", groups[0].FolderPath);
        Assert.Equal(new[] { "shot-1.png", "shot-3.png" }, groups[0].Items.Select(item => item.FileName).ToArray());
        Assert.Equal(@"E:\Uploads\PC", groups[1].FolderPath);
        Assert.Equal(new[] { "shot-2.png" }, groups[1].Items.Select(item => item.FileName).ToArray());
    }

    [Fact]
    public void ShouldFlushManualMetadataSharedTextField_MixedSelectionWithBlankSharedText_DoesNotFlush()
    {
        var items = new List<ManualMetadataItem>
        {
            new() { GameName = "Disco Elysium: The Final Cut" },
            new() { GameName = "Roblox" }
        };

        var shouldFlush = MainWindow.ShouldFlushManualMetadataSharedTextField(items, string.Empty, item => item.GameName);

        Assert.False(shouldFlush);
    }

    [Fact]
    public void ShouldFlushManualMetadataSharedTextField_NonBlankSharedText_FlushesAcrossMixedSelection()
    {
        var items = new List<ManualMetadataItem>
        {
            new() { TagText = "Game Capture, Steam" },
            new() { TagText = "Game Capture, PC" }
        };

        var shouldFlush = MainWindow.ShouldFlushManualMetadataSharedTextField(items, "Game Capture, PC", item => item.TagText);

        Assert.True(shouldFlush);
    }

    [Fact]
    public void ShouldFlushManualMetadataSharedTextField_SingleBlankSelection_AllowsIntentionalClear()
    {
        var items = new List<ManualMetadataItem>
        {
            new() { SteamAppId = "632470" }
        };

        var shouldFlush = MainWindow.ShouldFlushManualMetadataSharedTextField(items, string.Empty, item => item.SteamAppId);

        Assert.True(shouldFlush);
    }
}
