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
            if (achievement != null && (!achievement.HasStableAchievementIdentity || achievement.Provider != definition.Provider || achievement.ProviderGameId != definition.GameId))
                throw new InvalidDataException("Choose an achievement from this checklist's game.");
            base.SetAchievementLink(trackerId, achievement?.ProviderAchievementId);
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
            foreach (var definition in PixelVault.Tracking.CenturyAchievementCatalog.All)
                if (!string.IsNullOrWhiteSpace(definition.Provider == "steam" ? steamAccount : retroAccount))
                    result.Add(AchievementsInfoWindow.CenturyPresentation(definition, Century(definition.Provider, definition.GameId, definition.Id,
                        definition.Provider == "steam" ? steamAccount : retroAccount)));
            return result;
        }

    }
}
