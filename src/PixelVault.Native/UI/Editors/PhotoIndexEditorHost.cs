using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    internal sealed class PhotoIndexEditorServices
    {
        /// <summary>Optional; when set (e.g. library toast), used for OK-only errors instead of a modal.</summary>
        public Action<string, MessageBoxImage> NotifyUser { get; set; }
        public Action<string> SetStatus { get; set; }
        public Action<string> Log { get; set; }
        public Func<string, RoutedEventHandler, string, Brush, Button> CreateButton { get; set; }
        public Func<string, List<PhotoIndexEditorRow>> LoadRows { get; set; }
        public Action<string, List<PhotoIndexEditorRow>> SaveRows { get; set; }
        public Func<string, string[]> ReadEmbeddedKeywordTagsDirect { get; set; }
        public Func<IEnumerable<string>, string> DetermineConsoleLabelFromTags { get; set; }
        public Func<string, string> BuildLibraryMetadataStamp { get; set; }
        public Action<string> OpenFolder { get; set; }
        public Action<string> OpenWithShell { get; set; }
        public Func<string, string> NormalizeGameId { get; set; }
        public Func<string, string> CleanTag { get; set; }
        public Func<string, string[]> ParseTagText { get; set; }
    }

    internal static class PhotoIndexEditorHost
    {
        static SolidColorBrush B(string hex) => UiBrushHelper.FromHex(hex);

        public static void Show(Window owner, string appVersion, string libraryRoot, Action<Window> assignEditorWindow, Action<Window> clearEditorWindowIfCurrent, PhotoIndexEditorServices services)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (services.SetStatus == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.SetStatus));
            if (services.Log == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.Log));
            if (services.CreateButton == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.CreateButton));
            if (services.LoadRows == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.LoadRows));
            if (services.SaveRows == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.SaveRows));
            if (services.ReadEmbeddedKeywordTagsDirect == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.ReadEmbeddedKeywordTagsDirect));
            if (services.DetermineConsoleLabelFromTags == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.DetermineConsoleLabelFromTags));
            if (services.BuildLibraryMetadataStamp == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.BuildLibraryMetadataStamp));
            if (services.OpenFolder == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.OpenFolder));
            if (services.OpenWithShell == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.OpenWithShell));
            if (services.NormalizeGameId == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.NormalizeGameId));
            if (services.CleanTag == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.CleanTag));
            if (services.ParseTagText == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.ParseTagText));
            services.SetStatus("Loading photo index");
            var allRows = services.LoadRows(libraryRoot);
            var editorWindow = new Window
            {
                Title = "PixelVault " + appVersion + " Photo Index",
                Width = 1500,
                Height = 900,
                MinWidth = 1180,
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
            headerStack.Children.Add(new TextBlock { Text = "Photo Index", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            headerStack.Children.Add(new TextBlock { Text = "This is the per-file index cache for the library. Edit Game ID, Console, or Tags here when you need to correct the recorded state for individual files.", Margin = new Thickness(0, 8, 0, 0), FontSize = 14, Foreground = B("#B7C6C0"), TextWrapping = TextWrapping.Wrap });
            headerStack.Children.Add(new TextBlock { Text = "Saving here rewrites the photo-level index and rebuilds the grouped library from it. A later scan or rebuild can resync these values from the files themselves.", Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = B("#D8C7A4"), TextWrapping = TextWrapping.Wrap });
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
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.Children.Add(new TextBlock { Text = "Search", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = B("#1F2A30") });
            var searchBox = new TextBox { Padding = new Thickness(10, 6, 10, 6), BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(searchBox, 1);
            controlGrid.Children.Add(searchBox);
            var helperText = new TextBlock { Text = "Edit the recorded Game ID, Console, and Tags for each indexed file. File path and metadata stamp stay read-only.", VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5F6970"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(helperText, 2);
            controlGrid.Children.Add(helperText);
            var pullFromFileButton = services.CreateButton("Pull From File", null, "#275D47", Brushes.White);
            pullFromFileButton.Width = 150;
            pullFromFileButton.Height = 42;
            pullFromFileButton.Margin = new Thickness(0, 0, 10, 0);
            pullFromFileButton.IsEnabled = false;
            Grid.SetColumn(pullFromFileButton, 3);
            controlGrid.Children.Add(pullFromFileButton);
            var deleteRowButton = services.CreateButton("Forget Row", null, "#A3473E", Brushes.White);
            deleteRowButton.Width = 134;
            deleteRowButton.Height = 42;
            deleteRowButton.Margin = new Thickness(0, 0, 10, 0);
            deleteRowButton.IsEnabled = false;
            Grid.SetColumn(deleteRowButton, 4);
            controlGrid.Children.Add(deleteRowButton);
            var reloadButton = services.CreateButton("Reload", null, "#EEF2F5", B("#33424D"));
            reloadButton.Width = 132;
            reloadButton.Height = 42;
            reloadButton.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(reloadButton, 5);
            controlGrid.Children.Add(reloadButton);
            var openFolderButton = services.CreateButton("Open Folder", null, "#20343A", Brushes.White);
            openFolderButton.Width = 148;
            openFolderButton.Height = 42;
            openFolderButton.Margin = new Thickness(0, 0, 10, 0);
            openFolderButton.IsEnabled = false;
            Grid.SetColumn(openFolderButton, 6);
            controlGrid.Children.Add(openFolderButton);
            var openFileButton = services.CreateButton("Open File", null, "#20343A", Brushes.White);
            openFileButton.Width = 132;
            openFileButton.Height = 42;
            openFileButton.Margin = new Thickness(0);
            openFileButton.IsEnabled = false;
            Grid.SetColumn(openFileButton, 7);
            controlGrid.Children.Add(openFileButton);
            bodyGrid.Children.Add(controlGrid);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
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
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "\u2605", Binding = new System.Windows.Data.Binding("Starred") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = 46 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Added", Binding = new System.Windows.Data.Binding("IndexAddedAtLocal"), IsReadOnly = true, Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Game ID", Binding = new System.Windows.Data.Binding("GameId") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Console", Binding = new System.Windows.Data.Binding("ConsoleLabel") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Tags", Binding = new System.Windows.Data.Binding("TagText") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(1.05, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new System.Windows.Data.Binding("FilePath"), IsReadOnly = true, Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Stamp", Binding = new System.Windows.Data.Binding("Stamp"), IsReadOnly = true, Width = 170 });
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
            Action<IEnumerable<string>> reselection = null;

            Func<List<PhotoIndexEditorRow>> selectedRows = delegate
            {
                return grid.SelectedItems.Cast<object>()
                    .OfType<PhotoIndexEditorRow>()
                    .Where(row => row != null)
                    .GroupBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            };

            refreshStatus = delegate
            {
                var selectedItems = selectedRows();
                var selected = selectedItems.FirstOrDefault();
                var selectedFile = selected == null ? string.Empty : selected.FilePath ?? string.Empty;
                var selectedFolder = string.IsNullOrWhiteSpace(selectedFile) ? string.Empty : (Path.GetDirectoryName(selectedFile) ?? string.Empty);
                openFileButton.IsEnabled = selectedItems.Count == 1 && !string.IsNullOrWhiteSpace(selectedFile) && File.Exists(selectedFile);
                openFolderButton.IsEnabled = selectedItems.Count == 1 && !string.IsNullOrWhiteSpace(selectedFolder) && Directory.Exists(selectedFolder);
                pullFromFileButton.IsEnabled = selectedItems.Any(row => !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath));
                deleteRowButton.IsEnabled = selectedItems.Count > 0;
                statusText.Text = grid.Items.Count + " visible row(s) | " + allRows.Count + " total | " + (dirty ? "Unsaved changes" : "Saved") + " | " + (selectedItems.Count == 0 ? "No row selected" : selectedItems.Count + " selected");
            };

            refreshGrid = delegate
            {
                var query = (searchBox.Text ?? string.Empty).Trim();
                IEnumerable<PhotoIndexEditorRow> rows = allRows;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    rows = rows.Where(row =>
                        (!string.IsNullOrWhiteSpace(row.GameId) && row.GameId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.ConsoleLabel) && row.ConsoleLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.TagText) && row.TagText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrWhiteSpace(row.FilePath) && row.FilePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                }
                grid.ItemsSource = rows.OrderBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                refreshStatus();
            };

            reselection = delegate(IEnumerable<string> filePaths)
            {
                var wanted = new HashSet<string>((filePaths ?? Enumerable.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
                if (wanted.Count == 0) return;
                grid.SelectedItems.Clear();
                foreach (var row in grid.Items.Cast<object>().OfType<PhotoIndexEditorRow>())
                {
                    if (!wanted.Contains(row.FilePath ?? string.Empty)) continue;
                    grid.SelectedItems.Add(row);
                }
                refreshStatus();
            };

            Action reloadRows = delegate
            {
                allRows = services.LoadRows(libraryRoot);
                dirty = false;
                refreshGrid();
                services.SetStatus("Photo index reloaded");
                services.Log("Reloaded photo index editor rows from cache.");
            };

            searchBox.TextChanged += delegate { refreshGrid(); };
            grid.SelectionChanged += delegate { refreshStatus(); };
            grid.CellEditEnding += delegate { dirty = true; refreshStatus(); };
            grid.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs ev)
            {
                if (ev.OriginalSource is CheckBox) { dirty = true; refreshStatus(); }
            };
            pullFromFileButton.Click += delegate
            {
                var selectedItems = selectedRows()
                    .Where(row => !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                    .ToList();
                if (selectedItems.Count == 0) return;
                foreach (var selected in selectedItems)
                {
                    var tags = services.ReadEmbeddedKeywordTagsDirect(selected.FilePath);
                    selected.ConsoleLabel = services.DetermineConsoleLabelFromTags(tags);
                    selected.TagText = string.Join(", ", tags);
                    selected.Stamp = services.BuildLibraryMetadataStamp(selected.FilePath);
                }
                dirty = true;
                grid.Items.Refresh();
                refreshStatus();
                services.SetStatus("Photo index refreshed from " + selectedItems.Count + " file(s)");
            };
            deleteRowButton.Click += delegate
            {
                var selectedItems = selectedRows();
                if (selectedItems.Count == 0) return;
                var choice = MessageBox.Show("Forget " + selectedItems.Count + " selected row(s) from the photo index?\n\nThis does not delete the file itself. If the file is still in the library, PixelVault can add it back on refresh or rebuild.", "Forget Photo Index Row", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) return;
                foreach (var selected in selectedItems) allRows.Remove(selected);
                dirty = true;
                refreshGrid();
                services.SetStatus("Photo index row(s) forgotten");
            };
            openFolderButton.Click += delegate
            {
                var selected = grid.SelectedItem as PhotoIndexEditorRow;
                if (selected != null) services.OpenFolder(Path.GetDirectoryName(selected.FilePath));
            };
            openFileButton.Click += delegate
            {
                var selected = grid.SelectedItem as PhotoIndexEditorRow;
                if (selected != null) services.OpenWithShell(selected.FilePath);
            };
            reloadButton.Click += delegate
            {
                var selectedItems = selectedRows()
                    .Where(row => !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                    .ToList();
                if (selectedItems.Count == 0)
                {
                    if (dirty)
                    {
                        var choice = MessageBox.Show("Discard unsaved photo index edits and reload the current cache from disk?", "Reload Photo Index", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    reloadRows();
                    return;
                }
                if (dirty)
                {
                    var choice = MessageBox.Show("Reloading from the selected file(s) will discard unsaved photo index edits. Continue?", "Reload Photo Index", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (choice != MessageBoxResult.OK) return;
                }
                var selectedPaths = selectedItems.Select(row => row.FilePath).ToList();
                var diskRows = services.LoadRows(libraryRoot);
                var rowMap = diskRows
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.FilePath))
                    .GroupBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
                int refreshed = 0;
                foreach (var path in selectedPaths)
                {
                    PhotoIndexEditorRow target;
                    if (!rowMap.TryGetValue(path, out target) || !File.Exists(path)) continue;
                    var tags = services.ReadEmbeddedKeywordTagsDirect(path);
                    target.ConsoleLabel = services.DetermineConsoleLabelFromTags(tags);
                    target.TagText = string.Join(", ", tags);
                    target.Stamp = services.BuildLibraryMetadataStamp(path);
                    refreshed++;
                }
                services.SaveRows(libraryRoot, diskRows);
                allRows = services.LoadRows(libraryRoot);
                dirty = false;
                refreshGrid();
                reselection(selectedPaths);
                services.SetStatus("Reloaded " + refreshed + " photo index row(s) from file");
                services.Log("Reloaded " + refreshed + " photo index row(s) from selected file(s).");
            };
            closeButton.Click += delegate
            {
                if (dirty)
                {
                    var choice = MessageBox.Show("You have unsaved photo index changes.\n\nClose without saving?", "Close Photo Index", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
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
                        row.GameId = services.NormalizeGameId(row.GameId);
                        row.ConsoleLabel = services.CleanTag(row.ConsoleLabel);
                        row.TagText = string.Join(", ", services.ParseTagText(row.TagText));
                    }
                    services.SaveRows(libraryRoot, allRows);
                    allRows = services.LoadRows(libraryRoot);
                    dirty = false;
                    refreshGrid();
                    services.SetStatus("Photo index saved");
                    services.Log("Saved " + allRows.Count + " photo index row(s) to cache.");
                }
                catch (Exception saveEx)
                {
                    services.SetStatus("Photo index save failed");
                    services.Log("Failed to save photo index. " + saveEx.Message);
                    MainWindow.NotifyOrMessageBox(services.NotifyUser, "Could not save the photo index." + Environment.NewLine + Environment.NewLine + saveEx.Message, MessageBoxImage.Error);
                }
            };
            editorWindow.Closed += delegate
            {
                clearEditorWindowIfCurrent(editorWindow);
                services.SetStatus("Ready");
            };

            refreshGrid();
            services.SetStatus("Photo index ready");
            services.Log("Opened photo index editor.");
            editorWindow.Show();
            editorWindow.Activate();
        }
    }
}