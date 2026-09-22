using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    // Account/provider/game keys contain no credentials. Persist baseline and delivered milestones together.
    internal sealed class AchievementNotifications
    {
        public sealed class Snapshot
        {
            public Dictionary<string, bool> Progress { get; set; } = new();
            public HashSet<int> Milestones { get; set; } = new();
            public HashSet<string> Announced { get; set; } = new();
        }
        public sealed class Notice
        {
            public string Title { get; set; }
            public string Message { get; set; }
            public string Provider { get; set; }
            public string Game { get; set; }
            public string Account { get; set; }
            public List<int> CrossedThresholds { get; set; } = new();
            public DateTime Utc { get; set; } = DateTime.UtcNow;
            public bool Read { get; set; }
        }
        public sealed class State
        {
            public bool Enabled { get; set; } = true;
            public bool IndividualUnlocks { get; set; }
            public int IntervalMinutes { get; set; } = 15;
            public List<int> Thresholds { get; set; } = new() { 25, 50, 75, 100 };
            public Dictionary<string, Snapshot> Snapshots { get; set; } = new();
            public List<Notice> History { get; set; } = new();
        }
        readonly string _path;
        public State Data { get; private set; } = new();
        public AchievementNotifications(string path)
        {
            _path = path;
            if (File.Exists(path))
            {
                try { Data = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new(); }
                catch (JsonException) { File.Copy(path, path + ".invalid-" + DateTime.UtcNow.Ticks); }
            }
            Data.Snapshots ??= new(); Data.History ??= new(); Data.Thresholds ??= new() { 25, 50, 75, 100 };
            Data.IntervalMinutes = Data.IntervalMinutes == 0 ? 0 : Math.Clamp(Data.IntervalMinutes, 5, 1440);
        }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(Data));
            File.Move(_path + ".tmp", _path, true);
        }
        public List<Notice> Observe(GameAchievementsFetchService.FetchResult result, string account)
        {
            var notices = new List<Notice>();
            if (!Data.Enabled || string.IsNullOrWhiteSpace(account) || result == null || result.IsError
                || !string.IsNullOrEmpty(result.ProgressErrorMessage) || result.Rows == null || result.Rows.Count == 0) return notices;
            var rows = result.Rows;
            if (rows.Any(r => r == null || !r.HasStableProviderIdentity || !r.ProgressKnown)) return notices;
            var first = rows[0];
            if (rows.Any(r => r.Provider != first.Provider || r.ProviderGameId != first.ProviderGameId)
                || rows.GroupBy(r => r.ProviderAchievementId).Any(g => g.Select(r => r.Unlocked).Distinct().Count() != 1)) return notices;
            var current = rows.GroupBy(r => r.ProviderAchievementId).ToDictionary(g => g.Key, g => g.First().Unlocked);
            var key = JsonSerializer.Serialize(new[] { first.Provider, account.Trim(), first.ProviderGameId });
            var percent = 100d * current.Count(p => p.Value) / current.Count;
            var thresholds = Data.Thresholds.Where(t => t > 0 && t <= 100).Append(100).Distinct().OrderBy(t => t).ToList();
            if (!Data.Snapshots.TryGetValue(key, out var previous))
            {
                Data.Snapshots[key] = new Snapshot { Progress = current,
                    Milestones = thresholds.Where(t => percent >= t).ToHashSet(),
                    Announced = current.Where(p => p.Value).Select(p => p.Key).ToHashSet() };
                return notices;
            }
            void Add(string message) => notices.Add(new Notice { Title = result.GameTitle ?? first.ProviderGameId,
                Message = message, Provider = first.Provider, Game = first.ProviderGameId, Account = account });
            previous.Progress ??= new(); previous.Milestones ??= new(); previous.Announced ??= new();
            var newUnlocks = rows.GroupBy(r => r.ProviderAchievementId).Select(g => g.First())
                .Where(r => r.Unlocked && previous.Progress.TryGetValue(r.ProviderAchievementId, out var was) && !was
                    && !previous.Announced.Contains(r.ProviderAchievementId)).ToList();
            // Catalog changes alone cannot prove a milestone crossing.
            if (previous.Progress.Keys.OrderBy(k => k).SequenceEqual(current.Keys.OrderBy(k => k)))
            {
                var before = 100d * previous.Progress.Count(p => p.Value) / previous.Progress.Count;
                var crossed = thresholds.Where(t => before < t && percent >= t && !previous.Milestones.Contains(t)).ToList();
                if (crossed.Count > 0)
                {
                    var highest = crossed.Last();
                    Add(highest == 100 ? "All achievements completed!" : highest + "% of achievements completed");
                    notices.Last().CrossedThresholds = crossed;
                }
            }
            if (Data.IndividualUnlocks)
                foreach (var row in newUnlocks) Add("Achievement unlocked: " + row.Title);
            previous.Milestones.UnionWith(thresholds.Where(t => percent >= t));
            previous.Announced.UnionWith(current.Where(p => p.Value).Select(p => p.Key));
            previous.Progress = current;
            Data.History.InsertRange(0, notices.AsEnumerable().Reverse());
            if (Data.History.Count > 500) Data.History.RemoveRange(500, Data.History.Count - 500);
            return notices;
        }
    }
}
