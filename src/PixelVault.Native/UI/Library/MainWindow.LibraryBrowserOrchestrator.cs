using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Forms = System.Windows.Forms;

namespace PixelVaultNative
{
    // Library browser UI orchestration (folder/detail panes, toolbar). Entry: LibraryBrowserHost → ShowLibraryBrowserCore.
    public sealed partial class MainWindow
    {
        internal void ShowLibraryBrowserCore(bool reuseMainWindow = false)
        {
            try
            {
                librarySession.EnsureLibraryRootAccessible("Library folder");
                var folders = new List<LibraryFolderInfo>();
                Action refreshIntakeReviewBadge = null;
                var libraryWindow = GetOrCreateLibraryBrowserWindow(reuseMainWindow);
                var root = new Grid { Background = Brush("#0F1519") };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var navChrome = BuildLibraryBrowserNavChrome();
                root.Children.Add(navChrome.NavBar);

                var contentGrid = new Grid();
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                var panes = BuildLibraryBrowserContentPanes(contentGrid);
                libraryWindow.Content = root;

                LibraryFolderInfo current = null;
                Action<string, bool> runLibraryScan = null;
                Action<bool> setLibraryBusyState = null;
                Action<LibraryFolderInfo> openLibraryMetadataEditor = null;
                Action<string> openSingleFileMetadataEditor = null;
                Action renderTiles = null;
                Action<bool> refreshLibraryFoldersAsync = null;
                Action prefillLibraryFoldersFromSnapshotAsync = null;
                Action applySearchFilter = null;
                Action refreshSortButtons = null;
                Action<string> setLibrarySortMode = null;
                Action<LibraryFolderInfo> showFolder = null;
                Action<List<LibraryFolderInfo>, string, bool, bool> runScopedCoverRefresh = null;
                Action refreshDetailSelectionUi = null;
                Action deleteSelectedLibraryFiles = null;
                Action openSelectedLibraryMetadataEditor = null;
                int intakeBadgeRefreshVersion = 0;
                bool preserveDetailScrollOnNextRender = false;
                double preservedDetailScrollOffset = 0;
                bool preserveFolderScrollOnNextRender = false;
                double preservedFolderScrollOffset = 0;
                string pendingLibrarySearchText = string.Empty;
                string appliedLibrarySearchText = string.Empty;
                int lastDetailTileSize = -1;
                int lastDetailColumns = -1;
                int lastFolderTileSize = -1;
                int lastFolderColumns = -1;
                bool libraryFoldersLoading = false;
                int libraryFolderRefreshVersion = 0;
                int detailRenderVersion = 0;
                var selectedDetailFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var collapsedLibraryPlatformSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var detailTiles = new List<Border>();
                int estimatedDetailRowHeight = 420;
                int detailSelectionAnchorIndex = -1;
                var detailFilesDisplayOrder = new List<string>();

                Func<List<string>> getVisibleDetailFilesOrdered = delegate
                {
                    if (current == null) return new List<string>();
                    if (detailFilesDisplayOrder != null && detailFilesDisplayOrder.Count > 0)
                    {
                        return detailFilesDisplayOrder
                            .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    return GetFilesForLibraryFolderEntry(current, false)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                };

                Func<List<string>> getSelectedDetailFiles = delegate
                {
                    if (current == null) return new List<string>();
                    var visibleFiles = getVisibleDetailFilesOrdered();
                    var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var stale in selectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) selectedDetailFiles.Remove(stale);
                    return visibleFiles.Where(path => selectedDetailFiles.Contains(path)).ToList();
                };

                Action<string, ModifierKeys> updateDetailSelection = delegate(string filePath, ModifierKeys mods)
                {
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        if ((mods & ModifierKeys.Control) == 0 && (mods & ModifierKeys.Shift) == 0)
                        {
                            selectedDetailFiles.Clear();
                            detailSelectionAnchorIndex = -1;
                        }
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        return;
                    }
                    var visibleFiles = getVisibleDetailFilesOrdered();
                    var idx = -1;
                    for (var i = 0; i < visibleFiles.Count; i++)
                    {
                        if (string.Equals(visibleFiles[i], filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i;
                            break;
                        }
                    }
                    if (idx < 0) return;

                    var ctrl = (mods & ModifierKeys.Control) != 0;
                    var shift = (mods & ModifierKeys.Shift) != 0;

                    if (shift && detailSelectionAnchorIndex >= 0 && detailSelectionAnchorIndex < visibleFiles.Count)
                    {
                        var a = detailSelectionAnchorIndex;
                        var b = idx;
                        if (a > b)
                        {
                            var t = a;
                            a = b;
                            b = t;
                        }
                        selectedDetailFiles.Clear();
                        for (var i = a; i <= b; i++) selectedDetailFiles.Add(visibleFiles[i]);
                    }
                    else if (ctrl)
                    {
                        if (selectedDetailFiles.Contains(filePath)) selectedDetailFiles.Remove(filePath);
                        else selectedDetailFiles.Add(filePath);
                        detailSelectionAnchorIndex = idx;
                    }
                    else
                    {
                        selectedDetailFiles.Clear();
                        selectedDetailFiles.Add(filePath);
                        detailSelectionAnchorIndex = idx;
                    }
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshDetailSelectionUi = delegate
                {
                    var selectedFiles = getSelectedDetailFiles();
                    foreach (var tile in detailTiles)
                    {
                        var file = tile == null ? string.Empty : tile.Tag as string;
                        var isSelected = !string.IsNullOrWhiteSpace(file) && selectedDetailFiles.Contains(file);
                        tile.Background = isSelected ? Brush("#1D2730") : Brush("#10181D");
                        tile.BorderBrush = isSelected ? Brush("#D46C63") : Brush("#2B3A44");
                        tile.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                    }
                    panes.DeleteSelectedButton.IsEnabled = current != null && selectedFiles.Count > 0;
                    panes.ThumbLabel.Text = selectedFiles.Count > 0 ? selectedFiles.Count + " selected" : "Screenshots";
                };
                panes.DetailRows.BeforeVisibleRowsRebuilt = delegate
                {
                    detailTiles.Clear();
                };
                panes.DetailRows.AfterVisibleRowsRebuilt = delegate
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshIntakeReviewBadge = delegate
                {
                    if (navChrome.IntakeReviewButton == null || navChrome.IntakeReviewBadge == null || navChrome.IntakeReviewBadgeText == null) return;
                    var refreshVersion = ++intakeBadgeRefreshVersion;
                    System.Threading.Tasks.Task.Factory.StartNew(delegate
                    {
                        try
                        {
                            EnsureSourceFolders();
                            return importService.BuildSourceInventory(false).TopLevelMediaFiles.Count;
                        }
                        catch
                        {
                            return -1;
                        }
                    }).ContinueWith(delegate(System.Threading.Tasks.Task<int> badgeTask)
                    {
                        libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (refreshVersion != intakeBadgeRefreshVersion) return;
                            var count = badgeTask.Status == TaskStatus.RanToCompletion ? badgeTask.Result : -1;
                            if (count > 0)
                            {
                                navChrome.IntakeReviewBadgeText.Text = IntakeBadgeCountText(count);
                                navChrome.IntakeReviewBadge.Visibility = Visibility.Visible;
                                navChrome.IntakeReviewButton.ToolTip = count + " intake item(s) waiting";
                            }
                            else
                            {
                                navChrome.IntakeReviewBadgeText.Text = string.Empty;
                                navChrome.IntakeReviewBadge.Visibility = Visibility.Collapsed;
                                navChrome.IntakeReviewButton.ToolTip = count == 0 ? "No intake items waiting" : "Preview upload queue";
                            }
                        }));
                    }, TaskScheduler.Default);
                };

