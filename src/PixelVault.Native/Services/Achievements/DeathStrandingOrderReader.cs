using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    // Supported Steam DC variant: signed compact int16 count (1140), then 96-byte
    // mission records. The 16-byte record prefix belongs to this entry, not the
    // preceding mission. Mission ID is +16; earned-rank mask is +88.
    static class DeathStrandingOrderReader
    {
        internal const int RecordCount = 1140, Stride = 96;
        internal static bool Supports(string id) => id == AchievementTrackingCatalog.DeathStrandingOrdersId;

        internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
        {
            if (data == null || data.Length < 3 + RecordCount * Stride || data.Length > DeathStrandingSaveReader.MaxPayload)
                throw new NotSupportedException("The save does not contain the supported premium-order table.");
            var definition = AchievementTrackingCatalog.Builtins().Single(d => Supports(d.Id));
            var targets = definition.Items.GroupBy(i => i.GroupId).ToDictionary(g => ulong.Parse(g.Key, CultureInfo.InvariantCulture), g => g.ToList());
            if (targets.Count != 540 || definition.Items.Count != 1080 || targets.Any(p => p.Key == 0 || p.Value.Count != 2
                || !p.Value.Select(i => i.Milestone).ToHashSet().SetEquals(new[] { "LL", "LLL" })
                || p.Value.Any(i => i.Id != p.Key.ToString(CultureInfo.InvariantCulture) + ":" + i.Milestone
                    || i.Identifiers.GetValueOrDefault("MissionId") != p.Key.ToString(CultureInfo.InvariantCulture))))
                throw new InvalidDataException("The bundled premium-order identities are incomplete.");

            Dictionary<ulong, ulong> found = null;
            int search = 0, candidates = 0;
            ReadOnlySpan<byte> marker = stackalloc byte[] { 0x81, 0x74, 0x04 };
            while (search <= data.Length - 3)
            {
                int relative = data.AsSpan(search).IndexOf(marker);
                if (relative < 0) break;
                int start = search + relative + 3; search = start;
                if (start > data.Length - RecordCount * Stride) continue;
                if (++candidates > 128) throw new NotSupportedException("Too many premium-order table candidates.");
                var ids = new HashSet<ulong>(); var ranks = new Dictionary<ulong, ulong>(); bool valid = true;
                for (int index = 0; index < RecordCount; index++)
                {
                    var record = data.AsSpan(start + index * Stride, Stride);
                    ulong id = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(16, 8));
                    ulong rank = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(88, 8));
                    // Reserved layout bytes and all mission/rank fields are checked,
                    // including nonstandard and empty slots. Delivery counters are
                    // ushort fields and can grow beyond the research probe's <8 hint.
                    if (record[1] != 0 || record[3] != 0 || record[0] is not (1 or 2 or 8) || record[2] is not (1 or 2 or 4)
                        || record[24] > 7 || record[25] != 0xff || record[30] != 0 || record[31] != 0
                        || id >= 10000000 || (rank & ~0x1ffUL) != 0 || (id == 0 ? rank != 0 : !ids.Add(id)))
                    { valid = false; break; }
                    if (targets.ContainsKey(id)) ranks.Add(id, rank);
                }
                if (!valid || ranks.Count != targets.Count) continue;
                if (found != null) throw new NotSupportedException("The save contains ambiguous premium-order tables.");
                found = ranks;
            }
            if (found == null) throw new NotSupportedException("The save has no complete supported 1140-record mission table. This game version needs a reader update.");
            var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = definition.Provider, GameId = definition.GameId,
                Account = account, ObservedUtcTicks = observed, Source = source,
                Evidence = evidence + " Order reader v1: one compact-counted 1140 × 96-byte mission table; bounded records, unique nonzero mission IDs and all 540 standard orders verified. Earned LL bit 7 and LLL bit 8 are independent; availability is unknown." };
            foreach (var item in definition.Items)
            {
                ulong mask = found[ulong.Parse(item.GroupId, CultureInfo.InvariantCulture)];
                int bit = item.Milestone == "LL" ? 7 : 8;
                snapshot.Items.Add(new AchievementTrackerProgress { ItemId = item.Id, Current = (mask & (1UL << bit)) != 0 ? 1 : 0, Availability = TrackerAvailability.Unknown });
            }
            return snapshot;
        }
    }
}
