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
        async Task SaveHubObservationAsync(GameAchievementsFetchService.FetchResult result, string account)
        {
            var first = result?.Rows?.FirstOrDefault();
            if (_achievementHubStore == null || first == null || !AchievementHubStore.CanStore(first.Provider, first.ProviderGameId, result)) return;
            try { await _achievementHubStore.SaveGameAsync(first.Provider, account, first.ProviderGameId, result, CancellationToken.None); }
            catch (Exception ex) { Log("Achievement hub snapshot could not be saved: " + ex.GetType().Name); }
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
            FrameworkElement Cover(AchievementHubGame game) => CreateAsyncImageTile(game.ArtPath, 420, 144, 210,
                Stretch.UniformToFill, game.Title, Brushes.White, new Thickness(0), new Thickness(0),
                Brushes.Transparent, new CornerRadius(8), Brushes.Transparent, new Thickness(0));
            void Open(Window hub, AchievementHubGame game) => AchievementsInfoWindow.ShowModal(hub,
                game.Provider == "steam" ? "Steam" : "Emulation", game.Folder, CurrentSteamWebApiKey(),
                CurrentRetroAchievementsApiKey(), CurrentSteamUserId64(), CurrentRetroAchievementsUsername(),
                "PixelVault/" + AppVersion, achievementGuideService);
            AchievementHubWindow.ShowFor(owner, new AchievementHubService(_achievementHubStore), Session, Cover, Open);
        }
    }
}
