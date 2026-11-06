using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class XboxAchievementIntegrationTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task BulkFetchExposesProviderFailureWhileInteractiveFetchKeepsSavedProgress(int status)
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-xbox-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AchievementHubStore(root);
            await store.SaveGameAsync("xbox", "111", "123", Result(true), default);
            var message = $"Xbox returned HTTP {status}. Try again later.";
            var service = new XboxAchievementService(store, (_, _) => throw new IOException(message));
            var title = new XboxAchievementService.XboxTitle { Id = "123", Name = "Saved game" };
            var saved = await service.FetchAsync("111", title, default);
            Assert.True(saved.Rows.Single().Unlocked);
            Assert.Contains(message, saved.ProgressErrorMessage);
            var error = await Assert.ThrowsAsync<IOException>(() => service.FetchAsync("111", title, default, useSavedOnError: false));
            Assert.Equal(message, error.Message);
            Assert.True((await store.LoadGameAsync("xbox", "111", "123", default)).Rows.Single().Unlocked);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("[{\"SortKey\":4294967295}]")]
    [InlineData("[null]")]
    [InlineData("null")]
    public void MalformedRowsBecomeRecoverablePerGameFailures(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var error = Assert.Throws<InvalidDataException>(() => XboxAchievementService.ParseRows(document.RootElement, "Example game"));
        Assert.Contains("Example game", error.Message);
        Assert.Contains("Saved progress was retained", error.Message);
    }

    [Fact]
    public async Task XboxAwardsJoinHubTotalsAndAreIdempotentAndAccountScoped()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-xbox-scores-" + Guid.NewGuid().ToString("N"));
        try
        {
            var guides = new AchievementGuideService(root);
            var store = new AchievementHubStore(Path.Combine(root,"hub"), guides);
            var result = Result(true); result.Rows[0].GlobalUnlockPercent = 1;
            await store.SaveGameAsync("xbox", "111", "123", result, default);
            await store.SaveGameAsync("xbox", "111", "123", result, default);
            var data = await store.LoadAsync("xbox", "111", default);
            var game = new AchievementHubGame { Provider="xbox", Id="123", Title="Xbox game" };
            var collection = AchievementHubCollection.Build(new[]{game},new Dictionary<string,AchievementHubStore.AccountData>{{"xbox",data}});
            Assert.Equal(40,collection.EarnedPoints);
            Assert.Equal(1,collection.Earned);
            Assert.Empty(guides.LoadAchievementScoreTotals("222"));
            var session = new AchievementHubSession { SteamAccount="steam-user",RetroAccount="retro-user",XboxAccount="111" };
            Assert.Equal("111",session.Account("xbox"));
            Assert.NotEqual(session.Scope,new AchievementHubSession { SteamAccount="steam-user",RetroAccount="retro-user",XboxAccount="222" }.Scope);
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } }
    }
    static GameAchievementsFetchService.FetchResult Result(bool unlocked, bool known = true) => new()
    {
        CapturedUtcTicks = DateTime.UtcNow.Ticks,
        Rows = new() { new() { Provider = "xbox", ProviderGameId = "123", ProviderAchievementId = "1",
            Title = "Example", ProgressKnown = known, Unlocked = unlocked } }
    };

    [Fact]
    public async Task XboxSnapshotsAreIsolatedByAccountAndProvider()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pv-xbox-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AchievementHubStore(directory);
            await store.SaveGameAsync("xbox", "111", "123", Result(true), default);
            await store.SaveGameAsync("xbox", "222", "123", Result(false), default);
            Assert.True((await store.LoadGameAsync("xbox", "111", "123", default)).Rows.Single().Unlocked);
            Assert.False((await store.LoadGameAsync("xbox", "222", "123", default)).Rows.Single().Unlocked);
            Assert.Null(await store.LoadGameAsync("retroachievements", "111", "123", default));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task UnknownXboxProgressPreservesSavedSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pv-xbox-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AchievementHubStore(directory);
            await store.SaveGameAsync("xbox", "111", "123", Result(true), default);
            await store.SaveGameAsync("xbox", "111", "123", Result(false, false), default);
            Assert.True((await store.LoadGameAsync("xbox", "111", "123", default)).Rows.Single().Unlocked);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
