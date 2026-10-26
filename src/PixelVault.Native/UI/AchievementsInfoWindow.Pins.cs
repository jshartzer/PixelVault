using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        readonly StackPanel _nextUpHost = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        IReadOnlyList<AchievementGuideEntry> _pins = Array.Empty<AchievementGuideEntry>();
        string _pinProvider, _pinGameId;

        void ReloadPins(bool reconcile)
        {
            if (_guideService == null) return;
            try
            {
                var first = _loadedResult?.Rows?.FirstOrDefault(row => row?.HasStableProviderIdentity == true);
                if (first != null)
                {
                    _pinProvider = first.Provider;
                    _pinGameId = first.ProviderGameId;
                }
                else if (string.Equals(_loadedResult?.SourceLabel, "Steam", StringComparison.OrdinalIgnoreCase))
                {
                    _pinProvider = "steam";
                    _pinGameId = _folder?.SteamAppId ?? _pinGameId;
                }
                else if (string.Equals(_loadedResult?.SourceLabel, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
                {
                    _pinProvider = "retroachievements";
                    _pinGameId = _folder?.RetroAchievementsGameId ?? _pinGameId;
                }
                if (reconcile) _guideService.ReconcileAchievementPins(_loadedResult?.Rows);
                if (!string.IsNullOrWhiteSpace(_pinGameId))
                    _pins = _guideService.LoadPinnedAchievements(_pinProvider, _pinGameId);
                RenderNextUp();
            }
            catch (Exception ex)
            {
                _error.Text = "Could not load Next Up targets. " + ex.Message;
                _error.Visibility = Visibility.Visible;
            }
        }

        bool IsPinned(GameAchievementsFetchService.AchievementRow row) => _pins.Any(pin => AchievementPinUi.Matches(pin, row));

        void ChangePin(GameAchievementsFetchService.AchievementRow row, bool pinned)
        {
            if (_isLoading || _isClosed || _guideService == null) return;
            if (!AchievementPinUi.Change(this, _guideService, row, pinned)) return;
            ReloadPins(false);
            RenderAchievementRows(_loadedResult?.Rows, _userAgent, _loadedResult?.SourceLabel);
            if (_pins.Count > 0) _nextUpHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            else _remainingOnly.Focus();
        }

        void AddPinAction(Border card, GameAchievementsFetchService.AchievementRow row)
        {
            if (_guideService == null || row?.HasStableProviderIdentity != true) return;
            var menu = new ContextMenu();
            card.ContextMenu = menu;
            AddCategoryAction(menu, row);
            var pinned = IsPinned(row);
            if (!pinned && (!row.ProgressKnown || row.Unlocked)) return;
            var action = new MenuItem { Header = pinned ? "Unpin from Next Up" : "Pin to Next Up" };
            AutomationProperties.SetName(action, action.Header + ": " + row.Title);
            action.Click += (_, e) => { e.Handled = true; ChangePin(row, !pinned); };
            menu.Items.Add(new Separator());
            menu.Items.Add(action);
            menu.Opened += (_, __) => action.IsEnabled = !_isLoading && !_isClosed;
            card.ContextMenu = menu;
        }

        void RenderNextUp()
        {
            _nextUpHost.Children.Clear();
            _nextUpHost.Visibility = _pins.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_pins.Count == 0) return;
            _nextUpHost.Children.Add(new TextBlock
            {
                Text = "Next Up · " + _pins.Count + " / " + AchievementGuideService.MaximumAchievementPins,
                Foreground = UiBrushHelper.FromHex("#9FC7B0"), FontWeight = FontWeights.SemiBold, FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            });
            var targets = new Grid();
            for (var i = 0; i < _pins.Count; i++)
            {
                var entry = _pins[i];
                targets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var live = _loadedResult?.Rows?.FirstOrDefault(row => AchievementPinUi.Matches(entry, row));
                var row = live ?? AchievementPinUi.StoredRow(entry);
                var content = new Grid { Margin = new Thickness(0, 0, i == _pins.Count - 1 ? 0 : 6, 0) };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = new StackPanel();
                label.Children.Add(new TextBlock { Text = entry.Title, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold });
                if (live == null || !live.ProgressKnown)
                    label.Children.Add(new TextBlock { Text = "Progress unavailable", FontSize = 10, Foreground = UiBrushHelper.FromHex("#9CB1BC") });
                var tags = AchievementGuideWindow.GuideReadTags(entry.Tags, entry.IsMissable, row.Unlocked);
                if (tags.Count > 0)
                {
                    var chip = AchievementCollectionUi.TagChip(tags[0]);
                    chip.HorizontalAlignment = HorizontalAlignment.Left;
                    chip.Margin = new Thickness(0, 3, 0, 0);
                    chip.MaxWidth = 130;
                    ((TextBlock)chip.Child).TextWrapping = TextWrapping.NoWrap;
                    ((TextBlock)chip.Child).TextTrimming = TextTrimming.CharacterEllipsis;
                    chip.ToolTip = string.Join(", ", tags);
                    label.Children.Add(chip);
                }
                var open = new Button
                {
                    Content = label, MinHeight = 36, Padding = new Thickness(8, 4, 8, 4), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = UiBrushHelper.FromHex("#17242B"), Foreground = Brushes.White, BorderBrush = UiBrushHelper.FromHex("#47765C"),
                    ToolTip = entry.Title + (live == null ? " — open saved guide; current progress unavailable" : " — open guide")
                };
                AutomationProperties.SetName(open, "Next Up: open guide for " + entry.Title);
                AccessibilityUi.TryApplyFocusVisualStyle(open);
                open.Click += delegate
                {
                    if (_isLoading || _isClosed) return;
                    if (live != null) OpenAchievementGuide(live, _userAgent);
                    else
                    {
                        AchievementGuideWindow.ShowStoredModal(this, _guideService, _loadedResult?.GameTitle ?? _folder?.Name, entry,
                            _trackingStore, _scoreSteamAccount, _scoreRetroAccount);
                        ReloadPins(false);
                        RenderAchievementRows(_loadedResult?.Rows, _userAgent, _loadedResult?.SourceLabel);
                    }
                };
                content.Children.Add(open);
                var unpin = AchievementPinUi.Button(row, true, () => ChangePin(row, false));
                unpin.Margin = new Thickness(2, 0, 0, 0);
                Grid.SetColumn(unpin, 1);
                content.Children.Add(unpin);
                Grid.SetColumn(content, i);
                targets.Children.Add(content);
            }
            _nextUpHost.Children.Add(targets);
        }
    }
}
