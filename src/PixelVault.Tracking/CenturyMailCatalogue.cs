using System.Text.Json;

namespace PixelVault.Tracking;

/// <summary>The complete authored email catalogue, including its presently unmapped entry.</summary>
public static class CenturyMailCatalogue
{
    public const string TrackerId = "death-stranding-dc.email-catalogue";
    public const int Total = 240;
    sealed class Slot { public string Id { get; set; } public bool Populated { get; set; } }
    sealed class Reference { public List<Slot> Slots { get; set; } public string UnmappedId { get; set; } }
    static readonly Lazy<(HashSet<string> Ids, string Unmapped)> ReferenceIds = new(() =>
    {
        using var stream = typeof(CenturyMailCatalogue).Assembly.GetManifestResourceStream("PixelVault.Tracking.CenturyMails.json");
        var reference = JsonSerializer.Deserialize<Reference>(stream);
        var slots = reference?.Slots;
        if (slots?.Count != 242 || slots.Count(m => m.Populated) != 239
            || slots.Where(m => m.Populated).Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != 239
            || slots.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != 240
            || slots.Any(m => !Guid.TryParse(m.Id, out var id) || id == Guid.Empty)
            || !Guid.TryParse(reference.UnmappedId, out var missing) || missing == Guid.Empty
            || slots.Any(m => m.Id == reference.UnmappedId))
            throw new InvalidDataException("The complete Century email catalogue is invalid.");
        return (slots.Where(m => m.Populated).Select(m => m.Id).Append(reference.UnmappedId).ToHashSet(StringComparer.Ordinal), reference.UnmappedId);
    });
    public static IReadOnlySet<string> ItemIds => ReferenceIds.Value.Ids;
    public static string UnmappedId => ReferenceIds.Value.Unmapped;

    // These Mountaineer messages depend on how long Sam takes to complete the urgent delivery.
    // Keep them visible in the catalogue, but don't require every timing outcome to reach 100%.
    public static bool IsMissable(string id) => id is "c923a3b7-eeac-7a4f-b183-20b89aafd199"
        or "7061a568-77ec-844f-b705-f4a7d607ced1" or "090e54e5-e35e-ca46-9513-b9d64cb05031"
        or "364094dd-5064-5c40-853c-926a48eb2d02";
    public static bool IsExcludedFromTotal(string id) => IsMissable(id);
}
