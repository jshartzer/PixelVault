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
                // Sized for the Phase B/C dashboard layout (hero + action cluster +
                // summary line + 5–7 stat cards + filmstrip + notes card + achievements).
                // 1040 still fits comfortably on 1080p displays after the OS chrome /
                // taskbar (typical usable height ~1032-1040px on a 1920x1080 monitor).
                Height = 1040,
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

            // Snapshot the file list + metadata index ONCE per open (PV-POL-GPRO-DATA-001).
            // The hero summary, stat strip, sessions section and Recent Captures all share
            // these inputs. Building the LibraryGameProfileSessionEntry list eagerly means
            // the session helper (Phase D.1) and the Sessions section (Phase D.2) consume a
            // single resolved-date snapshot - changing the threshold only re-buckets, never
            // re-reads metadata.
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
            var sessionEntries = orderedFiles
                .Select(row => new LibraryGameProfileSessionEntry(row.File, row.Date, IsVideo(row.File)))
                .ToList();
            var metrics = ComputeLibraryGameProfileMetrics(orderedFilePaths, metadataIndex, librarySessionThresholdMinutes);

            var achievementLookupFolder = ResolveLibraryBrowserAchievementLookupFolder(view);
            var achievementPlatformNorm = NormalizeConsoleLabel(
                achievementLookupFolder == null ? string.Empty : achievementLookupFolder.PlatformLabel);
            var steamStats = new LibraryGameProfileSteamStats
            {
                ShowSteamCards = GameAchievementsFetchService.ShouldShowSteamExtrasForProfile(
                    achievementPlatformNorm,
                    achievementLookupFolder ?? folder)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // The Edit Game pill in the action toolbar mutates folder.Name /
            // .CollectionNotes / IDs etc. and then asks us to refresh the profile in
            // place (PV-PLN-GPRO-001 step C.3 + L3 follow-up). We rebuild the hero
            // and re-pull the Game Notes card text from the now-mutated folder so
            // renamed / re-IDed / re-noted games update without reopening the window.
            //
            // Phase D.3 adds the threshold picker, which mutates
            // librarySessionThresholdMinutes. The Sessions stat card and the new
            // Sessions section both depend on the picker's current value, so we
            // slot them into ContentControl hosts and re-render only those slots
            // (plus the hero summary line) on threshold change instead of doing a
            // full body rebuild.
            Action refreshHero = null;
            Action refreshNotesCard = null;
            Action refreshStatsSection = null;
            Action refreshSessionsSection = null;
            Action<int> applyThreshold = null;
            refreshHero = delegate
            {
                if (root.Children.Count == 0) return;
                FrameworkElement existingHero = null;
                foreach (UIElement child in root.Children)
                {
                    if (child is FrameworkElement fe && Grid.GetRow(fe) == 0) { existingHero = fe; break; }
                }
                if (existingHero != null)
                {
                    var freshHero = BuildLibraryGameProfileHero(win, view, folder, metrics, refreshHero);
                    var idx = root.Children.IndexOf(existingHero);
                    root.Children.Remove(existingHero);
                    if (idx >= 0) root.Children.Insert(idx, freshHero); else root.Children.Add(freshHero);
                }
                if (refreshNotesCard != null) refreshNotesCard();
                win.Title = "PixelVault Game Profile - " + (string.IsNullOrWhiteSpace(folder.Name) ? "Game Profile" : folder.Name);
            };
            var heroElement = BuildLibraryGameProfileHero(win, view, folder, metrics, refreshHero);
            heroElement.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(heroElement);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 18, 22, 24)
            };
            Grid.SetRow(scroll, 1);
            var body = new StackPanel();
            scroll.Content = body;
            var statsHost = new ContentControl { Content = BuildLibraryGameProfileStats(metrics, steamStats) };
            body.Children.Add(statsHost);
            refreshStatsSection = delegate
            {
                statsHost.Content = BuildLibraryGameProfileStats(metrics, steamStats);
            };
            body.Children.Add(BuildLibraryGameProfileNotesCard(win, view, folder, out refreshNotesCard));
            body.Children.Add(BuildLibraryGameProfileCaptureFilmstrip(win, view, orderedFilePaths));
            var achievementHost = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
            body.Children.Add(achievementHost);
            BeginLoadLibraryGameProfileAchievements(win, achievementHost, view, steamStats, refreshStatsSection, lifetimeCts.Token);
            // Sessions live at the bottom of the body so achievements (the highest-
            // signal "did I beat this thing" surface) sits directly under the
            // recent-captures filmstrip. The Sessions ContentControl is still
            // refreshed in place when the threshold changes (refreshSessionsSection).
            var sessionsHost = new ContentControl
            {
                Content = BuildLibraryGameProfileSessionsSection(win, view, folder, sessionEntries, librarySessionThresholdMinutes, mins => applyThreshold(mins))
            };
            body.Children.Add(sessionsHost);
            root.Children.Add(scroll);

            refreshSessionsSection = delegate
            {
                sessionsHost.Content = BuildLibraryGameProfileSessionsSection(win, view, folder, sessionEntries, librarySessionThresholdMinutes, mins => applyThreshold(mins));
            };
            applyThreshold = delegate(int requestedMinutes)
            {
                var normalized = SettingsService.NormalizeLibrarySessionThresholdMinutes(requestedMinutes);
                if (librarySessionThresholdMinutes != normalized)
                {
                    librarySessionThresholdMinutes = normalized;
                    SaveSettings();
                }
                metrics = ComputeLibraryGameProfileMetrics(orderedFilePaths, metadataIndex, librarySessionThresholdMinutes);
                refreshStatsSection();
                refreshSessionsSection();
                refreshHero();
                // PV-POL-GPRO-SESSION-001: writing the same setting key the main
                // browser uses; if the browser is open in Sessions mode it needs
                // to re-render so its own grouping respects the new threshold.
                try { _libraryBrowserLiveWorkingSet?.RerenderFolderList?.Invoke(); }
                catch (Exception ex) { LogException("LibraryGameProfile.applyThreshold rerenderFolderList", ex); }
            };

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

        /// <summary>Steam dashboard extras (playtime, rare unlock count); mutated when achievements fetch completes.</summary>
        sealed class LibraryGameProfileSteamStats
        {
            public bool ShowSteamCards;
            public bool Hydrated;
            public int? SteamPlaytimeMinutes;
            public int? SteamRareUnlockedCount;
            public bool SteamGlobalRarityAvailable;
        }

        LibraryGameProfileMetrics ComputeLibraryGameProfileMetrics(
            IReadOnlyList<string> orderedFiles,
            IReadOnlyDictionary<string, LibraryMetadataIndexEntry> metadataIndex,
            int sessionThresholdMinutes)
        {
            var safeFiles = orderedFiles ?? Array.Empty<string>();
            var entries = new List<LibraryGameProfileSessionEntry>(safeFiles.Count);
            var videoCount = 0;
            foreach (var file in safeFiles)
            {
                var isVideo = IsVideo(file);
                if (isVideo) videoCount++;
                var captured = ResolveLibraryProfileCaptureDate(file, metadataIndex);
                if (captured > DateTime.MinValue)
                    entries.Add(new LibraryGameProfileSessionEntry(file, captured, isVideo));
            }
            // PV-PLN-GPRO-001 Phase D.1: route session counting through the pure
            // LibraryGameProfileSessionMath helper so the Phase A stat card and
            // the Phase D Sessions section can never disagree about what "1
            // session" means at a given threshold (PV-POL-GPRO-SESSION-001).
            var sessionCount = LibraryGameProfileSessionMath.CountSessions(entries, sessionThresholdMinutes);
            DateTime first = DateTime.MinValue;
            DateTime latest = DateTime.MinValue;
            if (entries.Count > 0)
            {
                first = entries[0].CapturedUtc;
                latest = first;
                for (var i = 1; i < entries.Count; i++)
                {
                    var captured = entries[i].CapturedUtc;
                    if (captured < first) first = captured;
                    if (captured > latest) latest = captured;
                }
            }
            return new LibraryGameProfileMetrics
            {
                CaptureCount = safeFiles.Count,
                VideoCount = videoCount,
                SessionCount = sessionCount,
                FirstCaptureDate = first,
                LatestCaptureDate = latest
            };
        }

        FrameworkElement BuildLibraryGameProfileHero(Window window, LibraryBrowserFolderView view, LibraryFolderInfo folder, LibraryGameProfileMetrics metrics, Action refreshHero)
        {
            const double profileCoverHeight = 276d;
            const double contentMarginV = 12d;
            const double heroBottomChromeGap = 8d;
            const double heroBottomChromeHeight = 42d;
            // Fixed header height: main row matches cover; second row holds Edit/Open/Art
            // (left) vs Favorite/Showcase/100% (right). Window resize cannot grow this chrome.
            var heroFixedHeight = contentMarginV + profileCoverHeight + heroBottomChromeGap + heroBottomChromeHeight + contentMarginV;
            var hero = new Grid
            {
                Height = heroFixedHeight,
                MinHeight = heroFixedHeight,
                MaxHeight = heroFixedHeight,
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

            var content = new Grid { Margin = new Thickness(26, contentMarginV, 26, contentMarginV), ClipToBounds = true };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(profileCoverHeight) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            const double profileCoverWidth = 184d;
            var profileCoverCorner = new CornerRadius(18);
            var coverArt = CreateAsyncImageTile(
                GetLibraryArtPathForDisplayOnly(folder),
                540,
                profileCoverWidth,
                profileCoverHeight,
                Stretch.UniformToFill,
                folder.Name ?? string.Empty,
                Brushes.White,
                new Thickness(0),
                new Thickness(0),
                Brush("#151F27"),
                profileCoverCorner,
                Brush("#3E5665"),
                new Thickness(1));
            // WPF's Border doesn't auto-clip its child to the CornerRadius - the
            // inner Image (Stretch=UniformToFill) was painting past the rounded
            // corners and looked like a square photo punched into a rounded frame.
            // Apply an explicit RectangleGeometry clip matching the cover's outer
            // rounded rect so the image, foil, and frame all respect the rounded
            // edge. Frozen for cheap reuse across hero rebuilds.
            var coverClipGeometry = new RectangleGeometry(
                new Rect(0, 0, profileCoverWidth, profileCoverHeight),
                profileCoverCorner.TopLeft,
                profileCoverCorner.TopLeft);
            if (coverClipGeometry.CanFreeze) coverClipGeometry.Freeze();
            coverArt.Clip = coverClipGeometry;
            // PV-PLN-GPRO-001 step C.2 follow-up: when the 100% complete toggle is on,
            // mirror the library tile's holofoil + gold-frame overlays on top of the
            // profile cover so the toggle has visible feedback right next to the
            // button. We reuse the BuildLibraryTileCompletionFoilOverlay /
            // BuildLibraryTileCompletionFrameOverlay helpers from the folder tile
            // path (PV-POL-GPRO-REUSE-001) instead of building a parallel surface.
            // Animation is hover-driven here (the profile hero doesn't scroll like the
            // tile rail does), with a centered idle state on first layout.
            FrameworkElement coverHost = coverArt;
            if (view != null && view.IsCompleted100Percent)
            {
                var foilWrap = new Grid
                {
                    Width = profileCoverWidth,
                    Height = profileCoverHeight,
                    Clip = coverClipGeometry
                };
                foilWrap.Children.Add(coverArt);
                var foilVisual = BuildLibraryTileCompletionFoilOverlay(profileCoverWidth, profileCoverHeight, profileCoverCorner.TopLeft);
                foilWrap.Children.Add(foilVisual.Root);
                foilWrap.Children.Add(BuildLibraryTileCompletionFrameOverlay(profileCoverWidth, profileCoverHeight, profileCoverCorner));
                foilWrap.Loaded += delegate { foilVisual.Update(0.5, 0.5, false); };
                foilWrap.MouseMove += delegate(object _, MouseEventArgs e)
                {
                    if (foilWrap.ActualWidth <= 0 || foilWrap.ActualHeight <= 0) return;
                    var pt = e.GetPosition(foilWrap);
                    foilVisual.Update(
                        Math.Max(0, Math.Min(1, pt.X / foilWrap.ActualWidth)),
                        Math.Max(0, Math.Min(1, pt.Y / foilWrap.ActualHeight)),
                        true);
                };
                foilWrap.MouseLeave += delegate { foilVisual.Update(0.5, 0.5, true); };
                coverHost = foilWrap;
            }
            coverHost.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 7, Direction = 270, Color = Color.FromArgb(120, 0, 0, 0), Opacity = 0.75 };
            Grid.SetRow(coverHost, 0);
            Grid.SetColumn(coverHost, 0);
            content.Children.Add(coverHost);

            // Copy column top-aligned inside fixed-height hero so long titles ellipsize
            // instead of growing the header when the window is resized wider.
            var copy = new StackPanel { Margin = new Thickness(24, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            copy.Children.Add(new TextBlock
            {
                Text = folder.Name ?? "Game Profile",
                Foreground = Brushes.White,
                FontSize = 38,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = folder.Name ?? "Game Profile"
            });
            var summaryLine = BuildLibraryGameProfileHeroSummaryLine(metrics);
            if (summaryLine is TextBlock sumTb)
            {
                sumTb.TextWrapping = TextWrapping.NoWrap;
                sumTb.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            if (summaryLine != null) copy.Children.Add(summaryLine);
            var badges = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var label in ResolveLibraryGameProfilePlatformLabels(view, folder))
            {
                var badge = BuildLibraryBrowserDetailTitlePlatformBadge(label);
                if (badge == null) continue;
                ApplyLibraryGameProfileHeroBadgeChrome(badge);
                if (badge is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
                badges.Children.Add(badge);
            }
            if (badges.Children.Count > 0) copy.Children.Add(badges);
            // Row 0 only: identity text + platform badges + external-ID pills. Edit /
            // Open / Change Art live on row 1 (left); Favorite / Showcase / 100% on row 1 (right).
            var idPills = BuildLibraryGameProfileIdPills(folder);
            if (idPills is FrameworkElement idFe)
            {
                idFe.HorizontalAlignment = HorizontalAlignment.Left;
                idFe.Margin = new Thickness(0, 6, 0, 0);
                copy.Children.Add(idFe);
            }
            Grid.SetRow(copy, 0);
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);

            var logoElement = BuildLibraryGameProfileHeroLogo(window, folder);
            if (logoElement != null)
            {
                Grid.SetRow(logoElement, 0);
                Grid.SetColumn(logoElement, 2);
                logoElement.VerticalAlignment = VerticalAlignment.Center;
                content.Children.Add(logoElement);
            }

            var bottomChrome = new Grid { Margin = new Thickness(0, heroBottomChromeGap, 0, 0) };
            bottomChrome.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomChrome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var editCircles = BuildLibraryGameProfileHeroEditActionCluster(window, view, folder, refreshHero);
            if (editCircles != null)
            {
                Grid.SetColumn(editCircles, 0);
                if (editCircles is FrameworkElement editFe) editFe.HorizontalAlignment = HorizontalAlignment.Left;
                bottomChrome.Children.Add(editCircles);
            }

            var stateStrip = BuildLibraryGameProfileHeroStateToggleCluster(window, view, folder, refreshHero);
            if (stateStrip != null)
            {
                Grid.SetColumn(stateStrip, 1);
                if (stateStrip is FrameworkElement stateFe) stateFe.HorizontalAlignment = HorizontalAlignment.Right;
                bottomChrome.Children.Add(stateStrip);
            }

            Grid.SetRow(bottomChrome, 1);
            Grid.SetColumnSpan(bottomChrome, 3);
            content.Children.Add(bottomChrome);

            hero.Children.Add(content);
            return hero;
        }

        // Right-column logo used by BuildLibraryGameProfileHero. Returns null when no
        // logo asset is available so column 2 collapses to 0 width and the copy
        // column gets the room. Logos render up to 320x200, vertical-centered, and
        // fade in once the bitmap has decoded so we don't flash a placeholder.
        FrameworkElement BuildLibraryGameProfileHeroLogo(Window window, LibraryFolderInfo folder)
        {
            var logoPath = GetLibraryHeroLogoPathForDisplayOnly(folder);
            if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath)) return null;
            var logo = new Image
            {
                MaxWidth = 320,
                MaxHeight = 200,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            QueueImageLoad(logo, logoPath, 720, loaded =>
            {
                logo.Source = loaded;
                logo.Visibility = Visibility.Visible;
            }, true);
            return logo;
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

        // Favorite / Showcase / 100% round toggles — anchored to the bottom-right of
        // the profile hero on their own row (separate from Edit / Open / Change Art).
        FrameworkElement BuildLibraryGameProfileHeroStateToggleCluster(Window profileWindow, LibraryBrowserFolderView view, LibraryFolderInfo folder, Action refreshHero)
        {
            if (view == null || folder == null) return null;
            var cluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Border favBtn = BuildLibraryGameProfileHeroToggleButton(
                "\uEB52", "#FF6B81", "#3A1C25",
                view.IsFavorite, "Remove from favorites", "Mark as favorite");
            favBtn.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (SetLibraryBrowserFavoriteState(view, !view.IsFavorite))
                {
                    ApplyLibraryGameProfileHeroToggleState(favBtn, "\uEB52", "#FF6B81", "#3A1C25",
                        view.IsFavorite, "Remove from favorites", "Mark as favorite");
                    TryLibraryToast(view.IsFavorite ? "Added to favorites." : "Removed from favorites.", MessageBoxImage.Information);
                }
            };
            cluster.Children.Add(favBtn);

            Border showcaseBtn = BuildLibraryGameProfileHeroToggleButton(
                "\uE735", "#F4C657", "#332608",
                view.IsShowcase, "Remove from showcase", "Add to showcase");
            showcaseBtn.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (SetLibraryBrowserShowcaseState(view, !view.IsShowcase))
                {
                    ApplyLibraryGameProfileHeroToggleState(showcaseBtn, "\uE735", "#F4C657", "#332608",
                        view.IsShowcase, "Remove from showcase", "Add to showcase");
                    TryLibraryToast(view.IsShowcase ? "Added to showcase." : "Removed from showcase.", MessageBoxImage.Information);
                }
            };
            cluster.Children.Add(showcaseBtn);

            Border completeBtn = BuildLibraryGameProfileHeroToggleButton(
                "\uED15", "#5DD68B", "#0D2E1A",
                view.IsCompleted100Percent, "Cleared 100% complete", "Mark 100% complete");
            completeBtn.Margin = new Thickness(0);
            completeBtn.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (!SetLibraryBrowserCompletionState(view, !view.IsCompleted100Percent)) return;
                TryLibraryToast(view.IsCompleted100Percent ? "Marked 100% complete." : "Cleared 100% complete.", MessageBoxImage.Information);
                if (refreshHero != null) refreshHero();
                try { _libraryBrowserLiveWorkingSet?.RerenderFolderList?.Invoke(); }
                catch (Exception ex) { LogException("LibraryGameProfile.completeBtn rerenderFolderList", ex); }
            };
            cluster.Children.Add(completeBtn);

            return cluster;
        }

        // Edit Game / Open Folder / Change Art circles — bottom-left of hero chrome.
        FrameworkElement BuildLibraryGameProfileHeroEditActionCluster(Window profileWindow, LibraryBrowserFolderView view, LibraryFolderInfo folder, Action refreshHero)
        {
            if (view == null || folder == null) return null;
            var cluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var sources = view.SourceFolders == null
                ? new List<LibraryFolderInfo>()
                : view.SourceFolders.Where(f => f != null).ToList();
            var multiConsole = view.IsMergedAcrossPlatforms && sources.Count > 1;
            var editTooltip = multiConsole
                ? "Edit Game - pick a platform (" + sources.Count + ")"
                : "Edit Game - name, notes, and external IDs";
            var editCircle = BuildLibraryGameProfileHeroActionCircle("\uE70F", editTooltip);
            if (multiConsole)
            {
                var editMenu = new ContextMenu { Placement = PlacementMode.Bottom };
                foreach (var source in sources)
                {
                    var platform = string.IsNullOrWhiteSpace(source.PlatformLabel)
                        ? (string.IsNullOrWhiteSpace(source.Name) ? "(unlabeled)" : source.Name.Trim())
                        : NormalizeConsoleLabel(source.PlatformLabel);
                    var sub = !string.IsNullOrWhiteSpace(source.Name)
                        && !string.Equals((source.Name ?? string.Empty).Trim(), platform, StringComparison.OrdinalIgnoreCase)
                            ? source.Name.Trim()
                            : null;
                    var item = new MenuItem { Header = sub == null ? platform : platform + " - " + sub };
                    item.Click += delegate { OpenLibraryFolderIdEditor(source, refreshHero, profileWindow); };
                    editMenu.Items.Add(item);
                }
                editCircle.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    editMenu.PlacementTarget = editCircle;
                    editMenu.IsOpen = true;
                };
            }
            else
            {
                editCircle.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    OpenLibraryFolderIdEditor(folder, refreshHero, profileWindow);
                };
            }
            cluster.Children.Add(editCircle);

            var folderPaths = GetLibraryBrowserSourceFolderPaths(view) ?? new List<string>();
            var openTooltip = folderPaths.Count == 0
                ? "Open Folder - no source folders to open"
                : (folderPaths.Count == 1
                    ? "Open Folder - " + Path.GetFileName(folderPaths[0])
                    : "Open Folders - " + folderPaths.Count + " source folders in File Explorer");
            var openCircle = BuildLibraryGameProfileHeroActionCircle("\uE8DA", openTooltip);
            if (folderPaths.Count == 0)
            {
                openCircle.Opacity = 0.4;
                openCircle.Cursor = null;
            }
            else
            {
                openCircle.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    foreach (var path in folderPaths) OpenFolder(path);
                };
            }
            cluster.Children.Add(openCircle);

            var artCircle = BuildLibraryGameProfileHeroActionCircle("\uE8B9", "Change Art - cover, banner, or local file");
            var artMenu = BuildLibraryGameProfileChangeArtMenu(profileWindow, view, folder, refreshHero);
            if (artMenu != null)
            {
                artCircle.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    artMenu.PlacementTarget = artCircle;
                    artMenu.IsOpen = true;
                };
            }
            else
            {
                artCircle.Opacity = 0.4;
                artCircle.Cursor = null;
            }
            cluster.Children.Add(artCircle);

            return cluster;
        }

        // Stateless circular action button used by the hero action cluster (Edit
        // Game / Open Folder / Change Art). Visually parallel to the toggle
        // buttons but always uses the muted "off" palette so it reads as an
        // action rather than a state. The caller wires MouseLeftButtonUp.
        Border BuildLibraryGameProfileHeroActionCircle(string glyph, string tooltip)
        {
            var circle = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = Brush("#1B2C3B"),
                BorderBrush = Brush("#33424D"),
                BorderThickness = new Thickness(1.4),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0),
                SnapsToDevicePixels = true
            };
            circle.Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 17,
                Foreground = Brush("#D7E2EA"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(tooltip))
            {
                circle.ToolTip = tooltip;
                AutomationProperties.SetName(circle, tooltip);
            }
            circle.MouseEnter += delegate { circle.Background = Brush("#264358"); };
            circle.MouseLeave += delegate { circle.Background = Brush("#1B2C3B"); };
            return circle;
        }

        // Change Art dropdown menu (PV-PLN-GPRO-001 step C.4 + circle refactor). The
        // hero's Change Art action circle attaches this menu so users can swap
        // covers/banners from SteamGridDB or a local file without leaving the
        // profile window. Each item routes through an existing helper
        // (ChooseLibraryAssetFromSteamGridDbAsync, SaveCustomCoverAsync,
        // OpenSavedCoversFolder) and asks refreshHero to rebuild the hero so new
        // art shows up immediately.
        ContextMenu BuildLibraryGameProfileChangeArtMenu(Window profileWindow, LibraryBrowserFolderView view, LibraryFolderInfo folder, Action refreshHero)
        {
            if (view == null || folder == null) return null;
            var menu = new ContextMenu { Placement = PlacementMode.Bottom };
            var hasToken = HasSteamGridDbApiToken();
            var actionFolders = GetLibraryBrowserActionFolders(view);
            if (actionFolders == null || actionFolders.Count == 0)
            {
                actionFolders = new List<LibraryFolderInfo> { folder };
            }
            var lookupFolder = GetLibraryBrowserPrimaryFolder(view) ?? folder;

            Action<LibraryBrowserFolderView> showFolderRefresh = delegate(LibraryBrowserFolderView _) { refreshHero?.Invoke(); };
            Action refreshHeroBanner = delegate { refreshHero?.Invoke(); };
            Action<string> toast = delegate(string msg)
            {
                if (!string.IsNullOrWhiteSpace(msg)) TryLibraryToast(msg, MessageBoxImage.Information);
            };

            var coverItem = new MenuItem
            {
                Header = "Choose Cover from SteamGridDB...",
                IsEnabled = hasToken && actionFolders.Count > 0
            };
            if (!hasToken) coverItem.ToolTip = "Add a SteamGridDB API token in Settings to enable.";
            coverItem.Click += async delegate
            {
                try
                {
                    await ChooseLibraryAssetFromSteamGridDbAsync(
                        profileWindow ?? this,
                        view,
                        lookupFolder,
                        actionFolders,
                        LibraryAssetPickerKind.Cover,
                        showFolderRefresh,
                        null,
                        refreshHeroBanner,
                        toast).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    LogException("Game profile | Choose cover", ex);
                    TryLibraryToast("Could not open cover picker: " + ex.Message, MessageBoxImage.Warning);
                }
            };

            var bannerItem = new MenuItem
            {
                Header = "Choose Banner from SteamGridDB...",
                IsEnabled = hasToken && actionFolders.Count > 0
            };
            if (!hasToken) bannerItem.ToolTip = "Add a SteamGridDB API token in Settings to enable.";
            bannerItem.Click += async delegate
            {
                try
                {
                    await ChooseLibraryAssetFromSteamGridDbAsync(
                        profileWindow ?? this,
                        view,
                        lookupFolder,
                        actionFolders,
                        LibraryAssetPickerKind.Banner,
                        showFolderRefresh,
                        null,
                        refreshHeroBanner,
                        toast).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    LogException("Game profile | Choose banner", ex);
                    TryLibraryToast("Could not open banner picker: " + ex.Message, MessageBoxImage.Warning);
                }
            };

            var localCoverItem = new MenuItem { Header = "Set Custom Cover...", IsEnabled = actionFolders.Count > 0 };
            localCoverItem.Click += async delegate
            {
                try
                {
                    Directory.CreateDirectory(savedCoversRoot);
                    var picked = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.jxr;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                    if (string.IsNullOrWhiteSpace(picked)) return;
                    await SaveCustomCoverAsync(actionFolders, picked).ConfigureAwait(true);
                    refreshHero?.Invoke();
                    TryLibraryToast("Cover saved.", MessageBoxImage.Information);
                    Log("Custom cover set for " + (folder.Name ?? "folder") + " (game profile).");
                }
                catch (Exception ex)
                {
                    LogException("Game profile | Set custom cover", ex);
                    TryLibraryToast("Cover save failed: " + ex.Message, MessageBoxImage.Warning);
                }
            };

            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };

            menu.Items.Add(coverItem);
            menu.Items.Add(bannerItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(localCoverItem);
            menu.Items.Add(openMyCoversItem);
            return menu;
        }

        Border BuildLibraryGameProfileHeroToggleButton(string glyph, string activeColorHex, string activeBgHex, bool isOn, string tooltipOn, string tooltipOff)
        {
            var btn = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1.4),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0),
                SnapsToDevicePixels = true
            };
            btn.Child = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyLibraryGameProfileHeroToggleState(btn, glyph, activeColorHex, activeBgHex, isOn, tooltipOn, tooltipOff);
            return btn;
        }

        void ApplyLibraryGameProfileHeroToggleState(Border btn, string glyph, string activeColorHex, string activeBgHex, bool isOn, string tooltipOn, string tooltipOff)
        {
            if (btn == null) return;
            btn.Background = Brush(isOn ? activeBgHex : "#1A2530");
            btn.BorderBrush = Brush(isOn ? activeColorHex : "#2C3D4A");
            if (btn.Child is TextBlock tb)
            {
                tb.Text = glyph;
                tb.Foreground = Brush(isOn ? activeColorHex : "#7E94A2");
            }
            var label = isOn ? tooltipOn : tooltipOff;
            btn.ToolTip = label;
            AutomationProperties.SetName(btn, label ?? string.Empty);
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

        FrameworkElement BuildLibraryGameProfileStats(LibraryGameProfileMetrics metrics, LibraryGameProfileSteamStats steamStats = null)
        {
            var ss = steamStats;
            var showSteam = ss != null && ss.ShowSteamCards;
            var root = new UniformGrid { Columns = showSteam ? 7 : 5 };
            var safe = metrics ?? new LibraryGameProfileMetrics();
            var firstCaptureText = safe.FirstCaptureDate > DateTime.MinValue
                ? FormatLibraryGameProfileDate(safe.FirstCaptureDate)
                : "\u2014";
            var latestCaptureText = safe.LatestCaptureDate > DateTime.MinValue
                ? FormatLibraryGameProfileDate(safe.LatestCaptureDate)
                : "\u2014";
            var latestCaptureRelative = FormatLibraryGameProfileRelative(safe.LatestCaptureDate);
            var cards = new List<FrameworkElement>
            {
                BuildLibraryGameProfileStatCard("Captures", safe.CaptureCount.ToString(CultureInfo.CurrentCulture)),
                BuildLibraryGameProfileStatCard("Videos", safe.VideoCount.ToString(CultureInfo.CurrentCulture)),
                BuildLibraryGameProfileStatCard("Sessions", safe.SessionCount.ToString(CultureInfo.CurrentCulture)),
                BuildLibraryGameProfileStatCard("First Capture", firstCaptureText),
                BuildLibraryGameProfileStatCard("Latest Capture", latestCaptureText, latestCaptureRelative)
            };
            if (showSteam)
            {
                if (!ss.Hydrated)
                {
                    cards.Add(BuildLibraryGameProfileStatCard("Steam time", "\u2014", "Loading\u2026"));
                    cards.Add(BuildLibraryGameProfileStatCard("Rare unlocked", "\u2014", "Loading\u2026"));
                }
                else
                {
                    FrameworkElement timeCard;
                    if (ss.SteamPlaytimeMinutes.HasValue)
                    {
                        var formatted = GameAchievementsFetchService.FormatSteamPlaytimeForDisplay(ss.SteamPlaytimeMinutes.Value);
                        timeCard = BuildLibraryGameProfileStatCard("Steam time", formatted, "Lifetime (Steam)");
                        ToolTipService.SetToolTip(timeCard,
                            "Total play time reported by Steam for this app on your account.");
                    }
                    else
                    {
                        var steamIdSet = !string.IsNullOrWhiteSpace(CurrentSteamUserId64());
                        var sub = steamIdSet
                            ? "Hidden, not owned, or API unavailable"
                            : "Add SteamID64 in Path Settings";
                        timeCard = BuildLibraryGameProfileStatCard("Steam time", "\u2014", sub);
                        ToolTipService.SetToolTip(timeCard,
                            "Requires Steam Web API key and SteamID64. Play time comes from GetOwnedGames.");
                    }

                    cards.Add(timeCard);

                    FrameworkElement rareCard;
                    if (!ss.SteamGlobalRarityAvailable)
                    {
                        rareCard = BuildLibraryGameProfileStatCard("Rare unlocked", "\u2014", "Rarity unavailable");
                        ToolTipService.SetToolTip(rareCard,
                            "Steam did not return global achievement percentages for this title.");
                    }
                    else
                    {
                        var n = ss.SteamRareUnlockedCount ?? 0;
                        rareCard = BuildLibraryGameProfileStatCard(
                            "Rare unlocked",
                            n.ToString(CultureInfo.CurrentCulture),
                            "\u2264" + GameAchievementsFetchService.SteamRareAchievementMaxPercent.ToString(CultureInfo.InvariantCulture) + "% rarity");
                        ToolTipService.SetToolTip(rareCard,
                            "Achievements you have unlocked whose global Steam completion rate is at or below "
                            + GameAchievementsFetchService.SteamRareAchievementMaxPercent.ToString(CultureInfo.InvariantCulture) + "%.");
                    }

                    cards.Add(rareCard);
                }
            }

            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].Margin = new Thickness(0, 0, i == cards.Count - 1 ? 0 : 12, 0);
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
        void BeginLoadLibraryGameProfileAchievements(
            Window owner,
            StackPanel host,
            LibraryBrowserFolderView view,
            LibraryGameProfileSteamStats steamStats,
            Action refreshStatsSection,
            CancellationToken cancellation)
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
                    if (steamStats != null && steamStats.ShowSteamCards)
                    {
                        steamStats.Hydrated = true;
                        if (result != null && !result.IsError
                            && string.Equals(result.SourceLabel ?? string.Empty, "Steam", StringComparison.OrdinalIgnoreCase))
                        {
                            steamStats.SteamPlaytimeMinutes = result.SteamPlaytimeMinutes;
                            steamStats.SteamRareUnlockedCount = result.SteamRareUnlockedCount;
                            steamStats.SteamGlobalRarityAvailable = result.SteamGlobalRarityAvailable;
                        }

                        try { refreshStatsSection?.Invoke(); }
                        catch (Exception ex) { LogException("LibraryGameProfile.refreshStatsSection", ex); }
                    }

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

        // PV-PLN-GPRO-001 Phase D.2-D.4: Sessions section. Walks the per-window
        // LibraryGameProfileSessionEntry snapshot built in ShowLibraryGameProfileWindow
        // through LibraryGameProfileSessionMath, renders a header (with the threshold
        // picker pill) + up to 6 inline session cards + a "View all sessions" CTA.
        // The threshold picker pill writes the shared librarySessionThresholdMinutes
        // setting via applyThreshold (Phase D.3) so the main browser's Sessions
        // grouping mode and the profile stay in sync (PV-POL-GPRO-SESSION-001).
        FrameworkElement BuildLibraryGameProfileSessionsSection(
            Window profileWindow,
            LibraryBrowserFolderView view,
            LibraryFolderInfo folder,
            IReadOnlyList<LibraryGameProfileSessionEntry> entries,
            int thresholdMinutes,
            Action<int> applyThreshold)
        {
            const int previewLimit = 6;
            var section = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, thresholdMinutes);
            section.Children.Add(BuildLibraryGameProfileSessionsHeader(thresholdMinutes, applyThreshold));
            if (sessions.Count == 0)
            {
                section.Children.Add(BuildLibraryGameProfileEmptyCard(
                    "No sessions yet - captures will be grouped here once we know when they were taken."));
                return section;
            }
            for (var i = 0; i < sessions.Count && i < previewLimit; i++)
                section.Children.Add(BuildLibraryGameProfileSessionCard(profileWindow, view, folder, sessions[i]));
            if (sessions.Count > previewLimit)
            {
                section.Children.Add(BuildLibraryGameProfileViewAllSessionsButton(
                    profileWindow, view, folder, sessions, thresholdMinutes));
            }
            return section;
        }

        FrameworkElement BuildLibraryGameProfileSessionsHeader(int thresholdMinutes, Action<int> applyThreshold)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = BuildLibraryGameProfileSectionTitle(
                "Sessions",
                "Captures grouped by gameplay gaps. Adjust the threshold to merge bursts or split them apart.");
            grid.Children.Add(titleStack);
            var thresholdPill = BuildLibraryGameProfileSessionThresholdPill(thresholdMinutes, applyThreshold);
            Grid.SetColumn(thresholdPill, 1);
            grid.Children.Add(thresholdPill);
            return grid;
        }

        // Threshold picker pill. The five preset values (30/60/90/120/180) match the
        // main browser's Sessions-mode threshold buttons in
        // MainWindow.LibraryBrowserShowOrchestration.cs:706-710 so changing the
        // threshold from either surface keeps the same set of options.
        Button BuildLibraryGameProfileSessionThresholdPill(int thresholdMinutes, Action<int> applyThreshold)
        {
            var current = SettingsService.NormalizeLibrarySessionThresholdMinutes(thresholdMinutes);
            var pill = Btn(
                "Threshold: " + FormatLibraryGameProfileThresholdLabel(current) + "  \u25BE",
                null,
                "#15242D",
                Brushes.White);
            pill.Height = 30;
            pill.MinWidth = 148;
            pill.FontSize = 12;
            pill.Padding = new Thickness(12, 0, 12, 0);
            pill.VerticalAlignment = VerticalAlignment.Top;
            pill.Margin = new Thickness(0, 4, 0, 0);
            ApplyLibraryPillChrome(pill, "#15242D", "#26404E", "#1F3340", "#0F1B22", "#D7E2EA");
            AutomationProperties.SetName(pill, "Session threshold (currently " + FormatLibraryGameProfileThresholdLabel(current) + ")");
            var menu = new ContextMenu { Placement = PlacementMode.Bottom };
            foreach (var preset in new[] { 30, 60, 90, 120, 180 })
            {
                var item = new MenuItem
                {
                    Header = FormatLibraryGameProfileThresholdLabel(preset),
                    IsCheckable = true,
                    IsChecked = preset == current
                };
                var capturedPreset = preset;
                item.Click += delegate
                {
                    if (applyThreshold != null) applyThreshold(capturedPreset);
                };
                menu.Items.Add(item);
            }
            pill.Click += delegate
            {
                menu.PlacementTarget = pill;
                menu.IsOpen = true;
            };
            return pill;
        }

        // Session card. Clicking the card opens the same browser-style session
        // window used in the main library's Sessions grouping mode (see
        // OpenLibrarySessionWindow in MainWindow.LibraryBrowserRender.DetailPane.cs)
        // so users get the same browse + select + per-file editor experience
        // they're used to. Hover chrome (background lift + cursor) communicates
        // the click affordance.
        FrameworkElement BuildLibraryGameProfileSessionCard(
            Window profileWindow,
            LibraryBrowserFolderView view,
            LibraryFolderInfo folder,
            LibraryGameProfileSession session)
        {
            var idleBg = Brush("#111A21");
            var hoverBg = Brush("#172430");
            var card = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(14),
                Background = idleBg,
                Cursor = Cursors.Hand,
                ToolTip = "Open this session"
            };
            card.MouseEnter += delegate { card.Background = hoverBg; };
            card.MouseLeave += delegate { card.Background = idleBg; };
            card.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                e.Handled = true;
                OpenLibraryGameProfileSessionWindow(profileWindow, view, folder, session);
            };
            AutomationProperties.SetName(card,
                "Open session of " + session.Count + (session.Count == 1 ? " capture" : " captures"));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var thumb = BuildLibraryGameProfileSessionThumbnail(session);
            Grid.SetColumn(thumb, 0);
            grid.Children.Add(thumb);

            var info = new StackPanel
            {
                Margin = new Thickness(14, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var localStart = session.StartUtc.Kind == DateTimeKind.Utc ? session.StartUtc.ToLocalTime() : session.StartUtc;
            var localEnd = session.EndUtc.Kind == DateTimeKind.Utc ? session.EndUtc.ToLocalTime() : session.EndUtc;
            info.Children.Add(new TextBlock
            {
                Text = localStart.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var durationText = FormatLibraryGameProfileSessionDuration(session.Duration);
            var rangeText = localStart.ToString("h:mm tt", CultureInfo.CurrentCulture)
                + " \u2192 "
                + localEnd.ToString("h:mm tt", CultureInfo.CurrentCulture);
            if (!string.IsNullOrEmpty(durationText)) rangeText += "  \u00B7  " + durationText;
            info.Children.Add(new TextBlock
            {
                Text = rangeText,
                Foreground = Brush("#9DB1BD"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var relative = FormatLibraryGameProfileRelative(localStart);
            if (!string.IsNullOrEmpty(relative))
            {
                info.Children.Add(new TextBlock
                {
                    Text = relative,
                    Foreground = Brush("#62768A"),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var stats = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            stats.Children.Add(new TextBlock
            {
                Text = session.Count.ToString(CultureInfo.CurrentCulture) + (session.Count == 1 ? " capture" : " captures"),
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right
            });
            if (session.VideoCount > 0)
            {
                stats.Children.Add(new TextBlock
                {
                    Text = session.VideoCount.ToString(CultureInfo.CurrentCulture) + (session.VideoCount == 1 ? " video" : " videos"),
                    Foreground = Brush("#86A0AE"),
                    FontSize = 11,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            Grid.SetColumn(stats, 2);
            grid.Children.Add(stats);

            card.Child = grid;
            return card;
        }

        // First image-like entry wins (newest-first traversal); a session that is
        // entirely videos or has no resolvable image falls back to the first entry
        // and lets CreateAsyncImageTile show its "CLIP" / filename text fallback.
        FrameworkElement BuildLibraryGameProfileSessionThumbnail(LibraryGameProfileSession session)
        {
            const double thumbWidth = 96;
            const double thumbHeight = 70;
            string previewPath = null;
            for (var i = 0; i < session.Entries.Count; i++)
            {
                var entry = session.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath)) continue;
                if (IsImage(entry.FilePath)) { previewPath = entry.FilePath; break; }
            }
            if (previewPath == null && session.Entries.Count > 0)
            {
                for (var i = 0; i < session.Entries.Count; i++)
                {
                    var entry = session.Entries[i];
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.FilePath)) { previewPath = entry.FilePath; break; }
                }
            }
            var fallbackText = string.Empty;
            if (!string.IsNullOrWhiteSpace(previewPath))
                fallbackText = IsVideo(previewPath) ? "CLIP" : Path.GetFileName(previewPath);
            return CreateAsyncImageTile(
                previewPath,
                CalculateLibraryDetailTileDecodeWidth((int)thumbWidth, ResolveLibraryDpiScale()),
                thumbWidth,
                thumbHeight,
                Stretch.UniformToFill,
                fallbackText,
                Brushes.White,
                new Thickness(0),
                new Thickness(0),
                Brush("#0E1418"),
                new CornerRadius(8),
                Brush("#263640"),
                new Thickness(1));
        }

        Button BuildLibraryGameProfileViewAllSessionsButton(
            Window profileWindow,
            LibraryBrowserFolderView view,
            LibraryFolderInfo folder,
            IReadOnlyList<LibraryGameProfileSession> sessions,
            int thresholdMinutes)
        {
            var btn = Btn("View all sessions", null, "#15242D", Brushes.White);
            btn.Height = 30;
            btn.MinWidth = 156;
            btn.FontSize = 12;
            btn.Padding = new Thickness(14, 0, 14, 0);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            btn.Margin = new Thickness(0, 12, 0, 0);
            ApplyLibraryPillChrome(btn, "#15242D", "#26404E", "#1F3340", "#0F1B22", "#D7E2EA");
            btn.ToolTip = sessions.Count.ToString(CultureInfo.CurrentCulture)
                + (sessions.Count == 1 ? " session" : " sessions")
                + " at " + FormatLibraryGameProfileThresholdLabel(thresholdMinutes) + " threshold";
            AutomationProperties.SetName(btn, "View all sessions");
            btn.Click += delegate
            {
                ShowLibraryGameProfileViewAllSessionsDialog(profileWindow, view, folder, sessions, thresholdMinutes);
            };
            return btn;
        }

        void ShowLibraryGameProfileViewAllSessionsDialog(
            Window owner,
            LibraryBrowserFolderView view,
            LibraryFolderInfo folder,
            IReadOnlyList<LibraryGameProfileSession> sessions,
            int thresholdMinutes)
        {
            var dialog = new Window
            {
                Title = "Sessions - " + (view == null || string.IsNullOrWhiteSpace(view.Name) ? "Game Profile" : view.Name),
                Width = 720,
                Height = 720,
                MinWidth = 520,
                MinHeight = 420,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0B1116"),
                ResizeMode = ResizeMode.CanResize
            };
            dialog.PreviewKeyDown += delegate(object _, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                dialog.Close();
            };
            var scroll = new ScrollViewer
            {
                Padding = new Thickness(20, 18, 20, 22),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = sessions.Count.ToString(CultureInfo.CurrentCulture)
                    + (sessions.Count == 1 ? " session" : " sessions")
                    + " at " + FormatLibraryGameProfileThresholdLabel(thresholdMinutes) + " threshold",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Newest first. Change the threshold from the profile to re-bucket.",
                Foreground = Brush("#8FA4B0"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });
            for (var i = 0; i < sessions.Count; i++)
                stack.Children.Add(BuildLibraryGameProfileSessionCard(dialog, view, folder, sessions[i]));
            scroll.Content = stack;
            dialog.Content = scroll;
            dialog.ShowDialog();
        }

        // Click handler for an individual session card on the profile. Reuses the
        // main browser's OpenLibrarySessionWindow (the same window the Sessions
        // grouping mode opens when a session card is clicked there) so the visual
        // and interaction model is identical from both surfaces. We synthesize the
        // LibraryDetailRenderGroup the main browser builds from its day-bucketed
        // pipeline; timeline contexts and per-file media layout caches stay null /
        // empty because they're optimization-only inputs (BuildLibraryDetailMasonryChunks
        // and BuildLibrarySessionDistinctContextLabel both null-check). Per-file
        // metadata edit is rerouted through a toast - the deeper "edit metadata"
        // path needs the live working set's selection plumbing, which the profile
        // doesn't own; users can do that from the main library if they need it.
        void OpenLibraryGameProfileSessionWindow(
            Window profileWindow,
            LibraryBrowserFolderView view,
            LibraryFolderInfo folder,
            LibraryGameProfileSession session)
        {
            if (session == null || session.Entries == null) return;
            var sessionFiles = new List<string>(session.Entries.Count);
            for (var i = session.Entries.Count - 1; i >= 0; i--)
            {
                var entry = session.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath)) continue;
                if (!File.Exists(entry.FilePath)) continue;
                sessionFiles.Add(entry.FilePath);
            }
            if (sessionFiles.Count == 0)
            {
                TryLibraryToast("No captures from this session are still on disk.", MessageBoxImage.Information);
                return;
            }
            var localStart = session.StartUtc.Kind == DateTimeKind.Utc ? session.StartUtc.ToLocalTime() : session.StartUtc;
            var localEnd = session.EndUtc.Kind == DateTimeKind.Utc ? session.EndUtc.ToLocalTime() : session.EndUtc;
            var headerText = localStart.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture);
            var rangeText = localStart.ToString("h:mm tt", CultureInfo.CurrentCulture)
                + " \u2192 " + localEnd.ToString("h:mm tt", CultureInfo.CurrentCulture);
            var durationText = FormatLibraryGameProfileSessionDuration(session.Duration);
            var subtitle = string.IsNullOrEmpty(durationText) ? rangeText : rangeText + "  \u00B7  " + durationText;
            var group = new LibraryDetailRenderGroup
            {
                CaptureDate = localStart.Date,
                SessionStartDate = localStart,
                SessionEndDate = localEnd,
                HeaderText = headerText,
                SubtitleText = subtitle,
                Files = sessionFiles
            };
            var emptyLayout = new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);
            Action<string> openSingleFileMetadataEditor = delegate
            {
                TryLibraryToast(
                    "Open the capture from the main library to edit its per-file metadata.",
                    MessageBoxImage.Information);
            };
            try
            {
                OpenLibrarySessionWindow(
                    profileWindow,
                    view,
                    group,
                    null,
                    emptyLayout,
                    ResolveLibraryDpiScale(),
                    openSingleFileMetadataEditor,
                    delegate { });
            }
            catch (Exception ex)
            {
                LogException("LibraryGameProfile.OpenLibraryGameProfileSessionWindow", ex);
                TryLibraryToast("Couldn't open session window: " + ex.Message, MessageBoxImage.Warning);
            }
        }

        // 30 -> "30 min", 60 -> "1 hr", 90 -> "1.5 hr", 120 -> "2 hr", 180 -> "3 hr".
        // Anything off the preset grid clamps to the nearest preset by virtue of
        // SettingsService.NormalizeLibrarySessionThresholdMinutes upstream.
        static string FormatLibraryGameProfileThresholdLabel(int minutes)
        {
            if (minutes < 60) return minutes.ToString(CultureInfo.CurrentCulture) + " min";
            if (minutes % 60 == 0) return (minutes / 60).ToString(CultureInfo.CurrentCulture) + " hr";
            return (minutes / 60.0).ToString("0.0", CultureInfo.CurrentCulture) + " hr";
        }

        // Same-second sessions render as "instant" so the card still has a meaningful
        // sub-line. Otherwise: minutes only when < 1 hour, else "Xh Ym" (Y suppressed
        // when 0).
        static string FormatLibraryGameProfileSessionDuration(TimeSpan duration)
        {
            if (duration.Ticks <= 0) return "instant";
            if (duration.TotalMinutes < 1) return "instant";
            if (duration.TotalMinutes < 60)
            {
                var minutes = (int)Math.Round(duration.TotalMinutes);
                if (minutes <= 0) minutes = 1;
                return minutes.ToString(CultureInfo.CurrentCulture) + " min";
            }
            var hours = (int)Math.Floor(duration.TotalHours);
            var remainder = (int)Math.Round(duration.TotalMinutes - (hours * 60));
            if (remainder >= 60) { hours++; remainder = 0; }
            if (remainder == 0) return hours.ToString(CultureInfo.CurrentCulture) + "h";
            return hours.ToString(CultureInfo.CurrentCulture) + "h " + remainder.ToString(CultureInfo.CurrentCulture) + "m";
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
        // The out-parameter <paramref name="refreshFromFolder"/> lets the orchestrator
        // re-pull notes from <paramref name="folder"/> after sibling surfaces (e.g.
        // the Edit Game form) mutate them, so the card and the editor stay in sync.
        FrameworkElement BuildLibraryGameProfileNotesCard(Window profileWindow, LibraryBrowserFolderView view, LibraryFolderInfo folder, out Action refreshFromFolder)
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

            refreshFromFolder = delegate
            {
                ApplyLibraryGameProfileNotesText(notesText, folder == null ? string.Empty : folder.CollectionNotes);
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
