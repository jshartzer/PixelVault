using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelVaultNative;

/// <summary>Optional, verified artwork replacements. Provider identities and progress never change.</summary>
public static class AchievementArtwork
{
    public sealed record Image(byte[] Bytes, string Sha256);
    sealed class Entry
    {
        public string Provider { get; set; }
        public string GameId { get; set; }
        public string AchievementId { get; set; }
        public string OriginalUrl { get; set; }
        public string Description { get; set; }
        public string File { get; set; }
        public string Sha256 { get; set; }
    }
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    static readonly Lazy<Entry[]> Bundled = new(() =>
    {
        using var stream = typeof(AchievementArtwork).Assembly.GetManifestResourceStream("PixelVault.Artwork.manifest.json");
        return JsonSerializer.Deserialize<Entry[]>(stream, Json) ?? Array.Empty<Entry>();
    });

    public static Image Find(string originalUrl, string dataRoot = null)
    {
        if (string.IsNullOrWhiteSpace(originalUrl)) return null;
        var century = PixelVault.Tracking.CenturyAchievementCatalog.ArtworkFromUri(originalUrl);
        if (century != null) return new(century, Convert.ToHexString(SHA256.HashData(century)).ToLowerInvariant());
        originalUrl = Canonical(originalUrl);
        // Imports override the bundled set, but corrupt/incomplete imports always fall back.
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            try
            {
                var root = Path.Combine(dataRoot, "achievement-artwork");
                var manifest = Path.Combine(root, "manifest.json");
                if (File.Exists(manifest) && new FileInfo(manifest).Length <= 2 * 1024 * 1024)
                {
                    var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllBytes(manifest), Json);
                    var entry = Unique(entries, originalUrl);
                    if (ValidEntry(entry))
                    {
                        var path = Path.Combine(root, entry.File);
                        if (File.Exists(path) && new FileInfo(path).Length <= 786432)
                        {
                            var image = Verify(File.ReadAllBytes(path), entry.Sha256);
                            if (image != null) return image;
                        }
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        var bundled = Unique(Bundled.Value, originalUrl);
        if (!ValidEntry(bundled)) return null;
        using var resource = typeof(AchievementArtwork).Assembly.GetManifestResourceStream("PixelVault.Artwork." + bundled.File);
        if (resource == null) return null;
        using var memory = new MemoryStream(); resource.CopyTo(memory);
        return Verify(memory.ToArray(), bundled.Sha256);
    }

    /// <summary>Finds an imported description for a Steam achievement whose Steam definition has none.</summary>
    public static string FindDescription(string provider, string gameId, string achievementId, string dataRoot = null)
    {
        if (!string.Equals(provider, "steam", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(achievementId) || string.IsNullOrWhiteSpace(dataRoot)) return null;
        try
        {
            var manifest = Path.Combine(Path.GetFullPath(dataRoot), "achievement-artwork", "manifest.json");
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > 2 * 1024 * 1024) return null;
            var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllBytes(manifest), Json);
            var matches = entries?.Where(e => e != null && e.Provider == "steam" && e.GameId == gameId
                && e.AchievementId == achievementId && !string.IsNullOrWhiteSpace(e.Description)).Take(2).ToArray();
            if (matches?.Length != 1) return null;
            var description = matches[0].Description.Trim();
            return description.Length <= 20000 ? description : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
    // Stored guides/pins may still reference an older Steam CDN for the same artwork.
    public static string Canonical(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort
            && new[] { "shared.fastly.steamstatic.com", "shared.akamai.steamstatic.com", "cdn.akamai.steamstatic.com", "cdn.cloudflare.steamstatic.com", "steamcdn-a.akamaihd.net", "media.steampowered.com" }.Contains(uri.Host)
            && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/(community_assets|steamcommunity/public)/images/apps/[0-9]+/[a-fA-F0-9]{40}\\.(jpg|png)$"))
            return "https://shared.fastly.steamstatic.com" + uri.AbsolutePath.Replace("/steamcommunity/public/", "/community_assets/", StringComparison.Ordinal);
        return value;
    }
    static Entry Unique(Entry[] entries, string url)
    {
        var matches = entries?.Where(e => e != null && Canonical(e.OriginalUrl) == url).Take(2).ToArray();
        return matches?.Length == 1 ? matches[0] : null;
    }
    static bool ValidEntry(Entry e) => e != null && e.Sha256?.Length == 64 && e.Sha256.All(Uri.IsHexDigit)
        && e.File == e.Sha256.ToLowerInvariant() + ".png";
    public static Image Verify(byte[] bytes, string sha)
    {
        if (bytes.Length < 33 || bytes.Length > 786432 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width <= 64 || height <= 64 || width > 2048 || height > 2048) return null;
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return actual.Equals(sha, StringComparison.OrdinalIgnoreCase) ? new(bytes, actual) : null;
    }
}
