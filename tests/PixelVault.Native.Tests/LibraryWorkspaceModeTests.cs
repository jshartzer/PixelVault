using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryWorkspaceModeTests
{
    [Fact]
    public void SyncWorkspaceMode_NullWorkingSet_DoesNotThrow()
    {
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(null!, "all");
    }

    [Fact]
    public void SyncWorkspaceMode_TimelineGrouping_SetsTimeline()
    {
        var ws = new MainWindow.LibraryBrowserWorkingSet();
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, "timeline");
        Assert.True(ws.IsTimelineWorkspaceMode);
        Assert.Equal(LibraryWorkspaceMode.Timeline, ws.WorkspaceMode);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("folders")]
    [InlineData("console")]
    public void SyncWorkspaceMode_NonTimelineFromDefault_SetsFolder(string grouping)
    {
        var ws = new MainWindow.LibraryBrowserWorkingSet();
        Assert.True(ws.IsFolderWorkspaceMode);
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, grouping);
        Assert.True(ws.IsFolderWorkspaceMode);
    }

    [Fact]
    public void SyncWorkspaceMode_PhotoPreserved_WhenGroupingStaysNonTimeline()
    {
        var ws = new MainWindow.LibraryBrowserWorkingSet { WorkspaceMode = LibraryWorkspaceMode.Photo };
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, "console");
        Assert.True(ws.IsPhotoWorkspaceMode);
    }

    [Fact]
    public void SyncWorkspaceMode_Timeline_OverridesPhoto()
    {
        var ws = new MainWindow.LibraryBrowserWorkingSet { WorkspaceMode = LibraryWorkspaceMode.Photo };
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, "timeline");
        Assert.True(ws.IsTimelineWorkspaceMode);
    }
}
