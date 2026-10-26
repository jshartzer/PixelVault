using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    /// <summary>Achievement statistic 39, corroborated by the user's 66 -> 67 interview-reading control.</summary>
    static class DeathStrandingInterviewReader
    {
        internal const string TrackerId = "death-stranding-dc.the-past-guides-the-present";
        internal const string ItemId = "interviews-read";
        internal const int CatalogueCount = 158, Stride = 13;
        internal static bool Supports(string id) => id == TrackerId;
        internal static AchievementTrackerDefinition Definition() => new()
        {
            Id = TrackerId, Provider = "steam", GameId = "1850570", GameTitle = "Death Stranding Director's Cut", AchievementId = "039",
            Title = "Interviews — The Past Guides the Present", CompletionLabel = "Interviews read",
            Description = "Read 100 interviews. Progress uses the game's achievement counter; individual interview read states have not been validated.",
            Items = new() { new() { Id = ItemId, Name = "Interviews read", Category = "Interviews", Target = 100,
                Detail = "Open interviews in the game's Data menu and read them. This total comes from the saved achievement statistic, not from guessing which titles are complete. The achievement requires 100 interviews; the installed catalogue contains 158 interview resources.",
                Identifiers = new() { ["AchievementStatisticId"] = "39", ["CatalogueCount"] = "158" } } }
        };
        internal static Dictionary<uint, byte> References()
        {
            using var stream = typeof(DeathStrandingInterviewReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingAchievementCounters.json");
            var refs = JsonSerializer.Deserialize<Dictionary<uint, byte>>(stream);
            if (refs?.Count != 49 || refs.GetValueOrDefault(39u) != 2 || refs.Values.Any(t => t is not (1 or 2)))
                throw new InvalidDataException("The achievement-statistic reference is incomplete.");
            return refs;
        }
        internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
        {
            if (data == null || data.Length < 10 + 47 * Stride || data.Length > DeathStrandingSaveReader.MaxPayload)
                throw new NotSupportedException("Unsupported interview-counter payload size.");
            var reference = References();int? found = null;int search = 0, candidates = 0;
            ReadOnlySpan<byte> prefix = stackalloc byte[] { 0x87, 0x9c, 0xa0, 0x1f, 0x13, 0x1c, 0x7e, 0xce, 0x01 };
            while (search <= data.Length - prefix.Length)
            {
                int relative = data.AsSpan(search).IndexOf(prefix);if (relative < 0) break;
                int start = search + relative + prefix.Length;search = start;
                if (++candidates > 128) throw new NotSupportedException("Too many achievement-statistic candidates.");
                if (start >= data.Length) continue;int count = data[start++];
                if (count < 47 || count > 49 || start > data.Length - count * Stride) continue;
                var seen = new HashSet<uint>();bool valid = true;ulong read = ulong.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    var row = data.AsSpan(start + i * Stride, Stride);uint id = BinaryPrimitives.ReadUInt32LittleEndian(row);byte type = row[4];ulong value = BinaryPrimitives.ReadUInt64LittleEndian(row.Slice(5));
                    if (!reference.TryGetValue(id, out var expected) || type != expected || !seen.Add(id) || (type == 1 && value > 1)) { valid = false;break; }
                    if (id == 39) read = value;
                }
                // Statistic IDs 7/8 are absent in the earlier validated saves. Every other reference is required.
                if (!valid || reference.Keys.Any(id => id is not (7 or 8) && !seen.Contains(id)) || read > CatalogueCount) continue;
                if (found.HasValue) throw new NotSupportedException("The interview achievement-counter table is ambiguous.");
                found = (int)read;
            }
            if (!found.HasValue) throw new NotSupportedException("No complete supported achievement-statistic table was found. Saved interview progress was preserved.");
            return new() { TrackerId = TrackerId, Provider = "steam", GameId = "1850570", Account = account, ObservedUtcTicks = observed, Source = source,
                Evidence = evidence + " Interview reader v1: complete 47–49-entry typed achievement-statistic table validated, including all required identities and bounded statistic 39. The user's one-interview control changes 66 to 67. Per-title read states are not inferred.",
                Items = new() { new() { ItemId = ItemId, Current = found.Value, Availability = TrackerAvailability.Unknown } } };
        }
    }
}
