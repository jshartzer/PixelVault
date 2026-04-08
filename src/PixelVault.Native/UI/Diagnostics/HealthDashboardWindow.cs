using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    /// <summary>
    /// Slice B (<c>PV-PLN-V1POL-001</c>): setup and operational health overview.
    /// </summary>
    static class HealthDashboardWindow
    {
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

            var troubleOn = d.GetTroubleshootingLoggingEnabled?.Invoke() ?? false;
            var redact = d.GetTroubleshootingLogRedactPaths?.Invoke() ?? false;
            stack.Children.Add(SectionCard(d, "Logging",
                Row(d, "Troubleshooting log", troubleOn ? "Enabled" : "Off", troubleOn ? DesignTokens.StatusOk : DesignTokens.StatusNeutral,
                    troubleOn ? "Extra timing/diagnostic lines to troubleshooting log." : "Enable in Settings → Diagnostics if support asks."),
                Row(d, "Path redaction", redact ? "On" : "Off", DesignTokens.StatusNeutral, "Troubleshooting log only."),
                Row(d, "Main log", d.LogFilePath?.Invoke() ?? "—", DesignTokens.StatusNeutral, "Standard session log."),
                Row(d, "Troubleshooting file", d.TroubleshootingLogFilePath?.Invoke() ?? "—", DesignTokens.StatusNeutral, troubleOn ? "May rotate when very large." : "—")));

            var summary = BuildSummaryText(d, sources, dest, library, exif, ffmpeg, cacheRoot, indexPath, session, hasToken, troubleOn, starredExport);

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
            string starredExport)
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
            return sb.ToString();
        }
    }
}
