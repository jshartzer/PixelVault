using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    /// <summary>
    /// Games pinned to the top of the Achievement Hub, by provider game key ("steam:123"). Durable user choices kept apart from
    /// the progress cache and from library Favorites; not account-scoped because they refer to library games.
    /// </summary>
    sealed class AchievementHubPins
    {
        sealed class FileData
        {
            public int Version { get; set; } = 1;
            public List<string> Games { get; set; } = new();
        }
        static readonly Regex ValidKey = new(@"^(steam|retroachievements):[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant);
        readonly string path;
        readonly List<string> keys = new();
        internal AchievementHubPins(string path)
        {
            this.path = Path.GetFullPath(path);
            try
            {
                if (File.Exists(this.path) && new FileInfo(this.path).Length <= 1024 * 1024)
                {
                    var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(this.path));
                    if (data?.Version == 1 && data.Games != null)
                        keys.AddRange(data.Games.Where(k => k != null && ValidKey.IsMatch(k)).Distinct(StringComparer.Ordinal));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { }
        }
        internal IReadOnlyList<string> Keys => keys;
        internal bool IsPinned(string key) => keys.Contains(key, StringComparer.Ordinal);
        /// <summary>Pins (appending) or unpins the game and saves. On a save failure the change is undone and the error rethrown.</summary>
        internal bool Toggle(string key)
        {
            if (key == null || !ValidKey.IsMatch(key)) throw new ArgumentException("Not a provider game key.", nameof(key));
            var index = keys.IndexOf(key);
            if (index >= 0) keys.RemoveAt(index); else keys.Add(key);
            try { Save(); }
            catch
            {
                if (index >= 0) keys.Insert(index, key); else keys.Remove(key);
                throw;
            }
            return index < 0;
        }
        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(new FileData { Games = keys.ToList() }));
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
