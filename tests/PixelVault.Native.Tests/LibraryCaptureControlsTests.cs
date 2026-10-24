using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class LibraryCaptureControlsTests
{
    [Fact]
    public void Filters_ShowSupportedMediaAndClearHiddenSelection()
    {
        OnSta(() =>
        {
            var panes = new MainWindow.LibraryBrowserPaneRefs();
            MainWindow.BuildLibraryCaptureControls(panes);
            var ws = new MainWindow.LibraryBrowserWorkingSet { Panes = panes };
            var files = new[] { "one.png", "two.JXR", "three.webp", "four.MP4", "five.mkv", "six.mov", "seven.avi", "eight.wmv", "nine.webm" };
            var renders = 0;
            MainWindow.WireLibraryCaptureFilters(ws, panes, () => renders++);
            ws.SelectedDetailFiles.Add("one.png");
            ws.DetailFilesDisplayOrder.AddRange(files);
            ws.DetailSelectionAnchorIndex = 0;
            panes.CaptureFilterClipsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(files.Skip(3), files.Where(f => MainWindow.LibraryCaptureMatchesMediaFilter(f, ws.CaptureMediaFilter)));
            Assert.Empty(ws.SelectedDetailFiles);
            Assert.Empty(ws.DetailFilesDisplayOrder);
            Assert.Equal(-1, ws.DetailSelectionAnchorIndex);
            Assert.Equal(1, renders);
            Assert.True(ws.ResetDetailRowsToLoadingOnNextRender);
            panes.CaptureFilterClipsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, renders);

            ws.SelectedDetailFiles.Add("four.MP4");
            panes.CaptureFilterScreenshotsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(files.Take(3), files.Where(f => MainWindow.LibraryCaptureMatchesMediaFilter(f, ws.CaptureMediaFilter)));
            Assert.Empty(ws.SelectedDetailFiles);
            panes.CaptureFilterAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(files, files.Where(f => MainWindow.LibraryCaptureMatchesMediaFilter(f, ws.CaptureMediaFilter)));
            Assert.Equal(3, renders);
        });
    }

    [Fact]
    public void Toolbar_KeepsDeleteVisibleOnRightAtNarrowWidths()
    {
        OnSta(() =>
        {
            var panes = new MainWindow.LibraryBrowserPaneRefs();
            var toolbar = MainWindow.BuildLibraryCaptureControls(panes);
            panes.CaptureGroupsToggle.Visibility = Visibility.Visible;
            panes.CaptureGroupsToggle.IsChecked = true;
            var ws = new MainWindow.LibraryBrowserWorkingSet { Panes = panes };
            MainWindow.WireLibraryCaptureFilters(ws, panes, () => { });
            var root = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 21, 26)), Padding = new Thickness(16), Child = toolbar };
            foreach (var width in new[] { 1000d, 640d, 480d })
            {
                foreach (var enabled in new[] { false, true })
                {
                    panes.DeleteSelectedButton.IsEnabled = enabled;
                    panes.ThumbLabel.Text = "999 selected";
                    panes.ThumbLabel.Visibility = Visibility.Visible;
                    Layout(root, width);
                    var deleteOrigin = panes.DeleteSelectedButton.TranslatePoint(new Point(0, 0), root);
                    var filterOrigin = panes.CaptureFilterAllButton.TranslatePoint(new Point(0, 0), root);
                    Assert.True(deleteOrigin.X > filterOrigin.X);
                    Assert.True(deleteOrigin.X + panes.DeleteSelectedButton.ActualWidth <= width - 15);
                    Assert.True(panes.DeleteSelectedButton.ActualWidth >= 150);
                    Assert.True(panes.DeleteSelectedButton.ActualHeight >= 38);
                    Assert.Equal(1, panes.DeleteSelectedButton.Opacity);
                    SavePreview(root, $"capture-toolbar-{width}-{(enabled ? "enabled" : "disabled")}.png");
                }
            }
            panes.CaptureFilterClipsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(root, 1000);
            SavePreview(root, "capture-toolbar-clips.png");
        });
    }

    static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new Size(width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
        root.UpdateLayout();
    }

    static void SavePreview(FrameworkElement root, string name)
    {
        var dir = Environment.GetEnvironmentVariable("PIXELVAULT_CAPTURE_CONTROLS_PREVIEW");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, name));
        encoder.Save(file);
    }
}
