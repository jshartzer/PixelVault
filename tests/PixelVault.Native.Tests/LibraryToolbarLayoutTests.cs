using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class LibraryToolbarLayoutTests
{
    [Theory]
    [InlineData(720)]
    [InlineData(1024)]
    [InlineData(1280)]
    public void ImportActionsRemainRightAlignedAndDoNotOverlapUtilities(double width)
    {
        OnSta(() =>
        {
            var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            var chrome = (MainWindow.LibraryBrowserNavChrome)typeof(MainWindow)
                .GetMethod("BuildLibraryBrowserNavChrome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
            var root = chrome.NavBar;
            root.Width = width;
            for (var i = 0; i < 3; i++)
            {
                root.Measure(new Size(width, double.PositiveInfinity));
                root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
                root.UpdateLayout();
            }
            Rect Bounds(FrameworkElement item) => new(item.TranslatePoint(new Point(), root), item.RenderSize);
            var imports = new[] { chrome.ImportButton, chrome.ImportCommentsButton, chrome.ManualImportButton };
            var utilities = new[] { chrome.SettingsButton, chrome.PhotographyGalleryButton, chrome.MyCoversButton, chrome.RefreshButton };
            foreach (var button in imports.Concat(utilities))
            {
                var rect = Bounds(button);
                Assert.True(rect.Width >= 40 && rect.Left >= 0 && rect.Right <= width, $"Control outside toolbar: {rect}, width {width}");
            }
            foreach (var import in imports)
                foreach (var utility in utilities)
                    Assert.False(Bounds(import).IntersectsWith(Bounds(utility)));
            Assert.InRange(Bounds(chrome.ManualImportButton).Right, width - 20, width - 18);
            Assert.True(Bounds(chrome.ImportButton).Left < Bounds(chrome.ImportCommentsButton).Left);
            Assert.True(Bounds(chrome.ImportCommentsButton).Left < Bounds(chrome.ManualImportButton).Left);
        });
    }
}
