using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PixelVaultNative
{
    sealed class TrackerAchievementPicker : ComboBox
    {
        readonly List<GameAchievementsFetchService.AchievementRow> achievements;
        internal TrackerAchievementPicker(IEnumerable<GameAchievementsFetchService.AchievementRow> achievements)
        {
            if(!System.Windows.SystemParameters.HighContrast)SetResourceReference(StyleProperty,typeof(ComboBox));
            this.achievements = (achievements ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Where(r => r != null).ToList();
            AutomationProperties.SetName(this, "Linked achievement (optional)");
            ToolTip = "Choose an achievement for this game, or leave the checklist game-wide.";
        }

        internal void SelectGame(string provider, string gameId, string achievementId = null)
        {
            Items.Clear(); Items.Add(new ComboBoxItem { Content = "No achievement link — game-wide" });
            var rows = achievements.Where(r => r.Provider == provider && r.ProviderGameId == gameId && r.HasStableProviderIdentity)
                .GroupBy(r => r.ProviderAchievementId, StringComparer.Ordinal).Select(g => g.First()).ToList();
            if (!string.IsNullOrEmpty(achievementId) && !rows.Any(r => r.ProviderAchievementId == achievementId))
                rows.Add(new GameAchievementsFetchService.AchievementRow { Provider = provider, ProviderGameId = gameId, ProviderAchievementId = achievementId, Title = "Achievement " + achievementId });
            foreach (var row in rows.OrderBy(r => r.Title)) Items.Add(new ComboBoxItem { Content = row.Title + " (" + row.ProviderAchievementId + ")", Tag = row });
            SelectedItem = Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as GameAchievementsFetchService.AchievementRow)?.ProviderAchievementId == achievementId)
                ?? Items[0];
        }
        internal GameAchievementsFetchService.AchievementRow Achievement => (SelectedItem as ComboBoxItem)?.Tag as GameAchievementsFetchService.AchievementRow;
    }
}
