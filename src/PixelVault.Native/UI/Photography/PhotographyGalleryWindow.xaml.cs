using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    public partial class PhotographyGalleryWindow : Window
    {
        readonly PhotographyGalleryHost _host;

        public PhotographyGalleryWindow(PhotographyGalleryHost host, Window owner)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            InitializeComponent();
            Owner = owner;
            Title = "Photography — PixelVault " + host.AppVersion;
            EmptyStateText.Text = "No captures tagged with \"" + host.GamePhotographyTag + "\" yet."
                + Environment.NewLine
                + "Add the tag in metadata review or manual import, then refresh.";

            OpenLibraryButton.Click += delegate { _host.OpenLibraryFolder(); };
            RefreshButton.Click += delegate { RunLoad(true); };
            GalleryList.MouseDoubleClick += delegate
            {
                if (GalleryList.SelectedItem is PhotographyGalleryEntry g && !string.IsNullOrWhiteSpace(g.FullPath))
                    _host.OpenImageWithShell(g.FullPath);
            };
            Loaded += delegate { RunLoad(false); };
            GalleryList.PreviewMouseWheel += GalleryList_OnPreviewMouseWheel;
        }

        static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            if (root is ScrollViewer existing) return existing;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

        void GalleryList_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scroll = FindScrollViewer(GalleryList);
            if (scroll == null) return;
            e.Handled = true;
            const double pixelsPerWheelLine = 28d;
            var deltaLines = e.Delta / 120d;
            var offset = scroll.VerticalOffset - deltaLines * pixelsPerWheelLine;
            if (offset < 0d) offset = 0d;
            var max = Math.Max(0d, scroll.ExtentHeight - scroll.ViewportHeight);
            if (offset > max) offset = max;
            scroll.ScrollToVerticalOffset(offset);
        }

        void RunLoad(bool forceRefresh)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            GalleryList.Visibility = Visibility.Collapsed;
            EmptyStateText.Visibility = Visibility.Collapsed;
            ListHostBorder.Visibility = Visibility.Collapsed;
            GalleryMetaText.Text = forceRefresh ? "Scanning library…" : "Loading…";
            if (forceRefresh) GalleryList.ItemsSource = null;

            Task.Run(() =>
            {
                _host.PrepareExifOnBackgroundThread();
                return _host.LoadTaggedImagePaths(forceRefresh);
            }).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        if (!IsLoaded) return;

                        if (t.IsFaulted)
                        {
                            var inner = t.Exception == null ? null : t.Exception.Flatten().InnerException;
                            var msg = inner == null ? "Photography gallery failed to load." : inner.Message;
                            _host.LogError("PhotographyGalleryWindow", t.Exception);
                            MessageBox.Show(msg, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                            Close();
                            return;
                        }

                        var files = t.Status == TaskStatus.RanToCompletion && t.Result != null ? t.Result : new List<string>();
                        var entries = _host.BuildEntries(files);
                        var shortRoot = _host.LibraryRoot;
                        if (shortRoot.Length > 64)
                            shortRoot = "…" + shortRoot.Substring(shortRoot.Length - 62);
                        GalleryMetaText.Text = entries.Count == 0
                            ? "No tagged photos · " + shortRoot
                            : entries.Count + " photo" + (entries.Count == 1 ? string.Empty : "s") + " · " + shortRoot;

                        if (entries.Count == 0)
                        {
                            GalleryList.ItemsSource = null;
                            GalleryList.Visibility = Visibility.Collapsed;
                            ListHostBorder.Visibility = Visibility.Collapsed;
                            EmptyStateText.Visibility = Visibility.Visible;
                            LoadingPanel.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            EmptyStateText.Visibility = Visibility.Collapsed;
                            ListHostBorder.Visibility = Visibility.Visible;
                            GalleryList.ItemsSource = entries;
                            GalleryList.Visibility = Visibility.Visible;
                            LoadingPanel.Visibility = Visibility.Collapsed;
                        }

                        _host.SetAppStatus("Photography gallery ready");
                    }
                    catch (Exception ex)
                    {
                        _host.LogError("PhotographyGalleryWindow", ex);
                        MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        void GalleryItemRoot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.DataContext is not PhotographyGalleryEntry entry || string.IsNullOrWhiteSpace(entry.FullPath)) return;
            if (border.Child is not StackPanel stack || stack.Children.Count < 1) return;
            if (stack.Children[0] is not Image image) return;
            image.Source = null;
            var winW = ActualWidth > 80 ? ActualWidth : 1200d;
            var decode = (int)Math.Max(960, Math.Min(4800, winW * 3.5));
            _host.QueueImageLoad(image, entry.FullPath, decode, loaded => { image.Source = loaded; });
        }
    }
}
