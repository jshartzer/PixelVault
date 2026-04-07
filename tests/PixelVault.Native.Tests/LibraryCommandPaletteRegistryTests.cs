using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public class LibraryCommandPaletteRegistryTests
{
    [Fact]
    public void ValidateInvariants_DoesNotThrow()
    {
        LibraryCommandPaletteRegistry.ValidateInvariants();
    }

    [Fact]
    public void All_IdsAreUniqueCaseInsensitive()
    {
        LibraryCommandPaletteRegistry.ValidateInvariants();
        var ids = LibraryCommandPaletteRegistry.All.Select(s => s.Id.ToLowerInvariant()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuildHandlerMap_FullContext_BindsEveryRegistryCommand()
    {
        LibraryCommandPaletteRegistry.ValidateInvariants();
        var ctx = new LibraryBrowserPaletteContext
        {
            RefreshLibraryFolders = () => { },
            ClearLibrarySearch = () => { },
            OpenSettings = () => { },
            OpenHealthDashboard = () => { },
            OpenGameIndex = () => { },
            OpenPhotoIndex = () => { },
            OpenFilenameRules = () => { },
            OpenPhotographyGallery = () => { },
            OpenSavedCoversFolder = () => { },
            RunImportQuick = () => { },
            RunImportWithReview = () => { },
            OpenManualIntake = () => { },
            OpenIntakePreview = () => { },
            ExportStarred = () => { },
            RefreshAllCovers = () => { },
            ShowKeyboardShortcuts = () => { },
            SortFoldersAlpha = () => { },
            SortFoldersDateCaptured = () => { },
            SortFoldersDateAdded = () => { },
            SortFoldersMostPhotos = () => { },
            FilterFoldersAll = () => { },
            FilterFolders100Percent = () => { },
            FilterFoldersCrossPlatform = () => { },
            FilterFolders25PlusCaptures = () => { },
            FilterFoldersNeedsSteamAppId = () => { },
            FilterFoldersNoCover = () => { },
            GroupFoldersAllGames = () => { },
            GroupFoldersByConsole = () => { },
            GroupFoldersTimeline = () => { },
            GroupFoldersFolderGrid = () => { }
        };
        var map = LibraryCommandPaletteRegistry.BuildHandlerMap(ctx);
        foreach (var spec in LibraryCommandPaletteRegistry.All)
            Assert.True(map.ContainsKey(spec.Id), "Missing handler for " + spec.Id);
    }
}
