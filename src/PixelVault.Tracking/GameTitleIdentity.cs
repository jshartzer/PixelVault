#nullable enable
namespace PixelVault.Tracking;

/// <summary>Shared native/web title aliases; named editions and remasters stay distinct.</summary>
public static class GameTitleIdentity
{
    public static string MatchKey(string name) => new(NameKey(name).Where(char.IsLetterOrDigit).ToArray());

    // Platform metadata can rule out a generation, but names alone cannot identify a remake.
    public static bool? XboxLegacyHint(string platform, string platforms)
    {
        if (string.Equals(platform, "Xbox 360", StringComparison.OrdinalIgnoreCase)) return true;
        var legacy = (platforms ?? "").Contains("Xbox 360", StringComparison.OrdinalIgnoreCase);
        var modern = (platforms ?? "").Contains("Xbox One", StringComparison.OrdinalIgnoreCase)
            || (platforms ?? "").Contains("Xbox Series", StringComparison.OrdinalIgnoreCase);
        return legacy == modern ? null : legacy;
    }

    public static T? MatchXbox<T>(string name, IEnumerable<T> titles, Func<T,string> id,
        Func<T,string> title, Func<T,bool> legacy, bool? legacyHint = null, string? linkedId = null) where T : class
    {
        var candidates = titles.Where(t => !string.IsNullOrWhiteSpace(linkedId) ? id(t) == linkedId
            : MatchKey(title(t)) == MatchKey(name) && (legacyHint is null || legacy(t) == legacyHint))
            .DistinctBy(id).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static string NameKey(string name)
    {
        var words=(name??"").Replace("™","").Replace("®","").ToUpperInvariant().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(words.Length>1)words[^1]=words[^1] switch
        {"II"=>"2","III"=>"3","IV"=>"4","V"=>"5","VI"=>"6","VII"=>"7","VIII"=>"8","IX"=>"9","X"=>"10",_=>words[^1]};
        return string.Join(" ",words);
    }
}
