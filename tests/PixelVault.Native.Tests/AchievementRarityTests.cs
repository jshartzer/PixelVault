using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementRarityTests
{
    [Theory]
    [InlineData(null, "Common")]
    [InlineData(double.NaN, "Common")]
    [InlineData(double.PositiveInfinity, "Common")]
    [InlineData(-1d, "Common")]
    [InlineData(101d, "Common")]
    [InlineData(100d, "Common")]
    [InlineData(10.001, "Common")]
    [InlineData(10d, "Bronze")]
    [InlineData(5.001, "Bronze")]
    [InlineData(5d, "Silver")]
    [InlineData(1.001, "Silver")]
    [InlineData(1d, "Gold")]
    [InlineData(0.010001, "Gold")]
    [InlineData(0.01, "Gold")]
    [InlineData(0.009999, "UberRare")]
    [InlineData(0.005, "UberRare")]
    [InlineData(0d, "UberRare")]
    public void TierBoundaries(double? percent, string expected) =>
        Assert.Equal(expected, AchievementRarityUi.Tier(percent).ToString());

    [Fact]
    public void BothViews_UseMutedRareCardsUntilProgressConfirmsAnUnlock()
    {
        OnSta(() =>
        {
            var window = CreateWindow(() => Task.FromResult(Result()));
            try
            {
                foreach (var method in new[] { "BuildAchievementGridCard", "BuildAchievementRowCard" })
                foreach (var percent in new[] { 0.005, 0.5, 3.0, 8.0 })
                {
                    var row = Row("Rare achievement", true, false);
                    row.GlobalUnlockPercent = percent;
                    Border Build() => (Border)typeof(AchievementsInfoWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, new object[] { row, "PixelVault.Tests" })!;
                    var locked = Build();
                    Assert.IsType<SolidColorBrush>(locked.Background);
                    Assert.Contains(AchievementRarityUi.Label(row), (string)locked.ToolTip);
                    if (percent == .005)
                        Assert.Contains(percent.ToString("0.########") + "% of players", (string)locked.ToolTip);
                    Assert.Contains(AchievementRarityUi.Label(row), AutomationProperties.GetName(locked));
                    row.Unlocked = true;
                    row.ProgressKnown = false;
                    Assert.Equal(locked.Background.ToString(), Build().Background.ToString());
                    row.ProgressKnown = true;
                    var earned = Build();
                    if (!SystemParameters.HighContrast)
                    {
                        Assert.IsType<LinearGradientBrush>(earned.Background);
                        Assert.IsType<LinearGradientBrush>(earned.BorderBrush);
                    }
                    row.GlobalUnlockPercent = 50;
                    Assert.IsType<SolidColorBrush>(Build().Background);
                    Assert.DoesNotContain("rarity", (string)Build().ToolTip);
                }
            }
            finally { window.Close(); }
        });
    }
}


