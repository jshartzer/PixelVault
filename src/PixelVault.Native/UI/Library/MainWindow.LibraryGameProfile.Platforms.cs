using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        event Action<string> GameProfileAchievementsChanged;

        void ShowLinkedGameAchievements(Window owner, LibraryFolderInfo folder, IEnumerable<LibraryFolderInfo> sources,
            bool remainingOnly = false, string initialPlatform = null)
        {
            if (folder == null) return;
            var store = new GameProfilePlatformStore(dataRoot);
            var identity = LibraryGameProfileWindowKey(folder);
            var preference = store.Get(identity);
            var xbox = new XboxAchievementService().SavedLibrary();
            var choices = GameProfilePlatformStore.Choices(folder.Name, sources.Concat(new[] { folder }), xbox.Titles, preference);
            var current = initialPlatform == null ? GameProfilePlatformStore.Default(choices, preference) : choices.FirstOrDefault(c => c.Key == initialPlatform);
            var steamAccount = CurrentSteamUserId64(); var retroAccount = CurrentRetroAchievementsUsername();
            string Account(GameProfilePlatformChoice choice) => choice.Provider == "xbox" ? xbox.Xuid : choice.Provider == "steam" ? steamAccount : retroAccount;
            var dialog = AchievementsInfoWindow.CreatePlatformWindow(owner, folder.Name, choices, current?.Key,
                (choice, force, token) => FetchProfilePlatformAsync(choice, Account(choice), force, token),
                choice =>
                {
                    var next = new GameProfilePlatformStore.Preference { DefaultKey = choice.Key, XboxTitleId = choice.Provider == "xbox" ? choice.Id : preference.XboxTitleId };
                    store.Save(identity, next); preference = next;
                }, steamAccount, retroAccount, xbox.Xuid, achievementGuideService, achievementTrackingStore, remainingOnly);
            dialog.ShowDialog();
            GameProfileAchievementsChanged?.Invoke(identity);
        }

        FrameworkElement BuildGameProfilePlatformControls(Window owner, StackPanel achievements, LibraryBrowserFolderView view,
            LibraryFolderInfo folder, LibraryGameProfileSteamStats steamStats, Action refreshStats, Action refreshHero, CancellationToken lifetime)
        {
            var store = new GameProfilePlatformStore(dataRoot);
            var identity = LibraryGameProfileWindowKey(folder);
            var preference = store.Get(identity);
            var panel = new GameProfilePlatformBar();
            var selector = panel.Selector; var sync = panel.Sync; var link = panel.Link; var status = panel.Status;
            var folders = (view.SourceFolders ?? new()).Concat(new[] { view.PrimaryFolder, folder }).ToList();
            CancellationTokenSource request = null;
            bool rebuilding = false;
            GameProfilePlatformChoice Selected() => (selector.SelectedItem as ComboBoxItem)?.Tag as GameProfilePlatformChoice;
            void Load(bool force)
            {
                request?.Cancel(); request?.Dispose(); request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                var token = request.Token;
                achievements.Children.Clear();
                var selected = Selected();
                steamStats.ShowSteamCards = selected?.Provider == "steam";
                steamStats.Hydrated = false; steamStats.SteamPlaytimeMinutes = null; steamStats.SteamRareUnlockedCount = null; steamStats.SteamGlobalRarityAvailable = false;
                refreshStats(); sync.IsEnabled = selected != null;
                if (selected == null)
                {
                    status.Text = string.IsNullOrEmpty(preference.DefaultKey) ? "Link an Xbox game to show its achievements, or add a Steam / RetroAchievements ID in Edit Metadata." : "The saved platform is unavailable. Reconnect its account or choose another platform.";
                    return;
                }
                sync.IsEnabled = false; status.Text = force ? "Syncing " + selected.Label + " achievements…" : "Loading " + selected.Label + " achievements…";
                BeginLoadLibraryGameProfileAchievements(owner, achievements, view, steamStats, refreshStats, refreshHero, token,
                    selected, force, () => { if (!token.IsCancellationRequested) { sync.IsEnabled = true; status.Text = selected.Label + " achievements · " + selected.Title; } });
            }
            void Rebuild()
            {
                rebuilding = true;
                var choices = GameProfilePlatformStore.Choices(folder.Name, folders, new XboxAchievementService().SavedLibrary().Titles, preference);
                var current = GameProfilePlatformStore.Default(choices, preference);
                selector.Items.Clear();
                if (current == null) selector.Items.Add(new ComboBoxItem { Content = choices.Count == 0 ? "No linked platform" : "Select a platform…", IsEnabled = false });
                selector.IsEnabled = choices.Count > 0;
                foreach (var choice in choices)
                {
                    var duplicates = choices.Count(c => c.Provider == choice.Provider) > 1;
                    selector.Items.Add(new ComboBoxItem { Content = choice.Label + (duplicates ? " · " + choice.Display + " · " + choice.Id : ""), Tag = choice });
                }
                selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as GameProfilePlatformChoice)?.Key == current?.Key) ?? selector.Items[0];
                link.Content = string.IsNullOrEmpty(preference.XboxTitleId) ? "Link Xbox game…" : "Change Xbox game…";
                rebuilding = false;
            }
            selector.SelectionChanged += (_, __) =>
            {
                if (rebuilding) return;
                var selected = Selected(); if (selected == null) { Load(false); return; }
                var next = new GameProfilePlatformStore.Preference { DefaultKey = selected.Key, XboxTitleId = selected.Provider == "xbox" ? selected.Id : preference.XboxTitleId };
                try { store.Save(identity, next); preference = next; Load(false); }
                catch (Exception ex) { Rebuild(); status.Text = "Could not save Default Platform: " + ex.Message; }
            };
            sync.Click += (_, __) => Load(true);
            link.Click += (_, __) =>
            {
                var title = PickXboxProfileTitle(owner, folder.Name);
                if (title == null) return;
                var next = new GameProfilePlatformStore.Preference { DefaultKey = "xbox:" + title.Id, XboxTitleId = title.Id };
                try { store.Save(identity, next); preference = next; Rebuild(); Load(false); }
                catch (Exception ex) { status.Text = "Could not link Xbox game: " + ex.Message; }
            };
            void OnAchievementsChanged(string changed)
            {
                if (changed != identity || lifetime.IsCancellationRequested) return;
                preference = store.Get(identity); Rebuild(); Load(false);
            }
            GameProfileAchievementsChanged += OnAchievementsChanged;
            owner.Closed += (_, __) => { GameProfileAchievementsChanged -= OnAchievementsChanged; request?.Cancel(); request?.Dispose(); };
            Rebuild();
            ScheduleLibraryGameProfileInitialLoad(owner, () => Load(false), lifetime);
            return panel;
        }

        internal static void ScheduleLibraryGameProfileInitialLoad(Window owner, Action load, CancellationToken lifetime)
        {
            void Start()
            {
                if (!lifetime.IsCancellationRequested && owner.IsVisible) load();
            }
            // Profile content can now be attached after the window's Loaded event.
            // Queue this case so the new content is installed before loading sections.
            if (owner.IsLoaded)
                owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(Start));
            else
            {
                RoutedEventHandler loaded = null;
                loaded = (_, __) => { owner.Loaded -= loaded; Start(); };
                owner.Loaded += loaded;
            }
        }

        XboxAchievementService.XboxTitle PickXboxProfileTitle(Window owner, string name)
        {
            var titles = new XboxAchievementService().SavedLibrary().Titles;
            if (titles.Count == 0) { MessageBox.Show(owner, "Connect Xbox and refresh your games in Settings → Achievements first.", "Link Xbox game"); return null; }
            var window = new XboxProfileLinkWindow(owner, name, titles);
            return window.ShowDialog() == true ? window.SelectedTitle : null;
        }

        async Task<GameAchievementsFetchService.FetchResult> FetchProfilePlatformAsync(GameProfilePlatformChoice choice, string account, bool force, CancellationToken token)
        {
            var store = _achievementHubStore ?? new AchievementHubStore(System.IO.Path.Combine(dataRoot, "achievement-hub"), achievementGuideService as IAchievementScoreStore);
            if (!force && !string.IsNullOrWhiteSpace(account))
            {
                var saved = await store.LoadGameAsync(choice.Provider, account, choice.Id, token).ConfigureAwait(false);
                if (saved != null)
                {
                    var result = new GameAchievementsFetchService.FetchResult { SourceLabel = choice.Label, GameTitle = choice.Title, Rows = saved.Rows, CapturedUtcTicks = saved.UpdatedUtc, ScoreTotals = saved.ScoreTotals,
                        DetailLine = choice.Label + " · " + saved.Rows.Count(r => r.ProgressKnown && r.Unlocked) + " / " + saved.Rows.Count + " achievements earned" };
                    if (choice.Provider == "steam")
                    {
                        result.SteamPlaytimeMinutes = new LocalProviderSummaryStore(dataRoot).Read().FirstOrDefault(s => s.Provider == "steam" && s.GameId == choice.Id && s.Account == account)?.PlaytimeMinutes;
                        result.SteamGlobalRarityAvailable = saved.Rows.Any(r => AchievementCollectionStats.ValidPercent(r.GlobalUnlockPercent));
                        if (result.SteamGlobalRarityAvailable) result.SteamRareUnlockedCount = saved.Rows.Count(r => r.ProgressKnown && r.Unlocked && AchievementCollectionStats.ValidPercent(r.GlobalUnlockPercent) && r.GlobalUnlockPercent <= GameAchievementsFetchService.SteamRareAchievementMaxPercent);
                    }
                    return result;
                }
            }
            if (choice.Provider == "xbox")
            {
                if (string.IsNullOrWhiteSpace(account)) return new() { ErrorMessage = "Connect Xbox in Settings → Achievements to sync this game." };
                return await new XboxAchievementService().FetchAsync(account, choice.XboxTitle, token).ConfigureAwait(false);
            }
            var fetched = await GameAchievementsFetchService.FetchAsync(choice.Provider == "steam" ? "Steam" : "Emulation", choice.Folder,
                CurrentSteamWebApiKey(), CurrentRetroAchievementsApiKey(), choice.Provider == "steam" ? account : CurrentSteamUserId64(),
                choice.Provider == "retroachievements" ? account : CurrentRetroAchievementsUsername(), "PixelVault/" + AppVersion, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(account)) await store.SaveGameAsync(choice.Provider, account, choice.Id, fetched, token).ConfigureAwait(false);
            return fetched;
        }
    }
}
