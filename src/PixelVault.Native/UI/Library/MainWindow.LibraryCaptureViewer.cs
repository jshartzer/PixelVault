using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>PV-PLN-LIBPV-001 — Open a non-modal viewer sized to each capture’s aspect (large max bounds); navigation uses <see cref="LibraryBrowserWorkingSet.DetailFilesDisplayOrder"/> (images only).</summary>
        void OpenLibraryCaptureViewer(Window sizeReferenceWindow, LibraryBrowserWorkingSet ws, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || !IsImage(filePath)) return;
            if (ws == null || sizeReferenceWindow == null) return;

            var paths = (ws.DetailFilesDisplayOrder ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p) && IsImage(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count == 0)
                paths = new List<string> { filePath };
            else if (!paths.Any(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)))
                paths.Insert(0, filePath);

            var idx = paths.FindIndex(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;

            var win = new LibraryCaptureViewerWindow(this, sizeReferenceWindow, paths, idx);
            win.Show();
            win.Activate();
        }

        LibraryTimelineCaptureContext TryGetLibraryTimelineCaptureContextForViewer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            if (librarySession == null || !librarySession.HasLibraryRoot) return null;
            var list = new List<string> { filePath };
            var idx = librarySession.LoadLibraryMetadataIndexForFilePaths(list);
            var rows = librarySession.LoadSavedGameIndexRows();
            var map = BuildLibraryTimelineCaptureContextMap(list, idx, rows, null);
            LibraryTimelineCaptureContext ctx;
            return map.TryGetValue(filePath, out ctx) ? ctx : null;
        }

        internal sealed class LibraryCaptureViewerWindow : Window
        {

            /// <summary>Max logical size of the photo surface (DIP); content uses the image’s pixel aspect inside this cap.</summary>
            const double ViewerMaxContentWidth = 1520;
            const double ViewerMaxContentHeight = 1000;
            const double ViewerFallbackContentWidth = 1360;
            const double ViewerFallbackContentHeight = 860;
            readonly MainWindow _host;
            readonly List<string> _paths;
            int _index;
            readonly Grid _photoSurface;
            readonly Image _image;
            readonly StackPanel _footerSlot;
            readonly Border _navLeft;
            readonly Border _navRight;
            readonly ScrollViewer _scroll;
            readonly Button _fitButton;
            readonly Button _actualButton;
            readonly TextBlock _status;
            bool _actualSize;
            int _loadVersion;
            Point? _panStart;
            double _panX, _panY;

            internal LibraryCaptureViewerWindow(MainWindow host, Window ownerWindow, List<string> paths, int startIndex)
            {
                _host = host;
                _paths = paths ?? new List<string>();
                _index = _paths.Count == 0 ? 0 : Math.Max(0, Math.Min(startIndex, _paths.Count - 1));

                Title = "Capture";
                Background = new SolidColorBrush(Color.FromRgb(16, 24, 29));
                ResizeMode = ResizeMode.CanResize;
                WindowStartupLocation = WindowStartupLocation.Manual;
                SizeToContent = SizeToContent.Manual;
                MinWidth = 360;
                MinHeight = 280;
                ShowInTaskbar = true;
                ShowActivated = true;
                if (ownerWindow != null)
                    Owner = ownerWindow;

                // Photo surface explicit W/H = image aspect × scale so Uniform stretch fills with no letterboxing.
                _image = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

                _footerSlot = new StackPanel { Orientation = Orientation.Vertical };

                _navLeft = BuildNavChrome("\u2039", "Previous capture");
                _navLeft.MouseLeftButtonDown += delegate { Navigate(-1); };
                _navRight = BuildNavChrome("\u203A", "Next capture");
                _navRight.MouseLeftButtonDown += delegate { Navigate(1); };

                _photoSurface = new Grid { ClipToBounds = true };
                _scroll = new ScrollViewer
                {
                    Content = _image,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    CanContentScroll = false
                };
                _photoSurface.Children.Add(_scroll);
                var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
                _fitButton = new Button { Content = "Fit", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4, 0, 4, 0) };
                _actualButton = new Button { Content = "100%", ToolTip = "Actual size: one image pixel per screen pixel", Padding = new Thickness(12, 4, 12, 4) };
                _fitButton.Click += delegate { SetViewMode(false); };
                _actualButton.Click += delegate { SetViewMode(true); };
                foreach (var nav in new[] { _navLeft, _navRight })
                {
                    nav.Width = 40;
                    ((TextBlock)nav.Child).FontSize = 24;
                }
                toolbar.Children.Add(_navLeft);
                toolbar.Children.Add(_navRight);
                toolbar.Children.Add(_fitButton);
                toolbar.Children.Add(_actualButton);
                _status = new TextBlock { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                toolbar.Children.Add(_status);
                var root = new DockPanel();
                DockPanel.SetDock(toolbar, Dock.Top);
                DockPanel.SetDock(_footerSlot, Dock.Bottom);
                root.Children.Add(toolbar);
                root.Children.Add(_footerSlot);
                root.Children.Add(_photoSurface);
                Content = root;
                _photoSurface.SizeChanged += delegate { UpdateImageDimensions(); };
                DpiChanged += delegate { UpdateImageDimensions(); };
                _image.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                {
                    if (!_actualSize) return;
                    _panStart = e.GetPosition(_scroll);
                    _panX = _scroll.HorizontalOffset;
                    _panY = _scroll.VerticalOffset;
                    _image.CaptureMouse();
                    e.Handled = true;
                };
                _image.MouseMove += delegate(object sender, MouseEventArgs e)
                {
                    if (!_panStart.HasValue) return;
                    var point = e.GetPosition(_scroll);
                    _scroll.ScrollToHorizontalOffset(_panX + _panStart.Value.X - point.X);
                    _scroll.ScrollToVerticalOffset(_panY + _panStart.Value.Y - point.Y);
                };
                _image.MouseLeftButtonUp += delegate { _image.ReleaseMouseCapture(); _panStart = null; };
                _image.LostMouseCapture += delegate { _panStart = null; };
                SetViewMode(false);

                if (_paths.Count > 0)
                    ApplyPhotoSurfaceDimensions(_paths[_index]);

                Loaded += delegate
                {
                    ReloadCurrent();
                    CenterOnWorkArea();
                };

                PreviewKeyDown += delegate(object _, KeyEventArgs e)
                {
                    if (e.Key == Key.Escape)
                    {
                        Close();
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Left)
                    {
                        Navigate(-1);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Right)
                    {
                        Navigate(1);
                        e.Handled = true;
                    }
                };

                Closing += delegate
                {
                    ++_loadVersion;
                    _image.Source = null;
                };
            }

            static bool TryGetImagePixelSize(string path, out int pixelWidth, out int pixelHeight)
            {
                pixelWidth = 0;
                pixelHeight = 0;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var decoder = BitmapDecoder.Create(
                            stream,
                            BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.OnDemand);
                        var frame = decoder.Frames[0];
                        pixelWidth = frame.PixelWidth;
                        pixelHeight = frame.PixelHeight;
                        return pixelWidth > 0 && pixelHeight > 0;
                    }
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>Choose an initial window size from the image aspect ratio.</summary>
            void ApplyPhotoSurfaceDimensions(string imagePath)
            {
                var wa = SystemParameters.WorkArea;
                const double margin = 56;
                var maxW = Math.Min(ViewerMaxContentWidth, Math.Max(640, wa.Width - margin));
                var maxH = Math.Min(ViewerMaxContentHeight, Math.Max(480, wa.Height - margin));
                double gridW, gridH;
                if (!string.IsNullOrWhiteSpace(imagePath)
                    && File.Exists(imagePath)
                    && TryGetImagePixelSize(imagePath, out var nw, out var nh))
                {
                    var s = Math.Min(maxW / nw, maxH / nh);
                    gridW = Math.Round(nw * s);
                    gridH = Math.Round(nh * s);
                }
                else
                {
                    gridW = Math.Min(ViewerFallbackContentWidth, maxW);
                    gridH = Math.Min(ViewerFallbackContentHeight, maxH);
                }

                Width = gridW + 16;
                Height = Math.Min(wa.Height, gridH + 160);
            }

            void CenterOnWorkArea()
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + Math.Round((wa.Width - ActualWidth) * 0.5);
                Top = wa.Top + Math.Round((wa.Height - ActualHeight) * 0.5);
            }

            static Border BuildNavChrome(string glyph, string automationName)
            {
                var b = new Border
                {
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child = new TextBlock
                    {
                        Text = glyph,
                        FontSize = 56,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0.72
                    }
                };
                AutomationProperties.SetName(b, automationName);
                return b;
            }

            void Navigate(int delta)
            {
                var n = _index + delta;
                if (n < 0 || n >= _paths.Count) return;
                _index = n;
                ReloadCurrent();
            }

            void UpdateNavChrome()
            {
                _navLeft.Visibility = _index <= 0 ? Visibility.Hidden : Visibility.Visible;
                _navRight.Visibility = _index >= _paths.Count - 1 ? Visibility.Hidden : Visibility.Visible;
            }

            void SetViewMode(bool actualSize)
            {
                _actualSize = actualSize;
                _image.ReleaseMouseCapture();
                _scroll.HorizontalScrollBarVisibility = actualSize ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                _scroll.VerticalScrollBarVisibility = actualSize ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                _fitButton.IsEnabled = actualSize;
                _actualButton.IsEnabled = !actualSize;
                _image.Cursor = actualSize ? Cursors.Hand : Cursors.Arrow;
                UpdateImageDimensions();
                _scroll.ScrollToHome();
            }

            void UpdateImageDimensions()
            {
                if (_actualSize && _image.Source is BitmapSource bitmap)
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    _image.Width = bitmap.PixelWidth / dpi.DpiScaleX;
                    _image.Height = bitmap.PixelHeight / dpi.DpiScaleY;
                }
                else
                {
                    _image.Width = Math.Max(1, _photoSurface.ActualWidth);
                    _image.Height = Math.Max(1, _photoSurface.ActualHeight);
                }
            }

            async void ReloadCurrent()
            {
                if (_paths.Count == 0) return;
                var path = _paths[_index];
                Title = Path.GetFileName(path) + " — PixelVault";

                var version = ++_loadVersion;

                _image.Source = null;
                // Hidden (not Collapsed) keeps the grid cell measured so the bitmap has a non-zero arrange slot.
                _image.Visibility = Visibility.Hidden;

                _footerSlot.Children.Clear();
                var surfaceW = _photoSurface.ActualWidth;
                var footerWidth = (int)Math.Max(400, Math.Round(surfaceW > 1 ? surfaceW - 48 : 800));
                var ctx = _host.TryGetLibraryTimelineCaptureContextForViewer(path);
                if (ctx != null)
                {
                    var footer = _host.BuildLibraryTimelineCaptureFooter(footerWidth, path, ctx);
                    if (footer != null) _footerSlot.Children.Add(footer);
                }
                else
                {
                    _footerSlot.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(220, 20, 28, 36)),
                        Padding = new Thickness(12, 10, 12, 10),
                        Child = new TextBlock
                        {
                            Text = Path.GetFileName(path),
                            Foreground = Brushes.White,
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap
                        }
                    });
                }

                _status.Text = "Loading...";
                UpdateNavChrome();
                _scroll.ScrollToHome();
                try
                {
                    // Keep the original pixels outside the shared thumbnail cache.
                    var bitmap = await Task.Run(() =>
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = stream;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    });
                    if (version != _loadVersion) return;
                    _image.Source = bitmap;
                    _image.Visibility = Visibility.Visible;
                    _status.Text = bitmap.PixelWidth + " \u00d7 " + bitmap.PixelHeight;
                    UpdateImageDimensions();
                }
                catch (Exception)
                {
                    if (version != _loadVersion) return;
                    _status.Text = "Unable to load image";
                }
            }
        }
    }
}
