using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class GameProfilePlatformTests
{
    [Fact]
    public void AlanWakeNumericProfileFindsXboxRomanSequelWithoutMatchingTheOriginal()
    {
        var titles = new[] { Xbox("1297287173", "Alan Wake"), Xbox("1693830737", "Alan Wake II") };
        var preference = new GameProfilePlatformStore.Preference();
        var choices = GameProfilePlatformStore.Choices("Alan Wake 2", Array.Empty<LibraryFolderInfo>(), titles, preference);
        Assert.Equal("xbox:1693830737", GameProfilePlatformStore.Default(choices, preference).Key);
        Assert.Equal("1693830737", Assert.Single(GameProfilePlatformStore.SearchXbox(titles, "Alan Wake 2")).Id);
        Assert.Empty(GameProfilePlatformStore.SearchXbox(titles, "Alan Wake Remastered"));
    }

    [Theory]
    [InlineData("Diablo® IV", "Diablo 4")]
    [InlineData("Alan Wake II", "Alan Wake 2")]
    [InlineData("  Example™  III ", "Example 3")]
    public void SequelNumbersAreEquivalentWithoutStrippingEditionNames(string title, string query)
    {
        Assert.Equal(GameProfilePlatformStore.NameKey(title), GameProfilePlatformStore.NameKey(query));
        Assert.NotEqual(GameProfilePlatformStore.NameKey(title), GameProfilePlatformStore.NameKey(query + " Remastered"));
    }

    static XboxAchievementService.XboxTitle Xbox(string id, string name = "Example") => new() { Id = id, Name = name };
    static LibraryFolderInfo Steam(string id = "123") => new() { Name = "Example", SteamAppId = id, PlatformLabel = "Steam" };

    [Fact]
    public void SharedGameKeepsDistinctProviderIdentitiesAndUsesSavedDefault()
    {
        var saved = new GameProfilePlatformStore.Preference();
        var choices = GameProfilePlatformStore.Choices("Example", new[] { Steam(), Steam() }, new[] { Xbox("123") }, saved);
        Assert.Equal(2, choices.Count);
        Assert.Equal("steam:123", GameProfilePlatformStore.Default(choices, saved).Key);
        saved.DefaultKey = "xbox:123";
        Assert.Equal("xbox:123", GameProfilePlatformStore.Default(choices, saved).Key);
        Assert.Equal("123", choices.Single(c => c.Provider == "steam").Folder.SteamAppId);
        Assert.Null(choices.Single(c => c.Provider == "xbox").Folder.SteamAppId);
    }

    [Fact]
    public void AmbiguousXboxEditionsRequireSelectionAndRemastersAreNotConflated()
    {
        var saved = new GameProfilePlatformStore.Preference();
        var choices = GameProfilePlatformStore.Choices("Example™", Array.Empty<LibraryFolderInfo>(),
            new[] { Xbox("1"), Xbox("2"), Xbox("3", "Example Remastered") }, saved);
        Assert.Empty(choices);
        Assert.Null(GameProfilePlatformStore.Default(choices, saved));
        saved.DefaultKey = "xbox:2";
        choices = GameProfilePlatformStore.Choices("Example", Array.Empty<LibraryFolderInfo>(), new[] { Xbox("1"), Xbox("2") }, saved);
        Assert.Equal("2", GameProfilePlatformStore.Default(choices, saved).Id);
    }

    [Fact]
    public void ExplicitLinkAllowsDifferentNamesAndMissingSavedProviderDoesNotFallBack()
    {
        var saved = new GameProfilePlatformStore.Preference { XboxTitleId = "456", DefaultKey = "xbox:456" };
        var choices = GameProfilePlatformStore.Choices("Example", new[] { Steam() }, new[] { Xbox("456", "Other edition") }, saved);
        Assert.Equal("456", GameProfilePlatformStore.Default(choices, saved).Id);
        var disconnected = GameProfilePlatformStore.Choices("Example", new[] { Steam() }, Array.Empty<XboxAchievementService.XboxTitle>(), saved);
        Assert.Null(GameProfilePlatformStore.Default(disconnected, saved));
    }

    [Fact]
    public void PreferencesSurviveReopenWithoutChangingAnotherGame()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-platforms-" + Guid.NewGuid().ToString("N"));
        try
        {
            new GameProfilePlatformStore(root).Save("game1", new() { DefaultKey = "xbox:1", XboxTitleId = "1" });
            new GameProfilePlatformStore(root).Save("game2", new() { DefaultKey = "steam:2" });
            new GameProfilePlatformStore(root).Save("game1", new() { DefaultKey = "steam:3", XboxTitleId = "1" });
            var reopened = new GameProfilePlatformStore(root);
            Assert.Equal("steam:3", reopened.Get("game1").DefaultKey);
            Assert.Equal("1", reopened.Get("game1").XboxTitleId);
            Assert.Equal("steam:2", reopened.Get("game2").DefaultKey);
            Assert.Null(reopened.Get("new-game").DefaultKey);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void DeadSpaceMetadataExcludes360OriginalAndExplicitLinkOverridesSameName()
    {
        var legacy=Xbox("1161889879","Dead Space\uFFFD");legacy.Legacy=true;
        var modern=Xbox("2008403666","Dead Space");
        var folder=Steam("1693980");folder.IgdbPlatforms="Xbox Series X|S, PC (Microsoft Windows), PlayStation 5";
        var titles=new[]{legacy,modern};
        var choices=GameProfilePlatformStore.Choices("Dead Space",new[]{folder},titles,new());
        Assert.Equal(modern.Id,Assert.Single(choices,c=>c.Provider=="xbox").Id);
        var saved=new GameProfilePlatformStore.Preference{XboxTitleId=legacy.Id};
        choices=GameProfilePlatformStore.Choices("Dead Space",new[]{folder},titles,saved);
        Assert.Equal(legacy.Id,Assert.Single(choices,c=>c.Provider=="xbox").Id);
        folder.IgdbPlatforms="";
        choices=GameProfilePlatformStore.Choices("Dead Space",new[]{folder},titles,new());
        Assert.DoesNotContain(choices,c=>c.Provider=="xbox");
    }

}
