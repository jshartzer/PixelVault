#nullable enable
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryDetailRenderGuardTests
    {
        [Fact]
        public void CanApply_ReturnsTrueForCurrentRenderAndMatchingSelection()
        {
            using var controller = new LibraryDetailRenderCancellationController();
            var token = controller.BeginRender(4);

            Assert.True(LibraryDetailRenderGuard.CanApply(controller, 4, token, 4, true));
        }

        [Fact]
        public void CanApply_ReturnsFalseWhenRenderTokenIsStale()
        {
            using var controller = new LibraryDetailRenderCancellationController();
            var staleToken = controller.BeginRender(4);
            controller.BeginRender(5);

            Assert.False(LibraryDetailRenderGuard.CanApply(controller, 4, staleToken, 4, true));
        }

        [Fact]
        public void CanApply_ReturnsFalseWhenActiveRenderVersionChanged()
        {
            using var controller = new LibraryDetailRenderCancellationController();
            var token = controller.BeginRender(4);

            Assert.False(LibraryDetailRenderGuard.CanApply(controller, 4, token, 5, true));
        }

        [Fact]
        public void CanApply_ReturnsFalseWhenSelectionNoLongerMatches()
        {
            using var controller = new LibraryDetailRenderCancellationController();
            var token = controller.BeginRender(4);

            Assert.False(LibraryDetailRenderGuard.CanApply(controller, 4, token, 4, false));
        }
    }
}
