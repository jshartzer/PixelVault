using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    // Explicit DSDataLibrarySystemResource.AllInterview order from the installed archive.
    // No prose is bundled. Unsupported layouts or disagreement with statistic 39 fail closed.
    static class DeathStrandingInterviewCatalogueReader
    {
        internal const string TrackerId = "death-stranding-dc.interview-catalogue";
        internal const int Offset = 0x260;
        internal sealed class Interview { public string Id { get; set; } public bool Populated { get; set; } }
        internal static readonly byte[] Prefix = Convert.FromHexString("AC083BB0005AA1181A4317662FB2000000C03FC984C874B5EAB79C03");
        static readonly byte[] EarlierPrefix = Convert.FromHexString("AC083BB00050A1181A4317662FB2000000C03FC984C874B5EAB79C03");
        internal static List<Interview> References()
        {
            using var stream = typeof(DeathStrandingInterviewCatalogueReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingInterviews.json");
            var rows = JsonSerializer.Deserialize<List<Interview>>(stream, AchievementTrackingCatalog.Json);
            if (rows?.Count != 158 || rows.Count(r => r.Populated) != 149 || rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != 158
                || rows.Any(r => !Guid.TryParse(r.Id, out var id) || id == Guid.Empty))
                throw new InvalidDataException("The ordered interview reference catalogue is incomplete.");
            return rows;
        }
        internal static AchievementTrackerDefinition Definition() => new()
        {
            Id = TrackerId, Provider = "steam", GameId = "1850570", GameTitle = "Death Stranding Director's Cut", Title = "Interview catalogue",
            Items = References().Where(r => r.Populated).Select((r, i) => new AchievementTrackerItem { Id = r.Id, Name = r.Id, Order = i }).ToList()
        };
        internal static bool Supports(AchievementTrackerDefinition definition) => definition?.Id == TrackerId && definition.Provider == "steam"
            && definition.GameId == "1850570" && definition.Milestones?.Count == 0 && definition.CategoryTargets?.Count == 0
            && definition.Items?.Count == 149 && definition.Items.All(i => i != null && !i.Archived && i.Target == 1)
            && References().Where(r => r.Populated).Select(r => r.Id).ToHashSet(StringComparer.Ordinal).SetEquals(definition.Items.Select(i => i.Id));

        internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
        {
            if (data == null || data.Length < Offset + 158 || data.Length > DeathStrandingSaveReader.MaxPayload
                || !(data.AsSpan().StartsWith(Prefix) || data.AsSpan().StartsWith(EarlierPrefix)))
                throw new NotSupportedException("The interview catalogue has an unsupported save layout. Saved progress was preserved.");
            var rows = References(); int read = 0, collected = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                byte flag = data[Offset + i];
                if (flag is not (0 or 0x0b or 0x0d) || !rows[i].Populated && flag != 0)
                    throw new NotSupportedException("The interview catalogue contains an unsupported state. Saved progress was preserved.");
                if (flag == 0x0d) read++;
                if (flag != 0) collected++;
            }
            if (read != DeathStrandingInterviewReader.ReadCount(data))
                throw new NotSupportedException("Interview states disagree with the saved achievement counter. Saved progress was preserved.");
            var result = new AchievementTrackerSnapshot { TrackerId = TrackerId, Provider = "steam", GameId = "1850570", Account = account,
                ObservedUtcTicks = observed, Source = source, Evidence = evidence + " Interview catalogue v1: supported initial layout, ordered 158-resource reference, nine empty resources and complete statistic 39 validated. States: 0x00 locked, 0x0B collected/unread, 0x0D read. "
                + collected + " collected, " + read + " read. Both official and Century interview progress require reading; unread entries remain incomplete." };
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Populated) result.Items.Add(new AchievementTrackerProgress { ItemId = rows[i].Id,
                    Current = data[Offset + i] == 0x0d ? 1 : 0, Availability = data[Offset + i] == 0 ? TrackerAvailability.Locked : TrackerAvailability.Available });
            return result;
        }
    }
}
