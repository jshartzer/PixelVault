using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    internal sealed class BackgroundIntakeUndoneStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is true ? "Undone" : "In library";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Entry point for palette / host to open the modeless activity window (<c>PV-PLN-AINT-001</c> Slice 7).</summary>
    internal static class BackgroundIntakeActivityWindow
    {
        public static void ShowOrBringToFront(MainWindow host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            host.EnsureBackgroundIntakeActivityWindow();
        }

        internal static void ReloadIfOpen(MainWindow host) => host?.ReloadBackgroundIntakeActivityWindowIfOpen();
    }

    public sealed partial class MainWindow
    {
        Window _backgroundIntakeActivityWindow;
        Action _backgroundIntakeActivityReload;

        internal void EnsureBackgroundIntakeActivityWindow()
        {
            if (_backgroundIntakeActivityWindow != null)
            {
                _backgroundIntakeActivityReload?.Invoke();
                _backgroundIntakeActivityWindow.Activate();
                return;
            }

            var w = new Window
            {
                Title = "Background imports — PixelVault",
                Width = 980,
                Height = 520,
                MinWidth = 720,
                MinHeight = 360,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush(DesignTokens.PageBackground),
                ShowInTaskbar = false
            };

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Files moved by background auto-intake. Undo moves copies back to the upload folder; embedded metadata and comments may remain in the files (same as Undo Last Import).",
                Foreground = Brush(DesignTokens.TextLabelMuted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var allRowModels = new List<BackgroundIntakeActivityRowModel>();
            var innerGrids = new List<DataGrid>();
            var batchesPanel = new StackPanel();

            DataGrid CreateBatchRowsGrid(IList<BackgroundIntakeActivityRowModel> batchModels)
            {
                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    SelectionUnit = DataGridSelectionUnit.Cell,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brush(DesignTokens.BorderDefault),
                    Background = Brush(DesignTokens.PanelElevated),
                    Foreground = Brushes.White,
                    RowBackground = Brush(DesignTokens.PanelElevated),
                    AlternatingRowBackground = Brush("#141B20"),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 280,
                    ItemsSource = batchModels
                };
                grid.Columns.Add(new DataGridCheckBoxColumn
                {
                    Header = "Pick",
                    Binding = new Binding("IsSelected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                    Width = 48
                });
                grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding("Row.FileLabel"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "From (source folder)", Binding = new Binding("Row.SourceFolder"), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "To (library)", Binding = new Binding("Row.ResolvedLibraryPath"), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "Rule", Binding = new Binding("Row.RuleLabel"), Width = 120 });
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Status",
                    Binding = new Binding("Row.Undone") { Converter = new BackgroundIntakeUndoneStatusConverter() },
                    Width = 90
                });
                return grid;
            }

            void RebuildModels()
            {
                allRowModels.Clear();
                innerGrids.Clear();
                batchesPanel.Children.Clear();
                foreach (var batch in _backgroundIntakeActivitySession.GetBatchesSnapshot().OrderByDescending(b => b.CompletedUtc))
                {
                    var batchModels = new List<BackgroundIntakeActivityRowModel>();
                    foreach (var row in batch.Rows)
                    {
                        var m = new BackgroundIntakeActivityRowModel
                        {
                            BatchUtc = batch.CompletedUtc,
                            IsSelected = false,
                            Row = row
                        };
                        batchModels.Add(m);
                        allRowModels.Add(m);
                    }
                    var inner = CreateBatchRowsGrid(batchModels);
                    innerGrids.Add(inner);
                    var hdr = new TextBlock
                    {
                        Text = batch.CompletedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC · " + batch.Rows.Count + " file(s)",
                        Foreground = Brush(DesignTokens.TextLabelMuted),
                        FontSize = 13
                    };
                    var exp = new Expander
                    {
                        Header = hdr,
                        Content = inner,
                        IsExpanded = true,
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    batchesPanel.Children.Add(exp);
                }
            }

            RebuildModels();

            var batchScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = batchesPanel
            };
            Grid.SetRow(batchScroll, 1);
            root.Children.Add(batchScroll);

            void RefreshAllGrids()
            {
                foreach (var g in innerGrids) g.Items.Refresh();
            }

            _backgroundIntakeActivityReload = delegate
            {
                RebuildModels();
            };

            var toolbar = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            void AddBtn(string label, RoutedEventHandler click, string bg)
            {
                var b = Btn(label, click, bg, Brushes.White);
                b.Margin = new Thickness(0, 0, 8, 8);
                toolbar.Children.Add(b);
            }

            AddBtn("Select movable", delegate
            {
                foreach (var m in allRowModels.Where(x => x.Row != null && !x.Row.Undone)) m.IsSelected = true;
                RefreshAllGrids();
            }, DesignTokens.ActionSecondaryFill);

            AddBtn("Clear selection", delegate
            {
                foreach (var m in allRowModels) m.IsSelected = false;
                RefreshAllGrids();
            }, DesignTokens.ActionSecondaryFill);

            AddBtn("Open file location", delegate
            {
                var pick = allRowModels.FirstOrDefault(x => x.IsSelected && x.Row != null && !x.Row.Undone && !string.IsNullOrWhiteSpace(x.Row.ResolvedLibraryPath));
                if (pick == null)
                {
                    TryLibraryToast("Select one imported row that still has a resolved library path.", MessageBoxImage.Information);
                    return;
                }
                var path = pick.Row.ResolvedLibraryPath;
                if (!File.Exists(path))
                {
                    TryLibraryToast("That file path is no longer on disk.", MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + path + "\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    TryLibraryToast("Could not open Explorer: " + ex.Message, MessageBoxImage.Warning);
                }
            }, DesignTokens.ActionSecondaryFill);

            AddBtn("Undo selected", delegate { RunBackgroundIntakeSelectiveUndoFromUi(() => allRowModels, RefreshAllGrids, w); }, DesignTokens.ActionPrimaryFill);

            var close = Btn("Close", (_, __) => w.Close(), DesignTokens.ActionShortcutDismissFill, Brushes.White);
            close.Margin = new Thickness(0, 0, 0, 8);
            toolbar.Children.Add(close);

            Grid.SetRow(toolbar, 2);
            root.Children.Add(toolbar);

            w.Content = root;
            w.Closed += (_, __) =>
            {
                if (ReferenceEquals(_backgroundIntakeActivityWindow, w))
                {
                    _backgroundIntakeActivityWindow = null;
                    _backgroundIntakeActivityReload = null;
                }
            };

            _backgroundIntakeActivityWindow = w;
            w.Show();
        }

        internal void ReloadBackgroundIntakeActivityWindowIfOpen()
        {
            _backgroundIntakeActivityReload?.Invoke();
        }

        void RunBackgroundIntakeSelectiveUndoFromUi(Func<IEnumerable<BackgroundIntakeActivityRowModel>> getAllModels, Action refreshGrids, Window owner)
        {
            var entries = getAllModels()
                .Where(m => m.IsSelected && m.Row != null && !m.Row.Undone && m.Row.UndoSnapshot != null)
                .Select(m => BackgroundIntakeActivitySession.CloneEntry(m.Row.UndoSnapshot))
                .ToList();
            if (entries.Count == 0)
            {
                TryLibraryToast("Select one or more rows that are not already undone.", MessageBoxImage.Information);
                return;
            }

            const int confirmThreshold = 12;
            if (entries.Count >= confirmThreshold)
            {
                var c = MessageBox.Show(
                    owner,
                    entries.Count + " imported item(s) will be moved back to their source folders. Embedded metadata changes and comments will stay in the files.\n\nContinue?",
                    "Undo background import",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (c != MessageBoxResult.OK) return;
            }

            try
            {
                var fullManifest = importService.LoadUndoManifest();
                var undoResult = importService.ExecuteUndoImportMoves(entries);
                libraryScanner.RemoveLibraryMetadataIndexEntries(undoResult.RemovedFromLibraryPaths, libraryRoot);
                var merged = BackgroundIntakeActivitySession.MergeManifestAfterPartialUndo(fullManifest, entries, undoResult);
                importService.SaveUndoManifest(merged);
                var skipped = new HashSet<UndoImportEntry>(undoResult.RemainingEntries ?? new List<UndoImportEntry>(), UndoImportEntryEqualityComparer.Instance);
                var succeeded = entries.Where(e => !skipped.Contains(e)).ToList();
                _backgroundIntakeActivitySession.MarkUndone(succeeded);
                if (status != null) status.Text = undoResult.Moved > 0 ? "Undo: moved " + undoResult.Moved + " back" : "Undo: nothing moved";
                Log("Background intake selective undo: moved " + undoResult.Moved + ", skipped " + undoResult.Skipped + ".");
                RefreshPreview();
                refreshGrids?.Invoke();
                TryLibraryToast(undoResult.Moved > 0 ? "Moved " + undoResult.Moved + " file(s) back to the upload folder." : "No files were moved (see log).", MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                TryLibraryToast(ex.Message, MessageBoxImage.Error);
            }
        }
    }
}
