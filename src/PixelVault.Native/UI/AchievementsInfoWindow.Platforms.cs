using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        readonly ComboBox _platformSelector = new() { Width = 210, Height = 38, FontSize = 13 };
        FrameworkElement _platformPanel;
        int _loadGeneration;

        internal static AchievementsInfoWindow CreatePlatformWindow(Window owner, string title,
            IReadOnlyList<GameProfilePlatformChoice> choices, string defaultKey,
            Func<GameProfilePlatformChoice, bool, CancellationToken, Task<GameAchievementsFetchService.FetchResult>> fetch,
            Action<GameProfilePlatformChoice> saveChoice, string steamAccount, string retroAccount, string xboxAccount,
            IAchievementGuideService guides, AchievementTrackingStore trackingStore, bool remainingOnly = false)
        {
            var dialog = new AchievementsInfoWindow(owner, title, guides, null, "PixelVault", () => Task.FromResult(new GameAchievementsFetchService.FetchResult()));
            dialog._remainingOnly.IsChecked = remainingOnly;
            dialog._platformPanel.Visibility = Visibility.Visible;
            CancellationTokenSource request = null;
            bool selecting = true;
            foreach (var choice in choices)
                dialog._platformSelector.Items.Add(new ComboBoxItem { Content = choice.Label + (choices.Count(c => c.Provider == choice.Provider) > 1 ? " · " + choice.Display + " · " + choice.Id : ""), Tag = choice });
            var selected = choices.FirstOrDefault(c => c.Key == defaultKey);
            if (selected == null)
                dialog._platformSelector.Items.Insert(0, new ComboBoxItem { Content = choices.Count == 0 ? "No linked platform" : "Select a platform…", IsEnabled = false });
            dialog._platformSelector.SelectedItem = dialog._platformSelector.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as GameProfilePlatformChoice)?.Key == selected?.Key);
            dialog._platformSelector.IsEnabled = choices.Count > 0;
            void Configure(GameProfilePlatformChoice choice)
            {
                request?.Cancel(); request?.Dispose(); request = new CancellationTokenSource();
                var token = request.Token;
                dialog._loadGeneration++;
                dialog._isLoading = false; dialog.ClearUnlockFeedback(); dialog._loadedResult = null;
                dialog._stats = null; dialog._progressDetails.Content = null;
                dialog._list.Items.Clear(); dialog._gridContent.Children.Clear(); dialog._collapsedGroups.Clear();
                dialog._pins = Array.Empty<AchievementGuideEntry>(); dialog._pinGameId = dialog._pinProvider = null;
                dialog._nextUpHost.Children.Clear(); dialog._nextUpHost.Visibility = Visibility.Collapsed;
                dialog._updated.Visibility = dialog._earnedSummary.Visibility = dialog._error.Visibility = Visibility.Collapsed;
                dialog._guide.IsEnabled = false;
                dialog._folder = new LibraryFolderInfo { Name = title, GameId = choice?.Folder?.GameId,
                    SteamAppId = choice?.Provider == "steam" ? choice.Id : null,
                    RetroAchievementsGameId = choice?.Provider == "retroachievements" ? choice.Id : null };
                dialog._scoreSteamAccount = choice?.Provider == "steam" ? steamAccount : null;
                dialog._scoreRetroAccount = choice?.Provider == "retroachievements" ? retroAccount : null;
                dialog._scoreXboxAccount = choice?.Provider == "xbox" ? xboxAccount : null;
                dialog._trackingStore = choice == null || choice.Provider == "xbox" ? null : trackingStore;
                dialog._trackingButton.IsEnabled = dialog._trackingStore != null && choice != null;
                dialog._feedbackAccountScope = choice?.Key + "|" + (dialog._scoreSteamAccount ?? dialog._scoreRetroAccount ?? dialog._scoreXboxAccount);
                dialog.Title = choice == null ? "Achievements" : "Achievements — " + choice.Label;
                dialog._heading.Text = title; dialog._detail.Text = choice == null ? "Link a platform from the game profile to view achievements." : "Loading " + choice.Label + " achievements…";
                dialog._empty.Text = dialog._detail.Text; dialog._empty.Visibility = Visibility.Visible;
                dialog._refresh.IsEnabled = choice != null;
                bool firstLoad = true;
                dialog._fetchAchievements = () =>
                {
                    var force = !firstLoad; firstLoad = false;
                    return choice == null ? Task.FromResult(new GameAchievementsFetchService.FetchResult { ErrorMessage = "Choose a linked platform." }) : fetch(choice, force, token);
                };
            }
            Configure(selected);
            selecting = false;
            dialog._platformSelector.SelectionChanged += async (_, __) =>
            {
                if (selecting || dialog._platformSelector.SelectedItem is not ComboBoxItem item || item.Tag is not GameProfilePlatformChoice choice) return;
                try { saveChoice?.Invoke(choice); }
                catch (Exception ex)
                {
                    selecting = true;
                    dialog._platformSelector.SelectedItem = dialog._platformSelector.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as GameProfilePlatformChoice)?.Key == selected?.Key);
                    selecting = false; dialog._error.Text = "Could not save platform: " + ex.Message; dialog._error.Visibility = Visibility.Visible; return;
                }
                selected = choice; Configure(choice); await dialog.LoadAsync();
            };
            dialog.Loaded += async (_, __) => { if (selected != null) await dialog.LoadAsync(); };
            dialog.Closed += (_, __) => { request?.Cancel(); request?.Dispose(); };
            return dialog;
        }
    }
}
