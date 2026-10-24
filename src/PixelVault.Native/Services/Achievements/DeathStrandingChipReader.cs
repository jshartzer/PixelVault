using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    // Supported Steam DC collector table. The entire installed 190-key catalogue
    // (56 chips plus 134 customization entries) and every record boundary must match.
    static class DeathStrandingChipReader
    {
        internal const int RecordCount = 190, Stride = 12;
        internal static bool Supports(string id) => id == AchievementTrackingCatalog.DeathStrandingChipsId;
        internal static uint[] References()
        {
            using var stream = typeof(DeathStrandingChipReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingCollectors.json");
            var keys = JsonSerializer.Deserialize<uint[]>(stream, AchievementTrackingCatalog.Json);
            if (keys?.Length != RecordCount || keys.Distinct().Count() != RecordCount || keys.Contains(0U) || !keys.SequenceEqual(keys.OrderBy(k => k)))
                throw new InvalidDataException("The bundled collector catalogue is incomplete.");
            return keys;
        }
        internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
        {
            if (data == null || data.Length < 7 + RecordCount * Stride || data.Length > DeathStrandingSaveReader.MaxPayload)
                throw new NotSupportedException("Unsupported memory-chip payload size.");
            var keys = References();
            var definition = AchievementTrackingCatalog.Builtins().Single(d => Supports(d.Id));
            var targets = definition.Items.ToDictionary(i => unchecked((uint)int.Parse(i.Identifiers["HashId"], CultureInfo.InvariantCulture)), i => i);
            if (targets.Count != 56 || !targets.Keys.All(keys.Contains)
                || definition.Items.Any(i => i.Identifiers["ResourceUUID"] != i.Id || i.Identifiers["Milestone"] != "Restored"))
                throw new InvalidDataException("The bundled chip identities are incomplete.");
            Dictionary<uint, ushort> found = null;
            // Fixed serialization prefix validated in all captured copies. 190 is
            // the compact entry count; the following field's meaning is unknown.
            ReadOnlySpan<byte> marker = stackalloc byte[] { 0x81, 0xbe, 0x00, 0x81, 0x60, 0x09, 0x01 };
            int search = 0, candidates = 0;
            while (search <= data.Length - marker.Length)
            {
                int relative = data.AsSpan(search).IndexOf(marker);
                if (relative < 0) break;
                int start = search + relative + marker.Length; search = start;
                if (++candidates > 128) throw new NotSupportedException("Too many collector-table candidates.");
                if (start > data.Length - RecordCount * Stride) continue;
                var values = new Dictionary<uint, ushort>(); bool valid = true;
                for (int index = 0; index < RecordCount; index++)
                {
                    var record = data.AsSpan(start + index * Stride, Stride);
                    uint key = BinaryPrimitives.ReadUInt32LittleEndian(record);
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(10));
                    if (key != keys[index] || BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(4)) != uint.MaxValue
                        || BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(8)) != index
                        || flags is not (0 or 8 or 9 or 25 or 27))
                    { valid = false; break; }
                    values.Add(key, flags);
                }
                if (!valid) continue;
                if (found != null) throw new NotSupportedException("The memory-chip table is ambiguous.");
                found = values;
            }
            if (found == null) throw new NotSupportedException("No complete supported 190-entry collector table was found. This game version needs a reader update.");
            var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = definition.Provider, GameId = definition.GameId,
                Account = account, ObservedUtcTicks = observed, Source = source,
                Evidence = evidence + " Chip reader v1: full installed 190-key collector catalogue, serialization prefix, sentinels and sequential indices verified; all 56 chip targets covered. Restoration mask 0x02; pickup (0x01) and intermediate 0x10 do not count. Availability is unknown." };
            foreach (var item in definition.Items)
            {
                uint hash = unchecked((uint)int.Parse(item.Identifiers["HashId"], CultureInfo.InvariantCulture));
                snapshot.Items.Add(new AchievementTrackerProgress { ItemId = item.Id, Current = (found[hash] & 2) != 0 ? 1 : 0, Availability = TrackerAvailability.Unknown });
            }
            return snapshot;
        }
    }
}
