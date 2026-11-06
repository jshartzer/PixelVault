using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class GameProfilePlatformUiTests
{
    static XboxAchievementService.XboxTitle[] Titles() => new[]
    {
        new XboxAchievementService.XboxTitle { Id = "1", Name = "Alan Wake", Legacy = true },
        new XboxAchievementService.XboxTitle { Id = "2", Name = "Alan Wake II" },
        new XboxAchievementService.XboxTitle { Id = "3", Name = "Diablo IV" }
    };

    [Fact]
    public void XboxDialogSelectsARealListItemAndReturnsTheLinkedTitle() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var dialog = new XboxProfileLinkWindow(null, "Alan Wake 2", Titles());
        Exception? failure = null;
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                Assert.Single(dialog.Games.Items.Cast<object>());
                Assert.False(dialog.Link.IsEnabled);
                var data = Assert.IsType<XboxAchievementService.XboxTitle>(dialog.Games.Items[0]);
                Assert.Equal("2", data.Id);
                dialog.UpdateLayout();
                var container = Assert.IsType<ListBoxItem>(dialog.Games.ItemContainerGenerator.ContainerFromIndex(0));
                var peer = new ListBoxItemAutomationPeer(data, new ListBoxAutomationPeer(dialog.Games));
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                Assert.Same(data, dialog.Games.SelectedItem); Assert.True(dialog.Link.IsEnabled);
                SaveRender((FrameworkElement)dialog.Content, "xbox-link-dialog.png");
                ((IInvokeProvider)new ButtonAutomationPeer(dialog.Link).GetPattern(PatternInterface.Invoke)).Invoke();
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        }));
        Assert.True(dialog.ShowDialog());
        if (failure != null) throw failure;
        Assert.Equal("2", dialog.SelectedTitle.Id);
        var root = Path.Combine(Path.GetTempPath(), "pv-link-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            new GameProfilePlatformStore(root).Save("profile", new() { DefaultKey = "xbox:" + dialog.SelectedTitle.Id, XboxTitleId = dialog.SelectedTitle.Id });
            var saved = new GameProfilePlatformStore(root).Get("profile");
            var choices = GameProfilePlatformStore.Choices("Different capture name", Array.Empty<LibraryFolderInfo>(), Titles(), saved);
            Assert.Equal("xbox:2", GameProfilePlatformStore.Default(choices, saved).Key);
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public void SearchCanRecoverFromNoResultsAndClearsAStaleSelection() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var dialog = new XboxProfileLinkWindow(null, "Unmatched capture name", Titles());
        try
        {
            Assert.Empty(dialog.Games.Items.Cast<object>());
            dialog.ShowAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, dialog.Games.Items.Count);
            dialog.Games.SelectedIndex = 0; Assert.True(dialog.Link.IsEnabled);
            dialog.Search.Text = "No matches";
            Assert.Null(dialog.Games.SelectedItem); Assert.False(dialog.Link.IsEnabled);
            dialog.Search.Text = "diablo 4"; Assert.Single(dialog.Games.Items.Cast<object>());
        }
        finally { dialog.Close(); }
    });

    [Theory]
    [InlineData(760)]
    [InlineData(1040)]
    public void PlatformBarHasReadableThemedControlsAtProfileWidths(int width) => AchievementCollectionAccessTests.OnSta(() =>
    {
        var bar = new GameProfilePlatformBar { Margin = new Thickness(20) };
        bar.Selector.Items.Add(new ComboBoxItem { Content = "Xbox" }); bar.Selector.SelectedIndex = 0;
        bar.Status.Text = "Xbox achievements · Alan Wake II";
        var host = new Border { Background = UiBrushHelper.FromHex("#0B1318"), Width = width, Child = bar };
        host.Measure(new Size(width, 240)); host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height)); host.UpdateLayout();
        Assert.InRange(bar.Selector.ActualWidth, 180, width);
        Assert.Equal(38, bar.Selector.ActualHeight);
        Assert.Equal(38, bar.Sync.ActualHeight);
        Assert.NotNull(bar.Selector.Template);
        Assert.NotNull(bar.Link.Template);
        Assert.True(bar.Link.TranslatePoint(new Point(bar.Link.ActualWidth, 0), bar).X <= bar.ActualWidth);
        SaveRender(host, "platform-bar-" + width + ".png");
        bar.Sync.IsEnabled = false; host.UpdateLayout(); SaveRender(host, "platform-bar-disabled-" + width + ".png");
    });

    static void SaveRender(FrameworkElement element, string file)
    {
        var output = Environment.GetEnvironmentVariable("PIXELVAULT_UI_RENDER_DIR");
        if (string.IsNullOrEmpty(output)) return;
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var area = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            drawing.DrawRectangle(GameProfilePlatformBar.Surface, null, area);
            drawing.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null, area);
        }
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output); using var stream = File.Create(Path.Combine(output, file)); encoder.Save(stream);
    }
}
