using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelVault.Tracking;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        internal static AchievementGroupSummary CenturyAchievementContentGroup(string provider = null, string gameId = null) => new()
        {
            Name = CenturyAchievementCatalog.GroupName, Key = "century-achievements", IsContentGroup = true,
            Rows = new(), CenturyDefinitions = CenturyAchievementCatalog.ForGame(provider, gameId)
        };

        AchievementGroupSummary CenturyGroup()
        {
            var first = _loadedResult?.Rows?.FirstOrDefault(r => r?.HasStableProviderIdentity == true);
            var provider = first?.Provider ?? (!string.IsNullOrWhiteSpace(_folder?.SteamAppId) ? "steam" : "retroachievements");
            var group = CenturyAchievementContentGroup(provider, first?.ProviderGameId
                ?? (provider == "steam" ? _folder?.SteamAppId : _folder?.RetroAchievementsGameId));
            var account = provider == "steam" ? _scoreSteamAccount : _scoreRetroAccount;
            group.Rows = group.CenturyDefinitions.Select(d => CenturyPresentation(d, _trackingStore?.Century(d.Provider, d.GameId, d.Id, account))).ToList();
            return group;
        }

        internal static GameAchievementsFetchService.AchievementRow CenturyPresentation(CenturyAchievementDefinition definition, CenturyProgress progress = null) => new()
        {
            CenturyDefinitionId = definition.Id, Provider = definition.Provider, ProviderGameId = definition.GameId, ProviderAchievementId = definition.Id,
            IconUrlColor = CenturyAchievementCatalog.ArtworkUri(definition.Id, true), IconUrlGray = CenturyAchievementCatalog.ArtworkUri(definition.Id, false),
            Title = definition.Title, Description = definition.Description, GroupName = definition.Category,
            Points = definition.Points, AwardedPoints = progress?.Unlocked == true ? definition.Points : null,
            Meta = progress == null ? "Progress unavailable" : progress.Unlocked ? "Earned" : progress.Current + " / " + progress.Target,
            CounterCurrent = progress?.Current, CounterMinimum = progress == null ? null : 0, CounterTarget = progress?.Target,
            ProgressKnown = progress != null, Unlocked = progress?.Unlocked == true, UnlockUtcTicks = progress?.UnlockUtc ?? 0
        };

        internal static FrameworkElement BuildCenturyAchievementCard(CenturyAchievementDefinition definition, bool grid = true, CenturyProgress progress = null)
        {
            var row = CenturyPresentation(definition, progress);
            var card = grid ? CreateAchievementGridCard(row, "PixelVault/Century") : CreateAchievementRowCard(row, "PixelVault/Century");
            card.ToolTip = definition.Title + "\n" + definition.Description + "\n" + definition.Requirement
                + "\n" + definition.Points + " PV pts · " + row.Meta;
            return card;
        }

        IEnumerable<FrameworkElement> CenturyContent(AchievementGroupSummary group)
        {
            if (group.Total == 0)
            {
                yield return new TextBlock { Text = "No Centurian Achievements have been added for this game yet.", Foreground = UiBrushHelper.FromHex("#9CB1BC"), Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
                yield break;
            }
            var definitions = group.CenturyDefinitions.Where(d => _remainingOnly.IsChecked != true
                || group.Rows.Any(r => r.CenturyDefinitionId == d.Id && r.ProgressKnown && !r.Unlocked)).ToList();
            if (_showGrid)
            {
                var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, ItemWidth = 164, ItemHeight = 184 };
                foreach (var definition in definitions) cards.Children.Add(CenturyCard(definition, true));
                yield return cards;
            }
            else foreach (var definition in definitions) yield return CenturyCard(definition, false);
        }

        FrameworkElement CenturyCard(CenturyAchievementDefinition definition, bool grid)
        {
            var account = definition.Provider == "steam" ? _scoreSteamAccount : _scoreRetroAccount;
            var progress = _trackingStore?.Century(definition.Provider, definition.GameId, definition.Id, account);
            var card = BuildCenturyAchievementCard(definition, grid, progress);
            var row = (GameAchievementsFetchService.AchievementRow)card.Tag;
            AttachTrackingMenu(card, row);
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                var game = new AchievementHubGame { Provider = definition.Provider, Id = definition.GameId, Title = definition.GameTitle, Folder = _folder };
                new AchievementDetailWindow(this, game, row, new[] { row }, progress?.VerifiedUtc ?? 0, null, trackingStore: _trackingStore,
                    trackingAccount: definition.Provider == "steam" ? _scoreSteamAccount : _scoreRetroAccount).Show();
            };
            return card;
        }
    }
}
