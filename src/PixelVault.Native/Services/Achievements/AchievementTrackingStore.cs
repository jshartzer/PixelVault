using Microsoft.Data.Sqlite;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
namespace PixelVaultNative
{
    sealed partial class AchievementTrackingStore : TrackerRepository
    {
        internal GameMapStore Maps { get; }
        internal AchievementTrackingStore(string dataRoot) : base(dataRoot) { Maps = new GameMapStore(dataRoot); }
        protected override IReadOnlySet<string> GameFileAdapterIds => new TrackerRefreshService(this).GameFileAdapterIds;
        internal void SetAchievementLink(string trackerId, GameAchievementsFetchService.AchievementRow achievement)
        {
            var definition = Definition(trackerId);
            if (achievement != null && (!achievement.HasStableProviderIdentity || achievement.Provider != definition.Provider || achievement.ProviderGameId != definition.GameId))
                throw new InvalidDataException("Choose an achievement from this checklist's game.");
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO tracker_link(tracker_id,achievement_id) VALUES($t,$a) ON CONFLICT(tracker_id) DO UPDATE SET achievement_id=$a";
            cmd.Parameters.AddWithValue("$t", trackerId);
            cmd.Parameters.AddWithValue("$a", (object)achievement?.ProviderAchievementId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        internal IReadOnlyList<GameAchievementsFetchService.AchievementRow> AchievementChoices(string steamAccount, string retroAccount)
        {
            var hub = new AchievementHubStore(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)), "achievement-hub"));
            var result = new List<GameAchievementsFetchService.AchievementRow>();
            foreach (var identity in new[] { (Provider: "steam", Account: steamAccount), (Provider: "retroachievements", Account: retroAccount) })
            {
                if (string.IsNullOrWhiteSpace(identity.Account)) continue;
                var data = hub.LoadAsync(identity.Provider, identity.Account, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                result.AddRange(data.Games.Values.SelectMany(s => s.Rows).Where(r => r?.HasStableProviderIdentity == true));
            }
            return result;
        }

    }
}
