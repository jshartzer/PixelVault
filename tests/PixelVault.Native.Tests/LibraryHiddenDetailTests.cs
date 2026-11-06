using PixelVaultNative;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryHiddenDetailTests
{
    [Fact]
    public void CoverOnlyRender_CancelsPreviousWorkWithoutTouchingPanesOrServices()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            var ws = new MainWindow.LibraryBrowserWorkingSet
            {
                PendingRestoreDetailScrollAfterShow = 320,
                Current = new LibraryBrowserFolderView { ViewKey = "remembered-game" }
            };
            var token = ws.DetailRenderCancellation.BeginRender(1);
            // No pane, library session, or filesystem dependencies: hidden rendering
            // must return before accessing any of them, including capture-group reads.
            typeof(MainWindow).GetMethod("LibraryBrowserRenderSelectedFolderDetail", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host, new object?[] { ws, null, null, null, null, null, null });

            Assert.True(token.IsCancellationRequested);
            Assert.Equal("remembered-game", ws.Current.ViewKey);
            Assert.Equal(320, ws.PendingRestoreDetailScrollAfterShow);
            Assert.Equal(0, ws.DetailRenderSequence);
        });
    }

    [Fact]
    public void LeavingTimeline_CancelsPendingDetailButKeepsPhotoWorkWhenGroupingChanges()
    {
        var ws = new MainWindow.LibraryBrowserWorkingSet { WorkspaceMode = LibraryWorkspaceMode.Timeline };
        var timeline = ws.DetailRenderCancellation.BeginRender(1);
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, "all");
        Assert.True(timeline.IsCancellationRequested);
        Assert.True(ws.IsFolderWorkspaceMode);

        ws.WorkspaceMode = LibraryWorkspaceMode.Photo;
        var photo = ws.DetailRenderCancellation.BeginRender(2);
        MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, "console");
        Assert.False(photo.IsCancellationRequested);
        Assert.True(ws.IsPhotoWorkspaceMode);
        ws.DetailRenderCancellation.Dispose();
    }
}
