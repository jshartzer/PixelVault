using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        static readonly Brush LibraryTimelineTileFooterScrimBrush = CreateLibraryTimelineTileFooterScrimBrush();

        static Brush CreateLibraryTimelineTileFooterScrimBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 20, 28, 36), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 10, 18, 24), 0.4));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(244, 7, 12, 17), 1));
            brush.Freeze();
            return brush;
        }

        internal sealed class VirtualizedRowDefinition
        {
            public double Height;
            public Func<FrameworkElement> Build;
        }

        internal sealed class VirtualizedRowHost
        {
            public ScrollViewer ScrollViewer;
            public Border TopSpacer;
            public StackPanel VisibleRowsPanel;
            public Border BottomSpacer;
            public List<VirtualizedRowDefinition> Rows = new List<VirtualizedRowDefinition>();
            public int FirstVisibleIndex = -1;
            public int LastVisibleIndex = -1;
            public double ViewportHeightFallback = 720;
            public Action BeforeVisibleRowsRebuilt;
            public Action AfterVisibleRowsRebuilt;
            /// <summary>When true, visible row <see cref="FrameworkElement"/>s are keyed by row index and reused across scroll refreshes (same <see cref="Rows"/> model). Cleared on <see cref="SetVirtualizedRows"/>. Do not use when <see cref="BeforeVisibleRowsRebuilt"/> assumes a full rebuild every time (e.g. detail pane repopulates parallel tile lists).</summary>
            public bool RecycleVisibleRowElements;
            public readonly Dictionary<int, FrameworkElement> RecycledRowElements = new Dictionary<int, FrameworkElement>();
            /// <summary>Batches <see cref="RefreshVirtualizedRowHost"/> during window/pane resize so ScrollViewer layout storms do not queue hundreds of full virtual passes.</summary>
            internal DispatcherTimer ViewportResizeCoalesceTimer;
            /// <summary>Batches virtual row rebuilds during ScrollViewer offset changes so wheel/trackpad scrolling does not remeasure visible rows every tick (jitter).</summary>
            internal DispatcherTimer ScrollRefreshDebounceTimer;
        }

        const int VirtualizedRowHostViewportRefreshDebounceMs = 85;
        const int VirtualizedRowHostScrollRefreshDebounceMs = 48;

        void ScheduleVirtualizedRowHostScrollRefresh(VirtualizedRowHost host)
        {
            if (host == null || host.ScrollViewer == null) return;
            if (host.ScrollRefreshDebounceTimer == null)
            {
                host.ScrollRefreshDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VirtualizedRowHostScrollRefreshDebounceMs) };
                host.ScrollRefreshDebounceTimer.Tick += delegate
                {
                    host.ScrollRefreshDebounceTimer.Stop();
                    RefreshVirtualizedRowHost(host);
                };
            }
            host.ScrollRefreshDebounceTimer.Stop();
            host.ScrollRefreshDebounceTimer.Start();
        }

        void ScheduleVirtualizedRowHostViewportRefresh(VirtualizedRowHost host)
        {
            if (host == null || host.ScrollViewer == null) return;
            if (host.ViewportResizeCoalesceTimer == null)
            {
                host.ViewportResizeCoalesceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VirtualizedRowHostViewportRefreshDebounceMs) };
                host.ViewportResizeCoalesceTimer.Tick += delegate
                {
                    host.ViewportResizeCoalesceTimer.Stop();
                    host.ScrollRefreshDebounceTimer?.Stop();
                    RefreshVirtualizedRowHost(host);
                };
            }
            host.ViewportResizeCoalesceTimer.Stop();
            host.ViewportResizeCoalesceTimer.Start();
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
                if (Math.Abs(e.VerticalChange) > 0.1 || Math.Abs(e.HorizontalChange) > 0.1)
                {
                    host.ViewportResizeCoalesceTimer?.Stop();
                    var vh = host.ScrollViewer.ViewportHeight;
                    if (vh > 1 && (Math.Abs(e.VerticalChange) >= vh * 0.88 || Math.Abs(e.HorizontalChange) >= host.ScrollViewer.ViewportWidth * 0.88))
                    {
                        host.ScrollRefreshDebounceTimer?.Stop();
                        RefreshVirtualizedRowHost(host);
                    }
                    else
                        ScheduleVirtualizedRowHostScrollRefresh(host);
                    return;
                }
                if (Math.Abs(e.ViewportHeightChange) > 0.1 || Math.Abs(e.ViewportWidthChange) > 0.1)
                    ScheduleVirtualizedRowHostViewportRefresh(host);
            };
            host.ScrollViewer.SizeChanged += delegate { ScheduleVirtualizedRowHostViewportRefresh(host); };
            return host;
        }

        void SetVirtualizedRows(VirtualizedRowHost host, IEnumerable<VirtualizedRowDefinition> rows, bool resetScroll = false, double? restoreOffset = null)
        {
            if (host == null) return;
            host.ViewportResizeCoalesceTimer?.Stop();
            host.ScrollRefreshDebounceTimer?.Stop();
            host.Rows = rows == null ? new List<VirtualizedRowDefinition>() : new List<VirtualizedRowDefinition>(rows);
            host.FirstVisibleIndex = -1;
            host.LastVisibleIndex = -1;
            if (host.RecycleVisibleRowElements) host.RecycledRowElements.Clear();
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
                if (host.BeforeVisibleRowsRebuilt != null) host.BeforeVisibleRowsRebuilt();
                host.TopSpacer.Height = 0;
                host.BottomSpacer.Height = 0;
                host.VisibleRowsPanel.Children.Clear();
                if (host.RecycleVisibleRowElements) host.RecycledRowElements.Clear();
                host.FirstVisibleIndex = -1;
                host.LastVisibleIndex = -1;
                if (host.AfterVisibleRowsRebuilt != null) host.AfterVisibleRowsRebuilt();
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
            if (host.BeforeVisibleRowsRebuilt != null) host.BeforeVisibleRowsRebuilt();
            host.VisibleRowsPanel.Children.Clear();

            var measureWidth = host.ScrollViewer.ViewportWidth;
            if (measureWidth <= 0) measureWidth = host.ScrollViewer.ActualWidth;
            bool rowHeightChanged = false;
            double renderedHeight = 0;
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                var row = rows[i];
                if (row == null || row.Build == null) continue;
                FrameworkElement element = null;
                if (host.RecycleVisibleRowElements)
                {
                    if (!host.RecycledRowElements.TryGetValue(i, out element) || element == null)
                    {
                        element = row.Build();
                        if (element != null) host.RecycledRowElements[i] = element;
                    }
                }
                else
                {
                    element = row.Build();
                }
                if (element == null) continue;
                host.VisibleRowsPanel.Children.Add(element);
                if (measureWidth > 0)
                {
                    element.Measure(new Size(measureWidth, double.PositiveInfinity));
                    var measuredHeight = Math.Max(1, Math.Ceiling(element.DesiredSize.Height));
                    if (Math.Abs(measuredHeight - Math.Max(1, row.Height)) > 3)
                    {
                        row.Height = measuredHeight;
                        rowHeightChanged = true;
                    }
                }
                renderedHeight += Math.Max(1, row.Height);
            }

            if (host.RecycleVisibleRowElements)
            {
                foreach (var key in host.RecycledRowElements.Keys.ToList())
                {
                    if (key < firstIndex || key > lastIndex) host.RecycledRowElements.Remove(key);
                }
            }

            host.TopSpacer.Height = topHeight;
            host.BottomSpacer.Height = Math.Max(0, totalHeight - topHeight - renderedHeight);
            if (host.AfterVisibleRowsRebuilt != null) host.AfterVisibleRowsRebuilt();
            if (rowHeightChanged)
            {
                host.FirstVisibleIndex = -1;
                host.LastVisibleIndex = -1;
                host.ScrollViewer.Dispatcher.BeginInvoke(new Action(delegate
                {
                    RefreshVirtualizedRowHost(host);
                }), DispatcherPriority.Background);
            }
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

        FrameworkElement BuildLibraryTimelinePlatformChip(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "Other", StringComparison.OrdinalIgnoreCase)) return null;
            return new Border
            {
                Background = Brush("#162028"),
                BorderBrush = LibrarySectionAccentBrush(normalized),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = normalized,
                    Foreground = Brush("#DCE6EC"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        FrameworkElement BuildLibraryTimelineCaptureFooter(int size, string filePath, LibraryTimelineCaptureContext timelineContext)
        {
            if (timelineContext == null) return null;
            var savedComment = CleanComment(timelineContext.Comment ?? string.Empty);
            var editMode = false;
            var saveInFlight = false;
            var footer = new StackPanel();
            footer.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(timelineContext.GameTitle) ? "Unknown Game" : timelineContext.GameTitle,
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 40
            });
            var footerMetaRow = new DockPanel { Margin = new Thickness(0, 5, 0, 0), LastChildFill = false };
            var timeText = BuildLibraryTimelineCaptureTimeLabel(timelineContext.CaptureDate);
            if (!string.IsNullOrWhiteSpace(timeText))
            {
                var captureTimeBlock = new TextBlock
                {
                    Text = timeText,
                    Foreground = Brush("#9FB0BA"),
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(captureTimeBlock, Dock.Right);
                footerMetaRow.Children.Add(captureTimeBlock);
            }
            var platformChip = BuildLibraryTimelinePlatformChip(timelineContext.PlatformLabel);
            if (platformChip != null)
            {
                DockPanel.SetDock(platformChip, Dock.Left);
                footerMetaRow.Children.Add(platformChip);
            }
            if (footerMetaRow.Children.Count > 0) footer.Children.Add(footerMetaRow);

            var commentShell = new Grid { Margin = new Thickness(0, 6, 0, 0), Cursor = Cursors.IBeam };
            var commentDisplay = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 38,
                FontSize = 11.5
            };
            var commentEditor = new TextBox
            {
                FontSize = 11.5,
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brush("#182129"),
                Foreground = Brush("#F1E9DA"),
                BorderBrush = Brush("#35505D"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6)
            };
            var commentEditorBorder = new Border
            {
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(8),
                Background = Brush("#182129"),
                BorderBrush = Brush("#35505D"),
                BorderThickness = new Thickness(1),
                Child = commentEditor
            };
            Action refreshCommentChrome = delegate
            {
                var hasComment = !string.IsNullOrWhiteSpace(savedComment);
                commentDisplay.Text = hasComment ? savedComment : "Add comment";
                commentDisplay.Foreground = hasComment ? Brush("#C7D4DB") : Brush("#6E828E");
                commentDisplay.FontStyle = hasComment ? FontStyles.Normal : FontStyles.Italic;
                commentDisplay.Visibility = editMode ? Visibility.Collapsed : Visibility.Visible;
                commentEditorBorder.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
                commentDisplay.ToolTip = hasComment ? "Click to edit comment" : "Click to add a comment";
                if (!editMode) commentEditor.Text = savedComment;
            };
            Action beginCommentEdit = delegate
            {
                if (saveInFlight || editMode) return;
                editMode = true;
                commentEditor.Text = savedComment;
                refreshCommentChrome();
                commentEditor.Dispatcher.BeginInvoke(new Action(delegate
                {
                    commentEditor.Focus();
                    commentEditor.SelectAll();
                }), DispatcherPriority.Input);
            };
            Action<bool> finishCommentEdit = delegate(bool saveChanges)
            {
                if (!editMode) return;
                var nextComment = CleanComment(commentEditor.Text ?? string.Empty);
                editMode = false;
                if (!saveChanges || string.Equals(nextComment, savedComment, StringComparison.Ordinal))
                {
                    refreshCommentChrome();
                    return;
                }
                var previousComment = savedComment;
                savedComment = nextComment;
                timelineContext.Comment = nextComment;
                refreshCommentChrome();
                saveInFlight = true;
                SaveLibraryFileCommentByPath(filePath, nextComment, delegate(bool success)
                {
                    saveInFlight = false;
                    if (!success)
                    {
                        savedComment = previousComment;
                        timelineContext.Comment = previousComment;
                    }
                    refreshCommentChrome();
                });
            };
            commentShell.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (editMode) return;
                beginCommentEdit();
                e.Handled = true;
            };
            commentShell.MouseLeave += delegate
            {
                if (editMode) finishCommentEdit(true);
            };
            commentEditor.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    finishCommentEdit(true);
                    e.Handled = true;
                    return;
                }
                if (e.Key != Key.Escape) return;
                editMode = false;
                refreshCommentChrome();
                e.Handled = true;
            };
            commentEditor.LostKeyboardFocus += delegate
            {
                finishCommentEdit(true);
            };
            commentShell.Children.Add(commentDisplay);
            commentShell.Children.Add(commentEditorBorder);
            refreshCommentChrome();
            footer.Children.Add(commentShell);
            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = LibraryTimelineTileFooterScrimBrush,
                Padding = new Thickness(10, 20, 10, 8),
                MaxWidth = Math.Max(64, size),
                Child = footer
            };
        }

        static void SyncLibraryMasonryTileRoundedClip(Border tile)
        {
            if (tile == null) return;
            var w = tile.ActualWidth;
            var h = tile.ActualHeight;
            if (w <= 1d || h <= 1d) return;
            var r = tile.CornerRadius.TopLeft;
            if (r <= 0d)
            {
                tile.Clip = new RectangleGeometry(new Rect(0, 0, w, h));
                return;
            }
            tile.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        }

        Border CreateLibraryDetailTile(string file, int size, int decodePixelWidth, Func<bool> shouldLoad, Action<string> openSingleFileMetadataEditor, Action<string, ModifierKeys> updateDetailSelection, HashSet<string> selectedDetailFiles, Action refreshDetailSelectionUi, Action redrawDetailPane, Action<string> useFileAsFolderCover, int? masonryLayoutHeight = null, LibraryTimelineCaptureContext timelineContext = null)
        {
            var isVideoFile = IsVideo(file);
            var tileIsActive = true;
            Func<bool> shouldKeepLoading = delegate
            {
                return tileIsActive && (shouldLoad == null || shouldLoad());
            };
            var tile = new Border
            {
                Width = size,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brush("#10181D"),
                BorderBrush = Brush("#2B3A44"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = file
            };
            var contentRoot = new Grid { ClipToBounds = true };
            var placeholder = new TextBlock
            {
                Text = System.IO.Path.GetFileName(file),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
                Foreground = Brush("#F1E9DA"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            MediaElement videoPreviewMedia = null;
            TextBlock videoPreviewStatus = null;
            Border videoDurationBadge = null;
            TextBlock videoDurationText = null;
            DispatcherTimer videoPreviewStopTimer = null;
            bool videoPreviewReady = false;
            bool videoPreviewHovered = false;
            bool videoPreviewOpeningStarted = false;
            Action<VideoClipInfo> applyVideoInfo = delegate(VideoClipInfo info)
            {
                if (videoDurationBadge != null && videoDurationText != null)
                {
                    var durationLabel = info == null ? string.Empty : FormatVideoDuration(info.DurationSeconds);
                    videoDurationText.Text = durationLabel;
                    videoDurationBadge.Visibility = string.IsNullOrWhiteSpace(durationLabel) ? Visibility.Collapsed : Visibility.Visible;
                }
            };
            contentRoot.Children.Add(placeholder);
            contentRoot.Children.Add(image);
            Button detailStarButton = null;
            TextBlock detailStarGlyph = null;
            Action applyDetailStarVisual = null;
            Action<bool> showDetailStarChrome = null;
            if (!isVideoFile)
            {
                detailStarGlyph = new TextBlock
                {
                    FontSize = Math.Max(15d, Math.Min(22d, size / 11d)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = "\u2606"
                };
                applyDetailStarVisual = delegate
                {
                    if (detailStarGlyph == null) return;
                    var starred = TryGetLibraryFileStarredFromIndex(file, out var st) && st;
                    detailStarGlyph.Text = starred ? "\u2605" : "\u2606";
                    detailStarGlyph.Foreground = starred ? Brush("#EAC54F") : Brush("#C8FFFFFF");
                };
                detailStarButton = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 6, 0),
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Background = Brush("#66000000"),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    ToolTip = "Star / unstar (saved in photo index)",
                    Content = detailStarGlyph
                };
                showDetailStarChrome = delegate(bool show)
                {
                    if (detailStarButton == null) return;
                    detailStarButton.Opacity = show ? 1d : 0d;
                    detailStarButton.IsHitTestVisible = show;
                };
                applyDetailStarVisual();
                showDetailStarChrome(false);
                detailStarButton.Click += delegate
                {
                    ToggleLibraryFileStarredByPath(file, delegate
                    {
                        applyDetailStarVisual();
                        if (redrawDetailPane != null) redrawDetailPane();
                    });
                };
            }
            if (isVideoFile)
            {
                videoPreviewMedia = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
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
                contentRoot.Children.Add(videoPreviewMedia);
                contentRoot.Children.Add(videoPreviewStatus);
                contentRoot.Children.Add(new Border
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
                    Margin = new Thickness(8, 0, 0, timelineContext != null ? 76d : 8d),
                    Background = Brush("#A6141C21"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    Visibility = Visibility.Collapsed,
                    Child = videoDurationText
                };
                contentRoot.Children.Add(videoDurationBadge);
                videoPreviewStopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                applyVideoInfo(TryLoadCachedVideoClipInfo(file));
                WarmVideoClipInfo(file, delegate(VideoClipInfo info)
                {
                    tile.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!shouldKeepLoading()) return;
                        if (!string.Equals(tile.Tag as string, file, StringComparison.OrdinalIgnoreCase)) return;
                        applyVideoInfo(info);
                    }), DispatcherPriority.Background);
                }, shouldKeepLoading);
            }
            else applyVideoInfo(null);
            var timelineFooterHost = BuildLibraryTimelineCaptureFooter(size, file, timelineContext);
            if (timelineFooterHost != null) contentRoot.Children.Add(timelineFooterHost);
            if (!isVideoFile && detailStarButton != null) contentRoot.Children.Add(detailStarButton);
            tile.Child = contentRoot;
            QueueImageLoad(image, file, decodePixelWidth, delegate(BitmapImage loaded)
            {
                image.Source = loaded;
                image.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
                if (masonryLayoutHeight.HasValue && loaded.PixelWidth > 0 && loaded.PixelHeight > 0)
                {
                    var cw = (double)Math.Max(1, size);
                    var ch = (double)Math.Max(1, masonryLayoutHeight.Value);
                    var cellAr = cw / ch;
                    var bmpAr = loaded.PixelWidth / (double)loaded.PixelHeight;
                    var rel = Math.Abs(cellAr - bmpAr) / (0.5d * (cellAr + bmpAr));
                    image.Stretch = rel < 0.02d ? Stretch.Uniform : Stretch.UniformToFill;
                }
                else
                    image.Stretch = Stretch.UniformToFill;
            }, true, shouldKeepLoading);
            tile.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var clicked = sender as Border;
                var clickedFile = clicked == null ? string.Empty : clicked.Tag as string;
                updateDetailSelection(clickedFile, Keyboard.Modifiers);
                if (e.ClickCount >= 2 && !string.IsNullOrWhiteSpace(clickedFile))
                {
                    if (libraryDoubleClickSetsFolderCover && !isVideoFile && IsImage(clickedFile) && useFileAsFolderCover != null)
                        useFileAsFolderCover(clickedFile);
                    else OpenWithShell(clickedFile);
                }
            };
            tile.MouseRightButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var clicked = sender as Border;
                var clickedFile = clicked == null ? string.Empty : clicked.Tag as string;
                if (string.IsNullOrWhiteSpace(clickedFile)) return;
                if (selectedDetailFiles.Contains(clickedFile))
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                }
                else
                {
                    updateDetailSelection(clickedFile, Keyboard.Modifiers);
                }
            };
            tile.MouseEnter += delegate
            {
                if (!isVideoFile && showDetailStarChrome != null && applyDetailStarVisual != null)
                {
                    applyDetailStarVisual();
                    showDetailStarChrome(true);
                }
                if (!isVideoFile) return;
                if (videoPreviewMedia == null || videoPreviewStatus == null) return;
                if (!shouldKeepLoading()) return;
                videoPreviewHovered = true;
                videoPreviewStopTimer.Stop();
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
                        }
                    }
                }
            };
            tile.MouseLeave += delegate
            {
                if (!isVideoFile && showDetailStarChrome != null) showDetailStarChrome(false);
                if (!isVideoFile) return;
                if (videoPreviewMedia == null || videoPreviewStatus == null) return;
                videoPreviewHovered = false;
                videoPreviewStopTimer.Stop();
                videoPreviewStatus.Visibility = Visibility.Collapsed;
                videoPreviewMedia.Visibility = Visibility.Collapsed;
                try
                {
                    videoPreviewMedia.Pause();
                    videoPreviewMedia.Position = TimeSpan.FromMilliseconds(250);
                }
                catch
                {
                }
            };
            if (videoPreviewMedia != null && videoPreviewStatus != null && videoPreviewStopTimer != null)
            {
                videoPreviewStopTimer.Tick += delegate
                {
                    videoPreviewStopTimer.Stop();
                    videoPreviewHovered = false;
                    videoPreviewStatus.Visibility = Visibility.Collapsed;
                    videoPreviewMedia.Visibility = Visibility.Collapsed;
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
                        videoPreviewStopTimer.Stop();
                        videoPreviewStopTimer.Start();
                        try
                        {
                            videoPreviewMedia.Play();
                        }
                        catch (Exception ex)
                        {
                            Log("Video preview Play: " + ex.Message);
                            videoPreviewStatus.Visibility = Visibility.Collapsed;
                            videoPreviewMedia.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        videoPreviewStatus.Visibility = Visibility.Collapsed;
                        videoPreviewMedia.Visibility = Visibility.Hidden;
                    }
                };
                videoPreviewMedia.MediaEnded += delegate
                {
                    try
                    {
                        videoPreviewMedia.Position = TimeSpan.FromMilliseconds(250);
                        if (videoPreviewHovered) videoPreviewMedia.Play();
                    }
                    catch (Exception ex)
                    {
                        Log("Video preview MediaEnded: " + ex.Message);
                        videoPreviewStopTimer.Stop();
                        videoPreviewStatus.Visibility = Visibility.Collapsed;
                        videoPreviewMedia.Visibility = Visibility.Hidden;
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
                    try
                    {
                        videoPreviewMedia.Stop();
                    }
                    catch (Exception ex)
                    {
                        Log("Video preview MediaFailed cleanup: " + ex.Message);
                    }
                };
            }
            tile.Unloaded += delegate
            {
                tileIsActive = false;
                if (!isVideoFile) return;
                if (videoPreviewStopTimer != null) videoPreviewStopTimer.Stop();
                if (videoPreviewStatus != null) videoPreviewStatus.Visibility = Visibility.Collapsed;
                if (videoPreviewMedia == null) return;
                videoPreviewHovered = false;
                videoPreviewReady = false;
                videoPreviewOpeningStarted = false;
                videoPreviewMedia.Visibility = Visibility.Hidden;
                try
                {
                    videoPreviewMedia.Stop();
                    videoPreviewMedia.Source = null;
                }
                catch (Exception ex)
                {
                    Log("Video tile unload cleanup: " + ex.Message);
                }
            };
            var contextMenu = new ContextMenu();
            var openItem = new MenuItem { Header = "Open" };
            openItem.Click += delegate { OpenWithShell(file); };
            var openFolderItem = new MenuItem { Header = "Open Folder" };
            openFolderItem.Click += delegate { OpenFolder(System.IO.Path.GetDirectoryName(file) ?? string.Empty); };
            var editItem = new MenuItem { Header = "Edit Metadata" };
            editItem.Click += delegate { openSingleFileMetadataEditor(file); };
            var starMenuItem = new MenuItem { Header = "Add star" };
            var photoTagMenuItem = new MenuItem { Header = "Add Game Photography tag" };
            contextMenu.Opened += delegate
            {
                var starred = TryGetLibraryFileStarredFromIndex(file, out var st) && st;
                starMenuItem.Header = starred ? "Remove star" : "Add star";
                var hasPhoto = LibraryFileIndexHasGamePhotographyTag(file);
                photoTagMenuItem.Header = hasPhoto ? "Remove Game Photography tag" : "Add Game Photography tag";
            };
            starMenuItem.Click += delegate
            {
                ToggleLibraryFileStarredByPath(file, delegate
                {
                    if (redrawDetailPane != null) redrawDetailPane();
                });
            };
            photoTagMenuItem.Click += delegate
            {
                ToggleLibraryFileGamePhotographyTagByPath(file);
                if (redrawDetailPane != null) redrawDetailPane();
            };
            var copyPathItem = new MenuItem { Header = "Copy File Path" };
            copyPathItem.Click += delegate
            {
                try
                {
                    Clipboard.SetText(file);
                }
                catch (Exception ex)
                {
                    Log("Copy file path to clipboard failed. " + ex.Message);
                }
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
            if (libraryDoubleClickSetsFolderCover && useFileAsFolderCover != null && !isVideoFile && IsImage(file))
            {
                var useCoverItem = new MenuItem { Header = "Use as folder cover" };
                useCoverItem.Click += delegate { useFileAsFolderCover(file); };
                contextMenu.Items.Add(useCoverItem);
            }
            contextMenu.Items.Add(starMenuItem);
            contextMenu.Items.Add(photoTagMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(copyPathItem);
            tile.ContextMenu = contextMenu;
            if (masonryLayoutHeight.HasValue)
            {
                var totalH = Math.Max(1, masonryLayoutHeight.Value);
                tile.Height = totalH;
                tile.BorderThickness = new Thickness(0);
                contentRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
                contentRoot.VerticalAlignment = VerticalAlignment.Stretch;
                image.Stretch = Stretch.UniformToFill;
                image.HorizontalAlignment = HorizontalAlignment.Stretch;
                image.VerticalAlignment = VerticalAlignment.Stretch;
                if (videoPreviewMedia != null)
                {
                    videoPreviewMedia.Stretch = Stretch.UniformToFill;
                    videoPreviewMedia.HorizontalAlignment = HorizontalAlignment.Stretch;
                    videoPreviewMedia.VerticalAlignment = VerticalAlignment.Stretch;
                }
                tile.SizeChanged += delegate
                {
                    SyncLibraryMasonryTileRoundedClip(tile);
                };
                tile.Loaded += delegate(object s, RoutedEventArgs e)
                {
                    SyncLibraryMasonryTileRoundedClip(tile);
                };
            }
            return tile;
        }
    }
}
