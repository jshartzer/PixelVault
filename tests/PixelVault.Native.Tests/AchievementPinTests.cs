using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementPinTests
{
    [Fact]
    public void Pins_PersistInInsertionOrderAcrossRestartImportAndDefinitionSync()
    {
        using var scope = new PinScope();
        var rows = new[] { Row("A"), Row("B"), Row("C"), Row("D") };
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows);
        Assert.Equal(AchievementPinChange.Changed, scope.Service.SetAchievementPinned(rows[2], true));
        scope.Service.SetAchievementPinned(rows[0], true);
        scope.Service.ImportGuideBundle("""
            {"schemaVersion":1,"provider":"steam","providerGameId":"123","achievements":[
                {"providerAchievementId":"C","guideText":"Keep this route.","tags":["collectible"],"groupName":"Expansion"}]}
            """, "steam", "123");
        var restarted = new AchievementGuideService(scope.Path);
        restarted.SyncDefinitionsAndLoadGuides("fixture", rows.Reverse());
        var pins = restarted.LoadPinnedAchievements(" STEAM ", " 123 ");
        Assert.Equal(new[] { "C", "A" }, pins.Select(pin => pin.ProviderAchievementId));
        Assert.Equal("Keep this route.", pins[0].GuideText);
        Assert.Equal("Expansion", pins[0].GroupName);
    }

    [Fact]
    public void ThreePinLimit_IsPerProviderGameAndUnpinReleasesSlot()
    {
        using var scope = new PinScope();
        var rows = new[] { Row("A"), Row("B"), Row("C"), Row("D"), Row("A", "456"), Row("A", "123", "retroachievements") };
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows);
        foreach (var row in rows.Take(3)) scope.Service.SetAchievementPinned(row, true);
        Assert.Equal(AchievementPinChange.Unchanged, scope.Service.SetAchievementPinned(rows[0], true));
        Assert.Equal(AchievementPinChange.LimitReached, scope.Service.SetAchievementPinned(rows[3], true));
        Assert.Equal(AchievementPinChange.Changed, scope.Service.SetAchievementPinned(rows[4], true));
        Assert.Equal(AchievementPinChange.Changed, scope.Service.SetAchievementPinned(rows[5], true));
        scope.Service.SetAchievementPinned(rows[1], false);
        Assert.Equal(AchievementPinChange.Changed, scope.Service.SetAchievementPinned(rows[3], true));
        Assert.Equal(new[] { "A", "C", "D" }, scope.Service.LoadPinnedAchievements("steam", "123").Select(p => p.ProviderAchievementId));
        Assert.Single(scope.Service.LoadPinnedAchievements("steam", "456"));
        Assert.Single(scope.Service.LoadPinnedAchievements("retroachievements", "123"));
    }

    [Theory]
    [InlineData(false, false, "ProgressUnavailable")]
    [InlineData(false, true, "ProgressUnavailable")]
    [InlineData(true, true, "AlreadyEarned")]
    public void NewPins_RequireKnownUnfinishedProgress(bool known, bool earned, string expected)
    {
        using var scope = new PinScope();
        var row = Row("A");
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { row });
        row.ProgressKnown = known;
        row.Unlocked = earned;
        Assert.Equal(expected, scope.Service.SetAchievementPinned(row, true).ToString());
        Assert.Empty(scope.Service.LoadPinnedAchievements("steam", "123"));
    }

    [Fact]
    public void Reconcile_RemovesOnlyConfirmedEarnedPinsAndNeverChangesGuides()
    {
        using var scope = new PinScope();
        var rows = new[] { Row("A"), Row("B"), Row("C"), Row("A", "456") };
        var entries = scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows);
        scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entries[0].AchievementId, GuideText = "Do not lose this." });
        foreach (var row in rows) scope.Service.SetAchievementPinned(row, true);
        rows[0].Unlocked = true;
        rows[1].ProgressKnown = false;
        rows[1].Unlocked = true;
        Assert.Equal(1, scope.Service.ReconcileAchievementPins(new[] { rows[0], rows[1] }));
        Assert.Equal(new[] { "B", "C" }, scope.Service.LoadPinnedAchievements("steam", "123").Select(p => p.ProviderAchievementId));
        Assert.Single(scope.Service.LoadPinnedAchievements("steam", "456"));
        Assert.Equal("Do not lose this.", scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows)[0].GuideText);
        Assert.Equal(0, scope.Service.ReconcileAchievementPins(null!));
    }

    [Fact]
    public void MissingDefinitions_KeepPinsAndRemainRecoverableAndUnpinnable()
    {
        using var scope = new PinScope();
        var a = Row("A");
        var b = Row("B");
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b });
        scope.Service.SetAchievementPinned(a, true);
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { b });
        scope.Service.ReconcileAchievementPins(new[] { b });
        var pin = Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123"));
        Assert.False(pin.IsActive);
        Assert.Equal("A", pin.ProviderAchievementId);
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b });
        Assert.True(Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123")).IsActive);
        a.ProgressKnown = false;
        Assert.Equal(AchievementPinChange.Changed, scope.Service.SetAchievementPinned(a, false));
    }

    [Fact]
    public void ConflictingDuplicateProgress_DoesNotRemovePin()
    {
        using var scope = new PinScope();
        var row = Row("A");
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { row });
        scope.Service.SetAchievementPinned(row, true);
        var earned = Row("A"); earned.Unlocked = true;
        Assert.Equal(0, scope.Service.ReconcileAchievementPins(new[] { row, earned }));
        Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123"));
    }

    [Fact]
    public void AdditiveMigrationAndRollingBackup_IncludePinsWithoutLosingExistingData()
    {
        using var scope = new PinScope();
        var row = Row("A");
        var entry = scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { row })[0];
        scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GuideText = "Existing notes." });
        // Represents the immediately preceding schema: definitions/guides exist, the new table does not.
        using (var connection = new SqliteConnection("Data Source=" + scope.Service.DatabasePath))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE achievement_pin;";
            command.ExecuteNonQuery();
        }
        var restarted = new AchievementGuideService(scope.Path);
        Assert.Empty(restarted.LoadPinnedAchievements("steam", "123"));
        Assert.Equal(AchievementPinChange.Changed, restarted.SetAchievementPinned(row, true));
        Assert.Equal("Existing notes.", Assert.Single(restarted.LoadPinnedAchievements("steam", "123")).GuideText);
        var backup = Directory.GetFiles(System.IO.Path.Combine(scope.Path, "guides", "backups"), "*.sqlite").Order().Last();
        using var saved = new SqliteConnection("Data Source=" + backup + ";Mode=ReadOnly");
        saved.Open();
        using var verify = saved.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM achievement_pin;";
        Assert.Equal(1L, verify.ExecuteScalar());
        verify.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", verify.ExecuteScalar());
    }

    [Fact]
    public async Task ConcurrentPinRequests_CannotExceedThree()
    {
        using var scope = new PinScope();
        var rows = Enumerable.Range(1, 6).Select(i => Row(i.ToString())).ToArray();
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows);
        var results = await Task.WhenAll(rows.Select(row => Task.Run(() => new AchievementGuideService(scope.Path).SetAchievementPinned(row, true))));
        Assert.Equal(3, results.Count(result => result == AchievementPinChange.Changed));
        Assert.Equal(3, results.Count(result => result == AchievementPinChange.LimitReached));
        Assert.Equal(3, scope.Service.LoadPinnedAchievements("steam", "123").Count);
    }

    internal static GameAchievementsFetchService.AchievementRow Row(string id, string game = "123", string provider = "steam") =>
        new() { Provider = provider, ProviderGameId = game, ProviderAchievementId = id, Title = "Target " + id, ProgressKnown = true };

    internal sealed class PinScope : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PixelVault-pin-tests-" + Guid.NewGuid().ToString("N"));
        public AchievementGuideService Service { get; }
        public PinScope() => Service = new AchievementGuideService(Path);
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
