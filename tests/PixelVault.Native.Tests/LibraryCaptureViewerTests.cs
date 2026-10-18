using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace PixelVaultNative.Tests;

public class LibraryCaptureViewerTests
{
    [Fact]
    public void ViewModes_ResizeToViewport_AndPreserveOriginalPixelDimensions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow.LibraryCaptureViewerWindow? window = null;
            try
            {
                window = new MainWindow.LibraryCaptureViewerWindow(null, null, new List<string>(), 0);
                T Field<T>(string name) => (T)window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                void Mode(bool actual) => window.GetType().GetMethod("SetViewMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { actual });
                var image = Field<Image>("_image");
                var surface = Field<Grid>("_photoSurface");
                var scroll = Field<ScrollViewer>("_scroll");
                image.Source = BitmapSource.Create(5000, 40, 96, 96, PixelFormats.Bgr32, null, new byte[5000 * 40 * 4], 5000 * 4);
                surface.Measure(new Size(800, 600));
                surface.Arrange(new Rect(0, 0, 800, 600));
                Mode(false);
                Assert.Equal(800, image.Width);
                Assert.Equal(600, image.Height);
                surface.Measure(new Size(1200, 700));
                surface.Arrange(new Rect(0, 0, 1200, 700));
                Mode(false);
                Assert.Equal(1200, image.Width);
                Assert.Equal(700, image.Height);
                Mode(true);
                var dpi = VisualTreeHelper.GetDpi(window);
                Assert.Equal(5000 / dpi.DpiScaleX, image.Width);
                Assert.Equal(40 / dpi.DpiScaleY, image.Height);
                Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
                Mode(false);
                Assert.Equal(1200, image.Width);
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.True(double.IsNaN(surface.Width));
                Assert.True(double.IsNaN(surface.Height));
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Viewer test timed out.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