                refreshSortButtons = delegate
                {
                    var normalized = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
                    Action<Button, bool> applyState = delegate(Button button, bool active)
                    {
                        if (button == null) return;
                        if (active) ApplyLibraryPillChrome(button, "#3A4652", "#566676", "#455463", "#2C3742", "#F4F7FA");
                        else ApplyLibraryPillChrome(button, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
                    };
                    applyState(panes.SortPlatformButton, string.Equals(normalized, "platform", StringComparison.OrdinalIgnoreCase));
                    applyState(panes.SortRecentButton, string.Equals(normalized, "recent", StringComparison.OrdinalIgnoreCase));
                    applyState(panes.SortPhotosButton, string.Equals(normalized, "photos", StringComparison.OrdinalIgnoreCase));
                };

                setLibrarySortMode = delegate(string mode)
                {
                    var normalized = NormalizeLibraryFolderSortMode(mode);
                    if (string.Equals(normalized, "played", StringComparison.OrdinalIgnoreCase)) normalized = "recent";
                    if (!string.Equals(normalized, NormalizeLibraryFolderSortMode(libraryFolderSortMode), StringComparison.OrdinalIgnoreCase))
                    {
                        libraryFolderSortMode = normalized;
                        SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons();
                };

                openSingleFileMetadataEditor = delegate(string filePath)
                {
                    if (current == null)
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    EnsureExifTool();
                    var visibleFiles = getVisibleDetailFilesOrdered();
                    var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    var selectedFiles = getSelectedDetailFiles();
                    HashSet<string> wantedFiles;
                    if (selectedFiles.Count > 0 && (string.IsNullOrWhiteSpace(filePath) || selectedDetailFiles.Contains(filePath)))
                        wantedFiles = new HashSet<string>(selectedFiles, StringComparer.OrdinalIgnoreCase);
                    else if (selectedFiles.Count == 0 && string.IsNullOrWhiteSpace(filePath))
                        wantedFiles = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    else if (!string.IsNullOrWhiteSpace(filePath) && visibleSet.Contains(filePath))
                        wantedFiles = new HashSet<string>(new[] { filePath }, StringComparer.OrdinalIgnoreCase);
                    else
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    if (wantedFiles.Count == 0)
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedItems = BuildLibraryMetadataItems(current)
                        .Where(item => wantedFiles.Contains(item.FilePath))
                        .ToList();
                    if (selectedItems.Count == 0)
                    {
                        MessageBox.Show("That capture could not be loaded for metadata editing.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedTitle = selectedItems.Count == 1
                        ? Path.GetFileName(selectedItems[0].FilePath)
                        : (visibleFiles.Count > 0 && selectedItems.Count == visibleFiles.Count
                            ? (current.Name + " (all " + selectedItems.Count + " captures)")
                            : (current.Name + " (" + selectedItems.Count + " selected)"));
                    status.Text = selectedItems.Count == 1 ? "Editing selected capture metadata" : "Editing selected capture metadata";
                    Log("Opening library metadata editor for " + selectedItems.Count + " selected capture(s) in " + current.Name + ".");
                    if (!ShowManualMetadataWindow(selectedItems, true, selectedTitle))
                    {
                        status.Text = "Library metadata unchanged";
                        return;
                    }
                    var currentFolderPath = current.FolderPath;
                    var currentPlatformLabel = current.PlatformLabel;
                    var currentName = current.Name;
                    RunLibraryMetadataWorkflowWithProgress(current, selectedItems, delegate
                    {
                        selectedDetailFiles.Clear();
                        detailSelectionAnchorIndex = -1;
                        current = string.IsNullOrWhiteSpace(currentFolderPath)
                            ? null
                            : new LibraryFolderInfo { FolderPath = currentFolderPath, PlatformLabel = currentPlatformLabel ?? string.Empty, Name = currentName ?? string.Empty };
                        if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                    });
                };

                openSelectedLibraryMetadataEditor = delegate { openSingleFileMetadataEditor(null); };

                deleteSelectedLibraryFiles = delegate
                {
                    if (current == null)
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedFiles = getSelectedDetailFiles()
                        .Where(file => !string.IsNullOrWhiteSpace(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (selectedFiles.Count == 0)
                    {
                        MessageBox.Show("Select one or more captures to delete.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var confirm = MessageBox.Show(
                        "Delete " + selectedFiles.Count + " selected capture(s) from the library?\n\nThis removes the file" + (selectedFiles.Count == 1 ? string.Empty : "s") + " from disk and removes the photo index record" + (selectedFiles.Count == 1 ? string.Empty : "s") + ".",
                        "Delete Capture",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.OK) return;

                    var removedFiles = new List<string>();
                    var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var failures = new List<string>();
                    foreach (var file in selectedFiles)
                    {
                        try
                        {
                            var directory = Path.GetDirectoryName(file) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(directory)) touchedDirectories.Add(directory);
                            DeleteMetadataSidecarIfPresent(file);
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                                removedFiles.Add(file);
                                Log("Library delete: " + file);
                            }
                            else
                            {
                                removedFiles.Add(file);
                            }
                        }
                        catch (Exception deleteEx)
                        {
                            failures.Add(Path.GetFileName(file) + ": " + deleteEx.Message);
                            Log("Library delete failed for " + file + ". " + deleteEx.Message);
                        }
                    }

                    if (removedFiles.Count > 0)
                    {
                        librarySession.RemoveLibraryMetadataIndexEntries(removedFiles);
                    }
                    foreach (var directory in touchedDirectories) TryDeleteEmptyDirectory(directory);
                    selectedDetailFiles.Clear();
                    detailSelectionAnchorIndex = -1;
                    current = string.IsNullOrWhiteSpace(current.FolderPath)
                        ? null
                        : new LibraryFolderInfo
                        {
                            FolderPath = current.FolderPath,
                            PlatformLabel = current.PlatformLabel ?? string.Empty,
                            Name = current.Name ?? string.Empty
                        };
                    if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                    status.Text = removedFiles.Count == 0
                        ? "No captures deleted"
                        : (failures.Count == 0
                            ? "Deleted " + removedFiles.Count + " capture(s)"
                            : "Deleted " + removedFiles.Count + " capture(s) with " + failures.Count + " failure(s)");
                    if (failures.Count > 0)
                    {
                        MessageBox.Show(
                            "Some files could not be deleted." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(8).ToArray()),
                            "PixelVault",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                };

                Action renderSelectedFolder = delegate
                {
                    var renderStopwatch = Stopwatch.StartNew();
                    var renderVersion = ++detailRenderVersion;
                    if (!preserveDetailScrollOnNextRender) preservedDetailScrollOffset = 0;
                    detailTiles.Clear();
                    if (current == null)
                    {
                        selectedDetailFiles.Clear();
                        detailSelectionAnchorIndex = -1;
                        detailFilesDisplayOrder.Clear();
                        SetVirtualizedRows(panes.DetailRows, new List<VirtualizedRowDefinition>(), true, null);
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=(none); rows=0; files=0", 40);
                        return;
                    }
                    var detailLayout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll);
                    var targetDetailColumns = detailLayout.Columns;
                    var size = detailLayout.TileSize;
                    lastDetailColumns = targetDetailColumns;
                    lastDetailTileSize = size;
                    estimatedDetailRowHeight = Math.Max(200, size + 96);
                    var shouldRestoreDetailScroll = preserveDetailScrollOnNextRender && preservedDetailScrollOffset > 0.1d;
                    var restoreDetailScrollOffset = shouldRestoreDetailScroll ? (double?)preservedDetailScrollOffset : null;
                    var restoreDetailScrollPending = shouldRestoreDetailScroll;
                    preserveDetailScrollOnNextRender = false;
                    var renderFolder = current;
                    SetVirtualizedRows(panes.DetailRows, new[]
                    {
                        new VirtualizedRowDefinition
                        {
                            Height = 44,
                            Build = delegate
                            {
                                return new TextBlock { Text = "Loading captures...", Foreground = Brush("#A7B5BD") };
                            }
                        }
                    }, true, null);
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                    Task.Run(async delegate
                    {
                        Action<LibraryDetailRenderSnapshot, bool> applyDetailSnapshot = null;
                        applyDetailSnapshot = delegate(LibraryDetailRenderSnapshot snapshot, bool logCompletion)
                        {
                            if (renderVersion != detailRenderVersion) return;
                            if (!SameLibraryFolderSelection(current, renderFolder)) return;
                            var visibleFiles = snapshot == null ? new List<string>() : (snapshot.VisibleFiles ?? new List<string>());
                            var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                            foreach (var stale in selectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) selectedDetailFiles.Remove(stale);
                            if (SameLibraryFolderSelection(current, renderFolder))
                            {
                                detailFilesDisplayOrder.Clear();
                                detailFilesDisplayOrder.AddRange(visibleFiles);
                            }
                            detailTiles.Clear();
                            if (snapshot == null || snapshot.Groups == null || snapshot.Groups.Count == 0)
                            {
                                detailFilesDisplayOrder.Clear();
                                SetVirtualizedRows(panes.DetailRows, new[]
                                {
                                    new VirtualizedRowDefinition
                                    {
                                        Height = 44,
                                        Build = delegate
                                        {
                                            return new TextBlock { Text = "No captures found in this folder.", Foreground = Brush("#A7B5BD") };
                                        }
                                    }
                                }, !restoreDetailScrollPending, restoreDetailScrollPending ? restoreDetailScrollOffset : null);
                                restoreDetailScrollPending = false;
                                if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                                if (logCompletion)
                                {
                                    renderStopwatch.Stop();
                                    LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + "; rows=1; files=0; size=" + size, 40);
                                }
                                return;
                            }

                            const int detailTileGap = 8;
                            var detailColumns = targetDetailColumns;
                            var virtualRows = new List<VirtualizedRowDefinition>();
                            foreach (var group in snapshot.Groups)
                            {
                                var groupDate = group.CaptureDate;
                                var groupFiles = group.Files ?? new List<string>();
                                virtualRows.Add(new VirtualizedRowDefinition
                                {
                                    Height = 34,
                                    Build = delegate
                                    {
                                        return new TextBlock
                                        {
                                            Text = groupDate.ToString("MMMM d, yyyy"),
                                            FontSize = 16,
                                            FontWeight = FontWeights.SemiBold,
                                            Foreground = Brush("#F1E9DA"),
                                            Margin = new Thickness(0, 0, 0, 10)
                                        };
                                    }
                                });
                                for (int rowStart = 0; rowStart < groupFiles.Count; rowStart += detailColumns)
                                {
                                    var rowFiles = groupFiles.Skip(rowStart).Take(detailColumns).ToList();
                                    virtualRows.Add(new VirtualizedRowDefinition
                                    {
                                        Height = estimatedDetailRowHeight,
                                        Build = delegate
                                        {
                                            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, detailTileGap) };
                                            for (int fileIndex = 0; fileIndex < rowFiles.Count; fileIndex++)
                                            {
                                                var file = rowFiles[fileIndex];
                                                var tile = CreateLibraryDetailTile(
                                                    file,
                                                    size,
                                                    delegate { return SameLibraryFolderSelection(current, renderFolder); },
                                                    openSingleFileMetadataEditor,
                                                    updateDetailSelection,
                                                    selectedDetailFiles,
                                                    refreshDetailSelectionUi);
                                                tile.Margin = new Thickness(0, 0, fileIndex < rowFiles.Count - 1 ? detailTileGap : 0, 0);
                                                detailTiles.Add(tile);
                                                rowPanel.Children.Add(tile);
                                            }
                                            return rowPanel;
                                        }
                                    });
                                }
                            }
                            SetVirtualizedRows(panes.DetailRows, virtualRows, !restoreDetailScrollPending, restoreDetailScrollPending ? restoreDetailScrollOffset : null);
                            restoreDetailScrollPending = false;
                            if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                            if (logCompletion)
                            {
                                renderStopwatch.Stop();
                                LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + "; groups=" + snapshot.Groups.Count + "; files=" + visibleFiles.Count + "; rows=" + virtualRows.Count + "; columns=" + detailColumns + "; size=" + size, 40);
                            }
                        };

                        try
                        {
                            var metadataIndex = librarySession.LoadLibraryMetadataIndex(false);
                            var detailFiles = GetFilesForLibraryFolderEntry(renderFolder, false)
                                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            var filesMissingCaptureTicks = new List<string>();
                            if (librarySession.HasLibraryRoot && detailFiles.Count > 0)
                            {
                                filesMissingCaptureTicks = detailFiles
                                    .Where(file =>
                                    {
                                        LibraryMetadataIndexEntry entry;
                                        if (!metadataIndex.TryGetValue(file, out entry) || entry == null || entry.CaptureUtcTicks <= 0) return true;
                                        return !string.Equals(entry.Stamp ?? string.Empty, BuildLibraryMetadataStamp(file), StringComparison.Ordinal);
                                    })
                                    .ToList();
                            }

                            Func<LibraryDetailRenderSnapshot> buildSnapshot = delegate
                            {
                                var datedFiles = detailFiles
                                    .Select(file => new { FilePath = file, CaptureDate = librarySession.ResolveIndexedLibraryDate(file, metadataIndex) })
                                    .OrderByDescending(entry => entry.CaptureDate)
                                    .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                                    .ToList();
                                var snapshot = new LibraryDetailRenderSnapshot
                                {
                                    VisibleFiles = datedFiles.Select(entry => entry.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                                };
                                foreach (var group in datedFiles
                                    .GroupBy(entry => entry.CaptureDate.Date)
                                    .OrderByDescending(group => group.Key))
                                {
                                    snapshot.Groups.Add(new LibraryDetailRenderGroup
                                    {
                                        CaptureDate = group.Key,
                                        Files = group.Select(entry => entry.FilePath).ToList()
                                    });
                                }
                                return snapshot;
                            };

                            var quickSnapshot = buildSnapshot();
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(quickSnapshot, true); }));

                            if (filesMissingCaptureTicks.Count > 0)
                            {
                                try
                                {
                                    var savedGameRows = librarySession.LoadSavedGameIndexRows();
                                    var metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(filesMissingCaptureTicks, CancellationToken.None).ConfigureAwait(false);
                                    var indexChanged = false;
                                    var gameRowsChanged = false;
                                    foreach (var file in filesMissingCaptureTicks)
                                    {
                                        EmbeddedMetadataSnapshot metadataSnapshot;
                                        if (!metadataByFile.TryGetValue(file, out metadataSnapshot) || metadataSnapshot == null) metadataSnapshot = new EmbeddedMetadataSnapshot();
                                        LibraryMetadataIndexEntry existingEntry;
                                        if (!metadataIndex.TryGetValue(file, out existingEntry)) existingEntry = null;
                                        var stamp = BuildLibraryMetadataStamp(file);
                                        var previousGameId = existingEntry == null ? string.Empty : NormalizeGameId(existingEntry.GameId);
                                        var previousConsole = existingEntry == null ? string.Empty : NormalizeConsoleLabel(existingEntry.ConsoleLabel);
                                        var rebuiltEntry = librarySession.BuildResolvedLibraryMetadataIndexEntry(file, stamp, metadataSnapshot, existingEntry, metadataIndex, savedGameRows);
                                        metadataIndex[file] = rebuiltEntry;
                                        SetCachedFileTags(file, ParseTagText(rebuiltEntry.TagText), MetadataCacheStamp(file));
                                        indexChanged = true;
                                        if (!string.Equals(previousGameId, NormalizeGameId(rebuiltEntry.GameId), StringComparison.OrdinalIgnoreCase)
                                            || !string.Equals(previousConsole, NormalizeConsoleLabel(rebuiltEntry.ConsoleLabel), StringComparison.OrdinalIgnoreCase))
                                        {
                                            gameRowsChanged = true;
                                        }
                                    }
                                    if (gameRowsChanged) librarySession.PersistGameIndexRows(savedGameRows);
                                    if (indexChanged) librarySession.SaveLibraryMetadataIndex(metadataIndex);
                                }
                                catch (Exception repairEx)
                                {
                                    Log("Library detail metadata repair failed for " + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + ". " + repairEx.Message);
                                }

                                var refinedSnapshot = buildSnapshot();
                                var layoutUnchanged = quickSnapshot.Groups.Count == refinedSnapshot.Groups.Count;
                                if (layoutUnchanged)
                                {
                                    for (int gi = 0; gi < quickSnapshot.Groups.Count && layoutUnchanged; gi++)
                                    {
                                        var ag = quickSnapshot.Groups[gi];
                                        var bg = refinedSnapshot.Groups[gi];
                                        if (ag.CaptureDate.Date != bg.CaptureDate.Date) layoutUnchanged = false;
                                        else
                                        {
                                            var af = ag.Files ?? new List<string>();
                                            var bf = bg.Files ?? new List<string>();
                                            if (af.Count != bf.Count) layoutUnchanged = false;
                                            else
                                            {
                                                for (int j = 0; j < af.Count && layoutUnchanged; j++)
                                                {
                                                    if (!string.Equals(af[j], bf[j], StringComparison.OrdinalIgnoreCase)) layoutUnchanged = false;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (!layoutUnchanged)
                                {
                                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(refinedSnapshot, false); }));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                            {
                                if (renderVersion != detailRenderVersion) return;
                                if (!SameLibraryFolderSelection(current, renderFolder)) return;
                                detailFilesDisplayOrder.Clear();
                                SetVirtualizedRows(panes.DetailRows, new[]
                                {
                                    new VirtualizedRowDefinition
                                    {
                                        Height = 44,
                                        Build = delegate
                                        {
                                            return new TextBlock { Text = "Failed to load captures.", Foreground = Brush("#D9A3A3") };
                                        }
                                    }
                                }, true, null);
                                if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                                Log("Library detail render failed for " + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + ". " + ex.Message);
                                renderStopwatch.Stop();
                            }));
                        }
                    });
                };

                Func<LibraryFolderInfo, int, int, bool, Button> buildFolderTile = delegate(LibraryFolderInfo folder, int tileWidth, int tileHeight, bool showPlatformBadge)
                {
                    var tile = new Button
                    {
                        Width = tileWidth,
                        Height = tileHeight + 76,
                        Margin = new Thickness(0, 0, 14, 16),
                        Padding = new Thickness(0),
                        Background = Brush("#151E24"),
                        BorderBrush = Brush("#25333D"),
                        BorderThickness = new Thickness(1),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Stretch
                    };
                    tile.Template = BuildRoundedTileButtonTemplate();
                    var tileStack = new StackPanel();
                    var imageBorder = new Border { Width = tileWidth, Height = tileHeight, Background = Brush("#0E1418"), CornerRadius = new CornerRadius(18), ClipToBounds = true };
                    if (showPlatformBadge)
                    {
                        var imageGrid = new Grid();
                        imageGrid.Children.Add(CreateAsyncImageTile(
                            GetLibraryArtPathForDisplayOnly(folder),
                            CalculateLibraryFolderArtDecodeWidth(tileWidth),
                            tileWidth,
                            tileHeight,
                            Stretch.UniformToFill,
                            folder.PlatformLabel,
                            Brushes.White,
                            new Thickness(0),
                            new Thickness(0),
                            Brushes.Transparent,
                            new CornerRadius(0),
                            Brushes.Transparent,
                            new Thickness(0)));
                        imageGrid.Children.Add(BuildLibraryTilePlatformBadge(folder.PlatformLabel));
                        imageBorder.Child = imageGrid;
                    }
                    else
                    {
                        imageBorder.Child = CreateAsyncImageTile(
                            GetLibraryArtPathForDisplayOnly(folder),
                            CalculateLibraryFolderArtDecodeWidth(tileWidth),
                            tileWidth,
                            tileHeight,
                            Stretch.UniformToFill,
                            folder.PlatformLabel,
                            Brushes.White,
                            new Thickness(0),
                            new Thickness(0),
                            Brushes.Transparent,
                            new CornerRadius(0),
                            Brushes.Transparent,
                            new Thickness(0));
                    }
                    tileStack.Children.Add(imageBorder);
                    tileStack.Children.Add(new TextBlock { Text = folder.Name, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.White, Margin = new Thickness(10, 12, 10, 3), FontWeight = FontWeights.SemiBold, FontSize = 13.5, Height = 34 });
                    tileStack.Children.Add(new TextBlock { Text = folder.PlatformLabel + " | " + folder.FileCount + " capture" + (folder.FileCount == 1 ? string.Empty : "s"), Foreground = Brush("#8FA4B0"), Margin = new Thickness(10, 0, 10, 10), FontSize = 10.5, Height = 16 });
                    tile.Content = tileStack;
                    tile.Click += delegate { showFolder(folder); };
                    var contextMenu = new ContextMenu();
                    var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
                    openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };
                    var setCoverItem = new MenuItem { Header = "Set Custom Cover..." };
                    setCoverItem.Click += delegate
                    {
                        Directory.CreateDirectory(savedCoversRoot);
                        var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                        if (string.IsNullOrWhiteSpace(pickedCover)) return;
                        SaveCustomCover(folder, pickedCover);
                        showFolder(folder);
                        if (renderTiles != null) renderTiles();
                        Log("Custom cover set for " + folder.Name + " | " + folder.PlatformLabel + ".");
                    };
                    var clearCoverItem = new MenuItem { Header = "Clear Custom Cover", IsEnabled = !string.IsNullOrWhiteSpace(CustomCoverPath(folder)) };
                    clearCoverItem.Click += delegate
                    {
                        ClearCustomCover(folder);
                        showFolder(folder);
                        if (renderTiles != null) renderTiles();
                        Log("Custom cover cleared for " + folder.Name + " | " + folder.PlatformLabel + ".");
                    };
                    var openFolderItem = new MenuItem { Header = "Open Folder" };
                    openFolderItem.Click += delegate { OpenFolder(folder.FolderPath); };
                    var editMetadataItem = new MenuItem { Header = "Edit Metadata" };
                    editMetadataItem.Click += delegate { openLibraryMetadataEditor(folder); };
                    var editIdsItem = new MenuItem { Header = "Edit IDs..." };
                    editIdsItem.Click += delegate
                    {
                        OpenLibraryFolderIdEditor(folder, delegate
                        {
                            showFolder(folder);
                            if (renderTiles != null) renderTiles();
                        });
                    };
                    var refreshFolderItem = new MenuItem { Header = "Refresh Folder" };
                    refreshFolderItem.Click += delegate
                    {
                        showFolder(folder);
                        if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                    };
                    var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art" };
                    fetchFolderCoverItem.Click += delegate
                    {
                        showFolder(folder);
                        runScopedCoverRefresh(new List<LibraryFolderInfo> { folder }, folder.Name + " | " + folder.PlatformLabel, true, false);
                    };
                    contextMenu.Items.Add(openFolderItem);
                    contextMenu.Items.Add(editMetadataItem);
                    contextMenu.Items.Add(editIdsItem);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(refreshFolderItem);
                    contextMenu.Items.Add(fetchFolderCoverItem);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(openMyCoversItem);
                    contextMenu.Items.Add(setCoverItem);
                    contextMenu.Items.Add(clearCoverItem);
                    tile.ContextMenu = contextMenu;
                    return tile;
                };

