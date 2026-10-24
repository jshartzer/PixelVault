using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    // Current Steam DC variant: uint32 count, unique (GUID, byte boolean) records,
    // followed by two empty typed-map counts. Unsupported variants fail closed.
    static class DeathStrandingWorldReader
    {
        internal sealed class Fact
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool Persistent { get; set; }
            public bool Default { get; set; }
        }
        internal static List<Fact> References()
        {
            using var stream = typeof(DeathStrandingWorldReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingFacts.json");
            var facts = JsonSerializer.Deserialize<List<Fact>>(stream, AchievementTrackingCatalog.Json);
            if (facts?.Count != 26 || facts.Select(f => f.Id).Distinct().Count() != 26
                || facts.Any(f => !Guid.TryParse(f.Id, out _) || !f.Persistent || f.Default))
                throw new InvalidDataException("The bundled persistent-fact defaults are incomplete.");
            return facts;
        }
        internal static bool Supports(string id) => id == AchievementTrackingCatalog.DeathStrandingBuildId || id == AchievementTrackingCatalog.DeathStrandingUpgradeId;

        internal static AchievementTrackerSnapshot Read(byte[] data, string trackerId, string account, long observed, string source, string evidence)
        {
            if (!Supports(trackerId)) throw new NotSupportedException("This save reader supports construction and maximum-upgrade milestones only.");
            if (data == null || data.Length < 32 || data.Length > DeathStrandingSaveReader.MaxPayload) throw new InvalidDataException("Unsupported persistent-fact payload size.");
            var facts = References(); var known = facts.Select(f => Guid.Parse(f.Id)).ToHashSet();
            var anchor = Guid.Parse(facts.Single(f => f.Name == "ldfp_builded_post").Id).ToByteArray();
            var first = data.AsSpan().IndexOf(anchor);
            if (first < 4 || data.AsSpan(first + 1).IndexOf(anchor) >= 0) throw new NotSupportedException("The save has no unique supported persistent-fact anchor.");
            Dictionary<Guid, bool> found = null; int foundCount = 0, attempts = 0;
            for (int start = first; start >= Math.Max(4, first - 100000 * 17); start -= 17)
            {
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start - 4, 4));
                long end = start + (long)count * 17;
                if (count < 26 || count > 100000 || end > data.Length - 8 || first >= end
                    || BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan((int)end, 8)) != 0) continue;
                if (++attempts > 64) throw new NotSupportedException("Too many persistent-fact table candidates.");
                var candidate = new Dictionary<Guid, bool>(); bool valid = true;
                for (int index = 0; index < count; index++)
                {
                    int pos = start + index * 17; var id = new Guid(data.AsSpan(pos, 16)); byte value = data[pos + 16];
                    if (id == Guid.Empty || value > 1 || !candidate.TryAdd(id, value == 1)) { valid = false; break; }
                }
                if (!valid || candidate.Keys.Count(known.Contains) < 15) continue;
                if (found != null) throw new NotSupportedException("The persistent-fact table is ambiguous.");
                found = candidate; foundCount = (int)count;
            }
            if (found == null) throw new NotSupportedException("The save does not contain the supported counted persistent-fact table.");
            var definition = AchievementTrackingCatalog.Builtins().Single(d => d.Id == trackerId);
            var result = new AchievementTrackerSnapshot { TrackerId = trackerId, Provider = "steam", GameId = "1850570", Account = account,
                ObservedUtcTicks = observed, Source = source, Evidence = evidence + " Counted persistent boolean map: " + foundCount
                + " unique GUID records, binary values and bounded empty-map markers verified. Missing trophy facts use extracted persistent default false; historical construction/maximum-upgrade credit only." };
            foreach (var item in definition.Items)
            {
                var reference = facts.SingleOrDefault(f => f.Id == item.Id && f.Name == item.Identifiers["FactName"]);
                if (reference == null) throw new NotSupportedException("The tracker no longer matches the persistent trophy-fact catalogue.");
                result.Items.Add(new AchievementTrackerProgress { ItemId = item.Id, Current = found.GetValueOrDefault(Guid.Parse(item.Id), reference.Default) ? 1 : 0,
                    Availability = TrackerAvailability.Unknown });
            }
            return result;
        }
    }
}
