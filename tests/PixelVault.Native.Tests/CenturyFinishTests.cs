using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public class CenturyFinishTests
{
    static GameAchievementsFetchService.AchievementRow Road(bool known, bool earned)
    {
        var definition = CenturyAchievementCatalog.Find("steam", "1850570", CenturyAchievementCatalog.DeathStrandingRoadsId);
        var row = AchievementsInfoWindow.CenturyPresentation(definition);
        row.ProgressKnown = known; row.Unlocked = earned; return row;
    }

    [Fact]
    public void FinishRequiresKnownEarnedCenturyAndNeverChangesTheRow() => AchievementCollectionAccessTests.OnSta(() =>
    {
        foreach (var state in new[] { (false, false), (false, true), (true, false) })
        {
            var image = new Image(); var icon = new Border { Child = image };
            var row = Road(state.Item1, state.Item2);
            Assert.False(CenturyAchievementFinish.Apply(icon, row));
            Assert.Same(image, icon.Child); Assert.Null(image.Effect);
            Assert.Equal(state.Item1, row.ProgressKnown); Assert.Equal(state.Item2, row.Unlocked);
        }
        var official = Road(true, true); official.CenturyDefinitionId = null;
        Assert.False(CenturyAchievementFinish.Apply(new Border { Child = new Image() }, official));
    });

    [Fact]
    public void PrismaticShaderRendersColorAndRespondsToLightInSoftware() => AchievementCollectionAccessTests.OnSta(() =>
    {
        if (SystemParameters.HighContrast) return;
        var row = Road(true, true); var image = new Image();
        AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "test");
        var icon = new Border { Width = 110, Height = 110, CornerRadius = new CornerRadius(9), Child = image };
        Assert.True(CenturyAchievementFinish.Apply(icon, row));
        Assert.Equal(new Thickness(3), icon.BorderThickness);
        Assert.True(CenturyAchievementFinish.Apply(icon, row)); // Reapplying must preserve a single image/effect.
        var effect = Assert.IsType<CenturyPrismaticEffect>(image.Effect);
        byte[] Render()
        {
            icon.Measure(new Size(110, 110)); icon.Arrange(new Rect(0, 0, 110, 110)); icon.UpdateLayout();
            var bitmap = new RenderTargetBitmap(110, 110, 96, 96, PixelFormats.Pbgra32); bitmap.Render(icon);
            var pixels = new byte[110 * 110 * 4]; bitmap.CopyPixels(pixels, 440, 0); return pixels;
        }
        effect.Light = new Point(.2, .25); var first = Render();
        effect.Light = new Point(.8, .75); var second = Render();
        var colored = 0; var changed = 0;
        for (var y = 10; y < 100; y++) for (var x = 10; x < 100; x++)
        {
            var i = (y * 110 + x) * 4;
            if (Math.Abs(first[i] - first[i + 2]) > 12) colored++;
            if (Math.Abs(first[i] - second[i]) + Math.Abs(first[i + 1] - second[i + 1]) + Math.Abs(first[i + 2] - second[i + 2]) > 15) changed++;
        }
        Assert.True(colored > 1000, $"Expected visible prismatic color, found {colored} pixels.");
        Assert.True(changed > 1000, $"Expected the reflection to move, found {changed} changed pixels.");
        Assert.True(row.ProgressKnown && row.Unlocked); Assert.Null(row.CounterCurrent);
    });
}
