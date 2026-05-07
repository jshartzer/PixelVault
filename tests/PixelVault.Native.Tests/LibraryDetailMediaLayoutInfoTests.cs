#nullable enable
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class LibraryDetailMediaLayoutInfoTests
    {
        [Theory]
        [InlineData(@"E:\Game Captures\Diablo IV\capture.jxr", false, true)]
        [InlineData(@"E:\Game Captures\Diablo IV\capture.JXR", false, true)]
        [InlineData(@"E:\Game Captures\Diablo IV\capture.png", false, false)]
        [InlineData(@"E:\Game Captures\Diablo IV\capture.jxr", true, false)]
        public void ShouldUseFastLibraryDetailAspectFallback_OnlyUsesJxrImages(string path, bool isVideo, bool expected)
        {
            Assert.Equal(expected, MainWindow.ShouldUseFastLibraryDetailAspectFallback(path, isVideo));
        }

        [Fact]
        public void ApplyFastLibraryDetailAspectFallback_UsesLandscapeScreenshotRatio()
        {
            var info = new LibraryDetailMediaLayoutInfo();

            MainWindow.ApplyFastLibraryDetailAspectFallback(info);

            Assert.Equal(1920, info.PixelWidth);
            Assert.Equal(1080, info.PixelHeight);
        }
    }
}
