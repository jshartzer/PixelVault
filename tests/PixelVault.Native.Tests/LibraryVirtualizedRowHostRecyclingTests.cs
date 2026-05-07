#nullable enable
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryVirtualizedRowHostRecyclingTests
    {
        [Fact]
        public void SelectVirtualizedRowElementCacheKeysToPrune_PreservesVisibleInclusiveRange()
        {
            var pruned = MainWindow.SelectVirtualizedRowElementCacheKeysToPrune(
                new[] { 0, 1, 2, 3, 4, 5 },
                firstVisibleIndex: 2,
                lastVisibleIndex: 4);

            Assert.Equal(new[] { 0, 1, 5 }, pruned);
        }

        [Fact]
        public void SelectVirtualizedRowElementCacheKeysToPrune_HandlesEmptyKeys()
        {
            var pruned = MainWindow.SelectVirtualizedRowElementCacheKeysToPrune(
                Enumerable.Empty<int>(),
                firstVisibleIndex: 2,
                lastVisibleIndex: 4);

            Assert.Empty(pruned);
        }
    }
}
