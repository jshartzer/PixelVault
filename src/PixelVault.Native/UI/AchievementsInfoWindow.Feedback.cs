using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        readonly AchievementUnlockTracker _unlockTracker = new AchievementUnlockTracker();
        string _feedbackAccountScope = Guid.NewGuid().ToString();
        HashSet<(string, string, string)> _newUnlocks = new HashSet<(string, string, string)>();
        readonly List<Action> _feedbackRestores = new List<Action>();
        readonly TextBlock _unlockNotice = new TextBlock
        {
            FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Foreground = UiBrushHelper.FromHex("#B8D5C4"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed
        };
        DispatcherTimer _feedbackTimer;

        void ShowUnlockFeedback()
        {
            if (_newUnlocks.Count == 0) return;
            _unlockNotice.Text = _newUnlocks.Count == 1 ? "1 achievement newly earned" : _newUnlocks.Count + " achievements newly earned";
            _unlockNotice.Visibility = Visibility.Visible;
            AutomationProperties.SetName(_unlockNotice, _unlockNotice.Text);
            AutomationProperties.SetLiveSetting(_unlockNotice, AutomationLiveSetting.Polite);
            // A static, short-lived accent works with reduced motion as well as normal settings.
            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _feedbackTimer.Tick += (_, __) => ClearUnlockFeedback();
            _feedbackTimer.Start();
        }

        void HighlightUnlocks()
        {
            foreach (var card in CollectionCards(_showGrid ? (DependencyObject)_gridContent : _list))
            {
                if (!(card.Tag is GameAchievementsFetchService.AchievementRow row) || !row.HasStableProviderIdentity
                    || !_newUnlocks.Contains(AchievementUnlockTracker.Identity(row))) continue;
                var brush = card.BorderBrush;
                var thickness = card.BorderThickness;
                card.BorderBrush = SystemParameters.HighContrast ? SystemColors.HighlightBrush : UiBrushHelper.FromHex("#C1E8CA");
                card.BorderThickness = new Thickness(2);
                _feedbackRestores.Add(() => { card.BorderBrush = brush; card.BorderThickness = thickness; });
            }
        }

        static IEnumerable<Border> CollectionCards(DependencyObject root)
        {
            if (root is Border card && card.Tag is GameAchievementsFetchService.AchievementRow) yield return card;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var nested in CollectionCards(child)) yield return nested;
        }

        void ClearUnlockFeedback()
        {
            _feedbackTimer?.Stop();
            _feedbackTimer = null;
            foreach (var restore in _feedbackRestores) restore();
            _feedbackRestores.Clear();
            _newUnlocks.Clear();
            _unlockNotice.Visibility = Visibility.Collapsed;
        }
    }
}
