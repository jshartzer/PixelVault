#nullable enable
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    public class SteamGridDbAssetPickerPreviewTests
    {
        [Theory]
        [InlineData("https://cdn.steamgriddb.com/grid/example.png", true)]
        [InlineData("http://cdn.steamgriddb.com/grid/example.png", true)]
        [InlineData("file:///C:/temp/example.png", false)]
        [InlineData(@"C:\temp\example.png", false)]
        [InlineData("", false)]
        public void IsSteamGridDbPickerRemoteImageUrl_OnlyAllowsHttpImages(string url, bool expected)
        {
            Assert.Equal(expected, MainWindow.IsSteamGridDbPickerRemoteImageUrl(url));
        }

        [Fact]
        public void BuildSteamGridDbPickerPreviewImageCacheKey_IncludesDecodeWidth()
        {
            var small = MainWindow.BuildSteamGridDbPickerPreviewImageCacheKey(" https://example.test/art.png ", 280);
            var large = MainWindow.BuildSteamGridDbPickerPreviewImageCacheKey("https://example.test/art.png", 900);

            Assert.Equal("https://example.test/art.png|decode=280", small);
            Assert.Equal("https://example.test/art.png|decode=900", large);
            Assert.NotEqual(small, large);
        }
    }
}
