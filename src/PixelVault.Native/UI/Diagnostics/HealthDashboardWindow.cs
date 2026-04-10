using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    /// <summary>
    /// Slice B (<c>PV-PLN-V1POL-001</c>): setup and operational health overview.
    /// </summary>
    static class HealthDashboardWindow
    {
        static readonly Style PlacementReportColumnHeaderStyle = CreatePlacementReportColumnHeaderStyle();

        static Style CreatePlacementReportColumnHeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            return style;
        }

        public static void ShowDialog(Window owner, SettingsShellDependencies d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));

            var window = new Window
            {
                Title = "PixelVault " + d.AppVersion + " — Setup & health",
                Width = 640,
                Height = 720,
                MinWidth = 520,
                MinHeight = 560,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = d.Brush(DesignTokens.PageBackground),
                ResizeMode = ResizeMode.CanResize
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(20, 18, 20, 18)
            };

            var stack = new StackPanel();

            stack.Children.Add(Headline("Setup & health", d));
            stack.Children.Add(Subtitle("Quick read on paths, tools, and logs. Fix issues from Path Settings.", d));

            var sources = SafeSourceRoots(d);
            var dest = d.GetDestinationRoot?.Invoke() ?? string.Empty;
            var library = d.GetLibraryRoot?.Invoke() ?? string.Empty;
            var exif = d.GetExifToolPath?.Invoke() ?? string.Empty;
            var ffmpeg = d.GetFfmpegPath?.Invoke() ?? string.Empty;
            var cacheRoot = d.GetCacheRoot?.Invoke() ?? string.Empty;
            var indexPath = d.GetActiveLibraryIndexDatabasePath?.Invoke() ?? string.Empty;
            var session = d.GetDiagnosticsSessionId?.Invoke() ?? string.Empty;
            var starredExport = d.GetStarredExportFolder?.Invoke() ?? string.Empty;

            stack.Children.Add(SectionCard(d, "This session",
                Row(d, "App version", d.AppVersion ?? "—", DesignTokens.StatusNeutral),
                Row(d, "Diagnostics session", string.IsNullOrWhiteSpace(session) ? "—" : session, DesignTokens.StatusNeutral)));

            var pathLines = new List<UIElement>
            {
                Row(d, "Cache folder", string.IsNullOrWhiteSpace(cacheRoot) ? "—" : cacheRoot,
                    DirOk(cacheRoot), DescribeDir(cacheRoot))
            };
            if (sources.Count == 0)
                pathLines.Add(Row(d, "Source folders", "(none configured)", DesignTokens.StatusWarn, "Add at least one intake folder in Path Settings."));
            else
            {
                for (var i = 0; i < sources.Count; i++)
                {
                    var p = sources[i];
                    pathLines.Add(Row(d, sources.Count == 1 ? "Source folder" : "Source " + (i + 1), p, DirOk(p), DescribeDir(p)));
                }
            }
            pathLines.Add(Row(d, "Destination", string.IsNullOrWhiteSpace(dest) ? "(not set)" : dest, PathState(dest, true), DescribeDir(dest)));
            pathLines.Add(Row(d, "Library", string.IsNullOrWhiteSpace(library) ? "(not set)" : library, PathState(library, true), DescribeDir(library)));
            if (!string.IsNullOrWhiteSpace(starredExport))
                pathLines.Add(Row(d, "Starred export", starredExport, DirOk(starredExport), DescribeDir(starredExport)));
            stack.Children.Add(SectionCard(d, "Paths", pathLines.ToArray()));

            var hasToken = d.HasSteamGridDbApiToken?.Invoke() ?? false;
            var hasSteamWeb = d.HasSteamWebApiKey?.Invoke() ?? false;
            var hasRa = d.HasRetroAchievementsApiKey?.Invoke() ?? false;
            stack.Children.Add(SectionCard(d, "External tools",
                Row(d, "ExifTool", string.IsNullOrWhiteSpace(exif) ? "(not set)" : exif, FileOk(exif), DescribeFile(exif)),
                Row(d, "FFmpeg", string.IsNullOrWhiteSpace(ffmpeg) ? "(optional, not set)" : ffmpeg,
                    string.IsNullOrWhiteSpace(ffmpeg) ? DesignTokens.StatusNeutral : FileOk(ffmpeg),
                    string.IsNullOrWhiteSpace(ffmpeg) ? "Optional for some video workflows." : DescribeFile(ffmpeg)),
                Row(d, "SteamGridDB", hasToken ? "API token on file" : "(optional, not set)",
                    hasToken ? DesignTokens.StatusOk : DesignTokens.StatusNeutral,
                    hasToken ? "Used for cover downloads." : "Optional unless you fetch covers from SteamGridDB."),
                Row(d, "Steam Web API", hasSteamWeb ? "API key configured" : "(optional, not set)",
                    hasSteamWeb ? DesignTokens.StatusOk : DesignTokens.StatusNeutral,
                    hasSteamWeb ? "Reserved for future Steam Web API features." : "Set in Path Settings or PIXELVAULT_STEAM_WEB_API_KEY when needed."),
                Row(d, "RetroAchievements", hasRa ? "API key configured" : "(optional, not set)",
                    hasRa ? DesignTokens.StatusOk : DesignTokens.StatusNeutral,
                    hasRa ? "Used for Edit IDs: RA game search against the live game database (API key only)." : "Set in Path Settings or PIXELVAULT_RETROACHIEVEMENTS_API_KEY when needed.")));

            string indexDetail;
            if (string.IsNullOrWhiteSpace(indexPath))
                indexDetail = "Open the library (or set Library folder) to create an index for that root.";
            else if (!File.Exists(indexPath))
                indexDetail = "File will be created when the library index is first used.";
            else
                try
                {
                    var len = new FileInfo(indexPath).Length;
                    indexDetail = "Present on disk (~" + (len / 1024) + " KB).";
                }
                catch
                {
                    indexDetail = "Present on disk.";
                }

            stack.Children.Add(SectionCard(d, "Library index (SQLite)",
                Row(d, "Active index file", string.IsNullOrWhiteSpace(indexPath) ? "(not applicable yet)" : indexPath,
                    string.IsNullOrWhiteSpace(indexPath)
                        ? DesignTokens.StatusNeutral
                        : File.Exists(indexPath) ? DesignTokens.StatusOk : DesignTokens.StatusNeutral,
                    indexDetail)));

            LibraryStoragePlacementHealthSnapshot placementHealth = null;
            try
            {
                placementHealth = d.GetLibraryStoragePlacementHealth?.Invoke();
            }
            catch
            {
                placementHealth = null;
            }
            if (placementHealth != null && placementHealth.IsApplicable)
            {
                var rowText = string.IsNullOrWhiteSpace(placementHealth.RowSummary) ? "—" : placementHealth.RowSummary;
                var fileText = string.IsNullOrWhiteSpace(placementHealth.IndexedFilesSummary) ? "—" : placementHealth.IndexedFilesSummary;
                var rowPill = placementHealth.RowNeedsAttention ? DesignTokens.StatusWarn
                    : rowText.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0
                      || rowText.IndexOf("No game rows", StringComparison.OrdinalIgnoreCase) >= 0
                        ? DesignTokens.StatusNeutral
                        : DesignTokens.StatusOk;
                var filePill = placementHealth.IndexedFilesNeedAttention ? DesignTokens.StatusWarn
                    : fileText.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0
                      || fileText.IndexOf("Could not read", StringComparison.OrdinalIgnoreCase) >= 0
                        ? DesignTokens.StatusWarn
                        : fileText.IndexOf("no entries yet", StringComparison.OrdinalIgnoreCase) >= 0
                          || fileText.IndexOf("unassigned", StringComparison.OrdinalIgnoreCase) >= 0
                          || fileText.IndexOf("No assigned captures", StringComparison.OrdinalIgnoreCase) >= 0
                            ? DesignTokens.StatusNeutral
                            : DesignTokens.StatusOk;
                var gameRowDetailsClick = placementHealth.GameRowMismatchTotalCount > 0
                    ? (Action)delegate { ShowStoragePlacementReportDialog(window, d, placementHealth, 0); }
                    : null;
                var fileDetailsClick = placementHealth.IndexedFileIssueTotalCount > 0
                    ? (Action)delegate { ShowStoragePlacementReportDialog(window, d, placementHealth, 1); }
                    : null;
                stack.Children.Add(SectionCard(d, "Library storage placement",
                    PlacementDetailRow(d, "Game index rows", rowText, rowPill,
                        "Compares each game’s saved folder to the folder PixelVault would use from your rules (Game Index → Target storage folder).",
                        gameRowDetailsClick),
                    PlacementDetailRow(d, "Indexed captures (photo index)", fileText, filePill,
                        "Captures with a GameId must live under that game’s library folder. Entries with no GameId are skipped.",
                        fileDetailsClick)));
            }

            var troubleOn = d.GetTroubleshootingLoggingEnabled?.Invoke() ?? false;
            var redact = d.GetTroubleshootingLogRedactPaths?.Invoke() ?? false;
            stack.Children.Add(SectionCard(d, "Logging",
                Row(d, "Troubleshooting log", troubleOn ? "Enabled" : "Off", troubleOn ? DesignTokens.StatusOk : DesignTokens.StatusNeutral,
                    troubleOn ? "Extra timing/diagnostic lines to troubleshooting log." : "Enable in Settings → Diagnostics if support asks."),
                Row(d, "Path redaction", redact ? "On" : "Off", DesignTokens.StatusNeutral, "Troubleshooting log only."),
                Row(d, "Main log", d.LogFilePath?.Invoke() ?? "—", DesignTokens.StatusNeutral, "Standard session log."),
                Row(d, "Troubleshooting file", d.TroubleshootingLogFilePath?.Invoke() ?? "—", DesignTokens.StatusNeutral, troubleOn ? "May rotate when very large." : "—")));

            var summary = BuildSummaryText(d, sources, dest, library, exif, ffmpeg, cacheRoot, indexPath, session, hasToken, troubleOn, starredExport, placementHealth);

            var actions = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
            void AddAction(string label, string bg, RoutedEventHandler click)
            {
                var b = d.Btn(label, click, bg, Brushes.White);
                b.Margin = new Thickness(0, 0, 10, 10);
                actions.Children.Add(b);
            }
            AddAction("Path Settings", DesignTokens.ActionPrimaryFill, delegate
            {
                d.OpenPathSettingsDialog?.Invoke();
                window.Close();
            });
            AddAction("Open logs folder", DesignTokens.ActionSecondaryFill, delegate
            {
                if (!string.IsNullOrWhiteSpace(d.LogsRoot) && Directory.Exists(d.LogsRoot)) d.OpenFolder(d.LogsRoot);
                else d.Log?.Invoke("Logs folder not available.");
            });
            AddAction("Copy summary", DesignTokens.ActionSecondaryFill, delegate
            {
                try
                {
                    Clipboard.SetText(summary);
                    d.Log?.Invoke("Health summary copied to clipboard.");
                }
                catch (Exception ex)
                {
                    d.Log?.Invoke("Could not copy summary. " + ex.Message);
                }
            });
            AddAction("Close", DesignTokens.ActionSecondaryFill, delegate { window.Close(); });
            stack.Children.Add(actions);

            scroll.Content = stack;
            window.Content = scroll;
            window.ShowDialog();
        }

        static IReadOnlyList<string> SafeSourceRoots(SettingsShellDependencies d)
        {
            try
            {
                var list = d.GetConfiguredSourceRoots?.Invoke();
                return list == null ? Array.Empty<string>() : list.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        static TextBlock Headline(string text, SettingsShellDependencies d)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = d.Brush(DesignTokens.TextOnInput),
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        static TextBlock Subtitle(string text, SettingsShellDependencies d)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = d.Brush(DesignTokens.TextLabelMuted),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };
        }

        static Border SectionCard(SettingsShellDependencies d, string title, params UIElement[] rows)
        {
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = d.Brush(DesignTokens.TextOnInput),
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 12)
            });
            foreach (var row in rows)
                inner.Children.Add(row);

            return new Border
            {
                Background = d.Brush(DesignTokens.PanelElevated),
                BorderBrush = d.Brush(DesignTokens.BorderDefault),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 14, 14, 14),
                Margin = new Thickness(0, 0, 0, 14),
                Child = inner
            };
        }

        static Grid Row(SettingsShellDependencies d, string label, string valueLine, string statusColorHex, string detail = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lab = new TextBlock
            {
                Text = label,
                Foreground = d.Brush(DesignTokens.TextLabelMuted),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap
            };
            var valPanel = new StackPanel();
            valPanel.Children.Add(new TextBlock
            {
                Text = valueLine,
                Foreground = d.Brush(DesignTokens.TextShortcutBody),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(detail))
                valPanel.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });

            var pill = new Border
            {
                Background = d.Brush(DesignTokens.InputBackground),
                BorderBrush = d.Brush(DesignTokens.BorderDefault),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = StatusWord(statusColorHex),
                    Foreground = d.Brush(statusColorHex),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };

            grid.Children.Add(lab);
            Grid.SetColumn(valPanel, 1);
            grid.Children.Add(valPanel);
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
            return grid;
        }

        /// <summary>Same as <see cref="Row"/>, with an optional clickable status pill that opens the placement report.</summary>
        static Grid PlacementDetailRow(SettingsShellDependencies d, string label, string valueLine, string statusColorHex, string detail, Action openReport)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lab = new TextBlock
            {
                Text = label,
                Foreground = d.Brush(DesignTokens.TextLabelMuted),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap
            };
            var valPanel = new StackPanel();
            valPanel.Children.Add(new TextBlock
            {
                Text = valueLine,
                Foreground = d.Brush(DesignTokens.TextShortcutBody),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(detail))
                valPanel.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });

            UIElement pill;
            if (openReport != null)
            {
                var pillBtn = new Button
                {
                    Content = StatusWord(statusColorHex),
                    Foreground = d.Brush(statusColorHex),
                    Background = d.Brush(DesignTokens.InputBackground),
                    BorderBrush = d.Brush(DesignTokens.BorderDefault),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(10, 0, 0, 0),
                    MinWidth = 0,
                    VerticalAlignment = VerticalAlignment.Top,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                ToolTipService.SetToolTip(pillBtn, "Open report");
                pillBtn.Click += delegate { openReport(); };
                pill = pillBtn;
            }
            else
            {
                pill = new Border
                {
                    Background = d.Brush(DesignTokens.InputBackground),
                    BorderBrush = d.Brush(DesignTokens.BorderDefault),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = StatusWord(statusColorHex),
                        Foreground = d.Brush(statusColorHex),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    }
                };
            }

            grid.Children.Add(lab);
            Grid.SetColumn(valPanel, 1);
            grid.Children.Add(valPanel);
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
            return grid;
        }

        const int PlacementDetailCap = 5000;

        static void ShowStoragePlacementReportDialog(Window owner, SettingsShellDependencies d, LibraryStoragePlacementHealthSnapshot snap, int initialTabIndex)
        {
            if (d == null || snap == null) return;

            var reportWindow = new Window
            {
                Title = "Library storage placement — report",
                Width = 920,
                Height = 520,
                MinWidth = 640,
                MinHeight = 420,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = d.Brush(DesignTokens.PageBackground),
                ResizeMode = ResizeMode.CanResize
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Review the grids below. Use Fix actions to correct issues without editing each row in Photo Index, or open the editors from the bottom row.",
                Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            var tabControl = new TabControl
            {
                Background = d.Brush(DesignTokens.PanelElevated),
                BorderBrush = d.Brush(DesignTokens.BorderDefault)
            };
            Grid.SetRow(tabControl, 1);

            var gameRows = snap.GameRowMismatches ?? Array.Empty<LibraryStoragePlacementGameRowMismatch>();
            var fileIssues = snap.IndexedFileIssues ?? Array.Empty<LibraryStoragePlacementIndexedFileIssue>();

            var gameHeaderText = "Game index rows";
            if (snap.GameRowMismatchTotalCount > gameRows.Count)
                gameHeaderText += " (" + gameRows.Count + " of " + snap.GameRowMismatchTotalCount + ")";
            else if (snap.GameRowMismatchTotalCount > 0)
                gameHeaderText += " (" + snap.GameRowMismatchTotalCount + ")";

            var fileHeaderText = "Indexed captures";
            if (snap.IndexedFileIssueTotalCount > fileIssues.Count)
                fileHeaderText += " (" + fileIssues.Count + " of " + snap.IndexedFileIssueTotalCount + ")";
            else if (snap.IndexedFileIssueTotalCount > 0)
                fileHeaderText += " (" + snap.IndexedFileIssueTotalCount + ")";

            var gameTab = new TabItem { Header = gameHeaderText };
            if (gameRows.Count == 0)
            {
                gameTab.Content = new TextBlock
                {
                    Text = snap.GameRowMismatchTotalCount > 0
                        ? "Mismatch count is " + snap.GameRowMismatchTotalCount + " but no rows are listed (refresh health or check logs)."
                        : "No row-level path mismatches.",
                    Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8)
                };
            }
            else
            {
                var gameStack = new StackPanel();
                if (snap.GameRowMismatchTotalCount > gameRows.Count)
                    gameStack.Children.Add(new TextBlock
                    {
                        Text = "Showing first " + gameRows.Count + " of " + snap.GameRowMismatchTotalCount + " mismatched rows (cap " + PlacementDetailCap + ").",
                        Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8),
                        TextWrapping = TextWrapping.Wrap
                    });
                var gameDg = BuildPlacementGameRowsDataGrid(d, gameRows);
                gameStack.Children.Add(gameDg);
                gameTab.Content = gameStack;
            }

            var fileTab = new TabItem { Header = fileHeaderText };
            if (fileIssues.Count == 0)
            {
                fileTab.Content = new TextBlock
                {
                    Text = snap.IndexedFileIssueTotalCount > 0
                        ? "Issues exist but no rows are listed (refresh health or check logs)."
                        : "No indexed capture placement issues.",
                    Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8)
                };
            }
            else
            {
                var fileStack = new StackPanel();
                if (snap.IndexedFileIssueTotalCount > fileIssues.Count)
                    fileStack.Children.Add(new TextBlock
                    {
                        Text = "Showing first " + fileIssues.Count + " of " + snap.IndexedFileIssueTotalCount + " issues (cap " + PlacementDetailCap + ").",
                        Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8),
                        TextWrapping = TextWrapping.Wrap
                    });
                fileStack.Children.Add(new TextBlock
                {
                    Text = "“Outside canonical folder” compares each file’s path stored in the photo index to the single canonical folder name computed for that GameId from the game index. A different folder next to it (even with a similar title) is not “inside” until the file lives under the canonical path or you refresh after moving files.",
                    Foreground = d.Brush(DesignTokens.TextShortcutMuted),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                });
                var fileDg = BuildPlacementIndexedFilesDataGrid(d, fileIssues);
                fileStack.Children.Add(fileDg);
                fileTab.Content = fileStack;
            }

            tabControl.Items.Add(gameTab);
            tabControl.Items.Add(fileTab);
            var idx = initialTabIndex <= 0 ? 0 : 1;
            tabControl.SelectedIndex = idx;
            layout.Children.Add(tabControl);

            var remediation = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void AddFix(string label, string bg, RoutedEventHandler click)
            {
                var b = d.Btn(label, click, bg, Brushes.White);
                b.Margin = new Thickness(0, 0, 10, 8);
                remediation.Children.Add(b);
            }
            if (snap.GameRowMismatchTotalCount > 0 && d.PlacementTryAlignGameIndexFoldersToCanonical != null)
            {
                AddFix("Fix game folders…", DesignTokens.ActionPrimaryFill, delegate
                {
                    var msg = "Move files on each game index row into the folders PixelVault expects for that title?\n\n"
                        + "This is the same kind of move as storage merge. A library backup is recommended for large libraries.";
                    if (MessageBox.Show(reportWindow, msg, "PixelVault", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    if (d.PlacementTryAlignGameIndexFoldersToCanonical.Invoke())
                    {
                        MessageBox.Show(reportWindow, "Game index folders were aligned. Reopen Setup & health to refresh counts.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        reportWindow.Close();
                    }
                    else
                        MessageBox.Show(reportWindow, "Could not align folders. See the log for details.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            if (snap.IndexedFileMisplacedTotalCount > 0 && d.PlacementMoveMisplacedCapturesToCanonical != null)
            {
                var m = snap.IndexedFileMisplacedTotalCount;
                AddFix("Move misplaced captures…", DesignTokens.ActionPrimaryFill, delegate
                {
                    var msg = "Move " + m + " misplaced capture(s) into the canonical folders for their GameId?\n\n"
                        + "If a file already exists at the destination, PixelVault may pick a new file name.";
                    if (MessageBox.Show(reportWindow, msg, "PixelVault", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    var moved = d.PlacementMoveMisplacedCapturesToCanonical.Invoke();
                    if (moved < 0)
                        MessageBox.Show(reportWindow, "Could not move captures. See the log for details.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else if (moved == 0)
                        MessageBox.Show(
                            reportWindow,
                            "No files were moved. If indexed paths no longer exist on disk, use Library → Refresh, then try again. Otherwise the placement check may already match what organize would do.",
                            "PixelVault",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    else
                    {
                        MessageBox.Show(reportWindow, "Moved " + moved + " file(s). Reopen Setup & health to refresh counts.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        reportWindow.Close();
                    }
                });
            }
            if (snap.IndexedFileOrphanTotalCount > 0 && d.PlacementClearOrphanPhotoGameIds != null)
            {
                var o = snap.IndexedFileOrphanTotalCount;
                AddFix("Clear orphan GameIds…", DesignTokens.ActionPrimaryFill, delegate
                {
                    var msg = "Clear GameId on " + o + " capture(s) whose GameId no longer exists in the game index?\n\n"
                        + "Those captures become unassigned (no GameId). You can assign them again later in Photo Index.";
                    if (MessageBox.Show(reportWindow, msg, "PixelVault", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    var cleared = d.PlacementClearOrphanPhotoGameIds.Invoke();
                    if (cleared < 0)
                        MessageBox.Show(reportWindow, "Could not clear GameIds. See the log for details.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                    {
                        MessageBox.Show(reportWindow, "Cleared GameId on " + cleared + " capture(s). Reopen Setup & health to refresh counts.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        reportWindow.Close();
                    }
                });
            }
            if (remediation.Children.Count > 0)
            {
                Grid.SetRow(remediation, 2);
                layout.Children.Add(remediation);
            }

            var footer = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            void AddFooter(string label, string bg, RoutedEventHandler click)
            {
                var b = d.Btn(label, click, bg, Brushes.White);
                b.Margin = new Thickness(0, 0, 10, 0);
                footer.Children.Add(b);
            }
            AddFooter("Copy report", DesignTokens.ActionSecondaryFill, delegate
            {
                try
                {
                    Clipboard.SetText(BuildStoragePlacementReportText(snap));
                    d.Log?.Invoke("Storage placement report copied to clipboard.");
                }
                catch (Exception ex)
                {
                    d.Log?.Invoke("Could not copy report. " + ex.Message);
                }
            });
            AddFooter("Open Game Index", DesignTokens.ActionSecondaryFill, delegate
            {
                d.OpenGameIndexEditor?.Invoke();
            });
            AddFooter("Open Photo Index", DesignTokens.ActionSecondaryFill, delegate
            {
                d.OpenPhotoIndexEditor?.Invoke();
            });
            AddFooter("Close", DesignTokens.ActionSecondaryFill, delegate { reportWindow.Close(); });
            Grid.SetRow(footer, remediation.Children.Count > 0 ? 3 : 2);
            layout.Children.Add(footer);

            reportWindow.Content = layout;
            reportWindow.ShowDialog();
        }

        static DataGrid BuildPlacementGameRowsDataGrid(SettingsShellDependencies d, IReadOnlyList<LibraryStoragePlacementGameRowMismatch> rows)
        {
            var gameDg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                ItemsSource = rows,
                Background = d.Brush(DesignTokens.PanelElevated),
                Foreground = d.Brush(DesignTokens.TextOnInput),
                BorderBrush = d.Brush(DesignTokens.BorderDefault),
                VerticalGridLinesBrush = d.Brush(DesignTokens.BorderDefault),
                HorizontalGridLinesBrush = d.Brush(DesignTokens.BorderDefault),
                AlternatingRowBackground = d.Brush(DesignTokens.InputBackground),
                RowBackground = d.Brush(DesignTokens.PanelElevated),
                MaxHeight = double.PositiveInfinity,
                ColumnHeaderStyle = PlacementReportColumnHeaderStyle
            };
            gameDg.Columns.Add(new DataGridTextColumn
            {
                Header = "GameId",
                Binding = new Binding("GameId"),
                Width = new DataGridLength(80)
            });
            gameDg.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Binding("Name"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            gameDg.Columns.Add(new DataGridTextColumn
            {
                Header = "Cached folder",
                Binding = new Binding("CachedFolderPath"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            gameDg.Columns.Add(new DataGridTextColumn
            {
                Header = "Canonical folder",
                Binding = new Binding("CanonicalFolderPath"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            return gameDg;
        }

        static DataGrid BuildPlacementIndexedFilesDataGrid(SettingsShellDependencies d, IReadOnlyList<LibraryStoragePlacementIndexedFileIssue> issues)
        {
            var fileDg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                ItemsSource = issues,
                Background = d.Brush(DesignTokens.PanelElevated),
                Foreground = d.Brush(DesignTokens.TextOnInput),
                BorderBrush = d.Brush(DesignTokens.BorderDefault),
                VerticalGridLinesBrush = d.Brush(DesignTokens.BorderDefault),
                HorizontalGridLinesBrush = d.Brush(DesignTokens.BorderDefault),
                AlternatingRowBackground = d.Brush(DesignTokens.InputBackground),
                RowBackground = d.Brush(DesignTokens.PanelElevated),
                MaxHeight = double.PositiveInfinity,
                ColumnHeaderStyle = PlacementReportColumnHeaderStyle
            };
            fileDg.Columns.Add(new DataGridTextColumn
            {
                Header = "Issue",
                Binding = new Binding("IssueKindDisplay"),
                Width = new DataGridLength(200)
            });
            fileDg.Columns.Add(new DataGridTextColumn
            {
                Header = "GameId",
                Binding = new Binding("GameId"),
                Width = new DataGridLength(80)
            });
            fileDg.Columns.Add(new DataGridTextColumn
            {
                Header = "File",
                Binding = new Binding("FilePath"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            fileDg.Columns.Add(new DataGridTextColumn
            {
                Header = "Expected folder",
                Binding = new Binding("CanonicalFolderPath"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            return fileDg;
        }

        static string BuildStoragePlacementReportText(LibraryStoragePlacementHealthSnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Library storage placement — report");
            sb.AppendLine();
            sb.AppendLine("Summary (rows): " + (snap.RowSummary ?? ""));
            sb.AppendLine("Summary (indexed files): " + (snap.IndexedFilesSummary ?? ""));
            sb.AppendLine();
            sb.AppendLine("Game index rows (cached path vs canonical):");
            var rows = snap.GameRowMismatches;
            if (rows == null || rows.Count == 0)
                sb.AppendLine("  (none listed)");
            else
            {
                foreach (var r in rows)
                {
                    sb.AppendLine("  GameId: " + (r.GameId ?? ""));
                    sb.AppendLine("  Name: " + (r.Name ?? ""));
                    sb.AppendLine("  Cached: " + (r.CachedFolderPath ?? ""));
                    sb.AppendLine("  Canonical: " + (r.CanonicalFolderPath ?? ""));
                    sb.AppendLine();
                }
            }
            if (snap.GameRowMismatchTotalCount > (rows?.Count ?? 0))
                sb.AppendLine("  (" + (snap.GameRowMismatchTotalCount - (rows?.Count ?? 0)) + " more row mismatches not listed; cap " + PlacementDetailCap + ".)");
            sb.AppendLine();
            sb.AppendLine("Indexed captures:");
            var files = snap.IndexedFileIssues;
            if (files == null || files.Count == 0)
                sb.AppendLine("  (none listed)");
            else
            {
                foreach (var f in files)
                {
                    sb.AppendLine("  " + (f.IssueKindDisplay ?? "") + " | GameId=" + (f.GameId ?? "") + " | File=" + (f.FilePath ?? ""));
                    if (!string.IsNullOrWhiteSpace(f.CanonicalFolderPath))
                        sb.AppendLine("    Expected folder: " + f.CanonicalFolderPath);
                }
            }
            if (snap.IndexedFileIssueTotalCount > (files?.Count ?? 0))
                sb.AppendLine("  (" + (snap.IndexedFileIssueTotalCount - (files?.Count ?? 0)) + " more file issues not listed; cap " + PlacementDetailCap + ".)");
            return sb.ToString();
        }

        static string StatusWord(string colorHex)
        {
            if (string.Equals(colorHex, DesignTokens.StatusOk, StringComparison.OrdinalIgnoreCase)) return "OK";
            if (string.Equals(colorHex, DesignTokens.StatusBad, StringComparison.OrdinalIgnoreCase)) return "Issue";
            if (string.Equals(colorHex, DesignTokens.StatusWarn, StringComparison.OrdinalIgnoreCase)) return "Check";
            return "Info";
        }

        static string DirOk(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return DesignTokens.StatusWarn;
            return Directory.Exists(path) ? DesignTokens.StatusOk : DesignTokens.StatusBad;
        }

        static string DescribeDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Not set.";
            return Directory.Exists(path) ? "Folder is reachable." : "Folder not found.";
        }

        static string PathState(string path, bool required)
        {
            if (string.IsNullOrWhiteSpace(path)) return required ? DesignTokens.StatusWarn : DesignTokens.StatusNeutral;
            return Directory.Exists(path) ? DesignTokens.StatusOk : DesignTokens.StatusBad;
        }

        static string FileOk(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return DesignTokens.StatusBad;
            return File.Exists(path) ? DesignTokens.StatusOk : DesignTokens.StatusBad;
        }

        static string DescribeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Not set.";
            return File.Exists(path) ? "Executable found." : "File not found.";
        }

        static string BuildSummaryText(
            SettingsShellDependencies d,
            IReadOnlyList<string> sources,
            string dest,
            string library,
            string exif,
            string ffmpeg,
            string cacheRoot,
            string indexPath,
            string session,
            bool hasSteamToken,
            bool troubleOn,
            string starredExport,
            LibraryStoragePlacementHealthSnapshot placementHealth)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PixelVault health summary");
            sb.AppendLine("Version: " + (d.AppVersion ?? "?"));
            sb.AppendLine("Session: " + (session ?? ""));
            sb.AppendLine("Time (local): " + DateTime.Now.ToString("u"));
            sb.AppendLine();
            sb.AppendLine("Paths:");
            sb.AppendLine("  Cache: " + (cacheRoot ?? ""));
            if (sources.Count == 0) sb.AppendLine("  Sources: (none)");
            else
                for (var i = 0; i < sources.Count; i++)
                    sb.AppendLine("  Source " + (i + 1) + ": " + sources[i] + " | exists=" + Directory.Exists(sources[i]));
            sb.AppendLine("  Destination: " + dest + " | exists=" + Directory.Exists(dest ?? ""));
            sb.AppendLine("  Library: " + library + " | exists=" + Directory.Exists(library ?? ""));
            if (!string.IsNullOrWhiteSpace(starredExport))
                sb.AppendLine("  Starred export: " + starredExport + " | exists=" + Directory.Exists(starredExport));
            sb.AppendLine();
            sb.AppendLine("Tools:");
            sb.AppendLine("  ExifTool: " + exif + " | exists=" + File.Exists(exif ?? ""));
            sb.AppendLine("  FFmpeg: " + (string.IsNullOrWhiteSpace(ffmpeg) ? "(optional, not set)" : ffmpeg) + " | exists=" + (string.IsNullOrWhiteSpace(ffmpeg) ? "optional" : File.Exists(ffmpeg).ToString()));
            sb.AppendLine("  SteamGridDB token: " + (hasSteamToken ? "configured" : "not set"));
            sb.AppendLine();
            sb.AppendLine("Index DB: " + (string.IsNullOrWhiteSpace(indexPath) ? "(n/a)" : indexPath) + " | exists=" + (string.IsNullOrWhiteSpace(indexPath) ? "n/a" : File.Exists(indexPath).ToString()));
            sb.AppendLine();
            sb.AppendLine("Logging: troubleshooting=" + troubleOn);
            sb.AppendLine("Main log: " + (d.LogFilePath?.Invoke() ?? ""));
            sb.AppendLine("Troubleshooting log: " + (d.TroubleshootingLogFilePath?.Invoke() ?? ""));
            sb.AppendLine();
            if (placementHealth != null && placementHealth.IsApplicable)
            {
                sb.AppendLine("Storage placement (rows): " + (placementHealth.RowSummary ?? ""));
                sb.AppendLine("Storage placement (indexed files): " + (placementHealth.IndexedFilesSummary ?? ""));
            }
            else sb.AppendLine("Storage placement: (n/a)");
            return sb.ToString();
        }
    }
}
