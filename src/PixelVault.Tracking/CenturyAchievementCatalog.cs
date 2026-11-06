namespace PixelVault.Tracking;

/// <summary>PV-authored goals, separate from provider achievements and personal progress.</summary>
public sealed record CenturyAchievementDefinition(string Id, string Provider, string GameId, string GameTitle,
    string Title, string Description, string Category, string Requirement, int Order)
{
    public int Points => CenturyAchievementCatalog.PointsPerAchievement;
}

public static class CenturyAchievementCatalog
{
    public const string GroupName = "Centurian Achievements";
    public const int PlannedPerGame = 5;
    public const int PointsPerAchievement = 100;
    public const string ConnectionStatus = "Awaiting save connection";
    public const string DeathStrandingRoadsId = "century.death-stranding-dc.all-roads";
    public const string DeathStrandingInterviewsId = "century.death-stranding-dc.all-interviews";
    public const string DeathStrandingMailsId = "century.death-stranding-dc.all-mails";
    public const string DeathStrandingLegendsId = "century.death-stranding-dc.all-lll-orders";
    public const string DeathStrandingCargoId = "century.death-stranding-dc.cargo-60000kg";

    static readonly CenturyAchievementDefinition[] Definitions =
    [
        new(DeathStrandingRoadsId, "steam", "1850570", "Death Stranding Director's Cut",
            "All Roads Lead Home", "Rebuild every road section in Death Stranding Director's Cut.",
            "Infrastructure",
            "Rebuild all 51 UC Route sections, including the Director's Cut mountain routes. Online contributions count.", 1),
        new(DeathStrandingInterviewsId, "steam", "1850570", "Death Stranding Director's Cut",
            "The Whole Story", "Read every interview in Death Stranding Director's Cut.", "Archives",
            "Read every entry in the interview catalogue. Unlocked unread interviews remain incomplete.", 2),
        new(DeathStrandingMailsId, "steam", "1850570", "Death Stranding Director's Cut",
            "Complete Correspondence", "Read every required email in Death Stranding Director's Cut.", "Archives",
            "Read every required entry in the email catalogue. Optional entries are not needed for completion.", 3),
        new(DeathStrandingLegendsId, "steam", "1850570", "Death Stranding Director's Cut",
            "Nothing Left to Deliver", "Earn Legend or higher on every standard order.", "Deliveries",
            "Earn a Legend or higher evaluation in a Premium Delivery for all 540 standard orders.", 4),
        new(DeathStrandingCargoId, "steam", "1850570", "Death Stranding Director's Cut",
            "The Weight of a Nation", "Deliver a total of 50,000 kg of cargo.", "Deliveries",
            "Deliver a cumulative total of 50,000 kg of cargo.", 5)
    ];

    public static IReadOnlyList<CenturyAchievementDefinition> ForGame(string provider, string gameId) =>
        Definitions.Where(d => d.Provider == provider && d.GameId == gameId).OrderBy(d => d.Order).ToArray();

    public static IReadOnlyList<CenturyAchievementDefinition> All => Array.AsReadOnly(Definitions);
    public static CenturyAchievementDefinition Find(string provider, string gameId, string id) =>
        Definitions.SingleOrDefault(d => d.Provider == provider && d.GameId == gameId && d.Id == id);
    public static string ArtworkUri(string id, bool unlocked) =>
        "pv-century://art/" + id + "/" + (unlocked ? id == DeathStrandingLegendsId ? "unlocked-v2" : "unlocked" : "locked");
    public static byte[] ArtworkFromUri(string value)
    {
        foreach (var definition in Definitions)
            foreach (var unlocked in new[] { false, true })
                if (value == ArtworkUri(definition.Id, unlocked)) return Artwork(definition.Id, unlocked);
        return null;
    }

    public static byte[] Artwork(string id, bool unlocked)
    {
        var name = id switch { DeathStrandingRoadsId => "all-roads", DeathStrandingInterviewsId => "all-interviews", DeathStrandingMailsId => "all-mails", DeathStrandingLegendsId => "all-lll-orders", DeathStrandingCargoId => "cargo-60000kg", _ => null };
        if (name == null) return null;
        using var stream = typeof(CenturyAchievementCatalog).Assembly.GetManifestResourceStream(
            "PixelVault.CenturyArtwork." + (unlocked ? name + "-unlocked" : "all-roads-locked") + ".png");
        if (stream == null) return null;
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
}
