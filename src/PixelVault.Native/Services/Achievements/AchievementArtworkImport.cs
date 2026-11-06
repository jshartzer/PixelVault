using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    sealed class AchievementArtworkImport
    {
        internal sealed class Report
        {
            public string GameId { get; set; }
            public string SourcePage { get; set; }
            public string Mode { get; set; }
            public JsonElement[] Matched { get; set; } = Array.Empty<JsonElement>();
            public JsonElement[] Review { get; set; } = Array.Empty<JsonElement>();
            public JsonElement[] Imported { get; set; } = Array.Empty<JsonElement>();
            public JsonElement[] Failed { get; set; } = Array.Empty<JsonElement>();
        }
        internal sealed record Plan(string Directory, string Page, Report Report);
        internal sealed record RunResult(int ExitCode, string Output, string Error);
        internal sealed record ProgressUpdate(string Stage, int Completed, int Total, string Title, string Message);
        internal event Action<ProgressUpdate> ProgressChanged;
        static readonly SemaphoreSlim Gate = new(1, 1);
        static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        readonly string root, game;
        readonly Func<ProcessStartInfo, CancellationToken, Task<RunResult>> run;
        readonly object[] catalogue;
        readonly HashSet<string> originalUrls;
        internal AchievementArtworkImport(string dataRoot, string gameId, IEnumerable<GameAchievementsFetchService.AchievementRow> rows,
            Func<ProcessStartInfo, CancellationToken, Task<RunResult>> runner = null)
        {
            if (!Regex.IsMatch(gameId ?? "", "^[0-9]+$")) throw new InvalidDataException("Choose a Steam game to import achievement artwork.");
            root = Path.Combine(Path.GetFullPath(dataRoot), "achievement-artwork"); game = gameId; run = runner ?? Run;
            var selected = rows.Where(r => r?.Provider == "steam" && r.ProviderGameId == game && r.HasStableProviderIdentity && !string.IsNullOrWhiteSpace(r.IconUrlColor)).ToArray();
            catalogue = selected.Select(r => (object)new { r.Provider, r.ProviderGameId, r.ProviderAchievementId, r.Title, r.Description, r.IconUrlColor }).ToArray();
            originalUrls = selected.Select(r => GameAchievementsFetchService.SteamAchievementIconCandidates(r.IconUrlColor).FirstOrDefault()).ToHashSet(StringComparer.Ordinal);
            if (catalogue.Length == 0) throw new InvalidDataException("Refresh this game's achievements before importing artwork.");
        }
        internal static bool ValidPage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && uri.Host == "www.exophase.com" && uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && Regex.IsMatch(uri.AbsolutePath, "^/game/[a-z0-9-]+/(trophies|achievements)/$");
        internal string DefaultPage()
        {
            var saved = Path.Combine(root, "imports", game, "source-page.txt");
            if (File.Exists(saved)) { var page = File.ReadAllText(saved).Trim(); if (ValidPage(page)) return page; }
            var manifest = Path.Combine(root, "manifest.json");
            if (File.Exists(manifest))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                    foreach (var entry in document.RootElement.EnumerateArray())
                        if (entry.TryGetProperty("gameId", out var id) && id.GetString() == game && entry.TryGetProperty("sourcePage", out var url) && ValidPage(url.GetString())) return url.GetString();
                }
                catch (JsonException) { }
            }
            return KnownEntries().Select(e => e.GetProperty("sourcePage").GetString()).FirstOrDefault() ?? "";
        }
        internal bool HasKnownArtwork => KnownEntries().Length > 0;
        JsonElement[] KnownEntries()
        {
            using var stream = typeof(AchievementArtwork).Assembly.GetManifestResourceStream("PixelVault.Artwork.manifest.json");
            return (JsonSerializer.Deserialize<JsonElement[]>(stream) ?? Array.Empty<JsonElement>())
                .Where(e => e.GetProperty("gameId").GetString() == game).ToArray();
        }
        internal async Task<Plan> Preview(string page, string sourceKind, string sourceFile, CancellationToken token)
        {
            ProgressChanged?.Invoke(new("prepare", 0, 0, "", "Preparing achievement matches…"));
            page = page?.Trim();
            if (!ValidPage(page)) throw new InvalidDataException("Enter the matching game's public Exophase trophy list URL, ending in /trophies/ or /achievements/.");
            if (!new[] { "page", "html", "mapping", "known" }.Contains(sourceKind)) throw new InvalidDataException("Choose an artwork source.");
            if ((sourceKind == "html" || sourceKind == "mapping") && !File.Exists(sourceFile)) throw new InvalidDataException("Choose the saved source file first.");
            var directory = Path.Combine(root, "imports", game, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            // Only definitions for this game leave the process. No player identity, progress or credentials.
            var definitions = JsonSerializer.Serialize(catalogue, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(directory, "catalogue.json"), definitions, token);
            if (sourceKind == "known")
            {
                var entries = KnownEntries();
                if (entries.Length == 0) throw new InvalidDataException("This game has no bundled reviewed artwork yet.");
                if (entries.Any(e => e.GetProperty("sourcePage").GetString() != page)) throw new InvalidDataException("The reviewed artwork belongs to a different source page. Choose Public page for a different edition.");
                sourceFile = Path.Combine(directory, "known.json");
                await File.WriteAllTextAsync(sourceFile, JsonSerializer.Serialize(entries, Json), token); sourceKind = "mapping";
            }
            else if (sourceKind != "page")
            {
                var copy = Path.Combine(directory, sourceKind == "html" ? "source.html" : "source.json");
                if (new FileInfo(sourceFile).Length > 4 * 1024 * 1024) throw new InvalidDataException("The source file is too large (maximum 4 MB).");
                File.Copy(sourceFile, copy); sourceFile = copy;
            }
            var result = await Execute(directory, page, sourceKind, sourceFile, false, token);
            await File.WriteAllTextAsync(Path.Combine(directory, "preview-report.json"), JsonSerializer.Serialize(result, Json), token);
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(directory), "source-page.txt"), page, token);
            return new(directory, page, result);
        }
        internal async Task<Report> Import(Plan plan, CancellationToken token)
        {
            if (plan.Report.GameId != game || plan.Report.Matched.Length == 0) throw new InvalidDataException("Preview this game's artwork matches first.");
            // Import precisely the accepted preview, without re-reading a changed web page or source file.
            var mapping = Path.Combine(plan.Directory, "accepted.json");
            await File.WriteAllTextAsync(mapping, JsonSerializer.Serialize(plan.Report.Matched, Json), token);
            ProgressChanged?.Invoke(new("waiting", 0, 0, "", "Preparing image import…"));
            await Gate.WaitAsync(token);
            try
            {
                var result = await Execute(plan.Directory, plan.Page, "mapping", mapping, true, token);
                token.ThrowIfCancellationRequested();
                if (result.Imported.Length > 0)
                {
                    ProgressChanged?.Invoke(new("install", 0, 0, "", "Installing verified artwork…"));
                    Install(plan.Directory);
                }
                result.Review = plan.Report.Review;
                return result;
            }
            finally { Gate.Release(); }
        }
        async Task<Report> Execute(string directory, string page, string kind, string source, bool apply, CancellationToken token)
        {
            var script = Path.Combine(directory, "import.py");
            using (var stream = typeof(AchievementArtworkImport).Assembly.GetManifestResourceStream("PixelVault.ArtworkImporter.py"))
            using (var output = File.Create(script)) await stream.CopyToAsync(output, token);
            var start = new ProcessStartInfo("python.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
            foreach (var arg in new[] { "-u", script, "--catalogue", Path.Combine(directory, "catalogue.json"), "--game", game,
                "--page-url", page, "--output", directory, "--cache", Path.Combine(root, ".downloads") }) start.ArgumentList.Add(arg);
            if (kind == "html" || kind == "mapping") { start.ArgumentList.Add("--" + kind); start.ArgumentList.Add(source); }
            if (apply) start.ArgumentList.Add("--apply");
            start.ArgumentList.Add("--progress");
            RunResult execution;
            try { execution = await run(start, token); }
            catch (System.ComponentModel.Win32Exception) { throw new InvalidDataException("Artwork import needs Python with Pillow installed on this PC. Python could not be started."); }
            if (execution.Error.Contains("No module named 'PIL'", StringComparison.Ordinal)) throw new InvalidDataException("Artwork import needs the Pillow image library. Install it with: python -m pip install Pillow");
            if (execution.Output.Contains("Python was not found", StringComparison.OrdinalIgnoreCase) || execution.Error.Contains("Python was not found", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Artwork import needs Python with Pillow installed on this PC. Python could not be started.");
            var file = Path.Combine(directory, "import-report.json");
            if (!File.Exists(file) || new FileInfo(file).Length > 8 * 1024 * 1024) throw Failure(execution);
            var report = JsonSerializer.Deserialize<Report>(await File.ReadAllTextAsync(file, token), Json);
            if (report?.GameId != game || report.Mode != (apply ? "apply" : "plan") || execution.ExitCode is not (0 or 2)) throw Failure(execution);
            return report;
        }
        static Exception Failure(RunResult execution)
        {
            var message = (execution.Error + "\n" + execution.Output).Trim();
            if (message.Contains("HTTP 403") || message.Contains("HTTP 429")) return new InvalidDataException("The trophy site blocked automatic access. Open the page in your browser, save its HTML, then choose Saved HTML here.");
            return new InvalidDataException(message.Length == 0 ? "Artwork import did not produce a report." : message.Substring(0, Math.Min(message.Length, 1600)));
        }
        void Install(string directory)
        {
            var entries = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(directory, "manifest.json"))) ?? throw new InvalidDataException("Missing imported artwork.");
            var copies = new List<(string Source, string Name)>();
            foreach (var entry in entries)
            {
                if (entry.GetProperty("gameId").GetString() != game || entry.GetProperty("provider").GetString() != "steam"
                    || !originalUrls.Contains(entry.GetProperty("originalUrl").GetString())) throw new InvalidDataException("Imported artwork belongs to a different game.");
                var sha = entry.GetProperty("sha256").GetString(); var name = entry.GetProperty("file").GetString();
                if (!Regex.IsMatch(sha ?? "", "^[a-f0-9]{64}$") || name != sha + ".png") throw new InvalidDataException("Invalid artwork filename.");
                var file = Path.Combine(directory, name);
                if (new FileInfo(file).Length > 786432 || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant() != sha) throw new InvalidDataException("Artwork verification failed.");
                copies.Add((file, name));
            }
            var manifest = Path.Combine(root, "manifest.json");
            var saved = File.Exists(manifest) ? JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(manifest)) : Array.Empty<JsonElement>();
            var merged = (saved ?? Array.Empty<JsonElement>()).ToDictionary(e => e.GetProperty("originalUrl").GetString(), StringComparer.Ordinal);
            foreach (var entry in entries) merged[entry.GetProperty("originalUrl").GetString()] = entry;
            // Existing games survive; immutable assets arrive before the manifest is atomically replaced.
            foreach (var copy in copies) File.Copy(copy.Source, Path.Combine(root, copy.Name), true);
            var temporary = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(merged.Values.ToArray(), Json), new UTF8Encoding(false)); File.Move(temporary, manifest, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static ProgressUpdate ParseProgress(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 4096) return null;
            try
            {
                using var json = JsonDocument.Parse(line);
                if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("type", out var type) || type.GetString() != "progress") return null;
                var update = json.RootElement.Deserialize<ProgressUpdate>(Json);
                if (update == null || !new[] { "prepare", "source", "matching", "images", "staging" }.Contains(update.Stage)
                    || update.Completed < 0 || update.Total < 0 || update.Completed > update.Total) return null;
                return update;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
        }
        async Task<string> ReadProgress(StreamReader reader)
        {
            var output = new StringBuilder(); string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var update = ParseProgress(line);
                if (update != null) ProgressChanged?.Invoke(update);
                else if (output.Length < 16384) output.AppendLine(line.Substring(0, Math.Min(line.Length, 16384 - output.Length)));
            }
            return output.ToString();
        }
        async Task<RunResult> Run(ProcessStartInfo start, CancellationToken token)
        {
            using var process = Process.Start(start) ?? throw new InvalidDataException("The artwork importer could not start.");
            var output = ReadProgress(process.StandardOutput); var error = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                await Task.WhenAll(output, error);
                if (!token.IsCancellationRequested) throw new InvalidDataException("The artwork import timed out. Try a saved HTML page or a smaller reviewed mapping.");
                throw;
            }
            return new(process.ExitCode, await output, await error);
        }
    }
}
