using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    sealed partial class AchievementHubWindow
    {
        readonly TextBlock scoreTotal = Text("— PV pts", 36, true);
        readonly TextBlock scoreEarned = Text("—", 22, true);
        readonly TextBlock scoreCompletion = Text("—", 22, true);
        readonly TextBlock scorePerfect = Text("—", 22, true);
        readonly TextBlock scoreMonth = Text("—", 22, true);
        readonly ContentControl scoreRarest = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        string scoreRarestSignature;

        Border BuildScoreSection()
        {
            var content = new StackPanel();
            content.Children.Add(Text("Achievement score", 18, true));
            scoreTotal.Margin = new Thickness(0, 4, 0, 16);
            scoreTotal.Foreground = Color("#A4D5BB", SystemColors.WindowTextBrush);
            scoreTotal.ToolTip = "Saved PV points earned by your connected accounts in the current library. Finalized awards stay fixed.";
            content.Children.Add(scoreTotal);

            var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            stats.SizeChanged += (_, __) => stats.Columns = stats.ActualWidth >= 700 ? 4 : 2;
            void Stat(string label, TextBlock value, string explanation)
            {
                var block = new StackPanel { Margin = new Thickness(0, 0, 12, 10), ToolTip = explanation };
                block.Children.Add(value);
                var caption = Text(label, 12); caption.Margin = new Thickness(0, 3, 0, 0);
                block.Children.Add(caption);
                stats.Children.Add(block);
            }
            Stat("Points this month", scoreMonth, "Saved PV points for achievements unlocked this calendar month in your local time. Missing or future unlock dates are excluded.");
            Stat("Achievements earned", scoreEarned, "Known unlocked achievements across synced library games, including all achievement groups.");
            Stat("Completion", scoreCompletion, "Earned achievements divided by earned plus unearned achievements with known progress.");
            Stat("Perfect games", scorePerfect, "Synced games with at least one achievement and every achievement earned, including all groups.");
            content.Children.Add(stats);
            var rarestLabel = Text("Rarest earned", 12);
            rarestLabel.Margin = new Thickness(0, 2, 0, 6);
            content.Children.Add(rarestLabel);
            scoreRarest.Content = Text("—", 22, true);
            scoreRarestSignature = null;
            content.Children.Add(scoreRarest);
            var card = new Border
            {
                Background = Color("#192B26", SystemColors.WindowBrush),
                BorderBrush = Color("#365B4A", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 16), Child = content
            };
            AutomationProperties.SetName(card, "Achievement score and collection statistics");
            return card;
        }

        void ResetScore()
        {
            scoreTotal.Text = "— PV pts";
            scoreEarned.Text = scoreCompletion.Text = scorePerfect.Text = "—";
            scoreMonth.Text = "—";
            scoreRarest.Content = Text("—", 22, true);
            scoreRarestSignature = null;
            scoreTotal.ToolTip = "Loading saved achievement score…";
        }

        void RenderScore(AchievementHubCollection collection)
        {
            scoreTotal.Text = collection.HasPoints ? collection.EarnedPoints.ToString("N0", CultureInfo.CurrentCulture) + " PV pts" : "— PV pts";
            scoreMonth.Text = collection.HasPoints ? collection.PointsThisMonth.ToString("N0", CultureInfo.CurrentCulture) + " PV pts" : "—";
            var rarest = collection.RarestEarned;
            var rarestSignature = rarest == null ? "none" : string.Join("\u001f", rarest.Game.Key, rarest.Game.Title,
                rarest.Row.ProviderAchievementId, rarest.Row.Title, rarest.Row.Description, rarest.Row.GlobalUnlockPercent,
                rarest.Row.IconUrlColor, rarest.Row.Points, rarest.Row.AwardedPoints, rarest.Row.AwardProvisional, rarest.Row.UnlockUtcTicks);
            if (rarestSignature != scoreRarestSignature)
            {
                scoreRarestSignature = rarestSignature;
                scoreRarest.Content = rarest == null ? Text("—", 22, true) : RarestScoreAchievement(rarest);
            }
            var hasProgress = collection.GamesWithProgress > 0;
            scoreEarned.Text = hasProgress ? collection.Earned.ToString("N0", CultureInfo.CurrentCulture) : "—";
            var known = collection.Earned + collection.Unearned;
            scoreCompletion.Text = known > 0
                ? Math.Round(collection.Earned * 100d / known, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture) + "%" : "—";
            var perfect = collection.Entries.GroupBy(e => e.Game.Key)
                .Count(g => g.All(e => e.Row.ProgressKnown && e.Row.Unlocked));
            scorePerfect.Text = hasProgress ? perfect.ToString("N0", CultureInfo.CurrentCulture) : "—";
            var coverage = collection.LibraryGames == 0
                ? "Connect a Steam or RetroAchievements account to start your score."
                : "Connected accounts · current library · " + collection.GamesWithProgress.ToString("N0", CultureInfo.CurrentCulture)
                    + " of " + collection.LibraryGames.ToString("N0", CultureInfo.CurrentCulture) + " games synced · totals ignore filters";
            scoreTotal.ToolTip = "Saved PV points. Finalized awards stay fixed." + Environment.NewLine + coverage
                + (collection.ProvisionalAwards > 0 ? Environment.NewLine
                    + collection.ProvisionalAwards.ToString("N0", CultureInfo.CurrentCulture)
                    + " provisional awards included; these finalize when rarity is available." : "");
        }

        FrameworkElement RarestScoreAchievement(AchievementHubEntry entry)
        {
            var row = entry.Row;
            var image = new Image { Width = 56, Height = 56, Stretch = Stretch.UniformToFill };
            var icon = new Border { Width = 60, Height = 60, Child = image, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = Color("#11181D", SystemColors.WindowBrush), BorderBrush = Color("#365B4A", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0) };
            // A new Image per render prevents an older asynchronous download from replacing a newer rarest achievement.
            AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, UserAgent);
            var percent = row.GlobalUnlockPercent.Value.ToString(row.GlobalUnlockPercent < .01 ? "0.########" : "0.##", CultureInfo.CurrentCulture) + "%";
            var rarity = Text(percent, percent.Length > 9 ? 18 : 22, true);
            rarity.VerticalAlignment = VerticalAlignment.Center;
            rarity.Margin = new Thickness(14, 0, 0, 0);
            var title = Text(row.Title ?? "Achievement", 14, true);
            title.MaxHeight = 40; title.TextTrimming = TextTrimming.CharacterEllipsis;
            var game = Text(entry.Game.Title, 12); game.Margin = new Thickness(0, 4, 0, 0);
            game.TextWrapping = TextWrapping.NoWrap; game.TextTrimming = TextTrimming.CharacterEllipsis;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title); text.Children.Add(game);
            var layout = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left); DockPanel.SetDock(rarity, Dock.Right);
            layout.Children.Add(icon); layout.Children.Add(rarity); layout.Children.Add(text);
            var card = new Border { Child = layout, Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
                Background = Color("#14231E", SystemColors.WindowBrush), BorderBrush = Color("#365B4A", SystemColors.ActiveBorderBrush), BorderThickness = new Thickness(1) };
            var button = new Button { Content = card, Template = EntryTemplate, Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = entry.Game.Title + Environment.NewLine + AchievementsInfoWindow.BuildAchievementGridToolTip(row) };
            AutomationProperties.SetName(button, "Rarest earned: " + row.Title + ", " + entry.Game.Title + ", " + percent + " global unlock rate. Open achievement details.");
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            button.Click += async (_, __) => await OpenAchievementAsync(entry.Game, row);
            return button;
        }
    }
}
