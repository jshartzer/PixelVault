using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryBrowserShowOrchestrationTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldStartFirstPaintTracking_OnlyResetsForTrueNewSessions(bool reuseMainWindow, bool hasLiveWorkingSet, bool expected)
    {
        var actual = MainWindow.LibraryBrowserShowOrchestration.ShouldStartFirstPaintTracking(reuseMainWindow, hasLiveWorkingSet);
        Assert.Equal(expected, actual);
    }
}