                showFolder = delegate(LibraryFolderInfo info)
                {
                    if (!SameLibraryFolderSelection(current, info))
                    {
                        selectedDetailFiles.Clear();
                        detailSelectionAnchorIndex = -1;
                        detailFilesDisplayOrder.Clear();
                    }
                    preserveDetailScrollOnNextRender = false;
                    preservedDetailScrollOffset = 0;
                    panes.ThumbScroll.ScrollToVerticalOffset(0);
                    current = info;
                    activeSelectedLibraryFolder = CloneLibraryFolderInfo(info);
                    panes.DetailTitle.Text = info.Name;
                    panes.DetailMeta.Text = info.FileCount + " item(s) | " + info.PlatformLabel + " | " + info.FolderPath;
                    panes.PreviewImage.Source = null;
                    panes.PreviewImage.Visibility = Visibility.Collapsed;
                    var infoCapture = info;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var artPath = GetLibraryArtPathForDisplayOnly(infoCapture);
                            var pathOk = !string.IsNullOrWhiteSpace(artPath) && File.Exists(artPath);
                            _ = libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                            {
                                if (!SameLibraryFolderSelection(current, infoCapture)) return;
                                if (!pathOk)
                                {
                                    panes.PreviewImage.Source = null;
                                    panes.PreviewImage.Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    QueueImageLoad(panes.PreviewImage, artPath, CalculateLibraryBannerArtDecodeWidth(), delegate(BitmapImage loaded)
                                    {
                                        panes.PreviewImage.Source = loaded;
                                        panes.PreviewImage.Visibility = Visibility.Visible;
                                    }, true, delegate { return SameLibraryFolderSelection(current, infoCapture); });
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
                                Log("Library detail banner art resolve failed for " + (infoCapture.Name ?? "?") + ". " + ex.Message);
                            }));
                        }
                    });
                    renderSelectedFolder();
                };

                renderTiles = delegate
                {
                    var renderStopwatch = Stopwatch.StartNew();
                    var restoreFolderScrollOffset = preserveFolderScrollOnNextRender ? Math.Max(0, preservedFolderScrollOffset) : 0;
                    var shouldRestoreFolderScroll = preserveFolderScrollOnNextRender && restoreFolderScrollOffset > 0.1d;
                    preserveFolderScrollOnNextRender = false;
                    var sortMode = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
                    var flattenGroups = !string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase);
                    var searchText = appliedLibrarySearchText;
                    var filterSortStopwatch = Stopwatch.StartNew();
                    var visibleFolders = string.IsNullOrWhiteSpace(searchText)
                        ? folders
                        : folders.Where(folder =>
                            (!string.IsNullOrWhiteSpace(folder.Name) && folder.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.PlatformLabel) && folder.PlatformLabel.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.FolderPath) && folder.FolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.GameId) && folder.GameId.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();
                    var orderedVisibleFolders = visibleFolders
                        .OrderByDescending(folder => string.Equals(sortMode, "recent", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(folder) : DateTime.MinValue)
                        .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? folder.FileCount : 0)
                        .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(folder) : DateTime.MinValue)
                        .ThenBy(folder => string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase) ? PlatformGroupOrder(DetermineLibraryFolderGroup(folder)) : 0)
                        .ThenBy(folder => DetermineLibraryFolderGroup(folder))
                        .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    filterSortStopwatch.Stop();
                    var folderLayout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll);
                    var targetFolderColumns = folderLayout.Columns;
                    var tileWidth = folderLayout.TileSize;
                    lastFolderColumns = targetFolderColumns;
                    lastFolderTileSize = tileWidth;
                    var tileHeight = (int)Math.Round(tileWidth * 1.5d);
                    LibraryFolderInfo selectedFolder = null;
                    if (current != null)
                    {
                        selectedFolder = folders.FirstOrDefault(f => f.FolderPath == current.FolderPath && string.Equals(f.PlatformLabel, current.PlatformLabel, StringComparison.OrdinalIgnoreCase));
                        if (selectedFolder == null) selectedFolder = folders.FirstOrDefault(f => f.FolderPath == current.FolderPath);
                    }
                    if (selectedFolder != null)
                    {
                        if (!SameLibraryFolderSelection(current, selectedFolder)) showFolder(selectedFolder);
                        else current = selectedFolder;
                    }
                    else
                    {
                        current = null;
                        activeSelectedLibraryFolder = null;
                        selectedDetailFiles.Clear();
                        detailSelectionAnchorIndex = -1;
                        detailFilesDisplayOrder.Clear();
                        panes.DetailTitle.Text = "Select a folder";
                        panes.DetailMeta.Text = "Browse the library you chose in Settings.";
                        panes.PreviewImage.Source = null;
                        panes.PreviewImage.Visibility = Visibility.Collapsed;
                        renderSelectedFolder();
                    }

                    var folderCardHeight = tileHeight + 82;
                    var folderRowHeight = folderCardHeight + 14;
                    var folderColumns = targetFolderColumns;
                    var virtualRows = new List<VirtualizedRowDefinition>();
                    if (orderedVisibleFolders.Count == 0)
                    {
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = 44,
                            Build = delegate
                            {
                                return new TextBlock
                                {
                                    Text = libraryFoldersLoading
                                        ? "Loading library folders..."
                                        : (string.IsNullOrWhiteSpace(searchText) ? "No library folders found." : "No folders match the current search."),
                                    Foreground = Brush("#A7B5BD"),
                                    Margin = new Thickness(0, 12, 0, 0)
                                };
                            }
                        });
                        SetVirtualizedRows(panes.TileRows, virtualRows, true, null);
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=" + (libraryFoldersLoading ? "loading" : "empty") + "; foldersLoaded=" + folders.Count + "; visible=0; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=0; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                        return;
                    }
                    if (flattenGroups)
                    {
                        for (int rowStart = 0; rowStart < orderedVisibleFolders.Count; rowStart += folderColumns)
                        {
                            var rowFolders = orderedVisibleFolders.Skip(rowStart).Take(folderColumns).ToList();
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = folderRowHeight,
                                Build = delegate
                                {
                                    var flatWrap = new WrapPanel();
                                    foreach (var folder in rowFolders) flatWrap.Children.Add(buildFolderTile(folder, tileWidth, tileHeight, true));
                                    return new Border { Height = folderRowHeight, Background = Brushes.Transparent, Child = flatWrap };
                                }
                            });
                        }
                        SetVirtualizedRows(panes.TileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=flat; foldersLoaded=" + folders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=0; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                        return;
                    }

                    var folderGroups = orderedVisibleFolders
                        .GroupBy(folder => DetermineLibraryFolderGroup(folder))
                        .OrderBy(group => PlatformGroupOrder(group.Key))
                        .ThenBy(group => group.Key)
                        .ToList();
                    foreach (var folderGroup in folderGroups)
                    {
                        var groupFolders = folderGroup.ToList();
                        var groupLabel = folderGroup.Key;
                        var sectionCollapsed = collapsedLibraryPlatformSections.Contains(groupLabel);
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = 82,
                            Build = delegate
                            {
                                var gl = groupLabel;
                                return new Border
                                {
                                    Height = 82,
                                    Background = Brush("#161F24"),
                                    BorderBrush = Brush("#26363F"),
                                    BorderThickness = new Thickness(1),
                                    CornerRadius = new CornerRadius(10),
                                    Padding = new Thickness(10, 10, 14, 12),
                                    Child = BuildLibrarySectionHeader(gl, groupFolders.Count, collapsedLibraryPlatformSections.Contains(gl), delegate
                                    {
                                        if (collapsedLibraryPlatformSections.Contains(gl)) collapsedLibraryPlatformSections.Remove(gl);
                                        else collapsedLibraryPlatformSections.Add(gl);
                                        if (renderTiles != null) renderTiles();
                                    })
                                };
                            }
                        });
                        if (!sectionCollapsed)
                        {
                            for (int rowStart = 0; rowStart < groupFolders.Count; rowStart += folderColumns)
                            {
                                var rowFolders = groupFolders.Skip(rowStart).Take(folderColumns).ToList();
                                virtualRows.Add(new VirtualizedRowDefinition
                                {
                                    Height = folderRowHeight,
                                    Build = delegate
                                    {
                                        var groupWrap = new WrapPanel();
                                        foreach (var folder in rowFolders) groupWrap.Children.Add(buildFolderTile(folder, tileWidth, tileHeight, false));
                                        return new Border { Height = folderRowHeight, Background = Brushes.Transparent, Child = groupWrap };
                                    }
                                });
                            }
                        }
                    }
                    SetVirtualizedRows(panes.TileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
                    renderStopwatch.Stop();
                    LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=grouped; foldersLoaded=" + folders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=0; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                };

                refreshLibraryFoldersAsync = delegate(bool forceRefresh)
                {
                    var refreshVersion = ++libraryFolderRefreshVersion;
                    var loadingStatusText = forceRefresh || folders.Count > 0
                        ? "Refreshing library folders..."
                        : "Loading library folders...";
                    libraryFoldersLoading = true;
                    if (status != null) status.Text = loadingStatusText;
                    if (renderTiles != null) renderTiles();
                    System.Threading.Tasks.Task.Factory.StartNew(delegate
                    {
                        return librarySession.LoadLibraryFoldersCached(forceRefresh);
                    }).ContinueWith(delegate(System.Threading.Tasks.Task<List<LibraryFolderInfo>> loadTask)
                    {
                        libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (refreshVersion != libraryFolderRefreshVersion) return;
                            libraryFoldersLoading = false;
                            if (loadTask.IsFaulted)
                            {
                                var flattened = loadTask.Exception == null ? null : loadTask.Exception.Flatten();
                                var loadError = flattened == null ? new Exception("Library load failed.") : flattened.InnerExceptions.First();
                                if (status != null && string.Equals(status.Text, loadingStatusText, StringComparison.Ordinal)) status.Text = "Library load failed";
                                Log(loadError.ToString());
                                if (renderTiles != null) renderTiles();
                                MessageBox.Show(loadError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }

                            folders = loadTask.Status == TaskStatus.RanToCompletion && loadTask.Result != null
                                ? loadTask.Result
                                : new List<LibraryFolderInfo>();
                            if (status != null && string.Equals(status.Text, loadingStatusText, StringComparison.Ordinal)) status.Text = "Library ready";
                            if (renderTiles != null) renderTiles();
                        }));
                    }, TaskScheduler.Default);
                };
                activeLibraryFolderRefresh = refreshLibraryFoldersAsync;
                if (!reuseMainWindow)
                {
                    libraryWindow.Closed += delegate
                    {
                        if (activeLibraryFolderRefresh == refreshLibraryFoldersAsync) activeLibraryFolderRefresh = null;
                        activeSelectedLibraryFolder = null;
                    };
                }

                prefillLibraryFoldersFromSnapshotAsync = delegate
                {
                    System.Threading.Tasks.Task.Factory.StartNew(delegate
                    {
                        return librarySession.LoadLibraryFolderCacheSnapshot();
                    }).ContinueWith(delegate(System.Threading.Tasks.Task<List<LibraryFolderInfo>> snapshotTask)
                    {
                        libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (snapshotTask.IsFaulted || folders.Count > 0) return;
                            var snapshotFolders = snapshotTask.Status == TaskStatus.RanToCompletion && snapshotTask.Result != null
                                ? snapshotTask.Result
                                : null;
                            if (snapshotFolders == null || snapshotFolders.Count == 0) return;
                            folders = snapshotFolders;
                            if (status != null) status.Text = "Library ready";
                            if (renderTiles != null) renderTiles();
                        }));
                    }, TaskScheduler.Default);
                };
                setLibraryBusyState = delegate(bool isBusy)
                {
                    navChrome.RefreshButton.IsEnabled = !isBusy;
                    panes.EditMetadataButton.IsEnabled = !isBusy;
                    navChrome.FetchButton.IsEnabled = !isBusy;
                    navChrome.ImportButton.IsEnabled = !isBusy;
                    navChrome.ImportCommentsButton.IsEnabled = !isBusy;
                    navChrome.ManualImportButton.IsEnabled = !isBusy;
                    if (navChrome.IntakeReviewButton != null) navChrome.IntakeReviewButton.IsEnabled = !isBusy;
                };

                runLibraryScan = delegate(string folderPath, bool forceRescan)
                {
                    librarySession.RunLibraryMetadataScan(libraryWindow, folderPath, forceRescan, setLibraryBusyState, delegate
                    {
                        if (string.IsNullOrWhiteSpace(folderPath)) current = null;
                        else current = new LibraryFolderInfo { FolderPath = folderPath, PlatformLabel = current == null ? string.Empty : current.PlatformLabel, Name = current == null ? string.Empty : current.Name };
                        if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                    });
                };

                runScopedCoverRefresh = delegate(List<LibraryFolderInfo> requestedFolders, string scopeLabel, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh)
                {
                    var targetFolders = (requestedFolders ?? new List<LibraryFolderInfo>()).Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.FolderPath)).ToList();
                    if (targetFolders.Count == 0)
                    {
                        MessageBox.Show("No library folder is available for cover refresh.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var resolvedScopeLabel = string.IsNullOrWhiteSpace(scopeLabel) ? (targetFolders.Count == 1 ? "selected folder" : "library") : scopeLabel.Trim();
                    Window progressWindow = null;
                    TextBlock progressMeta = null;
                    ProgressBar progressBar = null;
                    Action<string> appendProgress = null;
                    Button actionButton = null;
                    bool refreshFinished = false;
                    CancellationTokenSource refreshCancellation = null;
                    Action finishButtons = delegate
                    {
                        if (setLibraryBusyState != null) setLibraryBusyState(false);
                        System.Windows.Input.Mouse.OverrideCursor = null;
                    };
                    try
                    {
                        actionButton = Btn("Cancel Refresh", null, "#7A2F2F", Brushes.White);
                        var coverRefreshView = WorkflowProgressWindow.Create(
                            libraryWindow,
                            "PixelVault Cover Refresh",
                            "Resolving IDs and fetching cover art",
                            "Preparing library entries...",
                            0,
                            1,
                            0,
                            true,
                            actionButton,
                            WorkflowProgressWindow.ScanStyleMaxLogLines);
                        progressWindow = coverRefreshView.Window;
                        progressMeta = coverRefreshView.MetaText;
                        progressBar = coverRefreshView.ProgressBar;
                        appendProgress = coverRefreshView.AppendLogLine;
                        actionButton.Click += delegate
                        {
                            if (!refreshFinished)
                            {
                                if (refreshCancellation != null && !refreshCancellation.IsCancellationRequested) refreshCancellation.Cancel();
                                actionButton.IsEnabled = false;
                                if (progressMeta != null) progressMeta.Text = "Cancel requested. Stopping the current lookup or download...";
                                appendProgress("Cancel requested. Stopping the current lookup or download.");
                            }
                            else if (progressWindow != null)
                            {
                                progressWindow.Close();
                            }
                        };
                        progressWindow.Show();
                        appendProgress("Starting cover refresh for " + resolvedScopeLabel + ".");
                        status.Text = targetFolders.Count == 1 ? "Resolving IDs and fetching folder cover art" : "Resolving IDs and fetching cover art";
                        if (setLibraryBusyState != null) setLibraryBusyState(true);
                        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                        refreshCancellation = new CancellationTokenSource();
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var result = await librarySession.RefreshLibraryCoversAsync(folders, targetFolders, delegate(int currentCount, int totalCount, string detail)
                            {
                                if (progressWindow == null) return;
                                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    if (progressBar == null || progressMeta == null) return;
                                    progressBar.IsIndeterminate = totalCount <= 0;
                                    if (totalCount > 0)
                                    {
                                        progressBar.Maximum = totalCount;
                                        progressBar.Value = Math.Min(currentCount, totalCount);
                                        var remaining = Math.Max(totalCount - currentCount, 0);
                                        progressMeta.Text = currentCount + " of " + totalCount + " steps complete | " + remaining + " remaining";
                                    }
                                    else
                                    {
                                        progressMeta.Text = detail;
                                    }
                                    appendProgress(detail);
                                }));
                            }, refreshCancellation.Token, forceRefreshExistingCovers, rebuildFullCacheAfterRefresh).ConfigureAwait(false);
                            return new[] { result.resolvedIds, result.coversReady };
                        }, refreshCancellation.Token).ContinueWith(delegate(System.Threading.Tasks.Task<int[]> refreshTask)
                        {
                            libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                            {
                                refreshFinished = true;
                                if (refreshCancellation != null)
                                {
                                    refreshCancellation.Dispose();
                                    refreshCancellation = null;
                                }
                                finishButtons();
                                if (refreshTask.IsCanceled || (refreshTask.IsFaulted && refreshTask.Exception != null && refreshTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                                {
                                    status.Text = targetFolders.Count == 1 ? "Folder cover refresh cancelled" : "Cover refresh cancelled";
                                    if (progressMeta != null) progressMeta.Text = "Cover refresh cancelled before completion.";
                                    appendProgress("Cover refresh cancelled.");
                                }
                                else if (refreshTask.IsFaulted)
                                {
                                    status.Text = targetFolders.Count == 1 ? "Folder cover refresh failed" : "Cover refresh failed";
                                    var flattened = refreshTask.Exception == null ? null : refreshTask.Exception.Flatten();
                                    var refreshError = flattened == null ? new Exception("Cover refresh failed.") : flattened.InnerExceptions.First();
                                    if (progressMeta != null) progressMeta.Text = refreshError.Message;
                                    appendProgress("ERROR: " + refreshError.Message);
                                    Log(refreshError.ToString());
                                    MessageBox.Show(refreshError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                                else
                                {
                                    var resolved = refreshTask.Result == null || refreshTask.Result.Length < 1 ? 0 : refreshTask.Result[0];
                                    var coversReady = refreshTask.Result == null || refreshTask.Result.Length < 2 ? 0 : refreshTask.Result[1];
                                    status.Text = targetFolders.Count == 1 ? "Folder cover refresh complete" : "Cover refresh complete";
                                    if (progressMeta != null) progressMeta.Text += " | complete";
                                    appendProgress("Cover refresh finished successfully.");
                                    if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                                    Log((targetFolders.Count == 1 ? "Folder" : "Library") + " cover art refresh complete for " + resolvedScopeLabel + ". Resolved " + resolved + " external ID entr" + (resolved == 1 ? "y" : "ies") + "; " + coversReady + " title" + (coversReady == 1 ? " has" : "s have") + " cover art ready.");
                                }
                                if (actionButton != null)
                                {
                                    actionButton.IsEnabled = true;
                                    actionButton.Content = "Close";
                                }
                            }));
                        });
                    }
                    catch (Exception ex)
                    {
                        refreshFinished = true;
                        if (refreshCancellation != null)
                        {
                            refreshCancellation.Dispose();
                            refreshCancellation = null;
                        }
                        finishButtons();
                        status.Text = "Cover refresh failed";
                        Log(ex.ToString());
                        if (progressMeta != null) progressMeta.Text = ex.Message;
                        if (appendProgress != null) appendProgress("ERROR: " + ex.Message);
                        if (actionButton != null)
                        {
                            actionButton.IsEnabled = true;
                            actionButton.Content = "Close";
                        }
                        MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                Action runCoverRefresh = delegate
                {
                    runScopedCoverRefresh(folders, "library", false, false);
                };
                applySearchFilter = delegate
                {
                    panes.SearchDebounceTimer.Stop();
                    if (string.Equals(appliedLibrarySearchText, pendingLibrarySearchText, StringComparison.OrdinalIgnoreCase)) return;
                    appliedLibrarySearchText = pendingLibrarySearchText;
                    if (renderTiles != null) renderTiles();
                };
                panes.SearchDebounceTimer.Tick += delegate
                {
                    applySearchFilter();
                };

                navChrome.RefreshButton.Click += delegate
                {
                    if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                };
                navChrome.SettingsButton.Click += delegate { ShowSettingsWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                navChrome.GameIndexButton.Click += delegate { OpenGameIndexEditor(); };
                navChrome.PhotoIndexButton.Click += delegate { OpenPhotoIndexEditor(); };
                navChrome.PhotographyGalleryButton.Click += delegate { ShowPhotographyGallery(libraryWindow); };
                navChrome.FilenameRulesButton.Click += delegate { OpenFilenameConventionEditor(); };
                navChrome.MyCoversButton.Click += delegate { OpenSavedCoversFolder(); };
                navChrome.ImportButton.Click += delegate { RunWorkflow(false); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                navChrome.ImportCommentsButton.Click += delegate { RunWorkflow(true); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                navChrome.ManualImportButton.Click += delegate { OpenManualIntakeWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                navChrome.FetchButton.Click += delegate
                {
                    var choice = MessageBox.Show(
                        "Refresh cover art for the entire library?",
                        "PixelVault",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question);
                    if (choice != MessageBoxResult.OK) return;
                    runCoverRefresh();
                };
                navChrome.IntakeReviewButton.Click += delegate
                {
                    ShowIntakePreviewWindow(false);
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };
                panes.OpenFolderButton.Click += delegate { if (current != null) OpenFolder(current.FolderPath); };
                openLibraryMetadataEditor = delegate(LibraryFolderInfo focusFolder)
                {
                    if (focusFolder == null)
                    {
                        MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    showFolder(focusFolder);
                    selectedDetailFiles.Clear();
                    detailSelectionAnchorIndex = -1;
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                    openSingleFileMetadataEditor(null);
                };
                panes.EditMetadataButton.Click += delegate { openSelectedLibraryMetadataEditor(); };
                panes.DeleteSelectedButton.Click += delegate { deleteSelectedLibraryFiles(); };
                panes.SortPlatformButton.Click += delegate { setLibrarySortMode("platform"); };
                panes.SortRecentButton.Click += delegate { setLibrarySortMode("recent"); };
                panes.SortPhotosButton.Click += delegate { setLibrarySortMode("photos"); };
                panes.DetailResizeDebounceTimer.Tick += delegate
                {
                    panes.DetailResizeDebounceTimer.Stop();
                    if (current == null) return;
                    var layout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll);
                    if (layout.Columns == lastDetailColumns && layout.TileSize == lastDetailTileSize) return;
                    preservedDetailScrollOffset = panes.ThumbScroll.VerticalOffset;
                    preserveDetailScrollOnNextRender = preservedDetailScrollOffset > 0.1d;
                    renderSelectedFolder();
                };
                panes.FolderResizeDebounceTimer.Tick += delegate
                {
                    panes.FolderResizeDebounceTimer.Stop();
                    var layout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll);
                    if (layout.Columns == lastFolderColumns && layout.TileSize == lastFolderTileSize) return;
                    preservedFolderScrollOffset = panes.TileScroll.VerticalOffset;
                    preserveFolderScrollOnNextRender = preservedFolderScrollOffset > 0.1d;
                    if (renderTiles != null) renderTiles();
                };
                panes.ThumbScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
                {
                    if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1)
                    {
                        if (current != null)
                        {
                            panes.DetailResizeDebounceTimer.Stop();
                            panes.DetailResizeDebounceTimer.Start();
                        }
                    }
                };
                panes.TileScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
                {
                    if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1)
                    {
                        panes.FolderResizeDebounceTimer.Stop();
                        panes.FolderResizeDebounceTimer.Start();
                    }
                };
                panes.SearchBox.TextChanged += delegate
                {
                    pendingLibrarySearchText = string.IsNullOrWhiteSpace(panes.SearchBox.Text) ? string.Empty : panes.SearchBox.Text.Trim();
                    panes.SearchDebounceTimer.Stop();
                    if (string.Equals(pendingLibrarySearchText, appliedLibrarySearchText, StringComparison.OrdinalIgnoreCase)) return;
                    panes.SearchDebounceTimer.Start();
                };
                panes.SearchBox.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key != System.Windows.Input.Key.Enter) return;
                    pendingLibrarySearchText = string.IsNullOrWhiteSpace(panes.SearchBox.Text) ? string.Empty : panes.SearchBox.Text.Trim();
                    if (!string.Equals(pendingLibrarySearchText, appliedLibrarySearchText, StringComparison.OrdinalIgnoreCase)) applySearchFilter();
                    e.Handled = true;
                };
                panes.SearchBox.LostKeyboardFocus += delegate
                {
                    pendingLibrarySearchText = string.IsNullOrWhiteSpace(panes.SearchBox.Text) ? string.Empty : panes.SearchBox.Text.Trim();
                    if (panes.SearchDebounceTimer.IsEnabled || !string.Equals(pendingLibrarySearchText, appliedLibrarySearchText, StringComparison.OrdinalIgnoreCase)) applySearchFilter();
                };
                libraryWindow.Activated += delegate
                {
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };

                refreshSortButtons();
                if (!reuseMainWindow) libraryWindow.Show();
                if (renderTiles != null) renderTiles();
                if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                var autoRefreshLibraryFoldersOnStartup = !librarySession.HasLibraryFolderCacheSnapshot();
                if (!autoRefreshLibraryFoldersOnStartup && status != null) status.Text = "Loading cached library folders...";
                if (prefillLibraryFoldersFromSnapshotAsync != null) prefillLibraryFoldersFromSnapshotAsync();
                if (refreshLibraryFoldersAsync != null && autoRefreshLibraryFoldersOnStartup) refreshLibraryFoldersAsync(false);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
