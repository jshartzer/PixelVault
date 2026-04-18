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

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        bool SetLibraryBrowserCompletionState(LibraryBrowserFolderView view, bool isCompleted)
        {
            if (view == null || librarySession == null || !librarySession.HasLibraryRoot) return false;
            var rows = librarySession.LoadSavedGameIndexRows();
            var targetFolders = GetLibraryBrowserActionFolders(view);
            if (targetFolders.Count == 0)
            {
                var primary = GetLibraryBrowserPrimaryFolder(view) ?? BuildLibraryBrowserDisplayFolder(view);
                if (primary != null) targetFolders.Add(primary);
            }
            if (targetFolders.Count == 0) return false;

            var changed = false;
            foreach (var folder in targetFolders.Where(folder => folder != null))
            {
                if (folder.PendingGameAssignment) return false;
                var row = FindSavedGameIndexRowById(rows, folder.GameId) ?? FindSavedGameIndexRowByIdentity(rows, folder.Name, folder.PlatformLabel);
                if (row == null)
                {
                    row = new GameIndexEditorRow
                    {
                        GameId = !string.IsNullOrWhiteSpace(NormalizeGameId(folder.GameId))
                            ? NormalizeGameId(folder.GameId)
                            : CreateGameId(rows.Select(existing => existing == null ? string.Empty : existing.GameId)),
                        FolderPath = folder.FolderPath ?? string.Empty,
                        Name = folder.Name ?? string.Empty,
                        PlatformLabel = folder.PlatformLabel ?? string.Empty,
                        SteamAppId = folder.SteamAppId ?? string.Empty,
                        SteamGridDbId = folder.SteamGridDbId ?? string.Empty,
                        RetroAchievementsGameId = folder.RetroAchievementsGameId ?? string.Empty,
                        SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                        SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                        FileCount = folder.FileCount,
                        PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                        FilePaths = folder.FilePaths ?? new string[0],
                        IsCompleted100Percent = folder.IsCompleted100Percent,
                        CompletedUtcTicks = folder.CompletedUtcTicks,
                        IsFavorite = folder.IsFavorite,
                        IsShowcase = folder.IsShowcase,
                        CollectionNotes = folder.CollectionNotes ?? string.Empty,
                        IndexAddedUtcTicks = DateTime.UtcNow.Ticks
                    };
                    rows.Add(row);
                }

                var targetCompletedTicks = isCompleted
                    ? (row.CompletedUtcTicks > 0 ? row.CompletedUtcTicks : DateTime.UtcNow.Ticks)
                    : 0L;

                if (row.IsCompleted100Percent != isCompleted
                    || row.CompletedUtcTicks != targetCompletedTicks
                    || !string.Equals(folder.GameId ?? string.Empty, row.GameId ?? string.Empty, StringComparison.Ordinal))
                {
                    row.IsCompleted100Percent = isCompleted;
                    row.CompletedUtcTicks = targetCompletedTicks;
                    if (string.IsNullOrWhiteSpace(folder.GameId) && !string.IsNullOrWhiteSpace(row.GameId)) folder.GameId = row.GameId;
                    changed = true;
                }

                folder.IsCompleted100Percent = isCompleted;
                folder.CompletedUtcTicks = targetCompletedTicks;
            }

            if (!changed) return false;

            librarySession.PersistGameIndexRows(rows);
            var resolvedCompletedTicks = targetFolders.Select(folder => folder == null ? 0L : folder.CompletedUtcTicks).Where(ticks => ticks > 0).DefaultIfEmpty(0L).Max();
            view.IsCompleted100Percent = targetFolders.Any(folder => folder != null && folder.IsCompleted100Percent);
            view.CompletedUtcTicks = resolvedCompletedTicks;
            if (view.PrimaryFolder != null)
            {
                view.PrimaryFolder.IsCompleted100Percent = view.IsCompleted100Percent;
                view.PrimaryFolder.CompletedUtcTicks = resolvedCompletedTicks;
            }
            return true;
        }

        FrameworkElement BuildLibraryBrowserDetailTitlePlatformBadge(string platformLabel)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            if (string.IsNullOrWhiteSpace(resolvedLabel)) return null;
            var iconPath = ResolveLibrarySectionIconPath(resolvedLabel);
            var iconSize = string.Equals(resolvedLabel, "PC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolvedLabel, "Xbox PC", StringComparison.OrdinalIgnoreCase)
                ? 20d
                : 22d;
            var badge = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(11),
                Background = Brush("#070B0E"),
                BorderBrush = Brush("#F4F8FB"),
                BorderThickness = new Thickness(1.15),
                Padding = new Thickness(5),
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                badge.Child = new Image
                {
                    Width = iconSize,
                    Height = iconSize,
                    Source = LoadImageSource(iconPath, 64),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    SnapsToDevicePixels = true
                };
            }
            else
            {
                badge.Child = new TextBlock
                {
                    Text = resolvedLabel == "Multiple Tags" ? "+" : "•",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("#F4F8FB"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
            }
            return badge;
        }

        void UpdateLibraryBrowserDetailTitleBadges(LibraryBrowserPaneRefs panes, LibraryBrowserFolderView view)
        {
            if (panes == null || panes.DetailTitleBadgePanel == null) return;
            panes.DetailTitleBadgePanel.Children.Clear();
            panes.DetailTitleBadgePanel.Visibility = Visibility.Collapsed;
            if (view == null)
            {
                return;
            }

            var labels = (view.PlatformLabels ?? new string[0])
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(NormalizeConsoleLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => PlatformGroupOrder(label))
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (labels.Count == 0 && !string.IsNullOrWhiteSpace(view.PrimaryPlatformLabel))
            {
                labels.Add(NormalizeConsoleLabel(view.PrimaryPlatformLabel));
            }

            foreach (var label in labels)
            {
                var badge = BuildLibraryBrowserDetailTitlePlatformBadge(label);
                if (badge != null) panes.DetailTitleBadgePanel.Children.Add(badge);
            }

            panes.DetailTitleBadgePanel.Visibility = panes.DetailTitleBadgePanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void RefreshPhotoWorkspacePlatformFilterBadges(LibraryBrowserWorkingSet ws, LibraryBrowserPaneRefs panes, LibraryBrowserFolderView game)
        {
            if (panes?.DetailTitleBadgePanel == null || ws == null) return;
            if (ws.WorkspaceMode != LibraryWorkspaceMode.Photo) return;
            panes.DetailTitleBadgePanel.Children.Clear();
            if (game == null || IsLibraryBrowserTimelineView(game))
            {
                panes.DetailTitleBadgePanel.Visibility = Visibility.Collapsed;
                return;
            }

            var labels = (game.PlatformLabels ?? Array.Empty<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(NormalizeConsoleLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => PlatformGroupOrder(label))
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (labels.Count == 0 && !string.IsNullOrWhiteSpace(game.PrimaryPlatformLabel))
                labels.Add(NormalizeConsoleLabel(game.PrimaryPlatformLabel));

            foreach (var label in labels)
            {
                var core = BuildLibraryBrowserDetailTitlePlatformBadge(label);
                if (core == null) continue;
                if (core is FrameworkElement fe) fe.Margin = new Thickness(0);
                var excluded = ws.PhotoRailExcludedConsoleLabels.Contains(label);
                var hit = new Border
                {
                    Child = core,
                    Opacity = excluded ? 0.35 : 1,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = excluded ? "Show " + label + " captures" : "Hide " + label + " captures"
                };
                hit.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (ws.PhotoRailExcludedConsoleLabels.Contains(label)) ws.PhotoRailExcludedConsoleLabels.Remove(label);
                    else ws.PhotoRailExcludedConsoleLabels.Add(label);
                    RefreshPhotoWorkspacePlatformFilterBadges(ws, panes, ws.Current);
                    ws.RefreshDetailPaneForPhotoFilters?.Invoke();
                };
                panes.DetailTitleBadgePanel.Children.Add(hit);
            }

            panes.DetailTitleBadgePanel.Visibility = panes.DetailTitleBadgePanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        FrameworkElement LibraryBrowserBuildFolderTile(
            LibraryBrowserFolderView folder,
            int tileWidth,
            int tileHeight,
            bool showPlatformBadge,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<bool> refreshLibraryFoldersAsync,
            Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh,
            Action<LibraryBrowserFolderView> openLibraryMetadataEditor,
            Action<string> libraryToast,
            LibraryBrowserWorkingSet ws)
        {
            var displayFolder = BuildLibraryBrowserDisplayFolder(folder);
            var actionFolder = GetLibraryBrowserPrimaryFolder(folder) ?? displayFolder;
            var actionFolders = GetLibraryBrowserActionFolders(folder);
            var badgePlatformLabel = folder == null ? string.Empty : folder.PrimaryPlatformLabel;
            var showPlatformContext = ShouldShowLibraryBrowserPlatformContext();
            var showPlatformBadgeOnTile = showPlatformBadge && showPlatformContext && !string.IsNullOrWhiteSpace(badgePlatformLabel);
            var tileFallbackText = string.IsNullOrWhiteSpace(folder == null ? string.Empty : folder.Name)
                ? badgePlatformLabel
                : folder.Name;
            var tileMargin = new Thickness(0, 0, 12, 14);
            var tile = new Button
            {
                Width = tileWidth,
                Height = tileHeight,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brush("#151E24"),
                BorderBrush = Brush("#25333D"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            tile.Template = BuildRoundedTileButtonTemplate();
            var coverCorner = new CornerRadius(12);
            var completedCardInset = 0d;
            var completedInnerCorner = coverCorner;
            var coverRoot = new Grid { Width = tileWidth, Height = tileHeight };
            var showCompletedCardTreatment = folder != null
                && folder.IsCompleted100Percent
                && ws?.WorkspaceMode != LibraryWorkspaceMode.Photo;
            var foilVisual = showCompletedCardTreatment ? BuildLibraryTileCompletionFoilOverlay(tileWidth, tileHeight, coverCorner.TopLeft) : null;
            coverRoot.Children.Add(CreateAsyncImageTile(
                GetLibraryArtPathForDisplayOnly(displayFolder),
                CalculateLibraryFolderArtDecodeWidth(tileWidth, ResolveLibraryDpiScale()),
                tileWidth,
                tileHeight,
                Stretch.UniformToFill,
                tileFallbackText,
                Brushes.White,
                new Thickness(0),
                new Thickness(0),
                Brushes.Transparent,
                new CornerRadius(0),
                Brushes.Transparent,
                new Thickness(0)));
            var overlayPadRight = showPlatformBadgeOnTile ? 84d : 10d;
            var titleBlock = new TextBlock
            {
                Text = folder.Name,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                MaxHeight = 30,
                LineHeight = 15
            };
            var subtitleBlock = new TextBlock
            {
                Text = BuildLibraryBrowserFolderTileSubtitle(folder),
                Foreground = Brush("#8FA4B0"),
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 28,
                Margin = new Thickness(0, 2, 0, 0),
                LineHeight = 13
            };
            var overlayStack = new StackPanel();
            overlayStack.Children.Add(titleBlock);
            overlayStack.Children.Add(subtitleBlock);
            var footerScrim = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = LibraryTimelineTileFooterScrimBrush,
                Padding = new Thickness(10, 18, overlayPadRight, 8),
                Child = overlayStack
            };
            coverRoot.Children.Add(footerScrim);
            if (showPlatformBadgeOnTile) coverRoot.Children.Add(BuildLibraryTilePlatformBadge(badgePlatformLabel));
            var contentInset = showCompletedCardTreatment ? completedCardInset : 0d;
            var cardHost = new Grid
            {
                Width = tileWidth,
                Height = tileHeight,
                Background = Brushes.Transparent
            };
            var imageBorder = new Border
            {
                Width = Math.Max(0, tileWidth - (contentInset * 2)),
                Height = Math.Max(0, tileHeight - (contentInset * 2)),
                Margin = new Thickness(contentInset),
                Background = Brush("#0E1418"),
                CornerRadius = showCompletedCardTreatment ? completedInnerCorner : coverCorner,
                ClipToBounds = true,
                Child = coverRoot
            };
            var coverRadius = showCompletedCardTreatment ? completedInnerCorner.TopLeft : coverCorner.TopLeft;
            var roundedCoverClip = new RectangleGeometry(
                new Rect(0, 0, Math.Max(0, tileWidth - (contentInset * 2)), Math.Max(0, tileHeight - (contentInset * 2))),
                coverRadius,
                coverRadius);
            if (roundedCoverClip.CanFreeze) roundedCoverClip.Freeze();
            imageBorder.Clip = roundedCoverClip;
            cardHost.Children.Add(imageBorder);
            if (foilVisual != null) cardHost.Children.Add(foilVisual.Root);
            if (showCompletedCardTreatment) cardHost.Children.Add(BuildLibraryTileCompletionFrameOverlay(tileWidth, tileHeight, coverCorner));
            tile.Content = cardHost;
            if (foilVisual != null)
            {
                ScrollViewer foilScrollHost = null;
                ScrollChangedEventHandler foilScrollChanged = null;
                MouseWheelEventHandler foilPreviewMouseWheel = null;
                EventHandler foilRendering = null;
                var foilLastScrollOffset = double.NaN;
                var foilScrollBiasY = 0d;
                var foilRenderIdleFrames = 0;

                Point ResolveTileViewportCenter()
                {
                    if (foilScrollHost == null || imageBorder == null || imageBorder.ActualWidth <= 0 || imageBorder.ActualHeight <= 0)
                        return new Point(0.5, 0.5);

                    try
                    {
                        var center = imageBorder.TranslatePoint(new Point(imageBorder.ActualWidth * 0.5, imageBorder.ActualHeight * 0.5), foilScrollHost);
                        var viewportWidth = Math.Max(1, foilScrollHost.ViewportWidth);
                        var viewportHeight = Math.Max(1, foilScrollHost.ViewportHeight);
                        return new Point(
                            Math.Max(0, Math.Min(1, center.X / viewportWidth)),
                            Math.Max(0, Math.Min(1, center.Y / viewportHeight)));
                    }
                    catch
                    {
                        return new Point(0.5, 0.5);
                    }
                }

                void UpdateFoilFromViewport(bool animate)
                {
                    var viewportCenter = ResolveTileViewportCenter();
                    var blendedY = Math.Max(0, Math.Min(1, viewportCenter.Y + foilScrollBiasY));
                    foilVisual.Update(viewportCenter.X, blendedY, animate);
                }

                void StopFoilRendering()
                {
                    if (foilRendering == null) return;
                    CompositionTarget.Rendering -= foilRendering;
                    foilRendering = null;
                }

                void EnsureFoilRendering()
                {
                    if (foilRendering != null) return;
                    foilRendering = delegate
                    {
                        if (foilScrollHost == null || !tile.IsLoaded || imageBorder == null || imageBorder.ActualWidth <= 0 || imageBorder.ActualHeight <= 0)
                        {
                            StopFoilRendering();
                            return;
                        }

                        var currentOffset = foilScrollHost.VerticalOffset;
                        var offsetDelta = double.IsNaN(foilLastScrollOffset) ? 0d : currentOffset - foilLastScrollOffset;
                        foilLastScrollOffset = currentOffset;

                        var updated = false;
                        if (Math.Abs(offsetDelta) > 0.01d)
                        {
                            foilScrollBiasY = Math.Max(-0.6d, Math.Min(0.6d, offsetDelta / 48d));
                            foilRenderIdleFrames = 6;
                            updated = true;
                        }
                        else if (Math.Abs(foilScrollBiasY) > 0.002d)
                        {
                            foilScrollBiasY *= 0.82d;
                            if (Math.Abs(foilScrollBiasY) < 0.002d) foilScrollBiasY = 0d;
                            updated = true;
                        }

                        if (updated)
                        {
                            UpdateFoilFromViewport(false);
                            return;
                        }

                        if (foilRenderIdleFrames > 0)
                        {
                            foilRenderIdleFrames--;
                            UpdateFoilFromViewport(false);
                            return;
                        }

                        StopFoilRendering();
                    };
                    CompositionTarget.Rendering += foilRendering;
                }

                void EnsureFoilScrollHost()
                {
                    if (foilScrollHost != null) return;
                    for (DependencyObject current = tile; current != null; current = VisualTreeHelper.GetParent(current))
                    {
                        if (current is ScrollViewer scrollViewer)
                        {
                            foilScrollHost = scrollViewer;
                            break;
                        }
                    }
                    if (foilScrollHost == null) return;

                    foilScrollChanged = delegate(object _, ScrollChangedEventArgs se)
                    {
                        var delta = Math.Abs(se.VerticalChange) > 0.001d
                            ? se.VerticalChange
                            : (double.IsNaN(foilLastScrollOffset) ? 0d : se.VerticalOffset - foilLastScrollOffset);
                        foilLastScrollOffset = se.VerticalOffset;
                        foilScrollBiasY = Math.Max(-0.6d, Math.Min(0.6d, delta / 48d));
                        foilRenderIdleFrames = 6;
                        EnsureFoilRendering();
                        UpdateFoilFromViewport(false);
                    };
                    foilScrollHost.ScrollChanged += foilScrollChanged;

                    foilPreviewMouseWheel = delegate(object _, MouseWheelEventArgs e)
                    {
                        foilScrollBiasY = Math.Max(-0.6d, Math.Min(0.6d, (-e.Delta) / 320d));
                        foilRenderIdleFrames = 8;
                        EnsureFoilRendering();
                        UpdateFoilFromViewport(false);
                    };
                    foilScrollHost.PreviewMouseWheel += foilPreviewMouseWheel;
                }

                tile.Loaded += delegate
                {
                    EnsureFoilScrollHost();
                    UpdateFoilFromViewport(false);
                };
                tile.Unloaded += delegate
                {
                    if (foilScrollHost != null && foilScrollChanged != null)
                        foilScrollHost.ScrollChanged -= foilScrollChanged;
                    if (foilScrollHost != null && foilPreviewMouseWheel != null)
                        foilScrollHost.PreviewMouseWheel -= foilPreviewMouseWheel;
                    StopFoilRendering();
                    foilScrollHost = null;
                    foilScrollChanged = null;
                    foilPreviewMouseWheel = null;
                    foilLastScrollOffset = double.NaN;
                    foilScrollBiasY = 0d;
                    foilRenderIdleFrames = 0;
                };
                tile.MouseMove += delegate(object _, MouseEventArgs e)
                {
                    if (imageBorder == null || imageBorder.ActualWidth <= 0 || imageBorder.ActualHeight <= 0) return;
                    var position = e.GetPosition(imageBorder);
                    foilVisual.Update(
                        Math.Max(0, Math.Min(1, position.X / imageBorder.ActualWidth)),
                        Math.Max(0, Math.Min(1, position.Y / imageBorder.ActualHeight)),
                        true);
                };
                tile.MouseLeave += delegate
                {
                    UpdateFoilFromViewport(true);
                };
            }
            tile.Click += delegate
            {
                showFolder(folder);
                if (ws?.WorkspaceMode == LibraryWorkspaceMode.Photo && renderTiles != null) renderTiles();
            };
            tile.PreviewMouseDoubleClick += delegate(object _, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (folder == null || IsLibraryBrowserTimelineView(folder) || IsLibraryBrowserTimelineMode()) return;
                e.Handled = true;
                LibraryBrowserEnterPhotoWorkspace(ws, folder, showFolder);
            };
            var contextMenu = new ContextMenu();
            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };
            var setCoverItem = new MenuItem { Header = "Set Custom Cover...", IsEnabled = actionFolders.Count > 0 };
            setCoverItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.jxr;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(pickedCover)) return;
                foreach (var targetFolder in actionFolders) SaveCustomCover(targetFolder, pickedCover);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                libraryToast?.Invoke("Cover saved");
                Log("Custom cover set for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };
            var clearCoverItem = new MenuItem
            {
                Header = "Clear Custom Cover",
                IsEnabled = actionFolders.Any(targetFolder => !string.IsNullOrWhiteSpace(CustomCoverPath(targetFolder)))
            };
            clearCoverItem.Click += delegate
            {
                foreach (var targetFolder in actionFolders.Where(targetFolder => !string.IsNullOrWhiteSpace(CustomCoverPath(targetFolder)))) ClearCustomCover(targetFolder);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                libraryToast?.Invoke("Cover cleared");
                Log("Custom cover cleared for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };
            var openFolderItem = new MenuItem { Header = BuildLibraryBrowserOpenFoldersLabel(folder) };
            openFolderItem.Click += delegate
            {
                foreach (var folderPath in GetLibraryBrowserSourceFolderPaths(folder)) OpenFolder(folderPath);
            };
            var copyFolderPathItem = new MenuItem { Header = "Copy folder path" };
            copyFolderPathItem.Click += delegate
            {
                try
                {
                    var paths = GetLibraryBrowserSourceFolderPaths(folder).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (paths.Count == 0) return;
                    Clipboard.SetText(paths.Count == 1 ? paths[0] : string.Join(Environment.NewLine, paths));
                }
                catch (Exception ex)
                {
                    Log("Copy folder path failed. " + ex.Message);
                }
            };
            var editMetadataItem = new MenuItem { Header = "Edit Metadata" };
            editMetadataItem.Click += delegate { openLibraryMetadataEditor(folder); };
            var markCompletedItem = new MenuItem
            {
                Header = "100% Achievements",
                IsCheckable = true,
                IsChecked = folder != null && folder.IsCompleted100Percent
            };
            markCompletedItem.Click += delegate
            {
                if (folder == null) return;
                var changed = SetLibraryBrowserCompletionState(folder, markCompletedItem.IsChecked);
                if (!changed)
                {
                    markCompletedItem.IsChecked = folder.IsCompleted100Percent;
                    return;
                }
                if (renderTiles != null) renderTiles();
                libraryToast?.Invoke(markCompletedItem.IsChecked ? "Marked 100% achievements" : "Cleared 100% achievements");
                Log((markCompletedItem.IsChecked ? "Marked" : "Cleared") + " 100% achievements for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };
            var editIdsItem = new MenuItem { Header = "Edit IDs...", IsEnabled = folder != null && !folder.IsMergedAcrossPlatforms };
            editIdsItem.Click += delegate
            {
                OpenLibraryFolderIdEditor(actionFolder, delegate
                {
                    showFolder(folder);
                    if (renderTiles != null) renderTiles();
                });
            };
            var refreshThisFolderItem = new MenuItem { Header = "Refresh this folder", IsEnabled = actionFolders.Count > 0 };
            refreshThisFolderItem.Click += delegate
            {
                LibraryBrowserRefreshFolderSelectionFromDisk(ws, ws?.Panes, folder, refreshLibraryFoldersAsync, showFolder);
            };
            var reloadLibraryListItem = new MenuItem { Header = "Reload library folder list" };
            reloadLibraryListItem.Click += delegate
            {
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            };
            var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art", IsEnabled = actionFolders.Count > 0 };
            fetchFolderCoverItem.Click += delegate
            {
                showFolder(folder);
                runScopedCoverRefresh(actionFolders, BuildLibraryBrowserActionScopeLabel(folder), true, false, true);
            };
            var chooseCoverItem = new MenuItem { Header = "Choose Cover...", IsEnabled = actionFolders.Count > 0 && HasSteamGridDbApiToken() };
            chooseCoverItem.Click += async delegate
            {
                await ChooseLibraryAssetFromSteamGridDbAsync(
                    Window.GetWindow(tile) ?? this,
                    folder,
                    actionFolder,
                    actionFolders,
                    LibraryAssetPickerKind.Cover,
                    showFolder,
                    renderTiles,
                    null,
                    libraryToast).ConfigureAwait(true);
            };
            contextMenu.Items.Add(openFolderItem);
            contextMenu.Items.Add(copyFolderPathItem);
            contextMenu.Items.Add(editMetadataItem);
            contextMenu.Items.Add(markCompletedItem);
            contextMenu.Items.Add(editIdsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(refreshThisFolderItem);
            contextMenu.Items.Add(reloadLibraryListItem);
            contextMenu.Items.Add(fetchFolderCoverItem);
            contextMenu.Items.Add(chooseCoverItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(openMyCoversItem);
            contextMenu.Items.Add(setCoverItem);
            contextMenu.Items.Add(clearCoverItem);
            tile.ContextMenu = contextMenu;
            var railShowsActiveGame = ws?.WorkspaceMode == LibraryWorkspaceMode.Photo
                && folder != null
                && SameLibraryBrowserSelection(ws.Current, folder);
            if (railShowsActiveGame)
            {
                return new Border
                {
                    CornerRadius = new CornerRadius(14),
                    BorderBrush = Brush("#4A9FE8"),
                    BorderThickness = new Thickness(2),
                    Background = Brushes.Transparent,
                    Margin = tileMargin,
                    SnapsToDevicePixels = true,
                    Child = tile
                };
            }
            tile.Margin = tileMargin;
            return tile;
        }

        void LibraryBrowserShowSelectedFolder(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            LibraryBrowserFolderView info,
            Action renderSelectedFolder)
        {
            var selectionChanged = !SameLibraryBrowserSelection(ws.Current, info);
            if (!selectionChanged && ws.Current != null && info != null)
            {
                if (!string.Equals(ws.Current.Name ?? string.Empty, info.Name ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(ws.Current.PrimaryFolderPath ?? string.Empty, info.PrimaryFolderPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    selectionChanged = true;
                }
            }
            if (selectionChanged)
            {
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.DetailFilesDisplayOrder.Clear();
                panes.PreviewImage.Source = null;
                panes.PreviewImage.Visibility = Visibility.Collapsed;
                panes.PreviewImage.Uid = Guid.NewGuid().ToString("N");
                if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo)
                {
                    ws.ScrollPhotoRailSelectionToTopPending = true;
                    ws.PhotoRailExcludedConsoleLabels.Clear();
                }
            }
            ws.ResetDetailRowsToLoadingOnNextRender = selectionChanged;
            ws.PreserveDetailScrollOnNextRender = false;
            ws.PreservedDetailScrollOffset = 0;
            if (ws.PendingRestoreDetailScrollAfterShow > 0.1d)
            {
                ws.PreserveDetailScrollOnNextRender = true;
                ws.PreservedDetailScrollOffset = ws.PendingRestoreDetailScrollAfterShow;
                ws.PendingRestoreDetailScrollAfterShow = 0;
            }
            else
            {
                panes.ThumbScroll.ScrollToVerticalOffset(0);
            }
            ws.Current = info;
            LogTroubleshooting("LibrarySelection",
                "selectionChanged=" + selectionChanged
                + "; resetDetailRows=" + ws.ResetDetailRowsToLoadingOnNextRender
                + "; restoreDetailScroll=" + ws.PreserveDetailScrollOnNextRender
                + "; detailScrollOffset=" + ws.PreservedDetailScrollOffset.ToString("0.0")
                + "; " + BuildLibraryBrowserTroubleshootingLabel(info));
            var displayFolder = BuildLibraryBrowserDisplayFolder(info);
            var actionFolder = GetLibraryBrowserPrimaryFolder(info) ?? displayFolder;
            var timelineView = IsLibraryBrowserTimelineView(info);
            activeSelectedLibraryFolder = timelineView ? null : CloneLibraryFolderInfo(actionFolder);
            panes.DetailTitle.Text = timelineView ? "Timeline" : info.Name;
            panes.DetailTitle.Visibility = Visibility.Visible;
            if (ws.WorkspaceMode != LibraryWorkspaceMode.Photo)
                UpdateLibraryBrowserDetailTitleBadges(panes, timelineView ? null : info);
            panes.DetailMeta.Text = BuildLibraryBrowserDetailMetaText(info, actionFolder);
            var openFolderLabel = BuildLibraryBrowserOpenFoldersLabel(info);
            panes.OpenFolderButton.ToolTip = openFolderLabel;
            AccessibilityUi.TrySetAutomationName(panes.OpenFolderButton, openFolderLabel);
            if (timelineView)
            {
                panes.PreviewImage.Source = null;
                panes.PreviewImage.Visibility = Visibility.Collapsed;
                LibraryBrowserClearAchievementsSummary(panes);
                PersistLibraryBrowserLastSelection(info);
                renderSelectedFolder();
                return;
            }
            if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo)
            {
                panes.PreviewImage.Source = null;
                panes.PreviewImage.Visibility = Visibility.Collapsed;
                PersistLibraryBrowserLastSelection(info);
                RefreshPhotoWorkspacePlatformFilterBadges(ws, panes, info);
                LibraryBrowserScheduleAchievementsSummaryRefresh(ws, panes, libraryWindow, info);
                renderSelectedFolder();
                return;
            }
            var infoCapture = info;
            _ = Task.Run(() =>
            {
                try
                {
                    var artPath = GetLibraryArtPathForDisplayOnly(displayFolder);
                    var pathOk = !string.IsNullOrWhiteSpace(artPath) && File.Exists(artPath);
                    _ = libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                    {
                        if (!SameLibraryBrowserSelection(ws.Current, infoCapture)) return;
                        if (!pathOk)
                        {
                            panes.PreviewImage.Source = null;
                            panes.PreviewImage.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            QueueImageLoad(panes.PreviewImage, artPath, CalculateLibraryBannerArtDecodeWidth(ResolveLibraryDpiScale(panes.PreviewImage)), delegate(BitmapImage loaded)
                            {
                                panes.PreviewImage.Source = loaded;
                                panes.PreviewImage.Visibility = Visibility.Visible;
                            }, true, delegate { return SameLibraryBrowserSelection(ws.Current, infoCapture); });
                        }
                    }));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _ = libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        LogException("Library banner art | " + (infoCapture.Name ?? "?"), ex);
                        LogTroubleshooting("LibraryBannerArtFail",
                            "type=" + ex.GetType().FullName
                            + "; message=" + ex.Message
                            + "; exception=" + FormatExceptionForTroubleshooting(ex)
                            + "; " + BuildLibraryBrowserTroubleshootingLabel(infoCapture));
                    }));
                }
            });
            PersistLibraryBrowserLastSelection(info);
            renderSelectedFolder();
        }
    }
}
