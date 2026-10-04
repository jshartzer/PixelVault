using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        AchievementHubStore _achievementHubStore;
        AchievementHubPins _achievementHubPins;
        async Task SaveHubObservationAsync(GameAchievementsFetchService.FetchResult result, string account)
        {
            var first = result?.Rows?.FirstOrDefault();
            if (_achievementHubStore == null || first == null || !AchievementHubStore.CanStore(first.Provider, first.ProviderGameId, result)) return;
            try { await _achievementHubStore.SaveGameAsync(first.Provider, account, first.ProviderGameId, result, CancellationToken.None); }
            catch (Exception ex) { Log("Achievement hub snapshot could not be saved: " + ex.GetType().Name); }
        }
        const int HubRarityBackfillPerCheck = 30;
        /// <summary>Background checks skip Steam rarity; fill it in a few games at a time with one public request each.</summary>
        async Task<int> BackfillHubRarityAsync(CancellationToken token)
        {
            var account = CurrentSteamUserId64();
            var key = CurrentSteamWebApiKey();
            if (_achievementHubStore == null || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(key)) return 0;
            var provider = GameAchievementsFetchService.SteamProvider;
            var pending = AchievementHubStore.GamesNeedingRarity(await _achievementHubStore.LoadAsync(provider, account, token), DateTime.UtcNow.Ticks);
            var filled = 0;
            foreach (var game in pending.Take(HubRarityBackfillPerCheck))
            {
                token.ThrowIfCancellationRequested();
                if (!int.TryParse(game, out var appId) || appId <= 0) continue;
                var percents = await GameAchievementsFetchService.TryLoadSteamGlobalAchievementPercentsAsync(appId, key.Trim(), "PixelVault/" + AppVersion, token);
                if (percents != null)
                {
                    try { await _achievementHubStore.ApplyRarityAsync(provider, account, game, percents, token); filled++; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log("Achievement hub rarity could not be saved: " + ex.GetType().Name); }
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            return filled;
        }
        void ShowAchievementHub(Window owner, LibraryBrowserWorkingSet ws)
        {
            _achievementHubStore ??= new AchievementHubStore(Path.Combine(dataRoot, "achievement-hub"));
            AchievementHubSession Session()
            {
                var session = new AchievementHubSession
                {
                    LibraryRoot = libraryRoot, SteamAccount = CurrentSteamUserId64(), SteamKey = CurrentSteamWebApiKey(),
                    RetroAccount = CurrentRetroAchievementsUsername(), RetroKey = CurrentRetroAchievementsApiKey()
                };
                // Raw library folders, independent of search, filters, and timeline/session projections.
                foreach (var view in BuildLibraryBrowserFolderViews(ws.Folders, "console"))
                {
                    var folder = ResolveLibraryBrowserAchievementLookupFolder(view);
                    if (!GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder?.PlatformLabel), folder, out var steam)) continue;
                    var id = steam ? folder.SteamAppId : folder.RetroAchievementsGameId;
                    if (!int.TryParse(id, out var numeric) || numeric <= 0) continue;
                    session.Games.Add(new AchievementHubGame
                    {
                        Provider = steam ? "steam" : "retroachievements", Id = numeric.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Title = view.Name, Folder = folder, ArtPath = GetLibraryArtPathForDisplayOnly(BuildLibraryBrowserDisplayFolder(view))
                    });
                }
                session.Games = session.Games.GroupBy(g => g.Key).Select(g => g.First()).ToList();
                return session;
            }
            FrameworkElement Cover(AchievementHubGame game, double width, double height) => CreateAsyncImageTile(game.ArtPath,
                (int)Math.Ceiling(width * 2.5), width, height,
                Stretch.UniformToFill, game.Title, Brushes.White, new Thickness(0), new Thickness(0),
                Brushes.Transparent, new CornerRadius(8), Brushes.Transparent, new Thickness(0));
            void Open(Window hub, AchievementHubGame game) => AchievementsInfoWindow.ShowModal(hub,
                game.Provider == "steam" ? "Steam" : "Emulation", game.Folder, CurrentSteamWebApiKey(),
                CurrentRetroAchievementsApiKey(), CurrentSteamUserId64(), CurrentRetroAchievementsUsername(),
                "PixelVault/" + AppVersion, achievementGuideService);
            _achievementHubPins ??= new AchievementHubPins(Path.Combine(dataRoot, "achievement-hub-pins.json"));
            AchievementHubWindow.ShowFor(owner, new AchievementHubService(_achievementHubStore), Session, Cover, Open, _achievementHubPins, achievementGuideService);
        }
    }
}
