using System.Text.Json;
using PixelVaultNative;

namespace PixelVault.Tracking;

public static class CenturyInterviewCatalogue
{
    public const string TrackerId = "death-stranding-dc.interview-catalogue";
    sealed record Entry(string Id, bool Populated);
    static HashSet<string> ReadIds()
    {
        using var stream=typeof(CenturyInterviewCatalogue).Assembly.GetManifestResourceStream("PixelVault.Tracking.CenturyInterviews.json");
        var rows=JsonSerializer.Deserialize<Entry[]>(stream,AchievementTrackingCatalog.Json);
        if(rows?.Length!=158||rows.Count(r=>r.Populated)!=149||rows.Select(r=>r.Id).Distinct().Count()!=158)throw new InvalidDataException("Invalid interview catalogue.");
        return rows.Where(r=>r.Populated).Select(r=>r.Id).ToHashSet(StringComparer.Ordinal);
    }
    public static IReadOnlySet<string> ItemIds { get; }=ReadIds();
    public static bool Matches(AchievementTrackerDefinition d)=>d?.Id==TrackerId&&d.Provider=="steam"&&d.GameId=="1850570"
        &&d.Milestones?.Count==0&&d.CategoryTargets?.Count==0&&d.Items?.Count==149
        &&d.Items.All(i=>i!=null&&!i.Archived&&i.Target==1)&&ItemIds.SetEquals(d.Items.Select(i=>i.Id));
}
