using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementPlatformWindowTests
{
    static List<GameProfilePlatformChoice> Choices() => new()
    {
        new() { Provider = "steam", Id = "3376990", Title = "Kabuto Park", Folder = new() { Name = "Kabuto Park", SteamAppId = "3376990" } },
        new() { Provider = "xbox", Id = "2026288209", Title = "Kabuto Park", Folder = new() { Name = "Kabuto Park" }, XboxTitle = new() { Name = "Kabuto Park", Id = "2026288209" } }
    };
    static GameAchievementsFetchService.FetchResult Result(string provider, int earned, bool known = true) => new()
    {
        SourceLabel = provider == "xbox" ? "Xbox" : "Steam", GameTitle = "Kabuto Park", CapturedUtcTicks = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc).Ticks,
        Rows = Enumerable.Range(0, 21).Select(i => new GameAchievementsFetchService.AchievementRow
        { Provider = provider, ProviderGameId = provider == "xbox" ? "2026288209" : "3376990", ProviderAchievementId = i.ToString(), Title = "Achievement " + (i + 1), ProgressKnown = known, Unlocked = i < earned }).ToList()
    };
    static T Field<T>(object value, string name) => AchievementCollectionAccessTests.Field<T>(value, name);
    static void Load(AchievementsInfoWindow window) => Field<Button>(window, "_refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Select(AchievementsInfoWindow window, string key)
    {
        var selector = Field<ComboBox>(window, "_platformSelector");
        selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(i => (i.Tag as GameProfilePlatformChoice)?.Key == key);
    }

    [Fact]
    public void SavedXboxDefaultShowsSixteenEarnedAndSwitchPersistsTheNewChoice() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-linked-viewer-" + Guid.NewGuid().ToString("N"));
        var store = new GameProfilePlatformStore(root); store.Save("kabuto", new() { DefaultKey = "xbox:2026288209", XboxTitleId = "2026288209" });
        var snapshot = Result("xbox", 16);
        var calls = new List<(string Key, bool Force)>();
        var window = AchievementsInfoWindow.CreatePlatformWindow(null, "Kabuto Park", Choices(), store.Get("kabuto").DefaultKey,
            (choice, force, token) => { calls.Add((choice.Key, force)); return Task.FromResult(choice.Provider == "xbox" ? snapshot : Result("steam", 0, false)); },
            choice => store.Save("kabuto", new() { DefaultKey = choice.Key, XboxTitleId = "2026288209" }), "steam-user", "retro-user", "xbox-user", null, null);
        try
        {
            Load(window);
            Assert.Same(snapshot, Field<GameAchievementsFetchService.FetchResult>(window, "_loadedResult"));
            Assert.Contains("16 of 21", Field<TextBlock>(window, "_earnedSummary").Text);
            Assert.Equal(("xbox:2026288209", false), calls.Single());
            Assert.Equal("xbox-user", Field<string>(window, "_scoreXboxAccount"));
            Select(window, "steam:3376990");
            Assert.Equal("steam:3376990", new GameProfilePlatformStore(root).Get("kabuto").DefaultKey);
            Assert.Null(Field<string?>(window, "_scoreXboxAccount"));
            Assert.Equal("steam-user", Field<string>(window, "_scoreSteamAccount"));
            Assert.Contains("21 progress unavailable", Field<TextBlock>(window, "_earnedSummary").Text);
            Assert.All(Field<GameAchievementsFetchService.FetchResult>(window, "_loadedResult").Rows, row => Assert.False(row.ProgressKnown));
            Select(window, "xbox:2026288209"); Load(window);
            Assert.Equal(("xbox:2026288209", true), calls.Last());
            Assert.Contains("16 of 21", Field<TextBlock>(window, "_earnedSummary").Text);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    });

    [Fact]
    public void SwitchingCancelsPendingFetchAndLateSteamResultsCannotReplaceXboxOrItsScope() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var pending = new TaskCompletionSource<GameAchievementsFetchService.FetchResult>();
        CancellationToken steamToken = default;
        var window = AchievementsInfoWindow.CreatePlatformWindow(null, "Kabuto Park", Choices(), "steam:3376990",
            (choice, force, token) => { if (choice.Provider == "steam") { steamToken = token; return pending.Task; } return Task.FromResult(Result("xbox", 16)); },
            null, "steam-user", "retro-user", "xbox-user", null, null);
        try
        {
            Load(window); Select(window, "xbox:2026288209");
            Assert.True(steamToken.IsCancellationRequested);
            pending.SetResult(Result("steam", 21));
            Assert.Equal("Xbox", Field<GameAchievementsFetchService.FetchResult>(window, "_loadedResult").SourceLabel);
            Assert.Contains("16 of 21", Field<TextBlock>(window, "_earnedSummary").Text);
            Assert.Null(Field<string?>(window, "_scoreSteamAccount"));
            Assert.True(Field<Button>(window, "_refresh").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void FailedRefreshRetainsSavedUnlocksAndTheirOriginalTimestamp() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var window = AchievementsInfoWindow.CreatePlatformWindow(null, "Kabuto Park", Choices(), "xbox:2026288209",
            (choice, force, token) => Task.FromResult(force ? new GameAchievementsFetchService.FetchResult { ErrorMessage = "Offline" } : Result("xbox", 16)),
            null, "steam-user", "retro-user", "xbox-user", null, null);
        try
        {
            Load(window); var timestamp = Field<TextBlock>(window, "_updated").Text;
            Assert.Contains("2026", timestamp);
            Load(window);
            Assert.Equal(timestamp, Field<TextBlock>(window, "_updated").Text);
            Assert.Contains("16 of 21", Field<TextBlock>(window, "_earnedSummary").Text);
            Assert.Contains("Showing the last loaded status", Field<TextBlock>(window, "_error").Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(680)]
    [InlineData(960)]
    public void PlatformSortAndMissableSelectorsUseTheProfileThemeAndFitTheWindow(int width) => AchievementCollectionAccessTests.OnSta(() =>
    {
        var window = AchievementsInfoWindow.CreatePlatformWindow(null, "Kabuto Park", Choices(), "xbox:2026288209",
            (_, _, _) => Task.FromResult(Result("xbox", 16)), null, "steam-user", "retro-user", "xbox-user", null, null);
        try
        {
            Load(window);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(width - 16, 760)); content.Arrange(new Rect(0, 0, width - 16, 760)); content.UpdateLayout();
            foreach (var field in new[] { "_platformSelector", "_sort", "_missableFilter" })
            {
                var combo = Field<ComboBox>(window, field); Assert.Equal(38, combo.ActualHeight); Assert.NotNull(combo.Template);
                var right = combo.TranslatePoint(new Point(combo.ActualWidth, 0), content).X;
                Assert.True(right <= content.ActualWidth);
            }
            var output = Environment.GetEnvironmentVariable("PIXELVAULT_UI_RENDER_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
                {
                    var rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                    drawing.DrawRectangle(window.Background, null, rect); drawing.DrawRectangle(new VisualBrush(content), null, rect);
                }
                image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(output); using var file = File.Create(Path.Combine(output, "achievement-window-" + width + ".png")); encoder.Save(file);
            }
        }
        finally { window.Close(); }
    });
}
