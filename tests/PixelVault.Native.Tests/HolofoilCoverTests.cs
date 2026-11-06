using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public class HolofoilCoverTests
{
    [Theory]
    [InlineData(AchievementCompletionTier.Base)]
    [InlineData(AchievementCompletionTier.BaseAndDlc)]
    [InlineData(AchievementCompletionTier.BaseAndDlcAndPv)]
    public void FramesRetainTheirArtworkAndOnlyDiamondReceivesFoilAndTilt(AchievementCompletionTier tier) => OnSta(() =>
    {
        var art = new Border { Width = 180, Height = 270, Background = Brushes.Crimson };
        var scene = new Grid { Width = 180, Height = 270, Margin = new Thickness(0,0,12,14) };
        var frame = MainWindow.BuildLibraryTileCompletionFrameOverlay(180,270,new CornerRadius(12),tier);
        var clicks = 0; var button = new Button { Content = art }; button.Click += (_,__) => clicks++;
        scene.Children.Add(button); scene.Children.Add(frame);
        var cover = new HolofoilCover(scene,art); cover.SetTier(tier);
        cover.Measure(new Size(500,500)); cover.Arrange(new Rect(0,0,192,284)); cover.UpdateLayout();
        Assert.Equal(180,cover.ActualWidth); Assert.Equal(270,cover.ActualHeight);
        Assert.Equal(new Thickness(0,0,12,14),cover.Margin); Assert.Equal(new Thickness(0),scene.Margin);
        Assert.Same(frame,scene.Children[1]); Assert.Null(frame.Effect);
        var diamond = tier == AchievementCompletionTier.BaseAndDlcAndPv;
        if (diamond && !SystemParameters.HighContrast) Assert.Equal(1,Assert.IsType<CenturyPrismaticEffect>(art.Effect).ColorRetention);
        else Assert.Null(art.Effect);
        cover.UpdatePointer(new Point(.9,.15));
        Assert.Equal(diamond && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast,cover.IsProjecting);
        var hit = VisualTreeHelper.HitTest(cover,new Point(90,135));
        Assert.NotNull(hit);
        DependencyObject target = hit!.VisualHit;
        while (target != null && target != button) target = VisualTreeHelper.GetParent(target);
        Assert.Same(button,target); // The visible 3D copy must leave the original button hit-testable.
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
        button.RaiseEvent(press); Assert.False(press.Handled);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Assert.Equal(1,clicks);
        cover.SetTier(AchievementCompletionTier.None);
        Assert.False(cover.IsProjecting); Assert.Null(art.Effect); Assert.Same(frame,scene.Children[1]);
    });

    [Fact]
    public void IncompleteCoversStayUnchangedAndUnloadReleasesProjection() => OnSta(() =>
    {
        var art = new Border { Width = 120, Height = 180, Background = Brushes.Blue };
        var cover = new HolofoilCover(art,art);
        cover.Measure(new Size(120,180)); cover.Arrange(new Rect(0,0,120,180)); cover.UpdateLayout();
        cover.UpdatePointer(new Point(.9,.1)); Assert.False(cover.IsProjecting); Assert.Null(art.Effect);
        cover.SetTier(AchievementCompletionTier.BaseAndDlcAndPv);
        cover.UpdatePointer(new Point(.9,.1));
        cover.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.False(cover.IsProjecting);
        if (art.Effect != null) Assert.False(art.Effect.HasAnimatedProperties);
        Assert.Equal(1,Assert.IsType<Border>(cover.Children[0]).Opacity);
    });

    [Fact]
    public void FoilPreservesCoverColorAndPerspectiveDoesNotGoBlank() => OnSta(() =>
    {
        if (SystemParameters.HighContrast) return;
        var art = new Border { Width = 120, Height = 180, Background = Brushes.Crimson };
        var cover = new HolofoilCover(art,art); cover.SetTier(AchievementCompletionTier.BaseAndDlcAndPv);
        cover.Measure(new Size(120,180)); cover.Arrange(new Rect(0,0,120,180)); cover.UpdateLayout();
        byte[] Center()
        {
            var image = new RenderTargetBitmap(120,180,96,96,PixelFormats.Pbgra32); image.Render(cover);
            var pixel = new byte[4]; image.CopyPixels(new Int32Rect(60,90,1,1),pixel,4,0); return pixel;
        }
        var flat = Center(); Assert.Equal(255,flat[3]); Assert.True(flat[2] > flat[0] + 50 && flat[2] > flat[1] + 50);
        cover.UpdatePointer(new Point(.9,.1)); cover.UpdateLayout();
        var tilted = Center(); Assert.True(tilted[3] > 240,"The projected cover must remain visible.");
        Assert.True(tilted[2] > tilted[0] + 40 && tilted[2] > tilted[1] + 40,"The foil must preserve the original red cover color.");
    });

    [Theory]
    [InlineData(AchievementCompletionTier.Base)]
    [InlineData(AchievementCompletionTier.BaseAndDlc)]
    public void LeavingDiamondRestoresTheOriginalEffectAndStopsProjection(AchievementCompletionTier tier) => OnSta(() =>
    {
        var original = new System.Windows.Media.Effects.BlurEffect { Radius = .1 };
        var art = new Border { Width=120, Height=180, Background=Brushes.Blue, Effect=original };
        var cover = new HolofoilCover(art,art);
        cover.Measure(new Size(120,180)); cover.Arrange(new Rect(0,0,120,180)); cover.UpdateLayout();
        cover.SetTier(AchievementCompletionTier.BaseAndDlcAndPv); cover.UpdatePointer(new Point(.9,.1));
        cover.SetTier(tier); cover.UpdatePointer(new Point(.9,.1));
        Assert.Same(original,art.Effect); Assert.False(cover.IsProjecting);
        Assert.Equal(1,Assert.IsType<Border>(cover.Children[0]).Opacity);
    });
}
