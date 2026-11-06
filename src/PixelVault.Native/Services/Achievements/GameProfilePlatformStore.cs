using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    sealed class GameProfilePlatformChoice
    {
        public string Provider, Id, Title;
        public LibraryFolderInfo Folder;
        public XboxAchievementService.XboxTitle XboxTitle;
        public string Key => Provider + ":" + Id;
        public string Label => Provider == "steam" ? "Steam" : Provider == "retroachievements" ? "RetroAchievements" : XboxTitle?.Legacy == true ? "Xbox 360" : "Xbox";
        public string Display => Provider == "xbox" ? XboxTitle?.Display ?? "Xbox (unavailable)" : Label;
    }

    sealed class GameProfilePlatformStore
    {
        internal sealed class Preference
        {
            public string DefaultKey { get; set; }
            public string XboxTitleId { get; set; }
        }
        readonly string path;
        static readonly object Gate = new();
        internal GameProfilePlatformStore(string root) => path = Path.Combine(root, "game-profile-platforms.json");
        Dictionary<string, Preference> Read() => File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, Preference>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid game platform settings.") : new();
        internal Preference Get(string game) { lock (Gate) return Read().GetValueOrDefault(game) ?? new(); }
        internal void Save(string game, Preference preference)
        {
            if (string.IsNullOrWhiteSpace(game)) throw new ArgumentException("A stable profile identity is required.");
            lock (Gate)
            {
                using var lease = LocalStoreLease.AcquireAsync(path, default).GetAwaiter().GetResult();
                var data = Read(); data[game] = preference;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temp, JsonSerializer.Serialize(data)); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        internal static string NameKey(string name)
        {
            return PixelVault.Tracking.GameTitleIdentity.NameKey(name);
        }
        internal static IEnumerable<XboxAchievementService.XboxTitle> SearchXbox(IEnumerable<XboxAchievementService.XboxTitle> titles, string query) =>
            titles.Where(t => NameKey(t.Name).Contains(NameKey(query), StringComparison.OrdinalIgnoreCase));
        internal static List<GameProfilePlatformChoice> Choices(string name, IEnumerable<LibraryFolderInfo> folders,
            IEnumerable<XboxAchievementService.XboxTitle> xbox, Preference preference)
        {
            var choices = new List<GameProfilePlatformChoice>();
            var sourceFolders = folders.Where(f => f != null).ToArray();
            foreach (var folder in sourceFolders)
            {
                if (uint.TryParse(folder.SteamAppId, out var steam) && steam > 0)
                    choices.Add(new() { Provider = "steam", Id = steam.ToString(), Title = folder.Name, Folder = folder });
                if (uint.TryParse(folder.RetroAchievementsGameId, out var retro) && retro > 0)
                    choices.Add(new() { Provider = "retroachievements", Id = retro.ToString(), Title = folder.Name, Folder = folder });
            }
            var hints = sourceFolders.Select(f => PixelVault.Tracking.GameTitleIdentity.XboxLegacyHint(f.PlatformLabel, f.IgdbPlatforms))
                .Where(h => h.HasValue).Distinct().ToArray();
            var linked = preference.XboxTitleId ?? (preference.DefaultKey?.StartsWith("xbox:") == true ? preference.DefaultKey.Substring(5) : null);
            var matched = PixelVault.Tracking.GameTitleIdentity.MatchXbox(name, xbox, t => t.Id, t => t.Name, t => t.Legacy,
                hints.Length == 1 ? hints[0] : null, linked);
            foreach (var title in matched == null ? Array.Empty<XboxAchievementService.XboxTitle>() : new[] { matched })
                choices.Add(new() { Provider = "xbox", Id = title.Id, Title = title.Name, XboxTitle = title,
                    Folder = new LibraryFolderInfo { Name = title.Name, PlatformLabel = title.Legacy ? "Xbox 360" : "Xbox" } });
            return choices.GroupBy(c => c.Key).Select(g => g.First()).ToList();
        }
        internal static GameProfilePlatformChoice Default(List<GameProfilePlatformChoice> choices, Preference saved)
        {
            if (!string.IsNullOrEmpty(saved.DefaultKey)) return choices.FirstOrDefault(c => c.Key == saved.DefaultKey);
            return choices.FirstOrDefault(c => c.Provider == "steam") ?? choices.FirstOrDefault(c => c.Provider == "retroachievements")
                ?? (choices.Count == 1 ? choices[0] : null);
        }
    }
}
