using System;
using System.Globalization;

namespace PixelVaultNative
{
    sealed class AchievementScoreTotals
    {
        public long Earned;
        public long EarnedThisMonth;
        public int ProvisionalAwards;
    }

    static class AchievementScoring
    {
        internal const int RuleVersion = 1;

        internal static DateTime MonthStartUtc(DateTime nowUtc, TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
            return TimeZoneInfo.ConvertTimeToUtc(new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), zone);
        }

        internal static int Points(double? percent)
        {
            if (!AchievementCollectionStats.ValidPercent(percent)) return 5;
            if (percent < .01) return 50;
            if (percent <= .1) return 45;
            if (percent <= 1) return 40;
            if (percent <= 2) return 35;
            if (percent <= 5) return 30;
            if (percent <= 10) return 25;
            if (percent <= 25) return 20;
            if (percent <= 50) return 15;
            if (percent <= 75) return 10;
            return 5;
        }

        // A game's available total uses the frozen award for earned achievements, plus current locked values.
        internal static int Value(GameAchievementsFetchService.AchievementRow row) => row.AwardedPoints ?? row.Points ?? 0;
        internal static bool Provisional(GameAchievementsFetchService.AchievementRow row) =>
            row.AwardedPoints.HasValue ? row.AwardProvisional : row.PointsProvisional;
        internal static string PointText(GameAchievementsFetchService.AchievementRow row) => row?.Points == null ? "— PV pts" :
            Value(row).ToString(CultureInfo.CurrentCulture) + " PV pts" + (Provisional(row) ? "*" : "");
        internal static string Label(GameAchievementsFetchService.AchievementRow row) => row?.Points == null ? "" :
            Value(row).ToString(CultureInfo.CurrentCulture) + " PV pts"
            + (Provisional(row) ? " (provisional)" : row.AwardedPoints.HasValue ? " earned" : "");
        internal static string Explanation(GameAchievementsFetchService.AchievementRow row) => row?.Points == null ? "" :
            Label(row) + ". " + (Provisional(row)
                ? "Rarity unavailable; starts at 5 points and finalizes when rarity becomes available."
                : row.AwardedPoints.HasValue ? "Awarded points are saved and do not change with later rarity updates."
                : "Based on the global unlock rate; points are saved when earned.");
    }
}
