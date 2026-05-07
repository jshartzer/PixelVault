#nullable enable
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryDetailDecodePrioritizationTests
    {
        [Theory]
        [InlineData(100, 100, 100, 300, true)]
        [InlineData(399, 50, 100, 300, true)]
        [InlineData(0, 100, 100, 300, false)]
        [InlineData(400, 100, 100, 300, false)]
        public void LibraryDetailRowShouldPrioritizeDecode_OnlyPrioritizesRowsIntersectingViewport(
            double rowTop,
            double rowHeight,
            double viewportOffset,
            double viewportHeight,
            bool expected)
        {
            Assert.Equal(expected, MainWindow.LibraryDetailRowShouldPrioritizeDecode(
                rowTop,
                rowHeight,
                viewportOffset,
                viewportHeight));
        }
    }
}
