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
        {
            AddCategoryMenu(menu, row, _loadedResult?.Rows, name => ChangeCategory(row, name),
                () => !_isLoading && !_isClosed, this, sharedCategories: () => _guideService.LoadCategoryNames());
            AddDlcMenu(menu, row, () => _guideService.LoadDlcNames(row.Provider, row.ProviderGameId)
                .Concat((_loadedResult?.Rows ?? new()).Select(r => AchievementOrganization.DlcName(r.DlcName, r.GroupName))),
                name => ChangeDlc(row, name), () => !_isLoading && !_isClosed, this);
        }

        internal static void AddCategoryMenu(ContextMenu menu, GameAchievementsFetchService.AchievementRow row,
            System.Collections.Generic.IEnumerable<GameAchievementsFetchService.AchievementRow> rows,
            Action<string> assign, Func<bool> enabled, Window owner, bool direct = false,
            Func<System.Collections.Generic.IEnumerable<string>> sharedCategories = null)
        {
            var category = new MenuItem { Header = "Assign category" };
            if (!direct) menu.Items.Add(category);
            var items = direct ? menu.Items : category.Items;
            void Populate(bool includeShared)
            {
                items.Clear();
                var shared = Enumerable.Empty<string>();
                Exception error = null;
                if (includeShared && sharedCategories != null)
                {
                    try { shared = sharedCategories().ToList(); }
                    catch (Exception ex) { error = ex; }
                }
                var names = shared.Concat(AchievementOrganization.DefaultCategories)
                    .Concat((rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Where(r => r != null).Select(r => AchievementOrganization.CategoryName(r.GroupName)))
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                {
                    var item = new MenuItem { Header = name, IsCheckable = true,
                        IsChecked = string.Equals(AchievementOrganization.CategoryName(row.GroupName), name, StringComparison.OrdinalIgnoreCase) };
                    AutomationProperties.SetName(item, "Assign " + name + " category: " + row.Title);
                    item.Click += (_, e) => { e.Handled = true; if (enabled()) assign(name); };
                    items.Add(item);
                }
                if (error != null) items.Add(new MenuItem { Header = "Shared categories unavailable", IsEnabled = false, ToolTip = error.Message });
                items.Add(new Separator());
                var custom = new MenuItem { Header = "Custom category…" };
                custom.Click += (_, e) => { e.Handled = true; if (enabled()) PromptCategory(owner, row, assign); };
                items.Add(custom);
            }
            Populate(false);
            category.SubmenuOpened += (_, __) => Populate(true);
            menu.Opened += (_, __) => { if (direct) { menu.IsEnabled = enabled(); Populate(true); } else category.IsEnabled = enabled(); };
        }

        internal static Button BuildCategoryPickerButton(Func<GameAchievementsFetchService.AchievementRow> selected,
            Func<System.Collections.Generic.IEnumerable<GameAchievementsFetchService.AchievementRow>> rows,
            Action<GameAchievementsFetchService.AchievementRow, string> assign, Func<bool> enabled, Window owner,
            Func<System.Collections.Generic.IEnumerable<string>> sharedCategories = null)
        {
            var button = new Button
            {
                Content = "Change category…", FontSize = 12, Padding = new Thickness(10, 4, 10, 4),
                Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : System.Windows.Media.Brushes.White,
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#20343A"),
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#3B5965"),
                ToolTip = "Change this achievement's category. DLC membership is preserved."
            };
            AutomationProperties.SetName(button, "Change achievement category");
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            button.Click += (_, __) =>
            {
                var row = selected();
                if (row?.HasStableProviderIdentity != true || !enabled()) return;
                var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                AddCategoryMenu(menu, row, rows().Where(r => r != null && r.Provider == row.Provider && r.ProviderGameId == row.ProviderGameId),
                    name => assign(row, name), enabled, owner, direct: true, sharedCategories: sharedCategories);
                menu.Closed += (_, __) => { if (ReferenceEquals(button.ContextMenu, menu)) button.ContextMenu = null; };
                button.ContextMenu = menu;
                menu.IsOpen = true;
            };
            return button;
        }

        internal static void AddDlcMenu(ContextMenu menu, GameAchievementsFetchService.AchievementRow row,
            Func<System.Collections.Generic.IEnumerable<string>> names, Action<string> assign, Func<bool> enabled, Window owner)
        {
            var dlc = new MenuItem { Header = "Assign DLC" };
            void Populate()
            {
                dlc.Items.Clear();
                void Add(string title, string value)
                {
                    var item = new MenuItem { Header = new TextBlock { Text = title }, IsCheckable = true,
                        IsChecked = string.Equals(AchievementOrganization.DlcName(row.DlcName, row.GroupName), value, StringComparison.OrdinalIgnoreCase) };
                    AutomationProperties.SetName(item, "Assign " + title + ": " + row.Title);
                    item.Click += (_, e) => { e.Handled = true; if (enabled()) assign(value); };
                    dlc.Items.Add(item);
                }
                Add("Base Game", "");
                try
                {
                    foreach (var name in names().Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) Add(name, name);
                }
                catch (Exception ex) { dlc.Items.Add(new MenuItem { Header = "Could not load DLC names", IsEnabled = false, ToolTip = ex.Message }); }
                dlc.Items.Add(new Separator());
                var create = new MenuItem { Header = "Create New DLC…" };
                create.Click += (_, e) => { e.Handled = true; if (enabled()) PromptCategory(owner, row, assign, dlc: true); };
                dlc.Items.Add(create);
            }
            // The placeholder allows keyboard/right-arrow expansion; choices load only when this submenu opens.
            dlc.Items.Add(new MenuItem { Header = "Base Game" });
            dlc.SubmenuOpened += (_, __) => Populate();
            menu.Opened += (_, __) => dlc.IsEnabled = enabled();
            menu.Items.Add(dlc);
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
                row.DlcName = saved.DlcName;
                row.GroupSortOrder = saved.GroupSortOrder;
                ExpandOrganization(row);
                ReloadPins(false);
                RenderAchievementRows(_loadedResult?.Rows, _userAgent, _loadedResult?.SourceLabel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the achievement category. " + ex.Message, "Achievement category",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ExpandOrganization(GameAchievementsFetchService.AchievementRow row)
        {
            var content = AchievementContentName(row);
            _collapsedGroups.Remove(content);
            _collapsedGroups.Remove(AchievementCategoryKey(content, AchievementOrganization.CategoryName(row.GroupName)));
        }

        void ChangeDlc(GameAchievementsFetchService.AchievementRow row, string name)
        {
            if (_isLoading || _isClosed) return;
            try
            {
                var saved = _guideService.AssignAchievementDlc(row, name);
                row.DlcName = saved.DlcName;
                ExpandOrganization(row);
                ReloadPins(false);
                RenderAchievementRows(_loadedResult?.Rows, _userAgent, _loadedResult?.SourceLabel);
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the achievement DLC. " + ex.Message, "Achievement DLC", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        static void PromptCategory(Window owner, GameAchievementsFetchService.AchievementRow row, Action<string> assign, bool dlc = false)
        {
            var dialog = new Window { Title = dlc ? "Name DLC" : "Assign category", Owner = owner, Width = 400,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = (dlc ? "DLC name for " : "Category for ") + row.Title, TextWrapping = TextWrapping.Wrap });
            var initial = dlc ? "" : AchievementOrganization.CategoryName(row.GroupName);
            var input = new TextBox { Text = initial, MaxLength = 120,
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
            if (dialog.ShowDialog() == true) assign(input.Text.Trim());
        }
    }
}
