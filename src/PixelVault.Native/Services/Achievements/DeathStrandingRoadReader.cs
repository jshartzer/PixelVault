using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    // Road identity comes from installed delivery point -> locator -> geometry -> StateLinkBooleanFact.
    // A restored road counts regardless of who contributed. This does not report ownership.
    static class DeathStrandingRoadReader
    {
        internal const string TrackerId = "death-stranding-dc.roads";
        internal sealed class Road
        {
            public string Title { get; set; }
            public string FactId { get; set; }
            public int LocatorId { get; set; }
            public int RoadId { get; set; }
        }
        internal static List<Road> References()
        {
            using var stream = typeof(DeathStrandingRoadReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingRoads.json");
            var roads = JsonSerializer.Deserialize<List<Road>>(stream, AchievementTrackingCatalog.Json);
            var expected = Enumerable.Range(1, 18).Select(i => "UC Route 23-" + i).Concat(Enumerable.Range(1, 33).Select(i => "UC Route 41-" + i)).ToHashSet(StringComparer.Ordinal);
            if (roads?.Count != 51 || !expected.SetEquals(roads.Select(r => r.Title)) || roads.Select(r => r.FactId).Distinct(StringComparer.Ordinal).Count() != 51
                || roads.Any(r => !Guid.TryParse(r.FactId, out var id) || id == Guid.Empty) || roads.Select(r => r.LocatorId).Distinct().Count() != 51 || roads.Select(r => r.RoadId).Distinct().Count() != 51)
                throw new InvalidDataException("The installed road reference catalogue is incomplete.");
            return roads;
        }
        internal static AchievementTrackerDefinition Definition() => new()
        {
            Id = TrackerId, Provider = "steam", GameId = "1850570", GameTitle = "Death Stranding Director's Cut", Title = "Roads", CompletionLabel = "Restored",
            Items = References().Select((r, i) => new AchievementTrackerItem { Id = r.FactId, Name = r.Title, Target = 1, Order = i }).ToList()
        };
        static string Route(AchievementTrackerItem item)
        {
            if (item == null) return null;
            // Only a complete route name, optionally followed by the checklist's descriptive parenthesis.
            var match = Regex.Match(item.Name ?? "", @"^(UC Route (?:23|41)-[1-9][0-9]*)(?:\s*\([^\r\n]*\))?\s*$", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }
        internal static bool Supports(AchievementTrackerDefinition definition)
        {
            if (definition?.Provider != "steam" || definition.GameId != "1850570" || definition.Items == null || definition.Milestones?.Count != 0) return false;
            var active = definition.Items.Where(i => !i.Archived).ToList();
            return active.Count == 51 && active.All(i => i.Target == 1) && active.Select(Route).Distinct(StringComparer.Ordinal).Count() == 51
                && References().Select(r => r.Title).ToHashSet(StringComparer.Ordinal).SetEquals(active.Select(Route));
        }
        internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
        {
            var (facts, count) = DeathStrandingWorldReader.ReadFacts(data);
            var result = new AchievementTrackerSnapshot { TrackerId = TrackerId, Provider = "steam", GameId = "1850570", Account = account, ObservedUtcTicks = observed, Source = source,
                Evidence = evidence + " Road reader v1: complete counted persistent boolean map (" + count + " unique records), extracted false defaults and all 51 geometry-linked highway identities validated. Restored world roads count regardless of builder; personal ownership is not inferred." };
            foreach (var road in References()) result.Items.Add(new AchievementTrackerProgress { ItemId = road.FactId, Current = facts.GetValueOrDefault(Guid.Parse(road.FactId)) ? 1 : 0, Availability = TrackerAvailability.Unknown });
            return result;
        }
        internal static AchievementTrackerSnapshot Project(AchievementTrackerSnapshot canonical, AchievementTrackerDefinition definition)
        {
            if (!Supports(definition) || canonical?.TrackerId != TrackerId || canonical.Provider != "steam" || canonical.GameId != "1850570" || canonical.Items?.Count != 51)
                throw new InvalidDataException("The road observation does not match the complete road checklist.");
            var refs = References(); var values = new Dictionary<string, AchievementTrackerProgress>(StringComparer.Ordinal);
            foreach (var row in canonical.Items)
                if (row == null || !refs.Any(r => r.FactId == row.ItemId) || row.Current is not (0 or 1) || !values.TryAdd(row.ItemId, row))
                    throw new InvalidDataException("The road observation contains unsupported identities or values.");
            var result = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = canonical.Provider, GameId = canonical.GameId, Account = canonical.Account,
                Source = canonical.Source, Evidence = canonical.Evidence, ObservedUtcTicks = canonical.ObservedUtcTicks };
            foreach (var item in definition.Items)
            {
                var reference = item.Archived ? null : refs.Single(r => r.Title == Route(item));
                result.Items.Add(new AchievementTrackerProgress { ItemId = item.Id, Current = reference == null ? null : values[reference.FactId].Current, Availability = TrackerAvailability.Unknown });
            }
            return result;
        }
    }
}
