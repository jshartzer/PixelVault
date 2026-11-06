using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

[CollectionDefinition("Achievement artwork root", DisableParallelization = true)]
public sealed class AchievementArtworkRootCollection { }

[Collection("Achievement artwork root")]
public sealed class AchievementArtworkUiTests
{
    const string Url = "https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg";
    static GameAchievementsFetchService.AchievementRow Row(bool known = true, bool unlocked = true) => new()
    {
        Provider = "steam", ProviderGameId = "1850570", ProviderAchievementId = "062", Title = "Homo Faber",
        IconUrlColor = Url, IconUrlGray = "https://artwork-test.invalid/locked.png", ProgressKnown = known, Unlocked = unlocked
    };
    static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Tree(child)) yield return nested;
    }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    static ConcurrentDictionary<string, byte[]> Cache => (ConcurrentDictionary<string, byte[]>)typeof(AchievementsInfoWindow)
        .GetField("AchievementIconCache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    [Theory]
    [InlineData("xbox", "2026288209", 1920, 1080, 704, 284, 512)]
    [InlineData("xbox", "2026288209", 960, 540, 352, 142, 256)]
    [InlineData("xbox", "other", 1920, 1080, 420, 0, 1080)]
    [InlineData("xbox", "2026288209", 1600, 1000, 300, 0, 1000)]
    public void XboxWidescreenCropKeepsTheCenteredBadge(string provider, string game, int width, int height, int x, int y, int side) =>
        Assert.Equal(new Int32Rect(x, y, side, side), AchievementsInfoWindow.XboxAchievementCrop(provider, game, width, height));

    [Theory]
    [InlineData("steam", 1920, 1080)]
    [InlineData("retroachievements", 1920, 1080)]
    [InlineData("xbox", 512, 512)]
    [InlineData("xbox", 256, 512)]
    public void OtherProvidersAndAlreadySquareOrPortraitArtArePreserved(string provider, int width, int height) =>
        Assert.Equal(Int32Rect.Empty, AchievementsInfoWindow.XboxAchievementCrop(provider, "2026288209", width, height));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void XboxCroppingRunsBeforeDownsamplingAndLockedGrayscale(bool unlocked) => AchievementCollectionAccessTests.OnSta(() =>
    {
        // A green badge inside a red widescreen frame makes an incorrect crop visible in the decoded pixels.
        var pixels = new byte[1920 * 1080 * 3];
        for (var y = 0; y < 1080; y++) for (var x = 0; x < 1920; x++)
        {
            var badge = x >= 704 && x < 1216 && y >= 284 && y < 796;
            pixels[(y * 1920 + x) * 3 + (badge ? 1 : 0)] = 255;
        }
        var bitmap = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Rgb24, null, pixels, 1920 * 3);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); var original = stream.ToArray();
        var url = "https://images-eds-ssl.xboxlive.com/image?url=crop-test-" + unlocked;
        Cache[url] = original;
        try
        {
            var row = new GameAchievementsFetchService.AchievementRow { Provider = "xbox", ProviderGameId = "2026288209", IconUrlColor = url, ProgressKnown = true, Unlocked = unlocked };
            var image = new Image(); AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test", 256); Pump();
            var source = Assert.IsAssignableFrom<BitmapSource>(image.Source);
            Assert.Equal(256, source.PixelWidth); Assert.Equal(256, source.PixelHeight);
            Assert.Equal(!unlocked, source is FormatConvertedBitmap);
            var rgb = new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0);
            var pixel = new byte[3]; rgb.CopyPixels(new Int32Rect(128, 128, 1, 1), pixel, 3, 0);
            if (unlocked) Assert.Equal(new byte[] { 0, 255, 0 }, pixel);
            else { Assert.Equal(pixel[0], pixel[1]); Assert.Equal(pixel[1], pixel[2]); }
            Assert.Same(original, Cache[url]);
        }
        finally { Cache.TryRemove(url, out _); }
    });

    [Theory]
    [InlineData("https://images-eds-ssl.xboxlive.com/image?url=icon", 8388608)]
    [InlineData("https://IMAGES-EDS-SSL.XBOXLIVE.COM/image?url=icon", 8388608)]
    [InlineData("https://images-eds-ssl.xboxlive.com.other.invalid/image", 786432)]
    [InlineData("http://images-eds-ssl.xboxlive.com/image", 786432)]
    [InlineData("https://shared.fastly.steamstatic.com/icon.jpg", 786432)]
    public void LargerArtworkAllowanceIsLimitedToTheXboxImageHost(string url, int expected) =>
        Assert.Equal(expected, AchievementsInfoWindow.AchievementIconDownloadLimit(url));

    [Fact]
    public void FullResolutionXboxPngDownloadsAndRendersEarnedAndLockedBadges() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var pixels = new byte[1024 * 1024 * 3]; new Random(42).NextBytes(pixels);
        var bitmap = BitmapSource.Create(1024, 1024, 96, 96, PixelFormats.Rgb24, null, pixels, 1024 * 3);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream(); encoder.Save(png); var bytes = png.ToArray();
        Assert.InRange(bytes.Length, 786433, 8388608);
        const string url = "https://images-eds-ssl.xboxlive.com/image?url=test-large-png";
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(bytes);
        });
        using var web = new TimeoutWebClient();
        Cache[url] = web.DownloadBytesAsync($"http://127.0.0.1:{port}/icon.png", AchievementsInfoWindow.AchievementIconDownloadLimit(url)).GetAwaiter().GetResult();
        server.GetAwaiter().GetResult();
        try
        {
            foreach (var unlocked in new[] { true, false })
            {
                var row = new GameAchievementsFetchService.AchievementRow { Provider = "xbox", IconUrlColor = url, ProgressKnown = true, Unlocked = unlocked };
                var image = new Image(); AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test"); Pump();
                Assert.Equal(Visibility.Visible, image.Visibility);
                Assert.Equal(108, Assert.IsAssignableFrom<BitmapSource>(image.Source).PixelWidth);
                Assert.Equal(!unlocked, image.Source is FormatConvertedBitmap);
            }
        }
        finally { Cache.TryRemove(url, out _); }
    });

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public void CommonLoaderUsesLockedArtUntilKnownEarnedDespiteHighResolutionReplacement(bool known, bool unlocked, bool grayscale) => AchievementCollectionAccessTests.OnSta(() =>
    {
        var row = Row(known, unlocked);
        Cache.TryGetValue(Url, out var priorColor); Cache.TryGetValue(row.IconUrlGray, out var priorGray);
        var locked = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, Enumerable.Range(0, 64 * 64 * 4).Select(i => i % 4 == 3 ? (byte)255 : (byte)111).ToArray(), 64 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(locked)); using var memory = new MemoryStream(); encoder.Save(memory);
        Cache[Url] = memory.ToArray(); Cache[row.IconUrlGray] = memory.ToArray();
        try
        {
            var lockedUrl = row.IconUrlGray;
            if (grayscale) row.IconUrlGray = "";
            var image = new Image();
            AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test", 108);
            Pump();
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(image.Source);
            Assert.Equal(known && unlocked ? 240 : 108, bitmap.PixelWidth);
            Assert.Equal(grayscale, bitmap is FormatConvertedBitmap);
            if (!(known && unlocked))
            {
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                if (!grayscale) Assert.Equal(111, pixels[0]);
                AchievementsInfoWindow.RefreshAchievementArtwork(new Border { Child = image }); Pump();
                Assert.Equal(108, Assert.IsAssignableFrom<BitmapSource>(image.Source).PixelWidth);
                row.ProgressKnown = row.Unlocked = true;
                AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test");
                Assert.Equal(240, Assert.IsType<BitmapImage>(image.Source).PixelWidth);
                // A queued locked download must not overwrite the badge after an unlock.
                row.Unlocked = false;
                AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test");
                row.Unlocked = true;
                AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test"); Pump();
                Assert.Equal(240, Assert.IsType<BitmapImage>(image.Source).PixelWidth);
                row.ProgressKnown = known; row.Unlocked = unlocked;
            }
            row.IconUrlGray = lockedUrl;
            Assert.Equal(Visibility.Visible, image.Visibility);
            Assert.Equal(Url, row.IconUrlColor); Assert.Equal(unlocked, row.Unlocked); Assert.Equal(known, row.ProgressKnown);
        }
        finally
        {
            if (priorColor == null) Cache.TryRemove(Url, out _); else Cache[Url] = priorColor;
            if (priorGray == null) Cache.TryRemove(row.IconUrlGray, out _); else Cache[row.IconUrlGray] = priorGray;
        }
    });

    [Theory]
    [InlineData("BuildAchievementRowCard")]
    [InlineData("BuildAchievementGridCard")]
    public void AchievementListAndGridRenderHighResolutionBadges(string method) => AchievementCollectionAccessTests.OnSta(() =>
    {
        var constructor = typeof(AchievementsInfoWindow).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var window = (AchievementsInfoWindow)constructor.Invoke(new object?[]
        {
            null, "Artwork test", null, null, "Test", (Func<Task<GameAchievementsFetchService.FetchResult>>)(() => Task.FromResult(new GameAchievementsFetchService.FetchResult()))
        });
        try
        {
            var card = (Border)typeof(AchievementsInfoWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { Row(), "Test" })!;
            Assert.Equal(240, Assert.IsType<BitmapImage>(Assert.Single(Tree(card).OfType<Image>()).Source).PixelWidth);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void LargerArtworkAndLowerTitlesFitTheOriginalCardBounds() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var constructor = typeof(AchievementsInfoWindow).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var window = (AchievementsInfoWindow)constructor.Invoke(new object?[] { null, "Artwork test", null, null, "Test", (Func<Task<GameAchievementsFetchService.FetchResult>>)(() => Task.FromResult(new GameAchievementsFetchService.FetchResult())) });
        try
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(11, 17, 22)) };
            foreach (var missable in new[] { false, true })
            {
                var row = Row(); row.Points = 40; row.IsMissable = missable;
                if (missable) row.Title = "An achievement with a much longer title";
                var card = (Border)typeof(AchievementsInfoWindow).GetMethod("BuildAchievementGridCard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { row, "Test" })!;
                panel.Children.Add(card);
            }
            panel.Measure(new Size(328, 184)); panel.Arrange(new Rect(0, 0, 328, 184)); panel.UpdateLayout();
            foreach (var card in panel.Children.OfType<Border>())
            {
                Assert.Equal(154, card.ActualWidth); Assert.Equal(174, card.ActualHeight);
                var image = Assert.Single(Tree(card).OfType<Image>()); Assert.True(image.ActualHeight >= 90);
                var title = Tree(card).OfType<TextBlock>().Single(t => t.Text == ((GameAchievementsFetchService.AchievementRow)card.Tag).Title);
                Assert.True(title.TranslatePoint(new Point(), card).Y >= image.TranslatePoint(new Point(), card).Y + image.ActualHeight);
                Assert.True(title.TranslatePoint(new Point(), card).Y + title.ActualHeight <= card.ActualHeight);
            }
            var output = Environment.GetEnvironmentVariable("PIXELVAULT_ARTWORK_CARD_REVIEW");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var bitmap = new RenderTargetBitmap(328, 184, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(output); encoder.Save(file);
            }
        }
        finally { window.Close(); }
    });
    [Fact]
    public void PhotoTimelineUsesActualArtworkPixelsAndStillOpensTheAchievement() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var row = Row(); var entry = new LibraryTimelineAchievement
        {
            Row = row, Game = new() { Provider = "steam", Id = "1850570", Title = "Death Stranding" }, UnlockLocal = DateTime.Now
        };
        var opened = false;
        var tile = MainWindow.BuildLibraryTimelineAchievementTile(entry, 180, 360, "Today", () => opened = true);
        Assert.Equal(240, Assert.IsType<BitmapImage>(Assert.Single(Tree(tile).OfType<Image>()).Source).PixelWidth);
        Assert.IsType<Button>(tile.Child).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(opened);
    });

    [Fact]
    public void LaterImportsApplyToAllBadgesThroughTheApplicationRootAndBrokenImportsKeepProviderFallback() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "PV-GlobalArt-" + Guid.NewGuid().ToString("N"));
        var oldRoot = AchievementsInfoWindow.AchievementArtworkDataRoot;
        var url = Url.Replace("1850570", "9999999");
        try
        {
            var image = AchievementArtwork.Find(Url)!;
            var folder = Path.Combine(root, "achievement-artwork"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, image.Sha256 + ".png"), image.Bytes);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new[] { new { originalUrl = url, file = image.Sha256 + ".png", sha256 = image.Sha256 } }));
            AchievementsInfoWindow.AchievementArtworkDataRoot = root;
            var row = Row(); row.IconUrlColor = url;
            var badge = new Image(); AchievementsInfoWindow.StartAchievementBadgeDownload(badge, row, "Test");
            Assert.Equal(240, Assert.IsType<BitmapImage>(badge.Source).PixelWidth);
            // A corrupt import must fall back to the existing provider bytes, including after prior success.
            File.WriteAllBytes(Path.Combine(folder, image.Sha256 + ".png"), new byte[] { 1 });
            var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 64 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var memory = new MemoryStream(); encoder.Save(memory);
            Cache[url] = memory.ToArray();
            var fallback = new Image(); AchievementsInfoWindow.StartAchievementBadgeDownload(fallback, row, "Test", 64); Pump();
            Assert.Equal(64, Assert.IsType<BitmapImage>(fallback.Source).PixelWidth);
        }
        finally
        {
            AchievementsInfoWindow.AchievementArtworkDataRoot = oldRoot; Cache.TryRemove(url, out _);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    });

    [Fact]
    public void ImportRefreshUpgradesAnAlreadyVisibleBadgeWithoutReopeningTheView() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "PV-RefreshArt-" + Guid.NewGuid().ToString("N"));
        var oldRoot = AchievementsInfoWindow.AchievementArtworkDataRoot;
        var url = Url.Replace("1850570", "9876543");
        try
        {
            AchievementsInfoWindow.AchievementArtworkDataRoot = root;
            var row = Row(); row.IconUrlColor = url;
            var small = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 64 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(small)); using var memory = new MemoryStream(); encoder.Save(memory); Cache[url] = memory.ToArray();
            var image = new Image(); AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "Test", 64); Pump();
            Assert.Equal(64, Assert.IsType<BitmapImage>(image.Source).PixelWidth);
            var replacement = AchievementArtwork.Find(Url)!; var folder = Path.Combine(root, "achievement-artwork"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, replacement.Sha256 + ".png"), replacement.Bytes);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new[] { new { originalUrl = url, file = replacement.Sha256 + ".png", sha256 = replacement.Sha256 } }));
            AchievementsInfoWindow.RefreshAchievementArtwork(new Border { Child = image });
            Assert.Equal(240, Assert.IsType<BitmapImage>(image.Source).PixelWidth);
        }
        finally { AchievementsInfoWindow.AchievementArtworkDataRoot = oldRoot; Cache.TryRemove(url, out _); if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public void OlderStoredSteamBadgeUrlsResolveToTheSameFullResolutionArt()
    {
        var current = AchievementArtwork.Find(Url)!;
        var legacy = Url.Replace("shared.fastly.steamstatic.com/community_assets", "steamcdn-a.akamaihd.net/steamcommunity/public");
        Assert.Equal(current.Sha256, AchievementArtwork.Find(legacy)!.Sha256);
        Assert.Null(AchievementArtwork.Find(legacy.Replace("steamcdn-a.akamaihd.net", "unrelated.invalid")));
    }
}
