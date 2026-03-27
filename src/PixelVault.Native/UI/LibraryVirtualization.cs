using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        sealed class VirtualizedRowDefinition
        {
            public double Height;
            public Func<FrameworkElement> Build;
        }

        sealed class VirtualizedRowHost
        {
            public ScrollViewer ScrollViewer;
            public Border TopSpacer;
            public StackPanel VisibleRowsPanel;
            public Border BottomSpacer;
            public List<VirtualizedRowDefinition> Rows = new List<VirtualizedRowDefinition>();
            public int FirstVisibleIndex = -1;
            public int LastVisibleIndex = -1;
            public double ViewportHeightFallback = 720;
        }

        VirtualizedRowHost CreateVirtualizedRowHost(Thickness margin, Brush background)
        {
            var host = new VirtualizedRowHost();
            var outerPanel = new StackPanel();
            host.TopSpacer = new Border { Height = 0 };
            host.VisibleRowsPanel = new StackPanel();
            host.BottomSpacer = new Border { Height = 0 };
            outerPanel.Children.Add(host.TopSpacer);
            outerPanel.Children.Add(host.VisibleRowsPanel);
            outerPanel.Children.Add(host.BottomSpacer);
            host.ScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = margin,
                Background = background,
                Content = outerPanel
            };
            host.ScrollViewer.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                if (Math.Abs(e.VerticalChange) > 0.1 || Math.Abs(e.ViewportHeightChange) > 0.1) RefreshVirtualizedRowHost(host);
            };
            host.ScrollViewer.SizeChanged += delegate
            {
                host.ScrollViewer.Dispatcher.BeginInvoke(new Action(delegate
                {
                    RefreshVirtualizedRowHost(host);
                }), DispatcherPriority.Background);
            };
            return host;
        }

        void SetVirtualizedRows(VirtualizedRowHost host, IEnumerable<VirtualizedRowDefinition> rows, bool resetScroll = false, double? restoreOffset = null)
        {
            if (host == null) return;
            host.Rows = rows == null ? new List<VirtualizedRowDefinition>() : new List<VirtualizedRowDefinition>(rows);
            host.FirstVisibleIndex = -1;
            host.LastVisibleIndex = -1;
            if (host.ScrollViewer != null)
            {
                if (resetScroll) host.ScrollViewer.ScrollToVerticalOffset(0);
                else if (restoreOffset.HasValue) host.ScrollViewer.ScrollToVerticalOffset(Math.Max(0, restoreOffset.Value));
            }
            RefreshVirtualizedRowHost(host);
            if (!resetScroll && restoreOffset.HasValue && host.ScrollViewer != null)
            {
                var wantedOffset = Math.Max(0, restoreOffset.Value);
                host.ScrollViewer.Dispatcher.BeginInvoke(new Action(delegate
                {
                    var maxOffset = Math.Max(0, host.ScrollViewer.ExtentHeight - host.ScrollViewer.ViewportHeight);
                    host.ScrollViewer.ScrollToVerticalOffset(Math.Min(wantedOffset, maxOffset));
                    RefreshVirtualizedRowHost(host);
                }), DispatcherPriority.Background);
            }
        }

        void RefreshVirtualizedRowHost(VirtualizedRowHost host)
        {
            if (host == null || host.ScrollViewer == null || host.VisibleRowsPanel == null || host.TopSpacer == null || host.BottomSpacer == null) return;
            var rows = host.Rows ?? new List<VirtualizedRowDefinition>();
            if (rows.Count == 0)
            {
                host.TopSpacer.Height = 0;
                host.BottomSpacer.Height = 0;
                host.VisibleRowsPanel.Children.Clear();
                host.FirstVisibleIndex = -1;
                host.LastVisibleIndex = -1;
                return;
            }

            var viewportHeight = host.ScrollViewer.ViewportHeight;
            if (viewportHeight <= 0) viewportHeight = host.ScrollViewer.ActualHeight;
            if (viewportHeight <= 0) viewportHeight = host.ViewportHeightFallback;
            var offset = Math.Max(0, host.ScrollViewer.VerticalOffset);
            var overscan = Math.Max(480, viewportHeight * 0.9d);
            var minY = Math.Max(0, offset - overscan);
            var maxY = offset + viewportHeight + overscan;

            double totalHeight = 0;
            foreach (var row in rows) totalHeight += Math.Max(1, row == null ? 1 : row.Height);

            var firstIndex = rows.Count - 1;
            double topHeight = 0;
            double cursor = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var rowHeight = Math.Max(1, rows[i] == null ? 1 : rows[i].Height);
                if (cursor + rowHeight >= minY)
                {
                    firstIndex = i;
                    topHeight = cursor;
                    break;
                }
                cursor += rowHeight;
            }

            var lastIndex = firstIndex;
            cursor = topHeight;
            for (int i = firstIndex; i < rows.Count; i++)
            {
                var rowHeight = Math.Max(1, rows[i] == null ? 1 : rows[i].Height);
                cursor += rowHeight;
                lastIndex = i;
                if (cursor >= maxY) break;
            }

            if (firstIndex == host.FirstVisibleIndex && lastIndex == host.LastVisibleIndex) return;

            host.FirstVisibleIndex = firstIndex;
            host.LastVisibleIndex = lastIndex;
            host.VisibleRowsPanel.Children.Clear();

            double renderedHeight = 0;
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                var row = rows[i];
                if (row == null || row.Build == null) continue;
                var element = row.Build();
                if (element == null) continue;
                host.VisibleRowsPanel.Children.Add(element);
                renderedHeight += Math.Max(1, row.Height);
            }

            host.TopSpacer.Height = topHeight;
            host.BottomSpacer.Height = Math.Max(0, totalHeight - topHeight - renderedHeight);
        }

        int CalculateVirtualizedTileColumns(ScrollViewer scrollViewer, int tileWidth, double horizontalGap, double widthAllowance)
        {
            if (tileWidth <= 0) return 1;
            var viewportWidth = scrollViewer == null ? 0 : scrollViewer.ViewportWidth;
            if (viewportWidth <= 0 && scrollViewer != null) viewportWidth = scrollViewer.ActualWidth;
            var availableWidth = Math.Max(tileWidth, viewportWidth - Math.Max(0, widthAllowance));
            var columns = (int)Math.Floor((availableWidth + horizontalGap) / (tileWidth + horizontalGap));
            return Math.Max(1, columns);
        }

        Border CreateLibraryDetailTile(string file, int size, Func<bool> shouldLoad, Action<string> openSingleFileMetadataEditor, Action<string, bool, bool> updateDetailSelection, HashSet<string> selectedDetailFiles, Action refreshDetailSelectionUi)
        {
            var isVideoFile = IsVideo(file);
            var tile = new Border
            {
                Width = size,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(0),
                Background = Brush("#10181D"),
                BorderBrush = Brush("#2B3A44"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = file
            };
            var presenter = new Grid();
            var placeholder = new TextBlock { Text = System.IO.Path.GetFileName(file), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), Foreground = Brush("#F1E9DA"), TextAlignment = TextAlignment.Center };
            var image = new Image { Width = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
            MediaElement videoPreviewMedia = null;
            TextBlock videoPreviewStatus = null;
            Border videoPreviewHint = null;
            Border videoDurationBadge = null;
            Border videoInfoBadge = null;
            TextBlock videoDurationText = null;
            TextBlock videoInfoText = null;
            DispatcherTimer videoPreviewStopTimer = null;
            bool videoPreviewReady = false;
            bool videoPreviewHovered = false;
            bool videoPreviewOpeningStarted = false;
            Action<VideoClipInfo> applyVideoInfo = delegate(VideoClipInfo info)
            {
                var fileName = System.IO.Path.GetFileName(file);
                if (!isVideoFile)
                {
                    tile.ToolTip = fileName + Environment.NewLine + GetLibraryDate(file).ToString("yyyy-MM-dd h:mm:ss tt");
                    return;
                }

                if (videoDurationBadge != null && videoDurationText != null)
                {
                    var durationLabel = info == null ? string.Empty : FormatVideoDuration(info.DurationSeconds);
                    videoDurationText.Text = durationLabel;
                    videoDurationBadge.Visibility = string.IsNullOrWhiteSpace(durationLabel) ? Visibility.Collapsed : Visibility.Visible;
                }

                if (videoInfoBadge != null && videoInfoText != null)
                {
                    var detailParts = new List<string>();
                    if (info != null && info.Width > 0 && info.Height > 0) detailParts.Add(info.Width + "x" + info.Height);
                    if (info != null && info.FrameRate > 0) detailParts.Add(FormatVideoFrameRate(info.FrameRate));
                    if (info != null && info.HasAudio) detailParts.Add("Audio");
                    videoInfoText.Text = string.Join(" | ", detailParts);
                    videoInfoBadge.Visibility = string.IsNullOrWhiteSpace(videoInfoText.Text) ? Visibility.Collapsed : Visibility.Visible;
                }

                var toolTipParts = new List<string>
                {
                    fileName,
                    GetLibraryDate(file).ToString("yyyy-MM-dd h:mm:ss tt")
                };
                var summary = FormatVideoClipInfoSummary(info);
                if (!string.IsNullOrWhiteSpace(summary)) toolTipParts.Add("Clip: " + summary);
                if (info != null && !string.IsNullOrWhiteSpace(info.VideoCodec)) toolTipParts.Add("Codec: " + info.VideoCodec);
                tile.ToolTip = string.Join(Environment.NewLine, toolTipParts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray());
            };
            presenter.Children.Add(placeholder);
            presenter.Children.Add(image);
            if (isVideoFile)
            {
                videoPreviewMedia = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsMuted = true,
                    Volume = 0,
                    Visibility = Visibility.Hidden,
                    IsHitTestVisible = false
                };
                videoPreviewStatus = new TextBlock
                {
                    Text = "Loading preview...",
                    Foreground = Brushes.White,
                    Background = Brush("#8A10181D"),
                    Padding = new Thickness(10, 4, 10, 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                presenter.Children.Add(videoPreviewMedia);
                presenter.Children.Add(videoPreviewStatus);
                presenter.Children.Add(new Border
                {
                    Background = Brush("#AA234A63"),
                    BorderBrush = Brush("#7AB4E3"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8, 8, 0, 0),
                    Padding = new Thickness(10, 5, 10, 5),
                    Child = new TextBlock
                    {
                        Text = "CLIP",
                        Foreground = Brushes.White,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                });
                videoDurationText = new TextBlock
                {
                    Foreground = Brush("#F5FBFF"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold
                };
                videoDurationBadge = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(8, 0, 0, 8),
                    Background = Brush("#A6141C21"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    Visibility = Visibility.Collapsed,
                    Child = videoDurationText
                };
                presenter.Children.Add(videoDurationBadge);
                videoInfoText = new TextBlock
                {
                    Foreground = Brush("#DCE8EF"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };
                videoInfoBadge = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 8, 8),
                    Background = Brush("#A6141C21"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    Visibility = Visibility.Collapsed,
                    Child = videoInfoText
                };
                presenter.Children.Add(videoInfoBadge);
                videoPreviewHint = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 8, 38),
                    Background = Brush("#9C0F151A"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    Child = new TextBlock
                    {
                        Text = "Hover to preview",
                        Foreground = Brush("#DCE8EF"),
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold
                    }
                };
                presenter.Children.Add(videoPreviewHint);
                videoPreviewStopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                applyVideoInfo(TryLoadCachedVideoClipInfo(file));
                WarmVideoClipInfo(file, delegate(VideoClipInfo info)
                {
                    tile.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!string.Equals(tile.Tag as string, file, StringComparison.OrdinalIgnoreCase)) return;
                        applyVideoInfo(info);
                    }), DispatcherPriority.Background);
                });
                WarmVideoPreviewClip(file, Math.Max(640, size * 2));
                try
                {
                    videoPreviewMedia.Source = new Uri(file);
                    videoPreviewOpeningStarted = true;
                    videoPreviewMedia.Play();
                }
                catch
                {
                    videoPreviewOpeningStarted = false;
                }
            }
            else
            {
                applyVideoInfo(null);
            }
            tile.Child = presenter;
            QueueImageLoad(image, file, size * 2, delegate(BitmapImage loaded)
            {
                image.Source = loaded;
                image.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            }, false, shouldLoad);
            tile.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var clicked = sender as Border;
                var clickedFile = clicked == null ? string.Empty : clicked.Tag as string;
                var additive = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
                updateDetailSelection(clickedFile, additive, additive);
                if (e.ClickCount >= 2 && !string.IsNullOrWhiteSpace(clickedFile)) OpenWithShell(clickedFile);
            };
            tile.MouseRightButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var clicked = sender as Border;
                var clickedFile = clicked == null ? string.Empty : clicked.Tag as string;
                var additive = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
                if (string.IsNullOrWhiteSpace(clickedFile)) return;
                if (selectedDetailFiles.Contains(clickedFile))
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                }
                else
                {
                    updateDetailSelection(clickedFile, additive, additive);
                }
            };
            tile.MouseEnter += delegate
            {
                if (!isVideoFile) return;
                if (videoPreviewMedia == null || videoPreviewStatus == null) return;
                videoPreviewHovered = true;
                videoPreviewStopTimer.Stop();
                videoPreviewHint.Visibility = Visibility.Collapsed;
                if (videoPreviewReady)
                {
                    videoPreviewStatus.Visibility = Visibility.Collapsed;
                    videoPreviewMedia.Visibility = Visibility.Visible;
                    try
                    {
                        videoPreviewMedia.Play();
                        videoPreviewStopTimer.Start();
                    }
                    catch
                    {
                        videoPreviewMedia.Visibility = Visibility.Collapsed;
                        videoPreviewHint.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    videoPreviewStatus.Visibility = Visibility.Visible;
                    if (!videoPreviewOpeningStarted)
                    {
                        try
                        {
                            videoPreviewMedia.Source = new Uri(file);
                            videoPreviewOpeningStarted = true;
                            videoPreviewMedia.Play();
                        }
                        catch
                        {
                            videoPreviewStatus.Visibility = Visibility.Collapsed;
                            videoPreviewHint.Visibility = Visibility.Visible;
                        }
                    }
                }
            };
            tile.MouseLeave += delegate
            {
                if (!isVideoFile) return;
                if (videoPreviewMedia == null || videoPreviewStatus == null) return;
                videoPreviewHovered = false;
                videoPreviewStopTimer.Stop();
                videoPreviewStatus.Visibility = Visibility.Collapsed;
                videoPreviewMedia.Visibility = Visibility.Collapsed;
                videoPreviewHint.Visibility = Visibility.Visible;
                try
                {
                    videoPreviewMedia.Pause();
                    videoPreviewMedia.Position = TimeSpan.FromMilliseconds(250);
                }
                catch
                {
                }
            };
            if (videoPreviewMedia != null && videoPreviewStatus != null && videoPreviewHint != null && videoPreviewStopTimer != null)
            {
                videoPreviewStopTimer.Tick += delegate
                {
                    videoPreviewStopTimer.Stop();
                    videoPreviewHovered = false;
                    videoPreviewStatus.Visibility = Visibility.Collapsed;
                    videoPreviewMedia.Visibility = Visibility.Collapsed;
                    videoPreviewHint.Visibility = Visibility.Visible;
                    try
                    {
                        videoPreviewMedia.Pause();
                        videoPreviewMedia.Position = TimeSpan.FromMilliseconds(250);
                    }
                    catch
                    {
                    }
                };
                videoPreviewMedia.MediaOpened += delegate
                {
                    videoPreviewReady = true;
                    try
                    {
                        videoPreviewMedia.Position = TimeSpan.FromMilliseconds(250);
                        if (videoPreviewHovered) videoPreviewMedia.Play();
                        else videoPreviewMedia.Pause();
                    }
                    catch
                    {
                    }
                    if (videoPreviewHovered)
                    {
                        videoPreviewStatus.Visibility = Visibility.Collapsed;
                        videoPreviewMedia.Visibility = Visibility.Visible;
                        videoPreviewHint.Visibility = Visibility.Collapsed;
                        videoPreviewStopTimer.Stop();
                        videoPreviewStopTimer.Start();
                        try
                        {
                            videoPreviewMedia.Play();
                        }
                        catch
                        {
                            videoPreviewStatus.Visibility = Visibility.Collapsed;
                            videoPreviewMedia.Visibility = Visibility.Collapsed;
                            videoPreviewHint.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        videoPreviewStatus.Visibility = Visibility.Collapsed;
                        videoPreviewMedia.Visibility = Visibility.Hidden;
                        videoPreviewHint.Visibility = Visibility.Visible;
                    }
                };
                videoPreviewMedia.MediaEnded += delegate
                {
                    try
                    {
                        videoPreviewMedia.Position = TimeSpan.FromMilliseconds(250);
                        if (videoPreviewHovered) videoPreviewMedia.Play();
                    }
                    catch
                    {
                        videoPreviewStopTimer.Stop();
                        videoPreviewStatus.Visibility = Visibility.Collapsed;
                        videoPreviewMedia.Visibility = Visibility.Hidden;
                        videoPreviewHint.Visibility = Visibility.Visible;
                    }
                };
                videoPreviewMedia.MediaFailed += delegate
                {
                    videoPreviewReady = false;
                    videoPreviewOpeningStarted = false;
                    videoPreviewHovered = false;
                    videoPreviewStopTimer.Stop();
                    videoPreviewStatus.Visibility = Visibility.Collapsed;
                    videoPreviewMedia.Visibility = Visibility.Hidden;
                    videoPreviewHint.Visibility = Visibility.Visible;
                    try
                    {
                        videoPreviewMedia.Stop();
                    }
                    catch
                    {
                    }
                };
            }
            var contextMenu = new ContextMenu();
            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += delegate { OpenWithShell(file); };
            var openFolderItem = new MenuItem { Header = "Open Folder" };
            openFolderItem.Click += delegate { OpenFolder(System.IO.Path.GetDirectoryName(file) ?? string.Empty); };
            var editItem = new MenuItem { Header = "Edit Metadata" };
            editItem.Click += delegate { openSingleFileMetadataEditor(file); };
            var copyPathItem = new MenuItem { Header = "Copy File Path" };
            copyPathItem.Click += delegate
            {
                try { Clipboard.SetText(file); } catch { }
            };
            contextMenu.Items.Add(openItem);
            if (isVideoFile)
            {
                var openPreviewClipItem = new MenuItem { Header = "Open 10s Preview Clip" };
                openPreviewClipItem.Click += async delegate
                {
                    openPreviewClipItem.IsEnabled = false;
                    if (status != null) status.Text = "Generating clip preview";
                    try
                    {
                        var previewPath = await Task.Run(delegate { return EnsureVideoPreviewClip(file, Math.Max(640, size * 2)); });
                        if (!string.IsNullOrWhiteSpace(previewPath) && System.IO.File.Exists(previewPath))
                        {
                            OpenWithShell(previewPath);
                            if (status != null) status.Text = "Opened clip preview";
                        }
                        else
                        {
                            if (status != null) status.Text = "Clip preview unavailable";
                            MessageBox.Show("PixelVault could not generate a preview clip for this video. Check the FFmpeg path in Path Settings and try again.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (status != null) status.Text = "Clip preview unavailable";
                        MessageBox.Show("PixelVault could not generate a preview clip for this video." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally
                    {
                        openPreviewClipItem.IsEnabled = true;
                    }
                };
                var copyClipDetailsItem = new MenuItem { Header = "Copy Clip Details" };
                copyClipDetailsItem.Click += delegate
                {
                    try
                    {
                        var info = EnsureVideoClipInfo(file);
                        var lines = new List<string>
                        {
                            System.IO.Path.GetFileName(file),
                            file,
                            "Captured: " + GetLibraryDate(file).ToString("yyyy-MM-dd h:mm:ss tt")
                        };
                        var summary = FormatVideoClipInfoSummary(info);
                        if (!string.IsNullOrWhiteSpace(summary)) lines.Add("Clip: " + summary);
                        if (info != null && !string.IsNullOrWhiteSpace(info.VideoCodec)) lines.Add("Codec: " + info.VideoCodec);
                        Clipboard.SetText(string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()));
                        if (status != null) status.Text = "Clip details copied";
                    }
                    catch
                    {
                    }
                };
                contextMenu.Items.Add(openPreviewClipItem);
                contextMenu.Items.Add(copyClipDetailsItem);
            }
            contextMenu.Items.Add(openFolderItem);
            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(copyPathItem);
            tile.ContextMenu = contextMenu;
            return tile;
        }
    }
}
