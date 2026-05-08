using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        readonly Dictionary<string, Window> _libraryGameProfileWindows = new Dictionary<string, Window>(StringComparer.OrdinalIgnoreCase);

        internal void LibraryBrowserOpenGameProfile(Window owner, LibraryBrowserFolderView view)
        {
            if (view == null || IsLibraryBrowserTimeProjectionView(view)) return;
            var folder = BuildLibraryBrowserDisplayFolder(view);
            if (folder == null) return;
            ShowLibraryGameProfileWindow(owner ?? this, view, folder);
        }

        static string LibraryGameProfileWindowKey(LibraryFolderInfo folder)
        {
            if (folder == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.GameId)) return "id:" + folder.GameId.Trim();
            if (!string.IsNullOrWhiteSpace(folder.FolderPath)) return "path:" + folder.FolderPath.Trim();
            if (!string.IsNullOrWhiteSpace(folder.Name)) return "name:" + folder.Name.Trim();
            return string.Empty;
        }

        void ShowLibraryGameProfileWindow(Window owner, LibraryBrowserFolderView view, LibraryFolderInfo folder)
        {
            var key = LibraryGameProfileWindowKey(folder);
            if (!string.IsNullOrEmpty(key)
                && _libraryGameProfileWindows.TryGetValue(key, out var existing)
                && existing != null
                && existing.IsLoaded)
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }

            var title = string.IsNullOrWhiteSpace(folder.Name) ? "Game Profile" : folder.Name.Trim();
            var win = new Window
            {
                Title = "PixelVault Game Profile - " + title,
                Width = 1180,
                // Sized for the Phase B dashboard layout (hero + summary line + 5 stat
                // cards + filmstrip + notes card + achievements). 980 fits comfortably on
                // 1080p displays after the OS chrome / taskbar.
                Height = 980,
                MinWidth = 900,
                MinHeight = 640,
                Owner = owner,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = Brush("#0B1116")
            };
            AutomationProperties.SetName(win, "Game Profile - " + title);

            // Cancels any in-flight async loads (achievements fetch, etc.) when the user closes the profile mid-load.
            var lifetimeCts = new CancellationTokenSource();
            win.Closed += delegate
            {
                try { lifetimeCts.Cancel(); } catch { }
                try { lifetimeCts.Dispose(); } catch { }
                if (!string.IsNullOrEmpty(key)
                    && _libraryGameProfileWindows.TryGetValue(key, out var tracked)
                    && ReferenceEquals(tracked, win))
                {
                    _libraryGameProfileWindows.Remove(key);
                }
            };
            win.PreviewKeyDown += delegate(object _, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                win.Close();
            };

            // Compute the per-profile metrics up front so the hero summary line and the
            // stat strip share a single snapshot (PV-POL-GPRO-DATA-001). The session count
            // here is a Phase A placeholder using the shared session-threshold setting; the
            // full session-grouping helper from PV-PLN-GPRO-001 Phase D will replace it.
            var files = GetFilesForLibraryFolderEntry(folder, false)
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var metadataIndex = librarySession == null || !librarySession.HasLibraryRoot
                ? new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
                : librarySession.LoadLibraryMetadataIndexForFilePaths(files);
            var orderedFiles = files
                .Select(file => new { File = file, Date = ResolveLibraryProfileCaptureDate(file, metadataIndex) })
                .OrderByDescending(row => row.Date)
                .ThenBy(row => row.File, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var orderedFilePaths = orderedFiles.Select(row => row.File).ToList();
            var metrics = ComputeLibraryGameProfileMetrics(orderedFilePaths, metadataIndex, librarySessionThresholdMinutes);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildLibraryGameProfileHero(win, view, folder, metrics));

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 18, 22, 24)
            };
            Grid.SetRow(scroll, 1);
            var body = new StackPanel();
            scroll.Content = body;
            body.Children.Add(BuildLibraryGameProfileStats(metrics));
            body.Children.Add(BuildLibraryGameProfileNotesCard(win, view, folder));
            body.Children.Add(BuildLibraryGameProfileCaptureFilmstrip(win, view, orderedFilePaths));
            var achievementHost = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
            body.Children.Add(achievementHost);
            BeginLoadLibraryGameProfileAchievements(win, achievementHost, view, lifetimeCts.Token);
            root.Children.Add(scroll);
            win.Content = root;
            if (!string.IsNullOrEmpty(key)) _libraryGameProfileWindows[key] = win;
            win.Show();
            win.Activate();
        }

        // Snapshot of the numbers the dashboard top-strip and hero summary line share.
        // Computed once per profile open from the same metadata-index read the rest of the
        // window already uses (PV-POL-GPRO-DATA-001).
        sealed class LibraryGameProfileMetrics
        {
            public int CaptureCount;
            public int VideoCount;
            public int SessionCount;
            public DateTime FirstCaptureDate;
            public DateTime LatestCaptureDate;
        }

        LibraryGameProfileMetrics ComputeLibraryGameProfileMetrics(
            IReadOnlyList<string> orderedFiles,
            IReadOnlyDictionary<string, LibraryMetadataIndexEntry> metadataIndex,
            int sessionThresholdMinutes)
        {
            var safeFiles = orderedFiles ?? Array.Empty<string>();
            var ascendingDates = safeFiles
                .Select(file => ResolveLibraryProfileCaptureDate(file, metadataIndex))
                .Where(date => date > DateTime.MinValue)
                .OrderBy(date => date)
                .ToList();
            return new LibraryGameProfileMetrics
            {
                CaptureCount = safeFiles.Count,
                VideoCount = safeFiles.Count(IsVideo),
                SessionCount = CountLibraryGameProfileSessions(ascendingDates, sessionThresholdMinutes),
                FirstCaptureDate = ascendingDates.Count == 0 ? DateTime.MinValue : ascendingDates[0],
                LatestCaptureDate = ascendingDates.Count == 0 ? DateTime.MinValue : ascendingDates[ascendingDates.Count - 1]
            };
        }

        // Phase A placeholder: counts gameplay sessions by walking an ascending date list and
        // starting a new session whenever the gap exceeds the shared session-threshold setting
        // (PV-POL-GPRO-SESSION-001). Phase D will replace this with the full session-grouping
        // helper that returns the per-session file slices the Sessions section needs.
        static int CountLibraryGameProfileSessions(IReadOnlyList<DateTime> sortedAscendingDates, int thresholdMinutes)
        {
            if (sortedAscendingDates == null || sortedAscendingDates.Count == 0) return 0;
            var threshold = TimeSpan.FromMinutes(SettingsService.NormalizeLibrarySessionThresholdMinutes(thresholdMinutes));
            var count = 1;
            for (var i = 1; i < sortedAscendingDates.Count; i++)
            {
                if (sortedAscendingDates[i] - sortedAscendingDates[i - 1] > threshold) count++;
            }
            return count;
        }

        FrameworkElement BuildLibraryGameProfileHero(Window window, LibraryBrowserFolderView view, LibraryFolderInfo folder, LibraryGameProfileMetrics metrics)
        {
            var hero = new Grid
            {
                MinHeight = 275,
                Background = Brush("#111A21"),
                ClipToBounds = true
            };
            var bannerPath = GetLibraryHeroBannerPathForDisplayOnly(folder);
            if (string.IsNullOrWhiteSpace(bannerPath) || !File.Exists(bannerPath))
                bannerPath = GetLibraryArtPathForDisplayOnly(folder);
            var banner = new Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0.58,
                Visibility = Visibility.Collapsed
            };
            hero.Children.Add(banner);
            QueueImageLoad(
                banner,
                bannerPath,
                CalculateLibraryBannerArtDecodeWidth(window, window, ResolveLibraryDpiScale()),
                loaded =>
                {
                    banner.Source = loaded;
                    banner.Visibility = Visibility.Visible;
                },
                true);
            hero.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(238, 8, 13, 17), 0),
                        new GradientStop(Color.FromArgb(170, 8, 13, 17), 0.42),
                        new GradientStop(Color.FromArgb(238, 8, 13, 17), 1)
                    },
                    new Point(0, 0),
                    new Point(1, 1))
            });
            hero.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 96,
                Background = new LinearGradientBrush(Color.FromArgb(0, 11, 17, 22), Color.FromArgb(255, 11, 17, 22), new Point(0, 0), new Point(0, 1))
            });

            var content = new Grid { Margin = new Thickness(26, 22, 26, 24) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            var cover = CreateAsyncImageTile(
                GetLibraryArtPathForDisplayOnly(folder),
                420,
                138,
                207,
                Stretch.UniformToFill,
                folder.Name ?? string.Empty,
                Brushes.White,
                new Thickness(0),
                new Thickness(0),
                Brush("#151F27"),
                new CornerRadius(16),
                Brush("#3E5665"),
                new Thickness(1));
            cover.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 7, Direction = 270, Color = Color.FromArgb(120, 0, 0, 0), Opacity = 0.75 };
            content.Children.Add(cover);

            var copy = new StackPanel { Margin = new Thickness(24, 14, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var logoPath = GetLibraryHeroLogoPathForDisplayOnly(folder);
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                var logo = new Image
                {
                    MaxWidth = 420,
                    MaxHeight = 94,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                QueueImageLoad(logo, logoPath, 720, loaded =>
                {
                    logo.Source = loaded;
                    logo.Visibility = Visibility.Visible;
                }, true);
                copy.Children.Add(logo);
            }
            copy.Children.Add(new TextBlock
            {
                Text = folder.Name ?? "Game Profile",
                Foreground = Brushes.White,
                FontSize = 38,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var summaryLine = BuildLibraryGameProfileHeroSummaryLine(metrics);
            if (summaryLine != null) copy.Children.Add(summaryLine);
            var badges = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foreach (var label in ResolveLibraryGameProfilePlatformLabels(view, folder))
            {
                var badge = BuildLibraryBrowserDetailTitlePlatformBadge(label);
                if (badge == null) continue;
                ApplyLibraryGameProfileHeroBadgeChrome(badge);
                if (badge is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
                badges.Children.Add(badge);
            }
            if (badges.Children.Count > 0) copy.Children.Add(badges);
            var idPills = BuildLibraryGameProfileIdPills(folder);
            if (idPills != null) copy.Children.Add(idPills);
            // The hero used to render a clipped CollectionNotes preview here. As of
            // PV-PLN-GPRO-001 step B.1 the dedicated Game Notes card in the body owns
            // the notes surface end-to-end (display + edit), so we let the hero focus
            // on identity (title / summary line / badges / IDs).
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);
            hero.Children.Add(content);
            return hero;
        }

        IEnumerable<string> ResolveLibraryGameProfilePlatformLabels(LibraryBrowserFolderView view, LibraryFolderInfo folder)
        {
            var labels = (view == null ? Array.Empty<string>() : view.PlatformLabels ?? Array.Empty<string>())
                .Concat(new[] { folder == null ? string.Empty : folder.PlatformLabel })
                .Select(NormalizeConsoleLabel)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PlatformGroupOrder)
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return labels.Count == 0 ? new[] { "Other" } : labels;
        }

        // External-ID pills (PV-PLN-GPRO-001 step B.3). Returns null when no IDs are
        // available so the hero just collapses the row. Steam / SteamGridDB /
        // RetroAchievements pills are clickable and open the matching external page;
        // Non-Steam stays informational because there is no canonical destination.
        FrameworkElement BuildLibraryGameProfileIdPills(LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId))
            {
                var id = folder.SteamAppId.Trim();
                var url = BuildLibraryGameProfileSteamUrl(id);
                panel.Children.Add(BuildLibraryGameProfileIdPill(
                    "Steam " + id,
                    url,
                    string.IsNullOrEmpty(url) ? "Steam App ID" : "Open in Steam"));
            }
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId))
            {
                var id = folder.SteamGridDbId.Trim();
                var url = BuildLibraryGameProfileSteamGridDbUrl(id);
                panel.Children.Add(BuildLibraryGameProfileIdPill(
                    "SteamGridDB " + id,
                    url,
                    string.IsNullOrEmpty(url) ? "SteamGridDB ID" : "Open SteamGridDB page"));
            }
            if (!string.IsNullOrWhiteSpace(folder.RetroAchievementsGameId))
            {
                var id = folder.RetroAchievementsGameId.Trim();
                var url = BuildLibraryGameProfileRetroAchievementsUrl(id);
                panel.Children.Add(BuildLibraryGameProfileIdPill(
                    "RetroAchievements " + id,
                    url,
                    string.IsNullOrEmpty(url) ? "RetroAchievements ID" : "Open RetroAchievements page"));
            }
            if (!string.IsNullOrWhiteSpace(folder.NonSteamId))
            {
                panel.Children.Add(BuildLibraryGameProfileIdPill(
                    "Non-Steam " + folder.NonSteamId.Trim(),
                    null,
                    "Non-Steam shortcut ID"));
            }
            return panel.Children.Count == 0 ? null : panel;
        }

        FrameworkElement BuildLibraryGameProfileIdPill(string label, string url, string tooltip)
        {
            var clickable = !string.IsNullOrEmpty(url);
            var pill = new Border
            {
                Padding = new Thickness(10, 4, 10, 5),
                CornerRadius = new CornerRadius(8),
                Background = Brush(clickable ? "#1B2C3B" : "#162028"),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = clickable ? System.Windows.Input.Cursors.Hand : null
            };
            pill.Child = new TextBlock
            {
                Text = label ?? string.Empty,
                Foreground = Brush(clickable ? "#D7E2EA" : "#9EAEB7"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            if (!string.IsNullOrEmpty(tooltip)) pill.ToolTip = tooltip;
            if (clickable)
            {
                AutomationProperties.SetName(pill, label + " (link)");
                pill.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    TryOpenLibraryGameProfileExternalUrl(url);
                };
                pill.MouseEnter += delegate { pill.Background = Brush("#264358"); };
                pill.MouseLeave += delegate { pill.Background = Brush("#1B2C3B"); };
            }
            else
            {
                AutomationProperties.SetName(pill, label);
            }
            return pill;
        }

        // Numeric-only validators; we never feed user-supplied free-form text into the
        // shell. Returns empty when the ID does not look like a Steam-style integer so
        // the pill renders as informational rather than clickable.
        static string BuildLibraryGameProfileSteamUrl(string appId)
        {
            var trimmed = (appId ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;
            for (var i = 0; i < trimmed.Length; i++) if (!char.IsDigit(trimmed[i])) return string.Empty;
            return "steam://nav/games/details/" + trimmed;
        }

        static string BuildLibraryGameProfileSteamGridDbUrl(string id)
        {
            var trimmed = (id ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;
            for (var i = 0; i < trimmed.Length; i++) if (!char.IsDigit(trimmed[i])) return string.Empty;
            return "https://www.steamgriddb.com/game/" + trimmed;
        }

        static string BuildLibraryGameProfileRetroAchievementsUrl(string id)
        {
            var trimmed = (id ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;
            for (var i = 0; i < trimmed.Length; i++) if (!char.IsDigit(trimmed[i])) return string.Empty;
            return "https://retroachievements.org/game/" + trimmed;
        }

        void TryOpenLibraryGameProfileExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogException("Game profile | open external URL: " + url, ex);
                TryLibraryToast("Couldn't open external link.", MessageBoxImage.Warning);
            }
        }

        FrameworkElement BuildLibraryGameProfileHeroSummaryLine(LibraryGameProfileMetrics metrics)
        {
            if (metrics == null) return null;
            var parts = new List<string>();
            if (metrics.CaptureCount > 0) parts.Add(metrics.CaptureCount.ToString(CultureInfo.CurrentCulture) + (metrics.CaptureCount == 1 ? " capture" : " captures"));
            if (metrics.VideoCount > 0) parts.Add(metrics.VideoCount.ToString(CultureInfo.CurrentCulture) + (metrics.VideoCount == 1 ? " video" : " videos"));
            if (metrics.SessionCount > 0) parts.Add(metrics.SessionCount.ToString(CultureInfo.CurrentCulture) + (metrics.SessionCount == 1 ? " session" : " sessions"));
            if (metrics.FirstCaptureDate > DateTime.MinValue && metrics.LatestCaptureDate > DateTime.MinValue)
            {
                parts.Add(metrics.FirstCaptureDate.Date == metrics.LatestCaptureDate.Date
                    ? FormatLibraryGameProfileDate(metrics.FirstCaptureDate)
                    : FormatLibraryGameProfileDate(metrics.FirstCaptureDate) + " \u2192 " + FormatLibraryGameProfileDate(metrics.LatestCaptureDate));
            }
            if (parts.Count == 0) return null;
            return new TextBlock
            {
                Text = string.Join(" \u00B7 ", parts),
                Foreground = Brush("#A9BAC4"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
        }

        // The folder-tile platform badge is intentionally bright (#F4F8FB at 1.15px) for tile
        // contrast; in the profile hero the same chrome reads as a hard outline against the
        // 38pt title. Tone the border down only for the hero so other surfaces are unchanged.
        static void ApplyLibraryGameProfileHeroBadgeChrome(FrameworkElement badge)
        {
            if (badge is not Border border) return;
            border.BorderBrush = UiBrushHelper.FromHex("#3E5665");
            border.BorderThickness = new Thickness(1);
        }

        FrameworkElement BuildLibraryGameProfileStats(LibraryGameProfileMetrics metrics)
        {
            var root = new UniformGrid { Columns = 5 };
            var safe = metrics ?? new LibraryGameProfileMetrics();
            var firstCaptureText = safe.FirstCaptureDate > DateTime.MinValue
                ? FormatLibraryGameProfileDate(safe.FirstCaptureDate)
                : "\u2014";
            var latestCaptureText = safe.LatestCaptureDate > DateTime.MinValue
                ? FormatLibraryGameProfileDate(safe.LatestCaptureDate)
                : "\u2014";
            var latestCaptureRelative = FormatLibraryGameProfileRelative(safe.LatestCaptureDate);
            var cards = new[]
            {
                BuildLibraryGameProfileStatCard("Captures", safe.CaptureCount.ToString(CultureInfo.CurrentCulture)),
                BuildLibraryGameProfileStatCard("Videos", safe.VideoCount.ToString(CultureInfo.CurrentCulture)),
                BuildLibraryGameProfileStatCard("Sessions", safe.SessionCount.ToString(CultureInfo.CurrentCulture)),
                BuildLibraryGameProfileStatCard("First Capture", firstCaptureText),
                BuildLibraryGameProfileStatCard("Latest Capture", latestCaptureText, latestCaptureRelative)
            };
            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(0, 0, i == cards.Length - 1 ? 0 : 12, 0);
                root.Children.Add(cards[i]);
            }
            return root;
        }

        FrameworkElement BuildLibraryGameProfileStatCard(string label, string value, string subText = null)
        {
            // Phase A: drop the 1px border and rely on background contrast for separation
            // (PV-PLN-GPRO-001 step A.4) so the strip reads as a dashboard rather than a form.
            var card = new Border
            {
                Padding = new Thickness(16, 14, 16, 12),
                CornerRadius = new CornerRadius(16),
                Background = Brush("#111A21"),
                MinHeight = 86
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush("#86A0AE"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            // Length-based scaling lets short integer values stay punchy ("12") while
            // longer values like a formatted date ("Mar 11, 2026") still fit on the
            // narrower 5-column strip without wrapping awkwardly mid-token.
            var length = value == null ? 0 : value.Length;
            double valueFontSize;
            if (length > 22) valueFontSize = 16;
            else if (length > 14) valueFontSize = 18;
            else valueFontSize = 26;
            stack.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontSize = valueFontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            if (!string.IsNullOrWhiteSpace(subText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subText,
                    Foreground = Brush("#7E94A2"),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            card.Child = stack;
            return card;
        }

        // Human-readable "time ago" used as the Latest Capture sub-line. Returns empty
        // string for unset dates (the stat card hides the sub-line when empty).
        static string FormatLibraryGameProfileRelative(DateTime date)
        {
            if (date <= DateTime.MinValue) return string.Empty;
            var today = DateTime.Now.Date;
            var when = date.Date;
            if (when == today) return "Today";
            if (when == today.AddDays(-1)) return "Yesterday";
            var deltaDays = (int)Math.Round((today - when).TotalDays);
            if (deltaDays > 0 && deltaDays < 14) return deltaDays + " days ago";
            if (deltaDays >= 14 && deltaDays < 60) return (deltaDays / 7) + " weeks ago";
            if (deltaDays >= 60 && deltaDays < 365)
            {
                var months = (int)Math.Round(deltaDays / 30.44);
                if (months <= 1) months = 2;
                return months + " months ago";
            }
            if (deltaDays >= 365)
            {
                var years = (int)Math.Round(deltaDays / 365.25);
                if (years <= 1) return "1 year ago";
                return years + " years ago";
            }
            return string.Empty;
        }

        // Fire-and-forget: kicks off the achievements fetch on a worker thread and marshals
        // the result back to the UI. Tied to <paramref name="cancellation"/> so closing the
        // profile window before the network call returns short-circuits the dispatcher work.
        void BeginLoadLibraryGameProfileAchievements(Window owner, StackPanel host, LibraryBrowserFolderView view, CancellationToken cancellation)
        {
            host.Children.Add(BuildLibraryGameProfileSectionTitle("Achievements", "Collected achievements are grouped first; hover any badge for details."));
            var loading = new TextBlock
            {
                Text = "Loading achievements...",
                Foreground = Brush("#8FA4B0"),
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 13
            };
            host.Children.Add(loading);
            var folder = ResolveLibraryBrowserAchievementLookupFolder(view);
            var platform = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            var steamLookupFromNonSteamEntry = IsLibraryBrowserSteamAchievementLookupFromNonSteamEntry(view, folder);
            var userAgent = "PixelVault/" + AppVersion;
            _ = Task.Run(async () =>
            {
                GameAchievementsFetchService.FetchResult result;
                try
                {
                    result = await GameAchievementsFetchService.FetchAsync(
                        platform,
                        folder,
                        CurrentSteamWebApiKey(),
                        CurrentRetroAchievementsApiKey(),
                        CurrentSteamUserId64(),
                        CurrentRetroAchievementsUsername(),
                        userAgent,
                        cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    result = new GameAchievementsFetchService.FetchResult { ErrorMessage = ex.Message };
                }

                if (cancellation.IsCancellationRequested) return;

                await owner.Dispatcher.InvokeAsync(() =>
                {
                    if (cancellation.IsCancellationRequested || !owner.IsLoaded) return;
                    host.Children.Remove(loading);
                    if (result == null || result.IsError)
                    {
                        host.Children.Add(BuildLibraryGameProfileEmptyCard(result == null ? "Achievements are not available for this game yet." : result.ErrorMessage));
                        return;
                    }

                    var rows = result.Rows ?? new List<GameAchievementsFetchService.AchievementRow>();
                    var progressKnown = rows.Any(row => row != null && row.ProgressKnown);
                    var earned = rows.Where(row => row != null && row.ProgressKnown && row.Unlocked).ToList();
                    var hideSteamDefinitionsForEmptyNonSteamEntry =
                        steamLookupFromNonSteamEntry
                        && string.Equals(result.SourceLabel ?? string.Empty, "Steam", StringComparison.OrdinalIgnoreCase)
                        && progressKnown
                        && earned.Count == 0;
                    var displayRows = rows
                        .Where(row => row != null)
                        .OrderBy(row => progressKnown && row.Unlocked ? 0 : 1)
                        .ThenBy(row => row.SortKey)
                        .ThenBy(row => row.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var totalRows = rows.Count;
                    var earnedCount = earned.Count;
                    var percentText = string.Empty;
                    double? percent = null;
                    if (progressKnown && totalRows > 0 && !hideSteamDefinitionsForEmptyNonSteamEntry)
                    {
                        percent = (double)earnedCount / totalRows;
                        percentText = " \u00B7 " + ((int)Math.Round(percent.Value * 100)).ToString(CultureInfo.CurrentCulture) + "%";
                    }
                    var summary = hideSteamDefinitionsForEmptyNonSteamEntry
                        ? "No Steam achievements have been obtained for this non-Steam entry"
                        : (progressKnown
                            ? earnedCount + " of " + totalRows + " earned" + percentText
                            : "Progress unknown - showing achievement definitions");
                    host.Children.Add(new TextBlock
                    {
                        Text = (result.SourceLabel ?? "Achievements") + ": " + summary,
                        Foreground = Brush("#B8C9D4"),
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 8, 0, 6)
                    });
                    if (percent.HasValue)
                        host.Children.Add(BuildLibraryGameProfileAchievementProgressBar(percent.Value));
                    if (hideSteamDefinitionsForEmptyNonSteamEntry)
                        return;

                    if (displayRows.Count == 0)
                    {
                        host.Children.Add(BuildLibraryGameProfileEmptyCard(progressKnown ? "No achievements earned yet." : "No achievement definitions were returned."));
                        return;
                    }

                    var container = new Border
                    {
                        Padding = new Thickness(12),
                        CornerRadius = new CornerRadius(16),
                        Background = Brush("#101820")
                    };
                    var grid = new WrapPanel { Orientation = Orientation.Horizontal };
                    var palette = LibraryGameProfileAchievementPalette.Default;
                    foreach (var row in displayRows)
                        grid.Children.Add(BuildLibraryGameProfileAchievementCard(row, userAgent, progressKnown, palette));
                    container.Child = grid;
                    host.Children.Add(container);
                }, DispatcherPriority.Background);
            });
        }

        // Thin progress bar shown under the achievement summary line when the source
        // reports per-user unlock data (PV-PLN-GPRO-001 step B.2). Stays muted so it
        // does not compete with the badge grid.
        FrameworkElement BuildLibraryGameProfileAchievementProgressBar(double fraction)
        {
            var clamped = double.IsNaN(fraction) ? 0 : Math.Max(0.0, Math.Min(1.0, fraction));
            var track = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Brush("#1A2530"),
                Margin = new Thickness(0, 0, 0, 12),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var fill = new Border
            {
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brush(clamped >= 0.999 ? "#C7A245" : "#4A9FE8")
            };
            track.SizeChanged += delegate(object _, SizeChangedEventArgs e)
            {
                fill.Width = Math.Max(0, e.NewSize.Width * clamped);
            };
            track.Child = fill;
            return track;
        }

        // Cached, frozen brushes for the achievement grid. Profiles can render hundreds
        // of cards at once; the previous code allocated 5 fresh SolidColorBrush instances
        // per card via Brush(hex), which adds up under the dispatcher.
        sealed class LibraryGameProfileAchievementPalette
        {
            internal static readonly LibraryGameProfileAchievementPalette Default = new LibraryGameProfileAchievementPalette();

            internal SolidColorBrush UnlockedCardBackground { get; }
            internal SolidColorBrush LockedCardBackground { get; }
            internal SolidColorBrush UnlockedCardBorder { get; }
            internal SolidColorBrush LockedCardBorder { get; }
            internal SolidColorBrush IconBackground { get; }
            internal SolidColorBrush IconBorder { get; }

            LibraryGameProfileAchievementPalette()
            {
                UnlockedCardBackground = Freeze(UiBrushHelper.FromHex("#192316"));
                LockedCardBackground = Freeze(UiBrushHelper.FromHex("#111A21"));
                UnlockedCardBorder = Freeze(UiBrushHelper.FromHex("#C7A245"));
                LockedCardBorder = Freeze(UiBrushHelper.FromHex("#31414C"));
                IconBackground = Freeze(UiBrushHelper.FromHex("#18242B"));
                IconBorder = Freeze(UiBrushHelper.FromHex("#30404C"));
            }

            static SolidColorBrush Freeze(SolidColorBrush brush)
            {
                if (brush != null && brush.CanFreeze && !brush.IsFrozen) brush.Freeze();
                return brush;
            }
        }

        FrameworkElement BuildLibraryGameProfileAchievementCard(GameAchievementsFetchService.AchievementRow row, string userAgent, bool progressKnown, LibraryGameProfileAchievementPalette palette)
        {
            var unlocked = row != null && row.ProgressKnown && row.Unlocked;
            var muted = progressKnown && !unlocked;
            var card = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(10),
                Background = unlocked ? palette.UnlockedCardBackground : palette.LockedCardBackground,
                BorderBrush = unlocked ? palette.UnlockedCardBorder : palette.LockedCardBorder,
                BorderThickness = new Thickness(unlocked ? 1.25 : 1),
                Opacity = muted ? 0.46 : 1,
                SnapsToDevicePixels = true
            };
            var iconHost = new Border
            {
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true,
                Background = palette.IconBackground,
                BorderBrush = palette.IconBorder,
                BorderThickness = new Thickness(1)
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            iconHost.Child = img;
            AchievementsInfoWindow.StartAchievementBadgeDownload(img, row, userAgent);
            card.Child = iconHost;
            card.ToolTip = BuildLibraryGameProfileAchievementToolTip(row, progressKnown);
            ToolTipService.SetShowDuration(card, 90000);
            return card;
        }

        FrameworkElement BuildLibraryGameProfileAchievementToolTip(GameAchievementsFetchService.AchievementRow row, bool progressKnown)
        {
            var stack = new StackPanel { MaxWidth = 320 };
            stack.Children.Add(new TextBlock
            {
                Text = row == null ? "Achievement" : row.Title ?? "Achievement",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(row == null ? string.Empty : row.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = row.Description,
                    Foreground = Brush("#B8C9D4"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }
            var earnedMeta = row == null ? string.Empty : AchievementsInfoWindow.FormatAchievementEarnedMeta(row.UnlockUtcTicks);
            var status = progressKnown
                ? ((row != null && row.Unlocked) ? (string.IsNullOrWhiteSpace(earnedMeta) ? "Collected" : earnedMeta) : "Not collected")
                : "Progress not tracked";
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(row == null ? string.Empty : row.Meta)
                    ? status
                    : row.Meta + " | " + status,
                Foreground = row != null && row.Unlocked ? Brush("#E7C66B") : Brush("#8FA4B0"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return stack;
        }

        FrameworkElement BuildLibraryGameProfileCaptureFilmstrip(Window profileWindow, LibraryBrowserFolderView view, IReadOnlyList<string> orderedFiles)
        {
            var section = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var files = (orderedFiles ?? Array.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .ToList();
            if (files.Count == 0)
            {
                section.Children.Add(BuildLibraryGameProfileEmptyCard("No captures are currently linked to this profile yet."));
                return section;
            }

            var shown = files.Take(14).ToList();
            var tempWs = new LibraryBrowserWorkingSet();
            tempWs.DetailFilesDisplayOrder.AddRange(files.Where(IsImage));
            var rail = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var file in shown)
                rail.Children.Add(BuildLibraryGameProfileCaptureTile(profileWindow, tempWs, file, 132, 82));
            rail.Children.Add(BuildLibraryGameProfileOpenPhotoViewTile(profileWindow, view, files.Count));

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 0, 6),
                Content = rail
            };
            section.Children.Add(scroller);
            return section;
        }

        FrameworkElement BuildLibraryGameProfileCaptureTile(Window profileWindow, LibraryBrowserWorkingSet tempWs, string file, double width, double height)
        {
            var tile = new Border
            {
                Width = width,
                Height = height,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Background = Brush("#111A21"),
                BorderBrush = Brush("#263640"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var grid = new Grid();
            var isVideo = IsVideo(file);
            // Routing through CreateAsyncImageTile for both images and videos lets the
            // thumbnail pipeline kick off ffmpeg poster generation for clips so they
            // render with a real frame thumbnail (with the "CLIP" text as fallback while
            // the poster is being created or if FFmpeg is not configured).
            if (IsImage(file) || isVideo)
            {
                grid.Children.Add(CreateAsyncImageTile(
                    file,
                    CalculateLibraryDetailTileDecodeWidth((int)width, ResolveLibraryDpiScale()),
                    width,
                    height,
                    Stretch.UniformToFill,
                    isVideo ? "CLIP" : Path.GetFileName(file),
                    Brushes.White,
                    new Thickness(0),
                    new Thickness(0),
                    Brushes.Transparent,
                    new CornerRadius(0),
                    Brushes.Transparent,
                    new Thickness(0)));
                if (isVideo) grid.Children.Add(BuildLibraryGameProfileVideoPlayIndicator());
                tile.ToolTip = Path.GetFileName(file);
                ToolTipService.SetShowDuration(tile, 90000);
                tile.MouseLeftButtonDown += delegate
                {
                    if (isVideo)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            Log("Open video failed: " + ex.Message);
                        }
                    }
                    else
                    {
                        OpenLibraryCaptureViewer(profileWindow, tempWs, file);
                    }
                };
            }
            tile.Child = grid;
            return tile;
        }

        static FrameworkElement BuildLibraryGameProfileVideoPlayIndicator()
        {
            var badge = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6),
                Child = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection { new Point(0, 0), new Point(10, 6), new Point(0, 12) },
                    Fill = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0)
                }
            };
            return badge;
        }

        FrameworkElement BuildLibraryGameProfileOpenPhotoViewTile(Window profileWindow, LibraryBrowserFolderView view, int captureCount)
        {
            var tile = new Border
            {
                Width = 132,
                Height = 82,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                Background = Brush("#15242D"),
                BorderBrush = Brush("#4A9FE8"),
                BorderThickness = new Thickness(1.25),
                Cursor = Cursors.Hand
            };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8)
            };
            stack.Children.Add(new TextBlock
            {
                Text = "Open Photo View",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = captureCount.ToString(CultureInfo.CurrentCulture) + (captureCount == 1 ? " capture" : " captures"),
                Foreground = Brush("#9DC7E8"),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            tile.Child = stack;
            tile.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                e.Handled = true;
                OpenLibraryGameProfilePhotoWorkspace(profileWindow, view);
            };
            return tile;
        }

        void OpenLibraryGameProfilePhotoWorkspace(Window profileWindow, LibraryBrowserFolderView view)
        {
            if (view == null) return;
            // ShowLibraryBrowser(true) sets _libraryBrowserLiveWorkingSet synchronously
            // inside LibraryBrowserShowOrchestration.Run when reuseMainWindow=true
            // (RegisterLibraryBrowserLiveWorkingSet on this thread). The defensive null
            // check below catches the edge case where the orchestration throws before
            // the registration line runs - which is logged to LogException by the host
            // - so we surface a toast instead of silently dropping the click.
            if (_libraryBrowserLiveWorkingSet == null || _libraryBrowserLiveWorkingSet.Panes == null)
                ShowLibraryBrowser(true);

            var ws = _libraryBrowserLiveWorkingSet;
            if (ws == null || ws.Panes == null || ws.OpenPhotoWorkspaceForFolder == null)
            {
                Log("Game profile: Photo View unavailable - library browser working set not initialized.");
                TryLibraryToast("Photo View is unavailable right now. Try reopening the Library window.", MessageBoxImage.Warning);
                return;
            }
            if (IsLibraryBrowserTimelineMode())
            {
                libraryGroupingMode = "all";
                SaveSettings();
                LibraryBrowserSyncWorkspaceModeWithGrouping(ws, libraryGroupingMode);
                ws.RerenderFolderList?.Invoke();
            }

            var target = ws.ViewFolders.FirstOrDefault(candidate => SameLibraryBrowserSelection(candidate, view)) ?? view;
            ws.OpenPhotoWorkspaceForFolder(target);
            (profileWindow == null ? this : profileWindow.Owner ?? this).Activate();
        }

        FrameworkElement BuildLibraryGameProfileSectionTitle(string title, string subtitle)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Foreground = Brush("#8FA4B0"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            return stack;
        }

        FrameworkElement BuildLibraryGameProfileEmptyCard(string message)
        {
            return new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(14),
                Background = Brush("#101820"),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(message) ? "Nothing to show yet." : message,
                    Foreground = Brush("#9FB1BC"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        // Game Notes card (PV-PLN-GPRO-001 step B.1). Renders folder.CollectionNotes
        // when present, otherwise an empty-state prompt; the Edit notes button opens
        // a small modal that writes through the same UpsertSavedGameIndexRow path the
        // Quick Edit Drawer uses, so the profile and the main browser stay in lockstep.
        FrameworkElement BuildLibraryGameProfileNotesCard(Window profileWindow, LibraryBrowserFolderView view, LibraryFolderInfo folder)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 18, 0, 0),
                Padding = new Thickness(18, 14, 18, 16),
                CornerRadius = new CornerRadius(16),
                Background = Brush("#111A21")
            };
            var stack = new StackPanel();
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headerLabel = new TextBlock
            {
                Text = "Game Notes",
                Foreground = Brush("#86A0AE"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(headerLabel);
            var editButton = Btn("Edit notes", null, "#1F3340", Brushes.White);
            editButton.Height = 28;
            editButton.MinWidth = 96;
            editButton.FontSize = 11.5;
            editButton.Padding = new Thickness(12, 0, 12, 0);
            ApplyLibraryPillChrome(editButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            Grid.SetColumn(editButton, 1);
            headerRow.Children.Add(editButton);
            stack.Children.Add(headerRow);

            var notesText = new TextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            stack.Children.Add(notesText);
            ApplyLibraryGameProfileNotesText(notesText, folder == null ? string.Empty : folder.CollectionNotes);
            card.Child = stack;

            editButton.Click += delegate
            {
                ShowLibraryGameProfileNotesEditor(
                    profileWindow,
                    view,
                    folder,
                    delegate(string updated)
                    {
                        ApplyLibraryGameProfileNotesText(notesText, updated);
                    });
            };
            return card;
        }

        static void ApplyLibraryGameProfileNotesText(TextBlock target, string notes)
        {
            if (target == null) return;
            var trimmed = (notes ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                target.Text = "No notes yet \u2014 add one to remember mods, settings, or run rules.";
                target.Foreground = UiBrushHelper.FromHex("#7E94A2");
                target.FontStyle = FontStyles.Italic;
            }
            else
            {
                target.Text = trimmed;
                target.Foreground = UiBrushHelper.FromHex("#D2DFE7");
                target.FontStyle = FontStyles.Normal;
            }
        }

        void ShowLibraryGameProfileNotesEditor(Window profileWindow, LibraryBrowserFolderView view, LibraryFolderInfo folder, Action<string> onSaved)
        {
            var dlg = new Window
            {
                Title = "Edit Game Notes",
                Width = 460,
                Height = 340,
                MinWidth = 380,
                MinHeight = 260,
                Owner = profileWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1820"),
                ShowInTaskbar = false
            };
            AutomationProperties.SetName(dlg, "Edit Game Notes");
            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new TextBlock
            {
                Text = "Notes for " + (folder == null || string.IsNullOrWhiteSpace(folder.Name) ? "this game" : folder.Name.Trim()),
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            var notesBox = new TextBox
            {
                Text = folder == null ? string.Empty : (folder.CollectionNotes ?? string.Empty),
                AcceptsReturn = true,
                AcceptsTab = false,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brush("#162028"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#2C3D4A"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 13
            };
            Grid.SetRow(notesBox, 1);
            root.Children.Add(notesBox);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancelButton = Btn("Cancel", null, "#1F2832", Brushes.White);
            cancelButton.Height = 32;
            cancelButton.MinWidth = 88;
            cancelButton.FontSize = 12;
            cancelButton.Margin = new Thickness(0, 0, 8, 0);
            ApplyLibraryPillChrome(cancelButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            cancelButton.Click += delegate { dlg.Close(); };
            buttonRow.Children.Add(cancelButton);

            var saveButton = Btn("Save", null, "#2A4A5E", Brushes.White);
            saveButton.Height = 32;
            saveButton.MinWidth = 96;
            saveButton.FontSize = 12;
            ApplyLibraryPillChrome(saveButton, "#2A4A5E", "#3A5F78", "#355A72", "#243A4A", "#D7E2EA");
            buttonRow.Children.Add(saveButton);
            Grid.SetRow(buttonRow, 2);
            root.Children.Add(buttonRow);

            saveButton.Click += delegate
            {
                var newText = (notesBox.Text ?? string.Empty).Trim();
                if (TryPersistLibraryGameProfileNotes(view, folder, newText))
                {
                    onSaved?.Invoke(newText);
                    dlg.Close();
                }
            };
            dlg.PreviewKeyDown += delegate(object _, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    dlg.Close();
                }
            };
            dlg.Loaded += delegate
            {
                notesBox.Focus();
                notesBox.CaretIndex = notesBox.Text == null ? 0 : notesBox.Text.Length;
            };
            dlg.Content = root;
            dlg.ShowDialog();
        }

        // Mirrors LibraryBrowserApplyQuickEditDrawerFields (notes branch only): writes
        // through the saved game-index rows, refreshes the folder cache, then propagates
        // to the live view + primary/source folders so the rest of the UI sees the change.
        bool TryPersistLibraryGameProfileNotes(LibraryBrowserFolderView view, LibraryFolderInfo folder, string newNotes)
        {
            try
            {
                if (folder == null || librarySession == null || !librarySession.HasLibraryRoot)
                {
                    TryLibraryToast("Notes are unavailable until the library is loaded.", MessageBoxImage.Warning);
                    return false;
                }
                if (folder.PendingGameAssignment)
                {
                    TryLibraryToast("Assign a game title first (pending resolution).", MessageBoxImage.Information);
                    return false;
                }
                var root = librarySession.LibraryRoot;
                if (string.IsNullOrWhiteSpace(root)) return false;

                var display = view == null ? folder : (BuildLibraryBrowserDisplayFolder(view) ?? folder);
                display.CollectionNotes = newNotes ?? string.Empty;
                UpsertSavedGameIndexRow(root, display);
                librarySession.RefreshFolderCacheAfterGameIndexChange();

                folder.CollectionNotes = display.CollectionNotes;
                if (view != null)
                {
                    view.CollectionNotes = display.CollectionNotes;
                    if (view.PrimaryFolder != null) view.PrimaryFolder.CollectionNotes = display.CollectionNotes;
                    if (view.SourceFolders != null)
                    {
                        foreach (var sf in view.SourceFolders)
                        {
                            if (sf != null) sf.CollectionNotes = display.CollectionNotes;
                        }
                    }
                    PopulateLibraryBrowserFolderViewSearchBlob(view);
                }

                TryLibraryToast("Notes saved.", MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                LogException("Game profile | Save notes", ex);
                TryLibraryToast("Could not save notes: " + ex.Message, MessageBoxImage.Warning);
                return false;
            }
        }

        DateTime ResolveLibraryProfileCaptureDate(string file, IReadOnlyDictionary<string, LibraryMetadataIndexEntry> metadataIndex)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return DateTime.MinValue;
            var dict = metadataIndex as Dictionary<string, LibraryMetadataIndexEntry>;
            if (librarySession != null && librarySession.HasLibraryRoot)
                return librarySession.ResolveIndexedLibraryDate(file, dict);
            return GetLibraryDate(file);
        }

        static string FormatLibraryGameProfileDate(DateTime date)
        {
            if (date <= DateTime.MinValue) return string.Empty;
            return date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        }
    }
}
