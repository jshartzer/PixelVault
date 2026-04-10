using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>LIBST: saved Game Index row title must drive <see cref="ManualMetadataItem.GameName"/> when index <c>GameId</c> resolves—never a conflicting filename stem (re-home / organize).</summary>
public sealed class LibraryMetadataGameNameRegressionTests
{
    [Fact]
    public void ResolveManualMetadataGameName_UsesSavedRowTitle_WhenRowResolved()
    {
        var row = new GameIndexEditorRow { GameId = "G00002", Name = "Portal", PlatformLabel = "Steam", FolderPath = "" };
        var name = MainWindow.ResolveManualMetadataGameName(
            row,
            guessedFromPathAndName: "Hades_Loading_Screen",
            folderPathUsable: false,
            fileName: "Hades_Loading_Screen.png",
            normalizeGameIndexName: (n, _) => (n ?? string.Empty).Trim(),
            getGameNameFromFileName: fn => Path.GetFileNameWithoutExtension(fn ?? string.Empty));
        Assert.Equal("Portal", name);
    }

    [Fact]
    public void ResolveManualMetadataGameName_FallsBackToGuess_WithoutSavedRow()
    {
        var name = MainWindow.ResolveManualMetadataGameName(
            savedGameRow: null,
            guessedFromPathAndName: "SomeScreenshot",
            folderPathUsable: false,
            fileName: "SomeScreenshot.png",
            normalizeGameIndexName: (n, _) => (n ?? string.Empty).Trim(),
            getGameNameFromFileName: fn => Path.GetFileNameWithoutExtension(fn ?? string.Empty));
        Assert.Equal("SomeScreenshot", name);
    }

    [Fact]
    public void ResolveManualMetadataGameName_SavedRowWins_OverGuess_WhenFolderPathUsable()
    {
        var row = new GameIndexEditorRow { GameId = "G1", Name = "Portal", PlatformLabel = "Steam", FolderPath = @"D:\lib\Portal" };
        var name = MainWindow.ResolveManualMetadataGameName(
            row,
            guessedFromPathAndName: "TimelineLabel",
            folderPathUsable: true,
            fileName: "x.png",
            normalizeGameIndexName: (n, _) => (n ?? string.Empty).Trim(),
            getGameNameFromFileName: fn => Path.GetFileNameWithoutExtension(fn ?? string.Empty));
        Assert.Equal("Portal", name);
    }
}
