using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    internal delegate void GameIndexBackgroundIntArrayDelegate(
        string windowTitle,
        string progressTitleText,
        string initialMetaText,
        string startStatusText,
        string canceledStatusText,
        string startLogLine,
        string failureStatusText,
        int totalWork,
        Func<Action<int, string>, CancellationToken, int[]> backgroundWork,
        Action<int[]> onSuccess,
        Action onCanceled = null);

    internal sealed class GameIndexEditorServices
    {
        public Action<string> SetStatus { get; set; }
        public Action<string> Log { get; set; }
        public Func<string, RoutedEventHandler, string, Brush, Button> CreateButton { get; set; }
        public Func<string, List<GameIndexEditorRow>> LoadRows { get; set; }
        /// <summary>Same data as <see cref="LoadRows"/> but safe from a thread-pool thread (no main-window status updates).</summary>
        public Func<string, List<GameIndexEditorRow>> LoadRowsForBackground { get; set; }
        public Action<string, List<GameIndexEditorRow>> SaveRows { get; set; }
        public Func<IEnumerable<string>, string> CreateGameId { get; set; }
        public Func<string, string, string> NormalizeGameIndexName { get; set; }
        public Func<string, string> NormalizeConsoleLabel { get; set; }
        public Func<List<GameIndexEditorRow>, List<GameIndexEditorRow>> MergeRows { get; set; }
        public Func<GameIndexEditorRow, string> BuildMergeKey { get; set; }
        public GameIndexBackgroundIntArrayDelegate RunBackgroundWorkflowIntArray { get; set; }
        public Action<CancellationToken, string> ThrowIfWorkflowCancellationRequested { get; set; }
        public Func<string, List<GameIndexEditorRow>, Action<int, int, string>, CancellationToken, Task<int>> ResolveMissingSteamAppIdsAsync { get; set; }
        public Func<string, List<GameIndexEditorRow>, Action<int, int, string>, CancellationToken, Task<int>> ResolveMissingSteamGridDbIdsAsync { get; set; }
        public Action<string> OpenFolder { get; set; }
    }

    internal static class GameIndexEditorHost
    {
        static SolidColorBrush B(string hex) => UiBrushHelper.FromHex(hex);

        /// <param name="preloadedRows">When set, skips synchronous <see cref="GameIndexEditorServices.LoadRows"/> (use after background load).</param>
        public static void Show(Window owner, string appVersion, string libraryRoot, Action<Window> assignEditorWindow, Action<Window> clearEditorWindowIfCurrent, GameIndexEditorServices services, IList<GameIndexEditorRow> preloadedRows = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (services.SetStatus == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.SetStatus));
            if (services.Log == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.Log));
            if (services.CreateButton == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.CreateButton));
            if (services.LoadRows == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.LoadRows));
            if (services.LoadRowsForBackground == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.LoadRowsForBackground));
            if (services.SaveRows == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.SaveRows));
            if (services.CreateGameId == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.CreateGameId));
            if (services.NormalizeGameIndexName == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.NormalizeGameIndexName));
            if (services.NormalizeConsoleLabel == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.NormalizeConsoleLabel));
            if (services.MergeRows == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.MergeRows));
            if (services.BuildMergeKey == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.BuildMergeKey));
            if (services.RunBackgroundWorkflowIntArray == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.RunBackgroundWorkflowIntArray));
            if (services.ThrowIfWorkflowCancellationRequested == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.ThrowIfWorkflowCancellationRequested));
            if (services.ResolveMissingSteamAppIdsAsync == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.ResolveMissingSteamAppIdsAsync));
            if (services.ResolveMissingSteamGridDbIdsAsync == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.ResolveMissingSteamGridDbIdsAsync));
            if (services.OpenFolder == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.OpenFolder));
            List<GameIndexEditorRow> allRows;
            if (preloadedRows != null)
            {
                allRows = new List<GameIndexEditorRow>(preloadedRows);
            }
            else
            {
                services.SetStatus("Loading game index");
                allRows = services.LoadRows(libraryRoot) ?? new List<GameIndexEditorRow>();
            }

            var editorWindow = new Window
            {
                Title = "PixelVault " + appVersion + " Game Index",
                Width = 1380,
                Height = 900,
                MinWidth = 1120,
                MinHeight = 760,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = B("#F3EEE4")
            };

            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border { Background = B("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 18) };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock { Text = "Game Index", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            headerStack.Children.Add(new TextBlock { Text = "Use this as the master game record: one row per game and platform, with duplicates merged together and platform variants kept separate.", Margin = new Thickness(0, 8, 0, 0), FontSize = 14, Foreground = B("#B7C6C0"), TextWrapping = TextWrapping.Wrap });
            headerStack.Children.Add(new TextBlock { Text = "Saving here updates the master index first, then re-syncs the library cache from it so AppIDs and titles stay consistent.", Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = B("#D8C7A4"), TextWrapping = TextWrapping.Wrap });
            header.Child = headerStack;
            root.Children.Add(header);

            var body = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1) };
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var bodyGrid = new Grid();
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.Child = bodyGrid;

            var controlGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.Children.Add(new TextBlock { Text = "Search", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = B("#1F2A30") });
            var searchBox = new TextBox { Padding = new Thickness(10, 6, 10, 6), BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(searchBox, 1);
            controlGrid.Children.Add(searchBox);
            var helperText = new TextBlock { Text = "Edit the master Game, Platform, Steam AppID, and STID fields. Game ID stays stable, and folder/file details stay read-only so photo-level assignments drive grouping.", VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5F6970"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(helperText, 2);
            controlGrid.Children.Add(helperText);
            var addRowButton = services.CreateButton("Add Game", null, "#8A5A17", Brushes.White);
            addRowButton.Width = 140;
            addRowButton.Height = 42;
            addRowButton.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(addRowButton, 3);
            controlGrid.Children.Add(addRowButton);
            var deleteRowButton = services.CreateButton("Delete Game", null, "#A3473E", Brushes.White);
            deleteRowButton.Width = 146;
            deleteRowButton.Height = 42;
            deleteRowButton.Margin = new Thickness(0, 0, 10, 0);
            deleteRowButton.IsEnabled = false;
            Grid.SetColumn(deleteRowButton, 4);
            controlGrid.Children.Add(deleteRowButton);
            var resolveIdsButton = services.CreateButton("Resolve IDs", null, "#275D47", Brushes.White);
            resolveIdsButton.Width = 160;
            resolveIdsButton.Height = 42;
            resolveIdsButton.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(resolveIdsButton, 5);
            controlGrid.Children.Add(resolveIdsButton);
            var reloadButton = services.CreateButton("Reload", null, "#EEF2F5", B("#33424D"));
            reloadButton.Width = 132;
            reloadButton.Height = 42;
            reloadButton.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(reloadButton, 6);
            controlGrid.Children.Add(reloadButton);
            var openFolderButton = services.CreateButton("Open Folder", null, "#20343A", Brushes.White);
            openFolderButton.Width = 148;
            openFolderButton.Height = 42;
            openFolderButton.Margin = new Thickness(0);
            openFolderButton.IsEnabled = false;
            Grid.SetColumn(openFolderButton, 7);
            controlGrid.Children.Add(openFolderButton);
            bodyGrid.Children.Add(controlGrid);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                BorderThickness = new Thickness(1),
                BorderBrush = B("#D7E1E8"),
                Background = Brushes.White,
                AlternatingRowBackground = B("#F7FAFC"),
                RowHeaderWidth = 0,
                Margin = new Thickness(0, 0, 0, 16)
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Game ID", Binding = new System.Windows.Data.Binding("GameId"), IsReadOnly = true, Width = 180 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Game", Binding = new System.Windows.Data.Binding("Name") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(1.15, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new System.Windows.Data.Binding("PlatformLabel") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 130 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Steam AppID", Binding = new System.Windows.Data.Binding("SteamAppId") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 130 });
            grid.Columns.Add(new DataGridTextColumn { Header = "STID", Binding = new System.Windows.Data.Binding("SteamGridDbId") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Files", Binding = new System.Windows.Data.Binding("FileCount"), IsReadOnly = true, Width = 74 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Folder", Binding = new System.Windows.Data.Binding("FolderPath"), IsReadOnly = true, Width = new DataGridLength(1.85, DataGridLengthUnitType.Star) });
            Grid.SetRow(grid, 1);
            bodyGrid.Children.Add(grid);

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var statusText = new TextBlock { Foreground = B("#5F6970"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            footerGrid.Children.Add(statusText);
            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var closeButton = services.CreateButton("Close", null, "#EEF2F5", B("#33424D"));
            closeButton.Width = 128;
            closeButton.Height = 44;
            closeButton.Margin = new Thickness(0, 0, 10, 0);
            var saveButton = services.CreateButton("Save Index", null, "#275D47", Brushes.White);
            saveButton.Width = 148;
            saveButton.Height = 44;
            saveButton.Margin = new Thickness(0);
            actionRow.Children.Add(closeButton);
            actionRow.Children.Add(saveButton);
            Grid.SetColumn(actionRow, 1);
            footerGrid.Children.Add(actionRow);
            Grid.SetRow(footerGrid, 2);
            bodyGrid.Children.Add(footerGrid);

            assignEditorWindow(editorWindow);
            editorWindow.Content = root;

            bool dirty = false;
            Action refreshStatus = null;
            Action refreshGrid = null;

            refreshStatus = delegate
            {
                var visibleCount = grid.Items.Count;
                var selected = grid.SelectedItem as GameIndexEditorRow;
                openFolderButton.IsEnabled = selected != null && !string.IsNullOrWhiteSpace(selected.FolderPath) && Directory.Exists(selected.FolderPath);
                deleteRowButton.IsEnabled = selected != null;
                var selectionText = selected == null ? "No row selected" : selected.Name + " | " + selected.PlatformLabel + " | " + (selected.GameId ?? string.Empty);
                statusText.Text = visibleCount + " visible row(s) | " + allRows.Count + " total | " + (dirty ? "Unsaved changes" : "Saved") + " | " + selectionText;
            };

            refreshGrid = delegate
            {
                var query = (searchBox.Text ?? string.Empty).Trim();
                IEnumerable<GameIndexEditorRow> rows = allRows;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    rows = rows.Where(row =>
                        (!string.IsNullOrWhiteSpace(row.GameId) && row.GameId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.Name) && row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.PlatformLabel) && row.PlatformLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.SteamAppId) && row.SteamAppId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.SteamGridDbId) && row.SteamGridDbId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.FolderPath) && row.FolderPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                }
                grid.ItemsSource = rows
                    .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.PlatformLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                refreshStatus();
            };

            object reloadCoordinator = new object();
            var latestReloadId = 0;
            Action reloadRows = delegate
            {
                int myId;
                lock (reloadCoordinator) { myId = ++latestReloadId; }
                void setReloadBusy(bool busy)
                {
                    reloadButton.IsEnabled = !busy;
                    resolveIdsButton.IsEnabled = !busy;
                    addRowButton.IsEnabled = !busy;
                    deleteRowButton.IsEnabled = !busy;
                    saveButton.IsEnabled = !busy;
                    grid.IsEnabled = !busy;
                }
                setReloadBusy(true);
                services.SetStatus("Reloading game index...");
                Task.Factory.StartNew(delegate
                {
                    return services.LoadRowsForBackground(libraryRoot);
                }).ContinueWith(delegate(Task<List<GameIndexEditorRow>> t)
                {
                    editorWindow.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!editorWindow.IsLoaded) return;
                        bool stillCurrent;
                        lock (reloadCoordinator) { stillCurrent = myId == latestReloadId; }
                        if (!stillCurrent) return;
                        setReloadBusy(false);
                        if (t.IsFaulted)
                        {
                            services.SetStatus("Game index reload failed");
                            var flattened = t.Exception == null ? null : t.Exception.Flatten();
                            var err = flattened == null ? new Exception("Reload failed.") : flattened.InnerExceptions.First();
                            services.Log("Game index reload failed. " + err);
                            MessageBox.Show("Could not reload the game index." + Environment.NewLine + Environment.NewLine + err.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                            refreshStatus();
                            return;
                        }
                        allRows = t.Status == TaskStatus.RanToCompletion && t.Result != null
                            ? t.Result
                            : new List<GameIndexEditorRow>();
                        dirty = false;
                        refreshGrid();
                        services.SetStatus("Game index reloaded");
                        services.Log("Reloaded game index editor rows from cache.");
                    }));
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            };

            searchBox.TextChanged += delegate { refreshGrid(); };
            grid.SelectionChanged += delegate { refreshStatus(); };
            grid.CellEditEnding += delegate { dirty = true; refreshStatus(); };
            addRowButton.Click += delegate
            {
                var newRow = new GameIndexEditorRow
                {
                    GameId = services.CreateGameId(allRows.Select(row => row.GameId)),
                    Name = string.Empty,
                    PlatformLabel = "Other",
                    SteamAppId = string.Empty,
                    SteamGridDbId = string.Empty,
                    FileCount = 0,
                    FolderPath = string.Empty,
                    PreviewImagePath = string.Empty,
                    FilePaths = new string[0]
                };
                allRows.Add(newRow);
                dirty = true;
                refreshGrid();
                grid.SelectedItem = newRow;
                grid.ScrollIntoView(newRow);
                services.SetStatus("New game row added");
            };
            deleteRowButton.Click += delegate
            {
                var selected = grid.SelectedItem as GameIndexEditorRow;
                if (selected == null) return;
                var choice = MessageBox.Show("Remove the selected row from the game index?\n\nThis only deletes the master record row.", "Delete Game Index Row", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) return;
                allRows.Remove(selected);
                dirty = true;
                refreshGrid();
                services.SetStatus("Game index row removed");
            };
            resolveIdsButton.Click += delegate
            {
                try
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                    foreach (var row in allRows)
                    {
                        row.Name = services.NormalizeGameIndexName(row.Name, row.FolderPath);
                        row.PlatformLabel = services.NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.PlatformLabel) ? "Other" : row.PlatformLabel.Trim());
                    }
                    allRows = services.MergeRows(allRows);
                    var rowsToResolve = allRows.ToList();
                    var appIdTargets = rowsToResolve
                        .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                        .GroupBy(services.BuildMergeKey, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .Count();
                    var steamGridDbTargets = appIdTargets;
                    var totalWork = appIdTargets + steamGridDbTargets;

                    services.RunBackgroundWorkflowIntArray(
                        "PixelVault " + appVersion + " Game Index Resolve Progress",
                        "Resolving external game IDs",
                        "Preparing game index rows...",
                        "Resolving game index IDs",
                        "Game index resolve canceled",
                        "Starting external game ID resolution for " + rowsToResolve.Count + " row(s).",
                        "Game index resolve failed",
                        totalWork,
                        delegate(Action<int, string> reportProgress, CancellationToken cancellationToken)
                        {
                            async Task<int[]> runAsync()
                            {
                                var appIdOffset = 0;
                                var steamGridDbOffset = appIdTargets;
                                services.ThrowIfWorkflowCancellationRequested(cancellationToken, "Game index resolve");
                                var resolvedAppIds = await services.ResolveMissingSteamAppIdsAsync(libraryRoot, rowsToResolve, delegate(int current, int total, string detail)
                                {
                                    reportProgress(appIdOffset + current, detail);
                                }, cancellationToken).ConfigureAwait(false);
                                services.ThrowIfWorkflowCancellationRequested(cancellationToken, "Game index resolve");
                                var resolvedSteamGridDbIds = await services.ResolveMissingSteamGridDbIdsAsync(libraryRoot, rowsToResolve, delegate(int current, int total, string detail)
                                {
                                    reportProgress(steamGridDbOffset + current, detail);
                                }, cancellationToken).ConfigureAwait(false);
                                services.ThrowIfWorkflowCancellationRequested(cancellationToken, "Game index resolve");
                                reportProgress(totalWork, "Game index ID resolution complete.");
                                return new[] { resolvedAppIds, resolvedSteamGridDbIds };
                            }
                            return runAsync().GetAwaiter().GetResult();
                        },
                        delegate(int[] result)
                        {
                            var resolvedAppIds = result == null || result.Length < 1 ? 0 : result[0];
                            var resolvedSteamGridDbIds = result == null || result.Length < 2 ? 0 : result[1];
                            allRows = rowsToResolve;
                            dirty = false;
                            refreshGrid();
                            services.SetStatus("Game index IDs resolved");
                            services.Log("Resolved " + resolvedAppIds + " Steam AppID entr" + (resolvedAppIds == 1 ? "y" : "ies") + " and " + resolvedSteamGridDbIds + " STID entr" + (resolvedSteamGridDbIds == 1 ? "y" : "ies") + " into the game index.");
                        });
                }
                catch (Exception resolveEx)
                {
                    services.SetStatus("Game index resolve failed");
                    services.Log("Failed to resolve external game index IDs. " + resolveEx.Message);
                    MessageBox.Show("Could not resolve external IDs for the game index." + Environment.NewLine + Environment.NewLine + resolveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            openFolderButton.Click += delegate
            {
                var selected = grid.SelectedItem as GameIndexEditorRow;
                if (selected != null) services.OpenFolder(selected.FolderPath);
            };
            reloadButton.Click += delegate
            {
                if (dirty)
                {
                    var choice = MessageBox.Show("Discard unsaved index edits and reload the current cache from disk?", "Reload Game Index", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (choice != MessageBoxResult.OK) return;
                }
                reloadRows();
            };
            closeButton.Click += delegate
            {
                if (dirty)
                {
                    var choice = MessageBox.Show("You have unsaved game index changes.\n\nClose without saving?", "Close Game Index", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.OK) return;
                }
                editorWindow.Close();
            };
            saveButton.Click += delegate
            {
                try
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                    foreach (var row in allRows)
                    {
                        row.Name = services.NormalizeGameIndexName(row.Name, row.FolderPath);
                        row.PlatformLabel = services.NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.PlatformLabel) ? "Other" : row.PlatformLabel.Trim());
                        row.SteamAppId = (row.SteamAppId ?? string.Empty).Trim();
                        row.SteamGridDbId = (row.SteamGridDbId ?? string.Empty).Trim();
                    }
                    allRows = services.MergeRows(allRows);
                    services.SaveRows(libraryRoot, allRows);
                    dirty = false;
                    refreshGrid();
                    services.SetStatus("Game index saved");
                    services.Log("Saved " + allRows.Count + " game index row(s) to cache.");
                }
                catch (Exception saveEx)
                {
                    services.SetStatus("Game index save failed");
                    services.Log("Failed to save game index. " + saveEx.Message);
                    MessageBox.Show("Could not save the game index." + Environment.NewLine + Environment.NewLine + saveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            editorWindow.Closed += delegate
            {
                clearEditorWindowIfCurrent(editorWindow);
                services.SetStatus("Ready");
            };

            refreshGrid();
            services.SetStatus("Game index ready");
            services.Log("Opened game index editor.");
            editorWindow.Show();
            editorWindow.Activate();
        }
    }
}