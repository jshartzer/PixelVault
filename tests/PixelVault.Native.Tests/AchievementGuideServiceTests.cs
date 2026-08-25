using System;
using System.IO;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementGuideServiceTests
{
    [Fact]
    public void SyncAndSave_PreservesGuideWhenProviderMetadataRefreshes()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        var initial = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.PORTAL", "Original title", "Original description")
        }).Single();

        service.SaveGuide(new AchievementGuideEdit
        {
            AchievementId = initial.AchievementId,
            GuideText = "Complete the chamber without moving the cube.",
            SourceUrl = "https://example.com/portal-guide",
            SourceTitle = "Portal guide",
            Tags = "missable, Puzzle, missable",
            IsMissable = true
        });

        var refreshed = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.PORTAL", "Localized title", "Updated official description")
        }).Single();

        Assert.Equal(initial.AchievementId, refreshed.AchievementId);
        Assert.Equal("Localized title", refreshed.Title);
        Assert.Equal("Updated official description", refreshed.Description);
        Assert.Equal("Complete the chamber without moving the cube.", refreshed.GuideText);
        Assert.Equal("https://example.com/portal-guide", refreshed.SourceUrl);
        Assert.Equal("missable, Puzzle", refreshed.Tags);
        Assert.True(refreshed.IsMissable);
        Assert.NotEmpty(Directory.EnumerateFiles(
            System.IO.Path.Combine(scope.Path, "guides", "backups"),
            "pixelvault-guides-*.sqlite"));
    }

    [Fact]
    public void Sync_MarksOmittedProviderAchievementsInactiveWithoutDeletingTheirGuides()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        var initial = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", ""),
            SteamRow("620", "ACH.TWO", "Two", "")
        });
        var omitted = initial.Single(entry => entry.ProviderAchievementId == "ACH.TWO");
        service.SaveGuide(new AchievementGuideEdit { AchievementId = omitted.AchievementId, GuideText = "Keep this." });

        service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", "")
        });
        var restored = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", ""),
            SteamRow("620", "ACH.TWO", "Two", "")
        }).Single(entry => entry.ProviderAchievementId == "ACH.TWO");

        Assert.Equal(omitted.AchievementId, restored.AchievementId);
        Assert.Equal("Keep this.", restored.GuideText);
        Assert.True(restored.IsActive);
    }

    [Fact]
    public void DatabasePath_IsDurableGuidesFolderOutsideCache()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);

        Assert.Equal(
            System.IO.Path.Combine(scope.Path, "guides", "pixelvault-guides.sqlite"),
            service.DatabasePath);
        Assert.DoesNotContain(
            System.IO.Path.DirectorySeparatorChar + "cache" + System.IO.Path.DirectorySeparatorChar,
            service.DatabasePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveGuide_ValidatesSourceUrlAndCreatesBackupAfterAuthoredWrite()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        var entry = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", "")
        }).Single();
        var backupRoot = System.IO.Path.Combine(scope.Path, "guides", "backups");
        var backupsBeforeSave = Directory.EnumerateFiles(backupRoot, "pixelvault-guides-*.sqlite").Count();

        Assert.Throws<ArgumentException>(() => service.SaveGuide(new AchievementGuideEdit
        {
            AchievementId = entry.AchievementId,
            GuideText = "Guide",
            SourceUrl = "not-a-url"
        }));
        Assert.Equal(backupsBeforeSave, Directory.EnumerateFiles(backupRoot, "pixelvault-guides-*.sqlite").Count());

        service.SaveGuide(new AchievementGuideEdit
        {
            AchievementId = entry.AchievementId,
            GuideText = "Guide",
            SourceUrl = "https://example.com/guide"
        });

        Assert.Equal(backupsBeforeSave + 1, Directory.EnumerateFiles(backupRoot, "pixelvault-guides-*.sqlite").Count());
    }

    [Fact]
    public void GuideBundle_PreviewsAndImportsMatchedChangesTransactionally()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        var catalog = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", ""),
            SteamRow("620", "ACH.TWO", "Two", "")
        });
        var first = catalog.Single(entry => entry.ProviderAchievementId == "ACH.ONE");
        service.SaveGuide(new AchievementGuideEdit
        {
            AchievementId = first.AchievementId,
            GuideText = "Already written.",
            SourceUrl = "https://example.com/main",
            SourceTitle = "Main guide",
            Tags = "story",
            IsMissable = false
        });
        var json = """
        {
          "schemaVersion": 1,
          "provider": "steam",
          "providerGameId": "620",
          "sourceUrl": "https://example.com/main",
          "sourceTitle": "Main guide",
          "achievements": [
            {
              "providerAchievementId": "ACH.ONE",
              "guideText": "Already written.",
              "tags": ["story"],
              "isMissable": false
            },
            {
              "providerAchievementId": "ACH.TWO",
              "guideText": "Do this before the final chamber.",
              "tags": ["missable", "puzzle"],
              "isMissable": true,
              "sourceUrl": "https://example.com/two",
              "sourceTitle": "Specific solution"
            },
            {
              "providerAchievementId": "ACH.UNKNOWN",
              "guideText": "This should remain unmatched.",
              "tags": [],
              "isMissable": false
            }
          ]
        }
        """;

        var preview = service.PreviewGuideImport(json, "steam", "620");

        Assert.True(preview.CanImport);
        Assert.Equal(3, preview.RequestedCount);
        Assert.Equal(2, preview.MatchedCount);
        Assert.Equal(1, preview.ChangedCount);
        Assert.Equal(1, preview.UnchangedCount);
        Assert.Equal(new[] { "ACH.UNKNOWN" }, preview.UnmatchedAchievementIds);

        var imported = service.ImportGuideBundle(json, "steam", "620");
        var refreshed = service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", ""),
            SteamRow("620", "ACH.TWO", "Two", "")
        }).Single(entry => entry.ProviderAchievementId == "ACH.TWO");

        Assert.Equal(1, imported.ImportedCount);
        Assert.Equal(1, imported.UnchangedCount);
        Assert.Equal("Do this before the final chamber.", refreshed.GuideText);
        Assert.Equal("https://example.com/two", refreshed.SourceUrl);
        Assert.Equal("Specific solution", refreshed.SourceTitle);
        Assert.Equal("missable, puzzle", refreshed.Tags);
        Assert.True(refreshed.IsMissable);
    }

    [Fact]
    public void GuideBundle_RejectsOpenGameIdentityMismatchAndUnsupportedSchema()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        service.SyncDefinitionsAndLoadGuides("pv-game", new[]
        {
            SteamRow("620", "ACH.ONE", "One", "")
        });
        var json = """
        {
          "schemaVersion": 2,
          "provider": "steam",
          "providerGameId": "999",
          "achievements": [
            { "providerAchievementId": "ACH.ONE", "guideText": "Guide", "tags": [], "isMissable": false }
          ]
        }
        """;

        var preview = service.PreviewGuideImport(json, "steam", "620");

        Assert.False(preview.IsValid);
        Assert.Contains(preview.ValidationErrors, error => error.Contains("schemaVersion", StringComparison.Ordinal));
        Assert.Contains(preview.ValidationErrors, error => error.Contains("providerGameId", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => service.ImportGuideBundle(json, "steam", "620"));
    }

    [Fact]
    public void GuideBundle_RejectsDuplicateAchievementIdsAndInvalidSourceUrl()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        var json = """
        {
          "schemaVersion": 1,
          "provider": "steam",
          "providerGameId": "620",
          "sourceUrl": "not-a-url",
          "achievements": [
            { "providerAchievementId": "ACH.ONE", "guideText": "First", "tags": [], "isMissable": false },
            { "providerAchievementId": "ACH.ONE", "guideText": "Second", "tags": [], "isMissable": false }
          ]
        }
        """;

        var preview = service.PreviewGuideImport(json, "steam", "620");

        Assert.False(preview.IsValid);
        Assert.Contains(preview.ValidationErrors, error => error.Contains("sourceUrl", StringComparison.Ordinal));
        Assert.Contains(preview.ValidationErrors, error => error.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void GuideBundle_ImportsRetroAchievementsProviderIdentity()
    {
        using var scope = new TempDataRoot();
        var service = new AchievementGuideService(scope.Path);
        var row = new GameAchievementsFetchService.AchievementRow
        {
            Provider = GameAchievementsFetchService.RetroAchievementsProvider,
            ProviderGameId = "123",
            ProviderAchievementId = "456",
            Title = "Retro challenge",
            Description = "Complete the challenge."
        };
        service.SyncDefinitionsAndLoadGuides("retro-game", new[] { row });
        var json = """
        {
          "schemaVersion": 1,
          "provider": "retroachievements",
          "providerGameId": "123",
          "sourceUrl": "https://example.com/retro-guide",
          "sourceTitle": "Retro guide",
          "achievements": [
            {
              "providerAchievementId": "456",
              "guideText": "Use the upper route and keep every checkpoint active.",
              "tags": ["challenge"],
              "isMissable": false
            }
          ]
        }
        """;

        var preview = service.PreviewGuideImport(json, "retroachievements", "123");
        var imported = service.ImportGuideBundle(json, "retroachievements", "123");
        var refreshed = service.SyncDefinitionsAndLoadGuides("retro-game", new[] { row }).Single();

        Assert.True(preview.CanImport);
        Assert.Equal(1, imported.ImportedCount);
        Assert.Equal("Use the upper route and keep every checkpoint active.", refreshed.GuideText);
        Assert.Equal("https://example.com/retro-guide", refreshed.SourceUrl);
    }

    static GameAchievementsFetchService.AchievementRow SteamRow(
        string appId,
        string achievementId,
        string title,
        string description)
    {
        return new GameAchievementsFetchService.AchievementRow
        {
            Provider = GameAchievementsFetchService.SteamProvider,
            ProviderGameId = appId,
            ProviderAchievementId = achievementId,
            SteamApiName = achievementId,
            Title = title,
            Description = description,
            IconUrlColor = "https://example.com/icon.png"
        };
    }

    sealed class TempDataRoot : IDisposable
    {
        public TempDataRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PixelVaultAchievementGuideTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch { }
        }
    }
}
