using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative;

static class DeathStrandingFacilityReader
{
    internal const string TrackerId = "death-stranding-dc.best-beloved";
    internal const int FacilityCount = 36;
    internal sealed record Household(uint HouseholdNameHash, string ItemId);

    static readonly Lazy<Household[]> Catalogue = new(() =>
    {
        using var stream = typeof(DeathStrandingFacilityReader).Assembly
            .GetManifestResourceStream("PixelVault.Tracking.DeathStrandingFacilities.json");
        var households = JsonSerializer.Deserialize<Household[]>(stream, AchievementTrackingCatalog.Json);
        if (households?.Length != 73 || households.Select(h => h.HouseholdNameHash).Distinct().Count() != 73
            || households.Where(h => h.ItemId != null).Select(h => h.ItemId).Distinct(StringComparer.Ordinal).Count() != FacilityCount
            || households.Count(h => h.ItemId != null) != FacilityCount)
            throw new InvalidDataException("The household catalogue is incomplete.");
        return households;
    });

    internal static bool Supports(AchievementTrackerDefinition definition) => definition?.Id == TrackerId
        && definition.Provider == "steam" && definition.GameId == "1850570" && definition.Milestones?.Count == 0
        && definition.Items?.Count == FacilityCount && definition.Items.All(i => !i.Archived && i.Target == 5)
        && Catalogue.Value.Where(h => h.ItemId != null).Select(h => h.ItemId)
            .ToHashSet(StringComparer.Ordinal).SetEquals(definition.Items.Select(i => i.Id));

    internal static AchievementTrackerSnapshot Read(byte[] data, string account, long observed, string source, string evidence)
    {
        ReadOnlySpan<byte> header = [0x2a, 0xb6, 0xaf, 0xe2, 0x81, 0x10, 0xac, 0x4c, 0x23, 0x95, 0xd3, 0x59];
        ReadOnlySpan<byte> trailer = [0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        if (data == null || data.Length > DeathStrandingSaveReader.MaxPayload)
            throw Unsupported();
        var payload = data.AsSpan();
        int start = payload.IndexOf(header);
        if (start < 0 || payload[(start + 1)..].IndexOf(header) >= 0)
            throw Unsupported();
        var remaining = payload[(start + header.Length)..];
        var snapshot = new AchievementTrackerSnapshot
        {
            TrackerId = TrackerId, Provider = "steam", GameId = "1850570", Account = account,
            ObservedUtcTicks = observed, Source = source,
            Evidence = evidence + " Facility reader v1: complete 73-record household block validated; all 36 tracked levels read directly."
        };
        foreach (var household in Catalogue.Value)
        {
            if (remaining.Length < 16 || remaining[0] != 0x80
                || BinaryPrimitives.ReadUInt32LittleEndian(remaining[1..]) != household.HouseholdNameHash)
                throw Unsupported();
            int scoreWidth = remaining[5] switch { < 0x80 => 1, 0x81 => 3, 0x80 => 5, _ => throw Unsupported() };
            int levelOffset = 5 + scoreWidth;
            int length = levelOffset + 10;
            if (remaining.Length < length || remaining[levelOffset] > 5 || remaining[levelOffset + 1] > 1
                || BinaryPrimitives.ReadUInt64LittleEndian(remaining[(levelOffset + 2)..]) != 0)
                throw Unsupported();
            if (household.ItemId != null)
                snapshot.Items.Add(new() { ItemId = household.ItemId, Current = remaining[levelOffset] });
            remaining = remaining[length..];
        }
        if (!remaining.StartsWith(trailer)) throw Unsupported();
        return snapshot;
    }

    static NotSupportedException Unsupported() => new("The save does not contain one complete supported household block.");
}
