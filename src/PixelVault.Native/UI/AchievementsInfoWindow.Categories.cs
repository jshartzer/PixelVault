using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        const string DlcCategoryPrefix = "DLC: ";
        internal static bool IsNamedDlcCategory(string name) =>
            (name ?? "").StartsWith(DlcCategoryPrefix, StringComparison.OrdinalIgnoreCase);

        void AddCategoryAction(ContextMenu menu, GameAchievementsFetchService.AchievementRow row)
            => AddCategoryMenu(menu, row, _loadedResult?.Rows, name => ChangeCategory(row, name),
                () => !_isLoading && !_isClosed, this);

        internal static void AddCategoryMenu(ContextMenu menu, GameAchievementsFetchService.AchievementRow row,
            System.Collections.Generic.IEnumerable<GameAchievementsFetchService.AchievementRow> rows,
            Action<string> assign, Func<bool> enabled, Window owner)
        {
            var category = new MenuItem { Header = "Assign category" };
            menu.Items.Add(category);
            var names = new[] { "Base Game", "DLC", "Difficulty", "Story", "Collectibles", "Multiplayer", "Miscellaneous" }
                .Concat((rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Select(EffectiveAchievementGroupName)
                    .Where(name => !IsNamedDlcCategory(name)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var item = new MenuItem { Header = name, IsCheckable = true,
                    IsChecked = string.Equals(EffectiveAchievementGroupName(row), name, StringComparison.OrdinalIgnoreCase)
                        || (name == "DLC" && IsNamedDlcCategory(row.GroupName)) };
                AutomationProperties.SetName(item, "Assign " + name + " category: " + row.Title);
                item.Click += (_, e) =>
                {
                    e.Handled = true;
                    if (!enabled()) return;
                    if (name == "DLC") PromptCategory(owner, row, assign, dlc: true);
                    else assign(name);
                };
                category.Items.Add(item);
            }
            category.Items.Add(new Separator());
            var custom = new MenuItem { Header = "Custom category…" };
            custom.Click += (_, e) => { e.Handled = true; if (enabled()) PromptCategory(owner, row, assign); };
            category.Items.Add(custom);
            menu.Opened += (_, __) => category.IsEnabled = enabled();
        }

        internal static AchievementGuideEntry SaveCategory(IAchievementGuideService service,
            GameAchievementsFetchService.AchievementRow row, string name, int order)
        {
            var entry = service.LoadGuideEntry(row.Provider, row.ProviderGameId, row.ProviderAchievementId)
                ?? throw new InvalidOperationException("The achievement is not available in the local catalog. Refresh and try again.");
            return service.SaveGuide(new AchievementGuideEdit
            {
                AchievementId = entry.AchievementId, GuideText = entry.GuideText,
                SourceUrl = entry.SourceUrl, SourceTitle = entry.SourceTitle, Tags = entry.Tags,
                IsMissable = entry.IsMissable, GroupName = name, GroupSortOrder = order
            });
        }

        void ChangeCategory(GameAchievementsFetchService.AchievementRow row, string name)
        {
            if (_isLoading || _isClosed) return;
            try
            {
                var order = _loadedResult?.Rows?.FirstOrDefault(r => string.Equals(
                    EffectiveAchievementGroupName(r), name, StringComparison.OrdinalIgnoreCase))?.GroupSortOrder ?? 0;
                var saved = SaveCategory(_guideService, row, name, order);
                row.GroupName = saved.GroupName;
                row.GroupSortOrder = saved.GroupSortOrder;
                _collapsedGroups.Remove(EffectiveAchievementGroupName(row));
                ReloadPins(false);
                RenderAchievementRows(_loadedResult?.Rows, _userAgent, _loadedResult?.SourceLabel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the achievement category. " + ex.Message, "Achievement category",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        static void PromptCategory(Window owner, GameAchievementsFetchService.AchievementRow row, Action<string> assign, bool dlc = false)
        {
            var dialog = new Window { Title = dlc ? "Name DLC" : "Assign category", Owner = owner, Width = 400,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = (dlc ? "DLC name for " : "Category for ") + row.Title, TextWrapping = TextWrapping.Wrap });
            var initial = dlc ? (IsNamedDlcCategory(row.GroupName) ? row.GroupName.Substring(DlcCategoryPrefix.Length) : "")
                : EffectiveAchievementGroupName(row);
            var input = new TextBox { Text = initial, MaxLength = dlc ? 120 - DlcCategoryPrefix.Length : 120,
                Margin = new Thickness(0, 10, 0, 12) };
            AutomationProperties.SetName(input, dlc ? "DLC name" : "Category name");
            panel.Children.Add(input);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "Assign", IsDefault = true, MinWidth = 75, Padding = new Thickness(8, 4, 8, 4),
                IsEnabled = !string.IsNullOrWhiteSpace(input.Text) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 75, Margin = new Thickness(8, 0, 0, 0) };
            input.TextChanged += (_, __) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
            save.Click += (_, __) => dialog.DialogResult = true;
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (_, __) => { input.Focus(); input.SelectAll(); };
            if (dialog.ShowDialog() == true) assign((dlc ? DlcCategoryPrefix : "") + input.Text.Trim());
        }
    }
}
