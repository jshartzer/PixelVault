using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PixelVault.Tracking;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        CompletionShowcaseWindow completionShowcaseWindow;

        void ShowCompletionShowcase(Window owner, LibraryBrowserWorkingSet ws)
        {
            if (completionShowcaseWindow != null) { completionShowcaseWindow.Activate(); return; }
            var views = new Dictionary<string, LibraryBrowserFolderView>();
            var window = new CompletionShowcaseWindow((entry, width) => CreateAsyncImageTile(entry.ArtPath,
                (int)Math.Ceiling(width * 2), width, width * 1.5, Stretch.UniformToFill, entry.Title, Brushes.White,
                new Thickness(0), new Thickness(0), Brush("#151E24"), new CornerRadius(12), Brush("#2D3C47"), new Thickness(1)),
                (sender, entry) => { if (views.TryGetValue(entry.Id, out var view)) LibraryBrowserShowAchievementsInfo(sender, view); })
            { Owner = owner ?? this };
            completionShowcaseWindow = window;
            var generation = 0;
            async void Refresh()
            {
                var request = ++generation;
                try
                {
                    _achievementHubStore ??= new AchievementHubStore(System.IO.Path.Combine(dataRoot, "achievement-hub"), achievementGuideService as IAchievementScoreStore);
                    var snapshots = new Dictionary<string, AchievementHubStore.Snapshot>();
                    foreach (var source in new[] { (Provider: "steam", Account: CurrentSteamUserId64()), (Provider: "retroachievements", Account: CurrentRetroAchievementsUsername()) })
                    {
                        if (string.IsNullOrWhiteSpace(source.Account)) continue;
                        var saved = await _achievementHubStore.LoadAsync(source.Provider, source.Account, _achievementMonitorCancellation.Token);
                        foreach (var game in saved.Games) snapshots[source.Provider + ":" + game.Key] = game.Value;
                    }
                    await System.Threading.Tasks.Task.Run(() => achievementGuideService?.LoadSavedAchievementGroups(snapshots.Values.SelectMany(g => g.Rows)));
                    if (request != generation || !window.IsVisible) return;
                    var entries = new List<CompletionShowcaseEntry>();
                    views.Clear();
                    foreach (var view in BuildLibraryBrowserFolderViews(ws.Folders, "all").Where(v => !IsLibraryBrowserTimeProjectionView(v)))
                    {
                        CompletionShowcaseEntry best = null;
                        LibraryBrowserFolderView selectedView = null;
                        foreach (var folder in view.SourceFolders.Count > 0 ? view.SourceFolders : new List<LibraryFolderInfo> { BuildLibraryBrowserDisplayFolder(view) })
                        {
                            if (folder == null || !GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder.PlatformLabel), folder, out var steam)) continue;
                            var key = (steam ? "steam:" + folder.SteamAppId : "retroachievements:" + folder.RetroAchievementsGameId);
                            if (!snapshots.TryGetValue(key, out var snapshot)) continue;
                            var tier = ClassifyLibraryAchievementCompletion(snapshot.Rows);
                            if (tier == AchievementCompletionTier.None || (best != null && tier <= best.Tier)) continue;
                            best = new CompletionShowcaseEntry(key, view.Name, folder.PlatformLabel,
                                GetLibraryArtPathForDisplayOnly(BuildLibraryBrowserDisplayFolder(view)), tier,
                                snapshot.Rows.Count(r => r.ProgressKnown && r.Unlocked), snapshot.Rows.Count);
                            selectedView = new LibraryBrowserFolderView { PrimaryFolder = folder, Name = view.Name };
                            selectedView.SourceFolders.Add(folder);
                        }
                        if (best != null) { entries.Add(best); views[best.Id] = selectedView; }
                    }
                    window.SetEntries(entries.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList());
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LogException("Completion collection", ex); window.SetError(); }
            }
            window.Activated += (_, __) => Refresh();
            window.Closed += (_, __) => { generation++; if (ReferenceEquals(completionShowcaseWindow, window)) completionShowcaseWindow = null; };
            window.Show();
        }
    }

    internal sealed record CompletionShowcaseEntry(string Id, string Title, string Platform, string ArtPath, AchievementCompletionTier Tier, int Earned = 0, int Total = 0);

    internal sealed class CompletionShowcaseWindow : Window
    {
        internal static readonly int[] CoverWidths = { 120, 160, 200 };
        readonly Func<CompletionShowcaseEntry, double, FrameworkElement> cover;
        readonly Action<Window, CompletionShowcaseEntry> open;
        readonly ComboBox size;
        readonly TextBlock status;
        readonly StackPanel groups;
        IReadOnlyList<CompletionShowcaseEntry> entries = Array.Empty<CompletionShowcaseEntry>();

        internal CompletionShowcaseWindow(Func<CompletionShowcaseEntry, double, FrameworkElement> cover, Action<Window, CompletionShowcaseEntry> open = null)
        {
            this.cover = cover; this.open = open;
            Title = "Completion case"; Width = 1779; Height = 1249; MinWidth = 480; MinHeight = 440;
            Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = UiBrushHelper.FromHex("#10171D"); Foreground = Brushes.White;
            TrackerFormStyle.Apply(this);
            var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            var close = LibraryButtonChrome.Btn("Close", null, "#20343A", Brushes.White); close.Click += (_, __) => Close();
            close.Padding = new Thickness(14, 6, 14, 6); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            heading.Children.Add(new TextBlock { Text = "Completion case", FontSize = 23, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            size = new ComboBox { ItemsSource = new[] { "Small · 120 × 180", "Medium · 160 × 240", "Large · 200 × 300" }, SelectedIndex = 2, Width = 178, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            AutomationProperties.SetName(size, "Cover size"); DockPanel.SetDock(size, Dock.Top); root.Children.Add(size);
            status = new TextBlock { Text = "Loading completed games…", Foreground = UiBrushHelper.FromHex("#9CABB7"), FontSize = 12, Margin = new Thickness(0, 0, 0, 18) };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
            groups = new StackPanel { Margin = new Thickness(6, 4, 6, 8) };
            root.Children.Add(new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            size.SelectionChanged += (_, __) => Render();
            KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !size.IsDropDownOpen) { Close(); e.Handled = true; } };
        }
        internal void SetEntries(IReadOnlyList<CompletionShowcaseEntry> values)
        {
            // Activation refreshes saved progress; equal results must retain frames, covers and scroll/focus.
            if (entries.SequenceEqual(values) && groups.Children.Count > 0) return;
            entries = values.ToArray(); Render();
        }
        internal void SetError() { status.Text = "Could not load saved achievement progress. Reopen the collection to try again."; }
        void Render()
        {
            var width = CoverWidths[Math.Clamp(size.SelectedIndex, 0, CoverWidths.Length - 1)];
            groups.Children.Clear();
            var eligible = entries.Where(e => e.Tier != AchievementCompletionTier.None && e.Total > 0).ToList();
            foreach (var tier in new[] { AchievementCompletionTier.Base, AchievementCompletionTier.BaseAndDlc, AchievementCompletionTier.BaseAndDlcAndPv })
            {
                var rows = eligible.Where(e => e.Tier == tier).ToList();
                var cabinet = new CompletionCasePanel(tier, width);
                AutomationProperties.SetName(cabinet, (tier == AchievementCompletionTier.Base ? "Gold · Base complete" : tier == AchievementCompletionTier.BaseAndDlcAndPv ? "Diamond · Base + DLC + PV complete" : "Emerald · Base + DLC complete") + ", " + rows.Count + " games");
                foreach (var entry in rows)
                {
                    var card = new Grid { Width = width, Height = width * 1.5, Background = Brushes.Transparent, Focusable = true, Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = "Double-click to open achievements" };
                    var roundedClip = new RectangleGeometry(new Rect(0,0,width,width*1.5),12,12); roundedClip.Freeze(); card.Clip = roundedClip;
                    AutomationProperties.SetName(card, entry.Title + ", " + entry.Earned + " of " + entry.Total + " achievements. Press Enter to open achievements.");
                    var artwork = cover(entry, width);
                    card.Children.Add(artwork);
                    var details = new StackPanel();
                    details.Children.Add(new TextBlock { Text = entry.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 50, TextTrimming = TextTrimming.CharacterEllipsis });
                    details.Children.Add(new TextBlock { Text = entry.Earned + " / " + entry.Total + " achievements", FontSize = 12, Margin = new Thickness(0, 5, 0, 3), Foreground = UiBrushHelper.FromHex("#D0DEDF") });
                    details.Children.Add(new TextBlock { Text = entry.Platform, FontSize = 11, Foreground = UiBrushHelper.FromHex("#A5B9C5") });
                    var fade = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(0,1) };
                    fade.GradientStops.Add(new GradientStop(Color.FromArgb(0,3,9,13),0));
                    fade.GradientStops.Add(new GradientStop(Color.FromArgb(220,3,9,13),.35));
                    fade.GradientStops.Add(new GradientStop(Color.FromArgb(245,3,9,13),1));
                    var caption = new Border { Child = details, Background = fade, VerticalAlignment = VerticalAlignment.Bottom,
                        Padding = new Thickness(9,28,9,10), Opacity = 0, IsHitTestVisible = false };
                    card.Children.Add(caption);
                    card.MouseEnter += (_, __) => caption.Opacity = 1;
                    card.MouseLeave += (_, __) => caption.Opacity = 0;
                    card.GotKeyboardFocus += (_, __) => { if (System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice) caption.Opacity = 1; };
                    card.LostKeyboardFocus += (_, __) => { if (!card.IsMouseOver) caption.Opacity = 0; };
                    card.MouseLeftButtonDown += (_, e) => { card.Focus(); if (e.ClickCount == 2) { open?.Invoke(this, entry); e.Handled = true; } };
                    card.KeyDown += (_, e) => { caption.Opacity = 1; if (e.Key == System.Windows.Input.Key.Enter) { open?.Invoke(this, entry); e.Handled = true; } };
                    // Round the original card, then project it outside that crop so tilted edges remain visible.
                    var finish = new HolofoilCover(card, artwork); finish.SetTier(entry.Tier);
                    cabinet.Children.Add(finish);
                }
                groups.Children.Add(cabinet);
            }
            status.Text = eligible.Count + " completed games · Double-click a cover to open achievements";
        }
    }
}
