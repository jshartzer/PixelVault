using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public class PvCompletionTests
{
    static readonly AchievementCompletionProgress Earned = new(true, true, false);
    [Fact]
    public void DiamondRequiresRealEarnedPvAndEveryOfficialAchievement()
    {
        var official = new[] { Earned, new AchievementCompletionProgress(true, true, true) };
        Assert.Equal(AchievementCompletionTier.BaseAndDlc, AchievementCompletion.Classify(official));
        Assert.Equal(AchievementCompletionTier.BaseAndDlc, AchievementCompletion.Classify(official, pvProgress: []));
        foreach (var pv in new[] { new AchievementCompletionProgress(false, true, false), new(true, false, false) })
            Assert.Equal(AchievementCompletionTier.BaseAndDlc, AchievementCompletion.Classify(official, pvProgress: [pv]));
        Assert.Equal(AchievementCompletionTier.BaseAndDlcAndPv, AchievementCompletion.Classify(official, pvProgress: [Earned]));
        Assert.Equal("base-dlc-pv", AchievementCompletion.Name(AchievementCompletion.Classify(official, pvProgress: [Earned])));
        Assert.Equal(AchievementCompletionTier.Base, AchievementCompletion.Classify([Earned, new(true, false, true)], pvProgress: [Earned]));
        Assert.Equal(AchievementCompletionTier.None, AchievementCompletion.Classify([new(true, false, false)], pvProgress: [Earned]));
        Assert.Equal(AchievementCompletionTier.Base, AchievementCompletion.Classify([], true, [Earned]));
        Assert.Equal(AchievementCompletionTier.None, AchievementCompletion.Classify([], pvProgress: [Earned]));
        // No DLC is required for a game that has none; all actual official goals still are.
        Assert.Equal(AchievementCompletionTier.BaseAndDlcAndPv, AchievementCompletion.Classify([Earned], pvProgress: [Earned]));
    }
    [Fact]
    public void CenturyDefinitionsStaySeparateFromProviderRowsAndHaveNoSyntheticProgress()
    {
        var section = AchievementsInfoWindow.CenturyAchievementContentGroup();
        Assert.Empty(section.Rows); Assert.Equal(0, section.Total); Assert.Null(section.Percent);
        Assert.Equal("Centurion Achievements", section.Name); Assert.True(section.IsContentGroup);
        var deathStranding = AchievementsInfoWindow.CenturyAchievementContentGroup("steam", "1850570");
        Assert.Equal(5, deathStranding.CenturyDefinitions.Count);
        var road = deathStranding.CenturyDefinitions.Single(d => d.Id == CenturyAchievementCatalog.DeathStrandingRoadsId);
        var interviews = deathStranding.CenturyDefinitions.Single(d => d.Id == CenturyAchievementCatalog.DeathStrandingInterviewsId);
        Assert.Equal("The Whole Story", interviews.Title); Assert.Equal(100, interviews.Points);
        Assert.Equal(CenturyAchievementCatalog.DeathStrandingRoadsId, road.Id);
        Assert.Equal("All Roads Lead Home", road.Title);
        Assert.Equal(5, deathStranding.Total); Assert.Equal(0, deathStranding.Earned);
        Assert.Null(deathStranding.Percent); Assert.False(deathStranding.ProgressKnown);
        Assert.Empty(deathStranding.Rows);
        Assert.Empty(CenturyAchievementCatalog.ForGame("retroachievements", "1850570"));
        Assert.Empty(CenturyAchievementCatalog.ForGame("steam", "1190460"));
        Assert.Equal(5, CenturyAchievementCatalog.PlannedPerGame);
        Assert.Equal(100, road.Points);
        var presentation = AchievementsInfoWindow.CenturyPresentation(road);
        Assert.False(presentation.HasStableProviderIdentity); Assert.False(presentation.ProgressKnown); Assert.False(presentation.Unlocked);
        Assert.Equal("100 PV pts", AchievementScoring.PointText(presentation));
        Assert.Equal("100 PV pts", AchievementScoring.Explanation(presentation));
        Assert.Null(presentation.AwardedPoints);
    }

    [Theory]
    [InlineData(CenturyAchievementCatalog.DeathStrandingRoadsId)]
    [InlineData(CenturyAchievementCatalog.DeathStrandingInterviewsId)]
    [InlineData(CenturyAchievementCatalog.DeathStrandingMailsId)]
    [InlineData(CenturyAchievementCatalog.DeathStrandingLegendsId)]
    [InlineData(CenturyAchievementCatalog.DeathStrandingCargoId)]
    public void CenturyUsesTheNormalCardSizeAndDistinctBundledArtWithoutAwardingProgress(string id) => OnSta(() =>
    {
        var road = CenturyAchievementCatalog.Find("steam", "1850570", id);
        var locked = CenturyAchievementCatalog.Artwork(road.Id, false);
        var unlocked = CenturyAchievementCatalog.Artwork(road.Id, true);
        Assert.NotNull(locked); Assert.NotNull(unlocked); Assert.False(locked.SequenceEqual(unlocked));
        Assert.Null(CenturyAchievementCatalog.Artwork("unknown", false));
        var card = Assert.IsType<Border>(AchievementsInfoWindow.BuildCenturyAchievementCard(road));
        Assert.Equal(154, card.Width); Assert.Equal(174, card.Height);
        var row = Assert.IsType<GameAchievementsFetchService.AchievementRow>(card.Tag);
        Assert.Equal(100, row.Points); Assert.False(row.Unlocked); Assert.False(row.ProgressKnown);
        var image = new Image(); AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "test");
        Assert.NotNull(image.Source);
        Assert.IsType<Border>(AchievementsInfoWindow.BuildCenturyAchievementCard(road, false));
    });
    [Theory]
    [InlineData(400)]
    [InlineData(680)]
    [InlineData(1400)]
    public void DiamondCabinetWrapsWithoutClippedCrestOrProtrudingFloors(double width) => OnSta(() =>
    {
        var panel = new CompletionCasePanel(AchievementCompletionTier.BaseAndDlcAndPv, 120);
        for (var i = 0; i < 14; i++) panel.Children.Add(new Border { Width = 120, Height = 180, Background = Brushes.DarkSlateBlue });
        panel.Measure(new Size(width, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height)); panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
        Assert.InRange(panel.OrnamentBounds.Top, 0, 20); Assert.True(panel.OrnamentBounds.Bottom <= panel.ActualHeight);
        Assert.True(panel.OrnamentBounds.Left >= 0); Assert.True(panel.OrnamentBounds.Right <= width);
        for (var row = 0; row < panel.ShelfCount; row++)
        {
            var card = panel.Children[row * panel.Columns]; var offset = VisualTreeHelper.GetOffset(card); var interior = panel.ShelfInterior(row, width);
            Assert.Equal(44, offset.X); Assert.InRange(Math.Abs((offset.Y - interior.Top) - (interior.Bottom - offset.Y - card.RenderSize.Height)), 0, 1);
        }
        var cover = new CompletionFrameElement(new CornerRadius(12), false, true) { Width = 200, Height = 300 };
        cover.Measure(new Size(200, 300)); cover.Arrange(new Rect(0, 0, 200, 300));
        var frame = new RenderTargetBitmap(240, 340, 96, 96, PixelFormats.Pbgra32); frame.Render(cover);
    });
}
