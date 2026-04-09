using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Saved game index rows with <see cref="LibraryFolderInfo"/> file paths refreshed and storage groups backfilled (PV-PLN-LIBST-001 Step 5).
        /// </summary>
        List<GameIndexEditorRow> BuildStorageMergeWorkingSet(string root)
        {
            var saved = MergeGameIndexRows(GetSavedGameIndexRowsForRoot(root) ?? new List<GameIndexEditorRow>());
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return saved;
            var folders = libraryScanner.LoadLibraryFoldersCached(root, true);
            var byGameId = (folders ?? new List<LibraryFolderInfo>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.GameId))
                .GroupBy(f => NormalizeGameId(f.GameId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var row in saved)
            {
                if (row == null) continue;
                var gid = NormalizeGameId(row.GameId);
                if (string.IsNullOrWhiteSpace(gid)) continue;
                if (!byGameId.TryGetValue(gid, out var folder)) continue;
                row.FilePaths = folder.FilePaths == null ? row.FilePaths : folder.FilePaths.ToArray();
                row.FolderPath = folder.FolderPath ?? row.FolderPath;
                row.FileCount = folder.FileCount;
                if (!string.IsNullOrWhiteSpace(folder.PreviewImagePath)) row.PreviewImagePath = folder.PreviewImagePath;
            }
            GameIndexStorageGroupBackfill.AssignDeterministicStorageGroupIds(
                saved,
                NormalizeGameIndexName,
                FoldGameTitleForIdentityMatch,
                MainWindow.NormalizeConsoleLabel,
                CleanTag,
                NormalizeGameId);
            return saved;
        }

        internal void OpenLibraryStorageMergeTool(Window owner)
        {
            var root = libraryRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                TryLibraryToast("Open a library folder first (Path Settings).", MessageBoxImage.Information);
                return;
            }

            List<GameIndexEditorRow> workingRows;
            try
            {
                workingRows = BuildStorageMergeWorkingSet(root);
            }
            catch (Exception ex)
            {
                Log("BuildStorageMergeWorkingSet: " + ex.Message);
                TryLibraryToast("Could not prepare merge preview. " + ex.Message, MessageBoxImage.Error);
                return;
            }

            var titleCounts = BuildGameIndexTitleCounts(workingRows);
            var plan = LibraryStorageMergePlanner.PlanDryRun(
                root,
                workingRows,
                NormalizeGameIndexName,
                GetSafeGameFolderName,
                MainWindow.NormalizeConsoleLabel,
                titleCounts,
                File.Exists,
                IsMedia,
                dir => Directory.Exists(dir) ? Directory.EnumerateFiles(dir) : Enumerable.Empty<string>());

            var w = new Window
            {
                Title = "PixelVault " + AppVersion + " — Merge platform folders",
                Width = 720,
                Height = 520,
                MinWidth = 520,
                MinHeight = 400,
                Owner = owner ?? this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = UiBrushHelper.FromHex("#F3EEE4")
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Merge shared storage groups",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiBrushHelper.FromHex("#1F2A30"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            grid.Children.Add(header);

            var body = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Text = BuildStorageMergeSummaryText(plan),
                Background = Brushes.White,
                BorderBrush = UiBrushHelper.FromHex("#D7E1E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10)
            };
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var closeBtn = new Button { Content = "Close", Width = 100, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
            closeBtn.Click += delegate { w.DialogResult = false; w.Close(); };
            var applyBtn = new Button { Content = "Apply moves", Width = 120, Height = 36, IsEnabled = plan.TotalFileMoves > 0 };
            applyBtn.Click += delegate
            {
                if (plan.TotalFileMoves <= 0) return;
                var confirm = MessageBox.Show(
                    "Move " + plan.TotalFileMoves + " file(s) into shared folders, update the photo index, then refresh the library cache?\n\n"
                    + (plan.TotalConflictRenames > 0 ? plan.TotalConflictRenames + " file(s) may be renamed if names collide.\n\n" : string.Empty)
                    + "A library backup is recommended before large merges.",
                    "Apply storage merge",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.OK) return;
                try
                {
                    var rows = BuildStorageMergeWorkingSet(root);
                    SaveSavedGameIndexRows(root, rows);
                    AlignLibraryFoldersToGameIndex(root, rows);
                    SaveSavedGameIndexRows(root, rows);
                    RefreshCachedLibraryFoldersFromGameIndex(root);
                    activeLibraryFolderRefresh?.Invoke(true);
                    Log("Storage merge applied: " + plan.TotalFileMoves + " file move(s).");
                    TryLibraryToast("Storage merge completed.", MessageBoxImage.Information);
                    w.DialogResult = true;
                    w.Close();
                }
                catch (Exception ex)
                {
                    Log("Apply storage merge failed: " + ex.Message);
                    TryLibraryToast("Merge failed. " + ex.Message, MessageBoxImage.Error);
                }
            };
            buttons.Children.Add(closeBtn);
            buttons.Children.Add(applyBtn);
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            w.Content = grid;
            w.ShowDialog();
        }

        static string BuildStorageMergeSummaryText(LibraryStorageMergeDryRunResult plan)
        {
            var sb = new StringBuilder();
            if (plan == null) return "(no plan)";

            foreach (var line in plan.Warnings)
                sb.AppendLine("Warning: " + line);

            if (plan.Groups == null || plan.Groups.Count == 0)
            {
                sb.AppendLine("No merges needed: every file for your storage groups is already under the shared target folder, or there are no multi-folder groups.");
                return sb.ToString();
            }

            sb.Append("Preview: ");
            sb.Append(plan.Groups.Count + " storage group(s), ");
            sb.Append(plan.TotalFileMoves + " file move(s)");
            if (plan.TotalConflictRenames > 0)
                sb.Append(", " + plan.TotalConflictRenames + " rename(s) if names clash");
            sb.AppendLine(".");
            sb.AppendLine();

            foreach (var g in plan.Groups)
            {
                sb.AppendLine("Group " + g.StorageGroupId);
                sb.AppendLine("  Target: " + g.TargetDirectory);
                sb.AppendLine("  Rows: " + string.Join(", ", g.MemberRows.Select(r => (r.GameId ?? string.Empty) + " " + (r.PlatformLabel ?? string.Empty)).Take(6)) + (g.MemberRows.Count > 6 ? " …" : string.Empty));
                sb.AppendLine("  Moves: " + g.FileMoves.Count);
                if (g.DirectoriesThatMayBeRemovedIfEmpty != null && g.DirectoriesThatMayBeRemovedIfEmpty.Count > 0)
                {
                    sb.AppendLine("  Folders that may be empty after (review before deleting manually):");
                    foreach (var d in g.DirectoriesThatMayBeRemovedIfEmpty.Take(12))
                        sb.AppendLine("    - " + d);
                    if (g.DirectoriesThatMayBeRemovedIfEmpty.Count > 12)
                        sb.AppendLine("    …");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
