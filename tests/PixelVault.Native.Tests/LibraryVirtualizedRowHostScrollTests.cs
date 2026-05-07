#nullable enable
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryVirtualizedRowHostScrollTests
    {
        [Fact]
        public void ShouldRefreshVirtualizedRowHostImmediatelyForScroll_DefersOrdinaryWheelDeltas()
        {
            Assert.False(MainWindow.ShouldRefreshVirtualizedRowHostImmediatelyForScroll(
                verticalChange: 120,
                horizontalChange: 0,
                viewportHeight: 900,
                viewportWidth: 1200));
        }

        [Fact]
        public void ShouldRefreshVirtualizedRowHostImmediatelyForScroll_RefreshesPageSizedVerticalJumps()
        {
            Assert.True(MainWindow.ShouldRefreshVirtualizedRowHostImmediatelyForScroll(
                verticalChange: 792,
                horizontalChange: 0,
                viewportHeight: 900,
                viewportWidth: 1200));
        }

        [Fact]
        public void ShouldRefreshVirtualizedRowHostImmediatelyForScroll_RefreshesPageSizedHorizontalJumps()
        {
            Assert.True(MainWindow.ShouldRefreshVirtualizedRowHostImmediatelyForScroll(
                verticalChange: 0,
                horizontalChange: -1056,
                viewportHeight: 900,
                viewportWidth: 1200));
        }
    }
}
