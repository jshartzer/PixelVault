using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    enum AchievementCollectionSort { Provider, RarestFirst, MostCommonFirst, NewestFirst, OldestFirst, ProgressHighestFirst, ProgressLowestFirst }

    sealed class AchievementCollectionStats
    {
        internal sealed class HistoryPoint { public DateTime DayUtc; public int Earned; }
        public int Total, Earned, Remaining, Unknown, DatedEarned, EarnedWithRarity;
        public int? Percent;
        public bool HasPoints;
        public long EarnedPoints, AvailablePoints;
        public int ProvisionalAwards;
        public GameAchievementsFetchService.AchievementRow Latest, Rarest;
        public List<HistoryPoint> History = new List<HistoryPoint>();

        internal static bool ValidPercent(double? value) => value.HasValue && !double.IsNaN(value.Value) && value >= 0 && value <= 100;
        internal static double? CounterPercent(GameAchievementsFetchService.AchievementRow row)
        {
            if (row?.ProgressKnown != true || !row.CounterCurrent.HasValue || !row.CounterMinimum.HasValue || !row.CounterTarget.HasValue)
                return null;
            var current = row.CounterCurrent.Value;
            var minimum = row.CounterMinimum.Value;
            var target = row.CounterTarget.Value;
            if (!double.IsFinite(current) || !double.IsFinite(minimum) || !double.IsFinite(target)
                || current < 0 || minimum < 0 || target <= minimum) return null;
            return Math.Clamp((current - minimum) / (target - minimum), 0, 1) * 100;
        }
        internal static bool ValidUnlock(GameAchievementsFetchService.AchievementRow row, long nowUtcTicks) =>
            row?.ProgressKnown == true && row.Unlocked && row.UnlockUtcTicks >= DateTime.UnixEpoch.Ticks
                && row.UnlockUtcTicks <= Math.Min(nowUtcTicks, DateTime.MaxValue.Ticks);

        internal static AchievementCollectionStats Build(IEnumerable<GameAchievementsFetchService.AchievementRow> rows, long nowUtcTicks,
            AchievementScoreTotals scoreTotals = null)
        {
            var all = (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Where(row => row != null).ToList();
            var earned = all.Where(row => row.ProgressKnown && row.Unlocked).ToList();
            var dated = earned.Where(row => ValidUnlock(row, nowUtcTicks)).ToList();
            var rarity = earned.Where(row => ValidPercent(row.GlobalUnlockPercent)).ToList();
            var stats = new AchievementCollectionStats
            {
                HasPoints = scoreTotals != null || all.Any(r => r.Points.HasValue),
                EarnedPoints = scoreTotals?.Earned ?? all.Sum(r => (long)(r.AwardedPoints ?? 0)),
                ProvisionalAwards = scoreTotals?.ProvisionalAwards ?? all.Count(r => r.AwardedPoints.HasValue && r.AwardProvisional),
                Total = all.Count, Earned = earned.Count, Remaining = all.Count(row => row.ProgressKnown && !row.Unlocked),
                Unknown = all.Count(row => !row.ProgressKnown), DatedEarned = dated.Count, EarnedWithRarity = rarity.Count,
                Latest = dated.OrderByDescending(row => row.UnlockUtcTicks).FirstOrDefault(),
                Rarest = rarity.OrderBy(row => row.GlobalUnlockPercent).FirstOrDefault()
            };
            stats.AvailablePoints = stats.EarnedPoints + all.Where(r => !r.AwardedPoints.HasValue).Sum(r => (long)(r.Points ?? 0));
            if (stats.Total > 0 && stats.Unknown == 0)
                stats.Percent = (int)Math.Round(stats.Earned * 100d / stats.Total, MidpointRounding.AwayFromZero);
            var cumulative = 0;
            foreach (var day in dated.GroupBy(row => new DateTime(row.UnlockUtcTicks, DateTimeKind.Utc).Date).OrderBy(day => day.Key))
            {
                cumulative += day.Count();
                stats.History.Add(new HistoryPoint { DayUtc = day.Key, Earned = cumulative });
            }
            return stats;
        }

        internal string ScoreSummary => EarnedPoints.ToString("N0") + " / " + AvailablePoints.ToString("N0") + " PV pts"
            + (ProvisionalAwards > 0 ? " · " + ProvisionalAwards + " provisional awards" : "");

        internal string Summary => (Percent.HasValue
            ? "\U0001F3C6 " + Earned + " of " + Total + " Achievements earned · " + Percent + "% · " + Remaining + " remaining"
            : "\U0001F3C6 " + Earned + " of " + Total + " Achievements earned · " + Remaining + " known remaining · " + Unknown + " progress unavailable")
            + (HasPoints ? " · " + ScoreSummary : "");

        internal static List<GameAchievementsFetchService.AchievementRow> Sort(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows, AchievementCollectionSort order, long nowUtcTicks)
        {
            var all = (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Where(row => row != null);
            if (order == AchievementCollectionSort.ProgressHighestFirst || order == AchievementCollectionSort.ProgressLowestFirst)
            {
                var ordered = all.OrderBy(row => !CounterPercent(row).HasValue);
                return (order == AchievementCollectionSort.ProgressHighestFirst
                    ? ordered.ThenByDescending(row => CounterPercent(row) ?? 0)
                    : ordered.ThenBy(row => CounterPercent(row) ?? 0)).ToList();
            }
            // LINQ's stable ordering keeps the incoming provider/default order for ties. Missing data is always last.
            if (order == AchievementCollectionSort.RarestFirst || order == AchievementCollectionSort.MostCommonFirst)
            {
                var ordered = all.OrderBy(row => !ValidPercent(row.GlobalUnlockPercent));
                return (order == AchievementCollectionSort.RarestFirst
                    ? ordered.ThenBy(row => ValidPercent(row.GlobalUnlockPercent) ? row.GlobalUnlockPercent.Value : 0)
                    : ordered.ThenByDescending(row => ValidPercent(row.GlobalUnlockPercent) ? row.GlobalUnlockPercent.Value : 0)).ToList();
            }
            if (order == AchievementCollectionSort.NewestFirst || order == AchievementCollectionSort.OldestFirst)
            {
                var ordered = all.OrderBy(row => !ValidUnlock(row, nowUtcTicks));
                return (order == AchievementCollectionSort.NewestFirst
                    ? ordered.ThenByDescending(row => ValidUnlock(row, nowUtcTicks) ? row.UnlockUtcTicks : 0)
                    : ordered.ThenBy(row => ValidUnlock(row, nowUtcTicks) ? row.UnlockUtcTicks : 0)).ToList();
            }
            return all.ToList();
        }
    }
}
