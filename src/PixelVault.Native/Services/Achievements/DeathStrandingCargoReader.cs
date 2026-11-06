using PixelVault.Tracking;

namespace PixelVaultNative;

/// <summary>Whole kilograms from statistic 49, corroborated against delivery records and the game/Steam totals.</summary>
static class DeathStrandingCargoReader
{
    internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
    {
        int kilograms = DeathStrandingInterviewReader.ReadStatistic(data, CenturyCargoGoal.StatisticId, CenturyCargoGoal.MaximumKilograms);
        return new() { TrackerId = CenturyCargoGoal.TrackerId, Provider = "steam", GameId = "1850570", Account = account,
            ObservedUtcTicks = observed, Source = source,
            Evidence = evidence + " Cargo reader v1: complete typed achievement-statistic table validated; statistic 49 is cumulative whole kilograms delivered. Fractional kilograms are not rounded up.",
            Items = new() { new() { ItemId = CenturyCargoGoal.ItemId, Current = kilograms, Availability = TrackerAvailability.Unknown } } };
    }
}
