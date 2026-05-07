#nullable enable
using System.Collections.Generic;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryDetailViewportFileOrderTests
    {
        [Fact]
        public void BuildLibraryDetailViewportFileOrder_PrioritizesRowsIntersectingViewport()
        {
            var rows = new List<MainWindow.VirtualizedRowDefinition>
            {
                Row(100, "a"),
                Row(100, "b"),
                Row(100, "c")
            };

            var order = MainWindow.BuildLibraryDetailViewportFileOrder(
                rows,
                new[] { "a", "b", "c", "d" },
                scrollOffset: 100,
                viewportHeight: 100,
                overscanMultiplier: 0,
                minimumOverscan: 0);

            Assert.Equal(new[] { "b" }, order.PrimaryFiles);
            Assert.Equal(new[] { "a", "c", "d" }, order.DeferredFiles);
        }

        [Fact]
        public void BuildLibraryDetailViewportFileOrder_UsesOverscanAndDedupesFallback()
        {
            var rows = new List<MainWindow.VirtualizedRowDefinition>
            {
                Row(100, "a", "b"),
                Row(100, "b", "c"),
                Row(100),
                Row(100, "d")
            };

            var order = MainWindow.BuildLibraryDetailViewportFileOrder(
                rows,
                new[] { "a", "b", "c", "d", "e" },
                scrollOffset: 100,
                viewportHeight: 100,
                overscanMultiplier: 0,
                minimumOverscan: 100);

            Assert.Equal(new[] { "a", "b", "c" }, order.PrimaryFiles);
            Assert.Equal(new[] { "d", "e" }, order.DeferredFiles);
            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, order.AllFiles());
        }

        static MainWindow.VirtualizedRowDefinition Row(double height, params string[] files) =>
            new MainWindow.VirtualizedRowDefinition
            {
                Height = height,
                Files = files.ToList()
            };
    }
}
