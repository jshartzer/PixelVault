using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementArtworkImportTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "PV-ArtImport-" + Guid.NewGuid().ToString("N"));
    const string Page = "https://www.exophase.com/game/death-stranding-director-s-cut-psn/trophies/";
    const string Url = "https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg";
    static GameAchievementsFetchService.AchievementRow Row(string game = "1850570") => new()
    {
        Provider = "steam", ProviderGameId = game, ProviderAchievementId = "062", Title = "Homo Faber", Description = "Fabricate all equipment.",
        IconUrlColor = Url, ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 123
    };
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    static string Arg(ProcessStartInfo start, string name) => start.ArgumentList[start.ArgumentList.IndexOf(name) + 1];
    static async Task<AchievementArtworkImport.RunResult> Runner(ProcessStartInfo start, CancellationToken token)
    {
        Assert.False(start.UseShellExecute); Assert.True(start.CreateNoWindow); Assert.Equal(ProcessWindowStyle.Hidden, start.WindowStyle);
        Assert.Contains("--progress", start.ArgumentList);
        var directory = Arg(start, "--output"); var game = Arg(start, "--game");
        var catalogue = await File.ReadAllTextAsync(Arg(start, "--catalogue"), token);
        Assert.DoesNotContain("Unlocked", catalogue); Assert.DoesNotContain("Account", catalogue); Assert.DoesNotContain("UnlockUtcTicks", catalogue);
        using var parsed = JsonDocument.Parse(catalogue); Assert.Single(parsed.RootElement.EnumerateArray());
        Assert.Equal(game, parsed.RootElement[0].GetProperty("ProviderGameId").GetString());
        Assert.True(File.Exists(start.ArgumentList[1])); // The embedded importer is available in published builds.
        var apply = start.ArgumentList.Contains("--apply");
        var matched = new[] { new { title = "Homo Faber", achievementId = "062", originalUrl = Url, sourceUrl = "https://m.exophase.com/psn/awards/l/8714e15.png" } };
        var imported = apply ? new[] { new { title = "Homo Faber", achievementId = "062" } } : Array.Empty<object>();
        var review = apply ? Array.Empty<object>() : new object[] { new { title = "Different edition", reason = "Name did not match." } };
        if (apply)
        {
            using var accepted = JsonDocument.Parse(await File.ReadAllTextAsync(Arg(start, "--mapping"), token));
            Assert.Single(accepted.RootElement.EnumerateArray()); Assert.Equal("062", accepted.RootElement[0].GetProperty("achievementId").GetString());
            var image = AchievementArtwork.Find(Url)!;
            await File.WriteAllBytesAsync(Path.Combine(directory, image.Sha256 + ".png"), image.Bytes, token);
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new[]
            {
                new { provider = "steam", gameId = game, originalUrl = Url, file = image.Sha256 + ".png", sha256 = image.Sha256 }
            }), token);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "import-report.json"), JsonSerializer.Serialize(new
        { gameId = game, sourcePage = Page, mode = apply ? "apply" : "plan", matched, imported, review, failed = Array.Empty<object>() }), token);
        return new(0, "", "");
    }
    [Fact]
    public async Task PreviewExportsOnlySelectedGameDefinitionsAndImportPreservesOtherGamesAndReview()
    {
        var active = Path.Combine(root, "achievement-artwork"); Directory.CreateDirectory(active);
        var unrelated = new { originalUrl = "https://other-game.invalid/keep.png", file = "keep.png" };
        var manifest = Path.Combine(active, "manifest.json"); File.WriteAllText(manifest, JsonSerializer.Serialize(new[] { unrelated }));
        var before = File.ReadAllBytes(manifest);
        var service = new AchievementArtworkImport(root, "1850570", new[] { Row(), Row("other-game") }, Runner);
        Assert.True(service.HasKnownArtwork); Assert.Equal(Page, service.DefaultPage());
        var plan = await service.Preview(Page, "known", "", default);
        Assert.Equal(before, File.ReadAllBytes(manifest)); Assert.Single(plan.Report.Review); Assert.Single(plan.Report.Matched);
        Assert.True(File.Exists(Path.Combine(plan.Directory, "preview-report.json")));
        var report = await service.Import(plan, default);
        Assert.Single(report.Imported); Assert.Single(report.Review);
        using var saved = JsonDocument.Parse(File.ReadAllText(manifest)); Assert.Equal(2, saved.RootElement.GetArrayLength());
        Assert.Contains(saved.RootElement.EnumerateArray(), e => e.GetProperty("originalUrl").GetString() == unrelated.originalUrl);
        Assert.Equal(AchievementArtwork.Find(Url)!.Sha256, AchievementArtwork.Find(Url, root)!.Sha256);
        Assert.Equal(Page, new AchievementArtworkImport(root, "1850570", new[] { Row() }, Runner).DefaultPage());
    }
    [Fact]
    public async Task BlockedSourceAndCancellationLeaveInstalledArtworkAloneAndReportUsefulRecovery()
    {
        var service = new AchievementArtworkImport(root, "1850570", new[] { Row() }, (_, _) => Task.FromResult(new AchievementArtworkImport.RunResult(1, "", "HTTP 403: refused")));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.Preview(Page, "page", "", default));
        Assert.Contains("Saved HTML", error.Message); Assert.False(File.Exists(Path.Combine(root, "achievement-artwork", "manifest.json")));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AchievementArtworkImport(root, "1850570", new[] { Row() }, Runner).Preview(Page, "known", "", cancellation.Token));
        Assert.False(File.Exists(Path.Combine(root, "achievement-artwork", "manifest.json")));
    }
    [Theory]
    [InlineData("http://www.exophase.com/game/test/trophies/")]
    [InlineData("https://unrelated.invalid/game/test/trophies/")]
    [InlineData("https://www.exophase.com@unrelated.invalid/game/test/trophies/")]
    public async Task SourceMustBeAnActualPublicGameTrophyPage(string page)
    {
        var service = new AchievementArtworkImport(root, "1850570", new[] { Row() }, (_, _) => throw new Exception("Must not launch"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.Preview(page, "page", "", default));
    }
    [Fact]
    public void ProfileButtonAndDialogRunPreviewImportAndRefreshWithoutCommandLines() => AchievementCollectionAccessTests.OnSta(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var clicked = false; var button = MainWindow.BuildGameProfileArtworkButton(() => clicked = true);
        Assert.Equal("Import achievement artwork", AutomationProperties.GetName(button)); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(clicked);
        var refreshed = false;
        var window = new AchievementArtworkImportWindow(null!, "Death Stranding", new AchievementArtworkImport(root, "1850570", new[] { Row() }, Runner), () => refreshed = true);
        try
        {
            var source = Field<ComboBox>(window, "source");
            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "known"));
            Assert.False(Field<Button>(window, "install").IsEnabled);
            Field<Button>(window, "preview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window.Pending);
            Assert.True(Field<Button>(window, "install").IsEnabled); Assert.Contains("Different edition", Field<TextBox>(window, "results").Text);
            Field<Button>(window, "install").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window.Pending);
            Assert.True(refreshed); Assert.Contains("Imported: Homo Faber", Field<TextBox>(window, "results").Text); Assert.False(Field<Button>(window, "install").IsEnabled);
            Assert.False(Field<ProgressBar>(window, "progress").IsIndeterminate); Assert.Equal(100, Field<ProgressBar>(window, "progress").Value);
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"matched\":6}")]
    [InlineData("{\"type\":\"progress\",\"stage\":\"images\",\"completed\":7,\"total\":6}")]
    [InlineData("{\"type\":\"progress\",\"stage\":\"images\",\"completed\":-1,\"total\":6}")]
    public void InvalidOrSummaryOutputIsNotProgress(string line) => Assert.Null(AchievementArtworkImport.ParseProgress(line));
    [Fact]
    public void ProgressLinesRetainTheCountTitleAndPhase()
    {
        var update = AchievementArtworkImport.ParseProgress("{\"type\":\"progress\",\"stage\":\"images\",\"completed\":2,\"total\":6,\"title\":\"Homo Faber\",\"message\":\"Checking original image\"}");
        Assert.NotNull(update); Assert.Equal(2, update.Completed); Assert.Equal(6, update.Total); Assert.Equal("Homo Faber", update.Title);
    }
    [Fact]
    public void RunningImportShowsLiveCountsAndStopEndsAnimationWithoutClaimingCompletion() => AchievementCollectionAccessTests.OnSta(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AchievementArtworkImport.RunResult> Slow(ProcessStartInfo start, CancellationToken token)
        {
            if (start.ArgumentList.Contains("--apply")) { started.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); }
            return await Runner(start, token);
        }
        var window = new AchievementArtworkImportWindow(null!, "Death Stranding", new AchievementArtworkImport(root, "1850570", new[] { Row() }, Slow));
        try
        {
            var source = Field<ComboBox>(window, "source"); source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "known"));
            Field<Button>(window, "preview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window.Pending);
            Field<Button>(window, "install").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(started.Task);
            var bar = Field<ProgressBar>(window, "progress"); Assert.Equal(Visibility.Visible, bar.Visibility); Assert.True(bar.IsIndeterminate);
            var handler = window.GetType().GetMethod("OnProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
            handler.Invoke(window, new object[] { new AchievementArtworkImport.ProgressUpdate("images", 2, 6, "Homo Faber", "Downloading artwork") });
            Assert.False(bar.IsIndeterminate); Assert.InRange(bar.Value, 1, 99);
            Assert.Contains("2 / 6", Field<TextBlock>(window, "progressText").Text); Assert.Contains("Homo Faber", Field<TextBlock>(window, "progressText").Text);
            Field<Button>(window, "close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window.Pending);
            Assert.False(bar.IsIndeterminate); Assert.True(bar.Value < 100); Assert.Equal("Stopped", Field<TextBlock>(window, "progressText").Text);
            handler.Invoke(window, new object[] { new AchievementArtworkImport.ProgressUpdate("images", 6, 6, "Late event", "Done") });
            Assert.Equal("Stopped", Field<TextBlock>(window, "progressText").Text);
            Assert.False(File.Exists(Path.Combine(root, "achievement-artwork", "manifest.json")));
        }
        finally { window.Close(); }
    });
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
