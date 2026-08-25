using System.Text.Json;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementProviderIdentityTests
{
    [Fact]
    public void AchievementRow_HasStableProviderIdentity_RequiresEveryProviderPart()
    {
        var complete = new GameAchievementsFetchService.AchievementRow
        {
            Provider = GameAchievementsFetchService.SteamProvider,
            ProviderGameId = "620",
            ProviderAchievementId = "ACH.PORTAL"
        };
        var missingAchievement = new GameAchievementsFetchService.AchievementRow
        {
            Provider = GameAchievementsFetchService.SteamProvider,
            ProviderGameId = "620"
        };

        Assert.True(complete.HasStableProviderIdentity);
        Assert.False(missingAchievement.HasStableProviderIdentity);
    }

    [Theory]
    [InlineData("{\"ID\":12345}", "ignored", "12345")]
    [InlineData("{\"AchievementID\":\"67890\"}", "ignored", "67890")]
    [InlineData("{\"Title\":\"Fallback\"}", "24680", "24680")]
    [InlineData("{\"Title\":\"No identity\"}", "not-an-id", "")]
    public void ResolveRetroAchievementId_PrefersExplicitNumericId_ThenNumericObjectKey(
        string json,
        string fallbackKey,
        string expected)
    {
        using var doc = JsonDocument.Parse(json);

        var actual = GameAchievementsFetchService.ResolveRetroAchievementId(doc.RootElement, fallbackKey);

        Assert.Equal(expected, actual);
    }
}
