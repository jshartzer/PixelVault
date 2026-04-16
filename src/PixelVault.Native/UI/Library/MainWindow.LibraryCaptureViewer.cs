using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// <summary>PV-PLN-LIBPV-001 — Open a non-modal viewer (fixed comfortable size); navigation uses <see cref="LibraryBrowserWorkingSet.DetailFilesDisplayOrder"/> (images only).</summary>
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
            const int ViewerDecodeMaxEdge = 4096;
            /// <summary>One consistent viewer size (DIP); not tied to the main window.</summary>
            const double ViewerWindowWidth = 1180;
            const double ViewerWindowHeight = 760;
            readonly MainWindow _host;
            readonly List<string> _paths;
            int _index;
            readonly Image _image;
            readonly StackPanel _footerSlot;
            readonly Border _navLeft;
            readonly Border _navRight;

            internal LibraryCaptureViewerWindow(MainWindow host, Window ownerWindow, List<string> paths, int startIndex)
            {
                _host = host;
                _paths = paths ?? new List<string>();
                _index = _paths.Count == 0 ? 0 : Math.Max(0, Math.Min(startIndex, _paths.Count - 1));

                Title = "Capture";
                Background = new SolidColorBrush(Color.FromRgb(16, 24, 29));
                ResizeMode = ResizeMode.CanResize;
                WindowStartupLocation = WindowStartupLocation.Manual;
                ShowInTaskbar = true;
                ShowActivated = true;
                if (ownerWindow != null)
                    Owner = ownerWindow;

                var wa = SystemParameters.WorkArea;
                Width = Math.Min(ViewerWindowWidth, Math.Max(640, wa.Width - 32));
                Height = Math.Min(ViewerWindowHeight, Math.Max(480, wa.Height - 32));
                Left = wa.Left + Math.Round((wa.Width - Width) * 0.5);
                Top = wa.Top + Math.Round((wa.Height - Height) * 0.5);

                // UniformToFill: scale without distortion so the image meets left/right edges (no letterboxing);
                // excess height is cropped in the photo row, matching a full-bleed capture viewer.
                _image = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

                _footerSlot = new StackPanel { Orientation = Orientation.Vertical };

                _navLeft = BuildNavChrome("\u2039", "Previous capture");
                _navLeft.MouseLeftButtonDown += delegate { Navigate(-1); };
                _navRight = BuildNavChrome("\u203A", "Next capture");
                _navRight.MouseLeftButtonDown += delegate { Navigate(1); };

                // Photo fills the client area; timeline footer (badges, comment, time) sits on a scrim over the
                // bottom of the image; prev/next are narrow side strips above the footer so they stay clickable.
                var photoGrid = new Grid { ClipToBounds = true };
                photoGrid.Children.Add(_image);
                var footerOverlay = new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    Child = _footerSlot
                };
                photoGrid.Children.Add(footerOverlay);
                _navLeft.HorizontalAlignment = HorizontalAlignment.Left;
                _navLeft.VerticalAlignment = VerticalAlignment.Stretch;
                _navLeft.Width = 88;
                _navRight.HorizontalAlignment = HorizontalAlignment.Right;
                _navRight.VerticalAlignment = VerticalAlignment.Stretch;
                _navRight.Width = 88;
                photoGrid.Children.Add(_navLeft);
                photoGrid.Children.Add(_navRight);

                Content = photoGrid;

                Loaded += delegate
                {
                    ReloadCurrent();
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
                    _image.Source = null;
                };
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

            void ReloadCurrent()
            {
                if (_paths.Count == 0) return;
                var path = _paths[_index];
                Title = Path.GetFileName(path) + " — PixelVault";

                _image.Source = null;
                // Hidden (not Collapsed) keeps the grid cell measured so the bitmap has a non-zero arrange slot.
                _image.Visibility = Visibility.Hidden;

                _footerSlot.Children.Clear();
                var footerWidth = (int)Math.Max(400, Math.Round(ActualWidth > 1 ? ActualWidth - 48 : Math.Max(Width, 400) - 48));
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

                var layoutW = ActualWidth > 1 ? ActualWidth : Math.Max(Width, 640);
                var decode = Math.Min(ViewerDecodeMaxEdge, Math.Max(640, (int)Math.Round(layoutW * 2)));
                // Do not pass shouldLoad: QueueImageLoad evaluates it on a background thread, and reading
                // WPF DispatcherObject.IsLoaded from Task.Run faults or fails the load for this window.
                _host.QueueImageLoad(
                    _image,
                    path,
                    decode,
                    delegate(BitmapImage bmp)
                    {
                        _image.Source = bmp;
                        _image.Visibility = Visibility.Visible;
                    },
                    true,
                    null);

                UpdateNavChrome();
            }
        }
    }
}
