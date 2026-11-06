using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    sealed class XboxAchievementService
    {
        internal sealed class XboxTitle
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool Legacy { get; set; }
            public long LastPlayedUtc { get; set; }
            public string Display => Name + (Legacy ? " (Xbox 360)" : "");
            public string LinkDisplay => Name + (Legacy ? " · Xbox 360" : " · Xbox") + " · Title ID " + Id;
        }
        internal sealed class Library
        {
            public string Xuid { get; set; }
            public List<XboxTitle> Titles { get; set; } = new();
        }
        internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelVault", "Xbox");
        static readonly SemaphoreSlim Gate = new(1, 1);
        static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNameCaseInsensitive = true };
        internal static string ApplicationDataRoot { get; private set; }
        internal static IAchievementGuideService Guides { get; private set; }
        readonly AchievementHubStore store;
        readonly Func<object, CancellationToken, Task<JsonDocument>> run;
        internal XboxAchievementService(AchievementHubStore store = null, Func<object, CancellationToken, Task<JsonDocument>> run = null)
        { this.store = store; this.run = run; }
        AchievementHubStore Store => store ?? new(Path.Combine(ApplicationDataRoot ?? Root, "achievement-hub"), Guides as IAchievementScoreStore);
        internal static void Configure(string root, IAchievementGuideService guides)
        {
            ApplicationDataRoot = root; Guides = guides;
            var service = new XboxAchievementService();
            var library = service.SavedLibrary();
            if (!string.IsNullOrWhiteSpace(library.Xuid)) SaveLibrary(library);
        }
        internal async Task ImportSavedAsync(CancellationToken token)
        {
            var library = SavedLibrary();
            if (string.IsNullOrWhiteSpace(library.Xuid)) return;
            var old = await new AchievementHubStore(Path.Combine(Root, "achievements")).LoadAsync("xbox", library.Xuid, token);
            foreach (var game in old.Games)
                await Store.SaveGameAsync("xbox", library.Xuid, game.Key, new() { Rows = game.Value.Rows, CapturedUtcTicks = game.Value.UpdatedUtc }, token);
        }
        internal bool Installed => File.Exists(Path.Combine(Root, "venv", "Scripts", "python.exe"));
        internal bool Connected => File.Exists(Path.Combine(Root, "tokens.dpapi"));

        async Task<JsonDocument> RunAsync(object request, CancellationToken token)
        {
            if (run != null) return await run(request, token);
            if (!Installed) throw new InvalidOperationException("Xbox support needs setup. Run scripts/Install-PixelVaultXbox.ps1 from the PixelVault source folder, then reopen this window.");
            await Gate.WaitAsync(token);
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Root, "venv", "Scripts", "python.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Xbox", "bridge.py"));
                using var process = Process.Start(start) ?? throw new IOException("Could not start Xbox support.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                var text = await output;
                await errors; // Drain diagnostics without logging authentication data.
                timeout.Token.ThrowIfCancellationRequested();
                JsonDocument document;
                try { document = JsonDocument.Parse(text); }
                catch (JsonException) { throw new IOException("Xbox support could not start. Run Install-PixelVaultXbox.ps1 to repair it."); }
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    var message = error.GetString(); document.Dispose();
                    throw new IOException(message);
                }
                if (process.ExitCode != 0) { document.Dispose(); throw new IOException("Xbox support exited unexpectedly."); }
                return document;
            }
            finally { Gate.Release(); }
        }
        internal async Task ConnectAsync(CancellationToken token)
        {
            using var result = await RunAsync(new { command = "connect" }, token);
            // Never display the previous account's library after switching accounts.
            SaveLibrary(new Library { Xuid = result.RootElement.GetProperty("xuid").GetString() });
        }
        internal async Task DisconnectAsync(CancellationToken token)
        {
            using var result = await RunAsync(new { command = "disconnect" }, token);
            File.Delete(Path.Combine(Root, "library.json"));
            if (ApplicationDataRoot != null) File.Delete(Path.Combine(ApplicationDataRoot, "xbox-library.json"));
        }
        internal Library SavedLibrary()
        {
            if (!Connected) return new();
            try { return JsonSerializer.Deserialize<Library>(File.ReadAllText(Path.Combine(Root, "library.json")), Json) ?? new(); }
            catch (Exception ex) when (ex is IOException || ex is JsonException) { return new(); }
        }
        static void SaveLibrary(Library library)
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "library.json");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, JsonSerializer.Serialize(library, Json)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            if (ApplicationDataRoot != null)
            {
                var shared = Path.Combine(ApplicationDataRoot, "xbox-library.json");
                var pending = shared + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(pending, JsonSerializer.Serialize(library, Json)); File.Move(pending, shared, true); }
                finally { if (File.Exists(pending)) File.Delete(pending); }
            }
        }
        internal async Task<Library> LoadLibraryAsync(CancellationToken token)
        {
            using var result = await RunAsync(new { command = "titles" }, token);
            var library = JsonSerializer.Deserialize<Library>(result.RootElement.GetRawText(), Json);
            SaveLibrary(library);
            await Store.SaveRecentAsync("xbox", library.Xuid, library.Titles.Where(t => t.LastPlayedUtc >= DateTime.UnixEpoch.Ticks && t.LastPlayedUtc <= DateTime.UtcNow.Ticks)
                .GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.Max(t => t.LastPlayedUtc)), token);
            return library;
        }
        internal async Task SyncLibraryAsync(Action<string> progress, CancellationToken token, string[] only = null)
        {
            await ImportSavedAsync(token);
            var library = await LoadLibraryAsync(token);
            var titles = library.Titles.Where(t => only == null || only.Contains(t.Id)).GroupBy(t => t.Id).Select(g => g.First()).ToList();
            var failures = 0;
            for (var i = 0; i < titles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke($"Syncing Xbox {i + 1} / {titles.Count}: {titles[i].Name}");
                try
                {
                    // Bulk sync must see provider failures so a rate limit or expired
                    // session stops the loop, even when this title has saved progress.
                    var result = await FetchAsync(library.Xuid, titles[i], token, useSavedOnError: false);
                    if (!string.IsNullOrEmpty(result.ProgressErrorMessage)) failures++;
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
                {
                    failures++;
                    if (ex.Message.Contains("429") || ex.Message.Contains("401") || ex.Message.Contains("403")) throw;
                }
                if (i + 1 < titles.Count) await Task.Delay(1500, token);
            }
            progress?.Invoke($"Xbox sync complete: {titles.Count - failures} games updated, {failures} unavailable. Saved progress is included in the Achievement Hub and Companion.");
        }
        internal static int SyncCommand(string root, string[] titles)
        {
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "PixelVault.settings.ini"))) return 2;
            Configure(root, new AchievementGuideService(root));
            var report = Path.Combine(root, "xbox-sync-status.json");
            void Status(string message) => File.WriteAllText(report, JsonSerializer.Serialize(new { message, updatedUtc = DateTime.UtcNow }));
            try { new XboxAchievementService().SyncLibraryAsync(Status, CancellationToken.None, titles).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Status("Xbox sync failed: " + ex.Message); return 1; }
        }
        internal async Task<GameAchievementsFetchService.FetchResult> FetchAsync(string xuid, XboxTitle title, CancellationToken token, bool useSavedOnError = true)
        {
            var captured = DateTime.UtcNow.Ticks;
            try
            {
                using var result = await RunAsync(new { command = "achievements", xuid, titleId = title.Id, legacy = title.Legacy }, token);
                var rows = ParseRows(result.RootElement.GetProperty("rows"), title.Name);
                var fetch = new GameAchievementsFetchService.FetchResult { SourceLabel = "Xbox", GameTitle = title.Name, Rows = rows,
                    DetailLine = "Xbox · " + rows.Count(r => r.ProgressKnown && r.Unlocked) + " / " + rows.Count + " achievements earned",
                    CapturedUtcTicks = captured };
                if (rows.Any(r => !r.ProgressKnown)) fetch.ProgressErrorMessage = "Xbox did not return known progress for every achievement. Saved progress was retained.";
                await Store.SaveGameAsync("xbox", xuid, title.Id, fetch, token);
                if (string.IsNullOrEmpty(fetch.ProgressErrorMessage))
                {
                    (Guides as IAchievementScoreStore)?.SyncAchievementScores(xuid, rows);
                    Guides?.SyncDefinitionsAndLoadGuides("", rows);
                }
                return fetch;
            }
            catch (Exception ex) when (useSavedOnError && (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException))
            {
                var saved = await Store.LoadGameAsync("xbox", xuid, title.Id, token);
                if (saved == null) throw;
                return new GameAchievementsFetchService.FetchResult { SourceLabel = "Xbox", GameTitle = title.Name, Rows = saved.Rows,
                    CapturedUtcTicks = saved.UpdatedUtc, DetailLine = "Saved Xbox progress · " + new DateTime(saved.UpdatedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"),
                    ProgressErrorMessage = ex.Message + " Showing saved progress." };
            }
        }
        internal static List<GameAchievementsFetchService.AchievementRow> ParseRows(JsonElement value, string title)
        {
            try
            {
                var rows = value.Deserialize<List<GameAchievementsFetchService.AchievementRow>>(Json);
                if (rows == null || rows.Any(row => row == null)) throw new JsonException();
                return rows;
            }
            catch (JsonException ex)
            {
                // Invalid data is handled by the cache fallback and per-title sync loop.
                throw new InvalidDataException("Xbox returned invalid achievement data for " + title + ". Saved progress was retained.", ex);
            }
        }
    }
}
