using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
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
            SizeChanged += PhotographyGalleryWindow_SizeChanged;
        }

        void PhotographyGalleryWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyPhotographyGalleryImageMaxHeights();
        }

        void ApplyPhotographyGalleryImageMaxHeights()
        {
            var maxH = Math.Max(320d, ActualHeight - 220d);
            if (GalleryList.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;
            for (var i = 0; i < GalleryList.Items.Count; i++)
            {
                var container = GalleryList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (container == null) continue;
                var image = FindVisualChild<Image>(container);
                if (image != null) image.MaxHeight = maxH;
            }
        }

        static T FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
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

        internal void RequestGalleryReload() => RunLoad(false);

        internal void RequestGalleryForceReload() => RunLoad(true);

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
                            GalleryList.UpdateLayout();
                            ApplyPhotographyGalleryImageMaxHeights();
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
            if (border.Tag as string == "PvPhotoGalleryItemWired") return;
            border.Tag = "PvPhotoGalleryItemWired";
            if (border.Child is not StackPanel stack || stack.Children.Count < 1) return;
            if (stack.Children[0] is not Grid imageHost) return;
            Image image = null;
            Button starButton = null;
            TextBlock starGlyph = null;
            foreach (var child in imageHost.Children)
            {
                if (image == null && child is Image img) image = img;
                else if (starButton == null && child is Button b) starButton = b;
            }
            if (image == null) return;
            if (starButton != null) starGlyph = FindVisualChild<TextBlock>(starButton);
            image.Source = null;
            var maxH = Math.Max(320d, ActualHeight > 120 ? ActualHeight - 220d : 700d);
            image.MaxHeight = maxH;
            var winW = ActualWidth > 80 ? ActualWidth : 1200d;
            var decode = (int)Math.Max(960, Math.Min(4800, winW * 3.5));
            _host.QueueImageLoad(image, entry.FullPath, decode, loaded => { image.Source = loaded; });

            void ApplyStarGlyphVisual()
            {
                if (starGlyph == null) return;
                starGlyph.Text = entry.Starred ? "\u2605" : "\u2606";
                starGlyph.Foreground = entry.Starred
                    ? new SolidColorBrush(Color.FromRgb(0xEA, 0xC5, 0x4F))
                    : new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            }
            ApplyStarGlyphVisual();
            PropertyChangedEventHandler onEntryStarChanged = null;
            onEntryStarChanged = delegate(object _, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(PhotographyGalleryEntry.Starred) || args.PropertyName == nameof(PhotographyGalleryEntry.StarGlyph))
                    ApplyStarGlyphVisual();
            };
            entry.PropertyChanged += onEntryStarChanged;
            border.Unloaded += delegate { entry.PropertyChanged -= onEntryStarChanged; };

            if (starButton != null && _host.TogglePhotoStarred != null)
            {
                void ShowStarChrome(bool show)
                {
                    starButton.Opacity = show ? 1d : 0d;
                    starButton.IsHitTestVisible = show;
                }
                ShowStarChrome(false);
                border.MouseEnter += delegate { ShowStarChrome(true); };
                border.MouseLeave += delegate { ShowStarChrome(false); };
                starButton.Click += delegate(object s, RoutedEventArgs ev)
                {
                    ev.Handled = true;
                    _host.TogglePhotoStarred(entry);
                };
            }

            var menu = new ContextMenu();
            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += delegate { _host.OpenImageWithShell(entry.FullPath); };
            var openFolderItem = new MenuItem { Header = "Open Folder" };
            openFolderItem.Click += delegate
            {
                if (_host.OpenContainingFolderForFile != null) _host.OpenContainingFolderForFile(entry.FullPath);
            };
            var editItem = new MenuItem { Header = "Edit Metadata" };
            editItem.Click += delegate
            {
                if (_host.OpenMetadataEditorForFile != null) _host.OpenMetadataEditorForFile(entry.FullPath);
            };
            var starMenuItem = new MenuItem { Header = "Add star" };
            var photoTagMenuItem = new MenuItem { Header = "Add Game Photography tag" };
            menu.Opened += delegate
            {
                starMenuItem.Header = entry.Starred ? "Remove star" : "Add star";
                var hasPhoto = _host.GetFileHasGamePhotographyTag != null && _host.GetFileHasGamePhotographyTag(entry.FullPath);
                photoTagMenuItem.Header = hasPhoto ? "Remove Game Photography tag" : "Add Game Photography tag";
            };
            starMenuItem.Click += delegate
            {
                _host.TogglePhotoStarred?.Invoke(entry);
            };
            photoTagMenuItem.Click += delegate
            {
                _host.ToggleGamePhotographyTagForFile?.Invoke(entry.FullPath);
            };
            var copyPathItem = new MenuItem { Header = "Copy File Path" };
            copyPathItem.Click += delegate
            {
                if (_host.CopyFilePathToClipboard != null) _host.CopyFilePathToClipboard(entry.FullPath);
            };
            menu.Items.Add(openItem);
            menu.Items.Add(openFolderItem);
            menu.Items.Add(editItem);
            menu.Items.Add(starMenuItem);
            menu.Items.Add(photoTagMenuItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(copyPathItem);
            border.ContextMenu = menu;
        }
    }
}
