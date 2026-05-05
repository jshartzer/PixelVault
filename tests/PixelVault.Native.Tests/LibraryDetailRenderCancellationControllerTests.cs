#nullable enable
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryDetailRenderCancellationControllerTests
    {
        [Fact]
        public void BeginRender_CancelsPreviousRenderToken()
        {
            using var controller = new LibraryDetailRenderCancellationController();

            var first = controller.BeginRender(1);
            Assert.False(first.IsCancellationRequested);
            Assert.True(controller.IsCurrent(1, first));

            var second = controller.BeginRender(2);
            Assert.True(first.IsCancellationRequested);
            Assert.False(second.IsCancellationRequested);
            Assert.False(controller.IsCurrent(1, first));
            Assert.True(controller.IsCurrent(2, second));
        }

        [Fact]
        public void CancelCurrent_CancelsActiveRenderToken()
        {
            using var controller = new LibraryDetailRenderCancellationController();

            var token = controller.BeginRender(7);
            controller.CancelCurrent();

            Assert.True(token.IsCancellationRequested);
            Assert.False(controller.IsCurrent(7, token));
        }

        [Fact]
        public void Dispose_CancelsActiveRenderToken()
        {
            var controller = new LibraryDetailRenderCancellationController();
            var token = controller.BeginRender(3);

            controller.Dispose();

            Assert.True(token.IsCancellationRequested);
            Assert.False(controller.IsCurrent(3, token));
        }
    }
}
