using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    // Installed MailResources order and the game's mail serializer, corroborated by two named read controls.
    // Notification, received state and opened state are independent. Padding bits are not interpreted.
    static class DeathStrandingMailCatalogueReader
    {
        internal const string TrackerId = "death-stranding-dc.email-catalogue";
        internal const int RecordCount = 512, RecordBytes = 20;
        internal const uint StartMarker = 0x16151fb1, EndMarker = 0x192d3a52;
        internal static readonly byte[] Signature = Convert.FromHexString("34B0EBA901");
        internal sealed class Mail { public string Id { get; set; } public bool Populated { get; set; } public bool Important { get; set; } public bool WaitIfUnread { get; set; } }
        internal sealed class Reference { public List<Mail> Slots { get; set; } public string UnmappedId { get; set; } }
        internal static Reference References()
        {
            using var stream = typeof(DeathStrandingMailCatalogueReader).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingMails.json");
            var reference = JsonSerializer.Deserialize<Reference>(stream, AchievementTrackingCatalog.Json);
            var slots = reference?.Slots;
            if (slots?.Count != 242 || slots.Count(m => m.Populated) != 239 || slots.Where(m => m.Populated).Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != 239
                || slots.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != 240 || slots.Any(m => !Guid.TryParse(m.Id, out var id) || id == Guid.Empty)
                || slots.Where(m => !m.Populated).Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != 1
                || !Guid.TryParse(reference.UnmappedId, out var missing) || missing == Guid.Empty || slots.Any(m => m.Id == reference.UnmappedId))
                throw new InvalidDataException("The installed mail reference catalogue is incomplete.");
            return reference;
        }
        internal static AchievementTrackerDefinition Definition()
        {
            var reference = References();
            return new() { Id = TrackerId, Provider = "steam", GameId = "1850570", GameTitle = "Death Stranding Director's Cut", Title = "Email catalogue",
                Items = reference.Slots.Where(m => m.Populated).Select(m => m.Id).Append(reference.UnmappedId)
                    .Select((id, i) => new AchievementTrackerItem { Id = id, Name = id, Order = i,
                        Missable = PixelVault.Tracking.CenturyMailCatalogue.IsMissable(id),
                        ExcludedFromTotal = PixelVault.Tracking.CenturyMailCatalogue.IsExcludedFromTotal(id) }).ToList() };
        }
        internal static bool Supports(AchievementTrackerDefinition definition) => definition?.Id == TrackerId && definition.Provider == "steam"
            && definition.GameId == "1850570" && definition.Milestones?.Count == 0 && definition.CategoryTargets?.Count == 0
            && definition.Items?.Count == 240 && definition.Items.All(i => i != null && !i.Archived && i.Target == 1)
            && Definition().Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal).SetEquals(definition.Items.Select(i => i.Id));
        static NotSupportedException Unsupported() => new("The mail state table has an unsupported or inconsistent format. Saved progress was preserved.");
        static int Variable(byte[] data, ref int position, int end)
        {
            if (position >= end) throw Unsupported();
            int value = unchecked((sbyte)data[position++]);
            int bytes = value == -128 ? 4 : value == -127 ? 2 : 0;
            if (bytes == 0) return value;
            if (position > end - bytes) throw Unsupported();
            value = bytes == 4 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position, 4)) : BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(position, 2));
            position += bytes; return value;
        }
        internal static int Section(byte[] data, int directory, int raw, uint marker)
        {
            int? found = null;
            for (int p = directory; p <= raw - 5; p++)
            {
                if (data[p] != 0x80 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 1, 4)) != marker) continue;
                int q = p + 5, offset = Variable(data, ref q, raw), version = Variable(data, ref q, raw);
                if (found.HasValue || version != 0 || offset < 0 || offset >= directory) throw Unsupported();
                found = offset;
            }
            return found ?? throw Unsupported();
        }
        internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence, bool verifyOpenedTotal = true)
        {
            if (data == null || data.Length < 16 || data.Length > DeathStrandingSaveReader.MaxPayload) throw Unsupported();
            int directory = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(data.Length - 8, 4));
            int raw = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(data.Length - 4, 4));
            if (directory <= 0 || raw <= directory || raw > data.Length - 8 || raw - directory > 65536) throw Unsupported();
            int start = Section(data, directory, raw, StartMarker), end = Section(data, directory, raw, EndMarker);
            if (start > directory - 17 - RecordCount * RecordBytes || end != start + 17 + RecordCount * RecordBytes
                || !data.AsSpan(start, Signature.Length).SequenceEqual(Signature)
                || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + 5, 4)) != RecordCount) throw Unsupported();
            var reference = References(); var lastIndex = reference.Slots.Select((m, i) => (m.Id, i)).GroupBy(x => x.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().i, StringComparer.Ordinal);
            var result = new AchievementTrackerSnapshot { TrackerId = TrackerId, Provider = "steam", GameId = "1850570", Account = account,
                ObservedUtcTicks = observed, Source = source };
            int read = 0, received = 0;
            for (int i = 0; i < RecordCount; i++)
            {
                int p = start + 9 + i * RecordBytes;
                int index = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 8, 2)), state = data[p + 15] & 3, flags = data[p + 16] & 15;
                bool opened = (flags & 4) != 0;
                if (state > 2 || opened && state != 2) throw Unsupported();
                if (i >= reference.Slots.Count)
                { if (index != ushort.MaxValue || state != 0 || opened) throw Unsupported(); continue; }
                var mail = reference.Slots[i];
                if (index != lastIndex[mail.Id] || ((flags & 2) != 0) != mail.Important || ((flags & 8) != 0) != mail.WaitIfUnread
                    || !mail.Populated && (state != 0 || opened)) throw Unsupported();
                if (!mail.Populated) continue;
                if (opened) read++; if (state == 2) received++;
                result.Items.Add(new AchievementTrackerProgress { ItemId = mail.Id, Current = opened ? 1 : 0,
                    Availability = state == 2 ? TrackerAvailability.Available : TrackerAvailability.Locked });
            }
            if (verifyOpenedTotal && read != DeathStrandingInterviewReader.ReadStatistic(data, 38, 240)) throw Unsupported();
            result.Items.Add(new AchievementTrackerProgress { ItemId = reference.UnmappedId, Current = null, Availability = TrackerAvailability.Unknown });
            result.Evidence = evidence + " Mail catalogue v1: bounded directory markers, supported 512-record layout, installed resource indices, duplicate empty aliases, meaningful flags validated. "
                + (verifyOpenedTotal ? "Complete statistic 38 validated. " : "")
                + received + " received; " + read + " opened. Notification markers do not establish reading. One catalogue title absent from the runtime resource order remains unknown.";
            return result;
        }
    }
}
