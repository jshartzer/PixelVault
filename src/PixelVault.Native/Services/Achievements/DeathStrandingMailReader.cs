using System;
namespace PixelVaultNative
{
    static class DeathStrandingMailReader
    {
        internal const string TrackerId = "death-stranding-dc.the-post-guides-the-present";
        internal const string ItemId = "mails-read";
        internal static AchievementTrackerDefinition Definition() => new()
        {
            Id=TrackerId,Provider="steam",GameId="1850570",GameTitle="Death Stranding Director's Cut",AchievementId="038",
            Title="Emails — The Post Guides the Present",CompletionLabel="Emails read",
            Description="Read 100 mails. Automatic progress uses the saved mail-reading statistic; individual email checkboxes are separate.",
            Items=new(){new(){Id=ItemId,Name="Emails read",Category="Emails",Target=100,
                Detail="Saved statistic 38, confirmed by the 138 to 139 mail-reading control. Individual email identities are not inferred.",
                Identifiers=new(){["AchievementStatisticId"]="38"}}}
        };
        internal static AchievementTrackerSnapshot Read(byte[] data,string account,long observed,string source,string evidence) => new()
        {
            TrackerId=TrackerId,Provider="steam",GameId="1850570",Account=account,ObservedUtcTicks=observed,Source=source,
            Evidence=evidence+" Mail counter v1: complete typed achievement-statistic table validated; statistic 38 bounded by 240 populated installed mail resources. Per-email states are not inferred.",
            Items=new(){new(){ItemId=ItemId,Current=DeathStrandingInterviewReader.ReadStatistic(data,38,240),Availability=TrackerAvailability.Unknown}}
        };
    }
}
