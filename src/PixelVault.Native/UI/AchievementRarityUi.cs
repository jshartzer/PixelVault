using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelVaultNative
{
    internal enum AchievementRarity { Common, Bronze, Silver, Gold, UberRare }

    internal static class AchievementRarityUi
    {
        internal static AchievementRarity Tier(double? percent)
        {
            if (!AchievementCollectionStats.ValidPercent(percent)) return AchievementRarity.Common;
            if (percent < .01) return AchievementRarity.UberRare;
            if (percent <= 1) return AchievementRarity.Gold;
            if (percent <= 5) return AchievementRarity.Silver;
            if (percent <= GameAchievementsFetchService.SteamRareAchievementMaxPercent) return AchievementRarity.Bronze;
            return AchievementRarity.Common;
        }

        internal static string Label(GameAchievementsFetchService.AchievementRow row)
        {
            var tier = Tier(row?.GlobalUnlockPercent);
            if (tier == AchievementRarity.UberRare) return "Uber rare (metallic red)";
            return tier == AchievementRarity.Common ? string.Empty : tier + " rarity";
        }

        // Static reflections keep earned medals bright without a per-card animation loop.
        internal static void Apply(Border card, Border icon, TextBlock title,
            GameAchievementsFetchService.AchievementRow row, bool list = false)
        {
            var tier = Tier(row?.GlobalUnlockPercent);
            if (tier == AchievementRarity.Common) return;
            bool earned = row.ProgressKnown && row.Unlocked;
            if (SystemParameters.HighContrast)
            {
                card.Background = SystemColors.WindowBrush;
                card.BorderBrush = SystemColors.WindowTextBrush;
                title.Foreground = SystemColors.WindowTextBrush;
                icon.BorderBrush = SystemColors.WindowTextBrush;
                return;
            }

            var colors = tier == AchievementRarity.UberRare
                ? new[] { "#7B4E54", "#E66773", "#FFD6DA", "#301B22", "#702F40", "#241B20" }
                : tier == AchievementRarity.Gold
                ? new[] { "#766640", "#E8BD59", "#FFF0AD", "#302818", "#5A4927", "#1D201D" }
                : tier == AchievementRarity.Silver
                ? new[] { "#63717F", "#B5C9DE", "#F1F7FF", "#202A35", "#435267", "#1B2026" }
                : new[] { "#785D4E", "#D69868", "#FFE0BC", "#2D211C", "#593B2A", "#211D1C" };
            var edge = earned ? Metal(colors[1], colors[2], colors[0]) : UiBrushHelper.FromHex(colors[0]);
            card.Background = earned ? Metal(colors[3], colors[4], colors[3])
                : UiBrushHelper.FromHex(colors[5]);
            card.BorderBrush = edge;
            card.BorderThickness = list ? new Thickness(3, 0, 0, 1) : new Thickness(1.5);
            icon.BorderBrush = edge;
            icon.BorderThickness = new Thickness(earned ? 2 : 1);
            title.Foreground = UiBrushHelper.FromHex(earned ? colors[2] : "#CCD0D2");
            if (earned)
            {
                var glow = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString(colors[1]),
                    BlurRadius = 9, ShadowDepth = 0, Opacity = .45
                };
                glow.Freeze();
                icon.Effect = glow;
            }
        }

        static Brush Metal(string dark, string light, string end)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(dark), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(light), .38));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(dark), .55));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(end), 1));
            brush.Freeze();
            return brush;
        }
    }
}
