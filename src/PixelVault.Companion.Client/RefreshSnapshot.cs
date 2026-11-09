using System.Text.Json;

namespace PixelVault.Companion.Client;

// A replaceable snapshot, not a command or an achievement award. Consumers always read
// authoritative data through their existing account-scoped stores after invalidation.
public sealed record RefreshSnapshot
{
    public int Schema { get; init; } = 1;
    public string WorkspaceId { get; init; } = "";
    public string Epoch { get; init; } = "";
    public long Sequence { get; init; }
    public long ObservedUtc { get; init; }
    public Dictionary<string,string> Versions { get; init; } = new();
    public Dictionary<string,string> Trackers { get; init; } = new();
    public Dictionary<string,string> Achievements { get; init; } = new();
    public JsonElement Connection { get; init; }
    public string Id => Epoch + ":" + Sequence;

    public bool Changed(RefreshSnapshot? previous, string domain) => previous is null
        || previous.WorkspaceId != WorkspaceId || previous.Versions.GetValueOrDefault(domain) != Versions.GetValueOrDefault(domain);
}
