using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed class DeathStrandingTrackerSource : ITrackerDataSource, ITrackerSourceChangeDetector, ITrackerGameFileSource
    {
        internal const string AdapterId = "death-stranding-dc.fabrication";
        internal const ulong SteamBase = 76561197960265728;
        readonly string storageRoot, workerPath;
        static readonly SharedSaveAnalysisCache SharedAnalysis = new();
        readonly SharedSaveAnalysisCache analysisCache;
        readonly Func<string, CancellationToken, Task<DeathStrandingReadResult>> readWorker;
        public string Id => AdapterId;
        public string Version => "1";
        public bool Supports(AchievementTrackerDefinition definition)=>definition?.Provider=="steam"&&definition.GameId=="1850570"
            &&(DeathStrandingFacilityReader.Supports(definition)||PixelVault.Tracking.CenturyCargoGoal.Matches(definition)||DeathStrandingMailCatalogueReader.Supports(definition)||definition.Id==DeathStrandingMailReader.TrackerId||definition.Id==AchievementTrackingCatalog.DeathStrandingId||DeathStrandingWorldReader.Supports(definition.Id)||DeathStrandingOrderReader.Supports(definition.Id)||DeathStrandingChipReader.Supports(definition.Id)||DeathStrandingInterviewReader.Supports(definition.Id)||DeathStrandingInterviewCatalogueReader.Supports(definition)||DeathStrandingRoadReader.Supports(definition));
        public string Coverage(AchievementTrackerDefinition definition)=>definition?.Id==PixelVault.Tracking.CenturyCargoGoal.TrackerId
            ?"Reads the cumulative delivered cargo weight in whole kilograms from the validated saved statistic table."
            :definition?.Id==DeathStrandingFacilityReader.TrackerId
            ?"Reads the saved whole-star connection level for all 36 Best Beloved facilities."
            :definition?.Id==DeathStrandingMailReader.TrackerId
            ?"Reads the saved mail-reading total from the complete validated statistic table. Per-email flags are not inferred."
            :DeathStrandingMailCatalogueReader.Supports(definition)
            ?"Reads verified opened states for 239 emails. Received unread mail remains incomplete; one catalogue title absent from the runtime list stays unknown."
            :DeathStrandingOrderReader.Supports(definition?.Id)
            ?definition.Id == PixelVault.Tracking.CenturyLegendsCatalogue.TrackerId
                ?"Reads a Legend or higher evaluation for every one of the 540 standard orders."
                :"Reads all 540 standard orders directly from a validated save copy. LL and LLL are independent earned ranks."
            :DeathStrandingInterviewReader.Supports(definition?.Id)
            ?"Reads the game's interview achievement counter from a complete validated statistic table. Individual interview read states are not inferred."
            :DeathStrandingInterviewCatalogueReader.Supports(definition)
            ?"Reads all 149 populated interview entries from validated save data. Only read interviews count toward the Century achievement; unlocked unread entries remain incomplete."
            :DeathStrandingChipReader.Supports(definition?.Id)
            ?"Reads all 56 memory chips from a validated save copy. Only restoration at a terminal counts; pickup alone does not."
            :DeathStrandingRoadReader.Supports(definition)
            ?"Reads all 51 restored UC Route sections directly from validated save data, regardless of builder. Archived unmatched records are not inferred."
            :"Reads the complete supported milestone catalogue directly from a validated copy of the game save.";
        public TrackerSourceCapabilities Capabilities => TrackerSourceCapabilities.Refresh | TrackerSourceCapabilities.ExternalObservationTime;
        public string ChangeSignature(TrackerSourceBinding binding) => TrackerSourceFiles.Signature(Candidates(ResolveProfileRoot(binding.Location, binding.Account), binding.SpecificSave), DeathStrandingSaveReader.MaxFile, allowConcurrentWrites: true);
        internal DeathStrandingTrackerSource(string trackingDirectory, string workerPath = null,
            SharedSaveAnalysisCache analysisCache = null, Func<string, CancellationToken, Task<DeathStrandingReadResult>> readWorker = null)
        {
            storageRoot = Path.Combine(Path.GetFullPath(trackingDirectory), "sources", "death-stranding-dc");
            this.workerPath = workerPath ?? Path.Combine(AppContext.BaseDirectory, "PixelVault.exe");
            this.analysisCache = analysisCache ?? SharedAnalysis;
            this.readWorker = readWorker ?? RunWorkerAsync;
        }
        internal static bool IsSteamAccount(string account) => account?.Length == 17 && ulong.TryParse(account, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id >= SteamBase && id - SteamBase <= uint.MaxValue;
        internal static string ProfileNumber(string account)
        {
            if (!IsSteamAccount(account)) throw new InvalidDataException("Connect a Steam account before configuring Death Stranding saves.");
            return (ulong.Parse(account, CultureInfo.InvariantCulture) - SteamBase).ToString(CultureInfo.InvariantCulture);
        }
        internal static string DefaultSaveRoot(string account) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KojimaProductions", "DeathStrandingDC", ProfileNumber(account));
        internal static string DiscoverGameDirectory()
        {
            var roots = new List<string>();
            try
            {
                var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrWhiteSpace(steam))
                {
                    roots.Add(steam); var libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraries) && new FileInfo(libraries).Length < 1024 * 1024)
                        roots.AddRange(Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value.Replace("\\\\", "\\")));
                }
                foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var name = "DEATH STRANDING DIRECTORS CUT"; var manifest = Path.Combine(root, "steamapps", "appmanifest_1850570.acf");
                    if (File.Exists(manifest) && new FileInfo(manifest).Length < 1024 * 1024)
                    { var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\""); if (match.Success) name = match.Groups[1].Value; }
                    var folder = Path.Combine(root, "steamapps", "common", name);
                    if (File.Exists(Path.Combine(folder, "ds.exe")) && File.Exists(Path.Combine(folder, "oo2core_7_win64.dll"))) return folder;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { }
            return "";
        }
        public void Validate(TrackerSourceBinding binding, AchievementTrackerDefinition definition)
        {
            if (binding.AdapterId != Id || binding.AdapterVersion != Version) throw new NotSupportedException("This Death Stranding adapter version is not supported.");
            if (!Supports(definition))
                throw new NotSupportedException("This Death Stranding checklist does not match a supported save catalogue.");
            if (definition.Id == PixelVault.Tracking.CenturyLegendsCatalogue.TrackerId && !PixelVault.Tracking.CenturyLegendsCatalogue.Matches(definition))
                throw new NotSupportedException("The LLL source requires all 540 original standard orders with one LLL requirement per order.");
            bool roads = DeathStrandingRoadReader.Supports(definition);
            var reference = definition.Id==DeathStrandingMailReader.TrackerId ? DeathStrandingMailReader.Definition() : DeathStrandingMailCatalogueReader.Supports(definition) ? DeathStrandingMailCatalogueReader.Definition() : roads || definition.Id == DeathStrandingFacilityReader.TrackerId ? definition : DeathStrandingInterviewCatalogueReader.Supports(definition) ? DeathStrandingInterviewCatalogueReader.Definition()
                : DeathStrandingInterviewReader.Supports(definition.Id) ? DeathStrandingInterviewReader.Definition() : AchievementTrackingCatalog.Builtins().Single(d => d.Id == definition.Id);
            if (definition.Items.Count != reference.Items.Count || !definition.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal).SetEquals(reference.Items.Select(i => i.Id)))
                throw new NotSupportedException("The save source needs the complete bundled milestone catalogue.");
            if ((DeathStrandingInterviewReader.Supports(definition.Id)||definition.Id==DeathStrandingMailReader.TrackerId) && definition.Items.Single().Target != 100)
                throw new NotSupportedException("The interview achievement counter requires its goal of 100.");
            if (!IsSteamAccount(binding.Account)) throw new InvalidDataException("The save reader needs this tracker’s connected Steam ID.");
            if (!Path.IsPathFullyQualified(binding.Location ?? "") || !Path.IsPathFullyQualified(binding.GameDirectory ?? "")) throw new InvalidDataException("Choose full paths for the save folder and game installation.");
            if (!string.IsNullOrWhiteSpace(binding.SpecificSave) && (!Path.IsPathFullyQualified(binding.SpecificSave) || !Path.GetFileName(binding.SpecificSave).Equals("checkpoint.dat", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The specific save override must be a checkpoint.dat file inside this account's save folder.");
            if (!string.IsNullOrWhiteSpace(binding.SpecificSave) && !Path.GetFullPath(binding.SpecificSave).StartsWith(Path.GetFullPath(binding.Location).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected checkpoint is outside the configured save folder.");
        }
        internal static string ResolveProfileRoot(string location, string account)
        {
            var root = Path.GetFullPath(location);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The save folder was not found. Check Source… or save once in the game.");
            var child = Path.Combine(root, ProfileNumber(account));
            if (Directory.Exists(child)) return child;
            return root;
        }
        internal static IReadOnlyList<FileInfo> Candidates(string root, string specificSave)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrWhiteSpace(specificSave))
            {
                var full = Path.GetFullPath(specificSave);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).Equals("checkpoint.dat", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The specific save is outside the configured account's save folder.");
                if (!File.Exists(full)) throw new FileNotFoundException("The selected checkpoint no longer exists. Clear the specific-save override to use the newest save.");
                return new[] { new FileInfo(full) };
            }
            var files = new List<FileInfo>();
            if (File.Exists(Path.Combine(root, "checkpoint.dat"))) files.Add(new FileInfo(Path.Combine(root, "checkpoint.dat")));
            foreach (var folder in Directory.EnumerateDirectories(root).Take(1024))
                if (Regex.IsMatch(Path.GetFileName(folder), @"^(autosave|manualsave|quicksave)\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    && File.Exists(Path.Combine(folder, "checkpoint.dat"))) files.Add(new FileInfo(Path.Combine(folder, "checkpoint.dat")));
            if (files.Count == 0) throw new InvalidDataException("No checkpoints were found in this account's save folder.");
            if (files.Count > 128) throw new InvalidDataException("Too many checkpoint slots. Select a specific checkpoint in Source….");
            return files.OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static async Task<(byte[] Bytes, long Ticks)> ReadStableAsync(string path, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var before = new FileInfo(path); var length = before.Length; var time = before.LastWriteTimeUtc;
                    if (length < 24 || length > DeathStrandingSaveReader.MaxFile) throw new InvalidDataException("The checkpoint size is invalid or unsupported.");
                    var bytes = new byte[(int)length];
                    // Do not deny the game write/delete access while copying. A concurrent
                    // save is rejected by stamp/length checks and the container checksum.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous);
                    if (stream.Length != length) throw new IOException("The checkpoint changed during copying.");
                    await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                    var after = new FileInfo(path);
                    if (stream.Length != length || after.Length != length || after.LastWriteTimeUtc != time) throw new IOException("The checkpoint changed during copying.");
                    return (bytes, time.Ticks);
                }
                catch (IOException) when (attempt < 2) { await Task.Delay(250, token).ConfigureAwait(false); }
                catch (InvalidDataException) when (attempt < 2) { await Task.Delay(250, token).ConfigureAwait(false); }
            }
        }
        public async Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding binding, AchievementTrackerDefinition definition, CancellationToken token)
        {
            Validate(binding, definition);
            if (!File.Exists(workerPath)) throw new FileNotFoundException("The packaged save-reader executable is missing. Rebuild or reinstall PixelVault.");
            if (!File.Exists(Path.Combine(binding.GameDirectory, "ds.exe")) || !File.Exists(Path.Combine(binding.GameDirectory, "oo2core_7_win64.dll"))) throw new FileNotFoundException("Choose the game installation containing ds.exe and oo2core_7_win64.dll.");
            string dllHash;
            using (var dll = new FileStream(Path.Combine(binding.GameDirectory, "oo2core_7_win64.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (dll.Length > 64 * 1024 * 1024) throw new InvalidDataException("The game compression library is too large.");
                dllHash = Convert.ToHexString(SHA256.HashData(dll));
            }
            var root = ResolveProfileRoot(binding.Location, binding.Account); var candidates = Candidates(root, binding.SpecificSave);
            var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding.TrackerId + "\n" + binding.Account)));
            var runs = Path.Combine(storageRoot, scope); Directory.CreateDirectory(runs);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            int skipped = 0; string lastInvalid = null;
            try
            {
                foreach (var candidate in candidates)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var read = await ReadStableAsync(candidate.FullName, deadline.Token).ConfigureAwait(false);
                        var digest = Convert.ToHexString(SHA256.HashData(read.Bytes));
                        bool roads = DeathStrandingRoadReader.Supports(definition);
                        var request = new DeathStrandingReadRequest { TrackerId = roads ? DeathStrandingRoadReader.TrackerId : definition.Id, Account = binding.Account, GameDirectory = binding.GameDirectory, ObservedUtc = read.Ticks,
                            SourceName = Path.GetRelativePath(root, candidate.FullName), SkippedNewer = skipped, AnalyzeAll = true };
                        var key = AnalysisKey(storageRoot, workerPath, binding.GameDirectory, binding.Account, digest, dllHash);
                        var serialized = await analysisCache.GetAsync(key, async ct =>
                        {
                            var run = Path.Combine(runs, "run-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(run);
                            using var lease = new FileStream(Path.Combine(run, ".lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                            try
                            {
                                await File.WriteAllBytesAsync(Path.Combine(run, "input.dat"), read.Bytes, ct).ConfigureAwait(false);
                                await File.WriteAllTextAsync(Path.Combine(run, "request.json"), JsonSerializer.Serialize(request, AchievementTrackingCatalog.Json), ct).ConfigureAwait(false);
                                await File.WriteAllTextAsync(Path.Combine(run, "manifest.json"), JsonSerializer.Serialize(new { Source = candidate.FullName, Sha256 = digest, SourceUtc = read.Ticks, Adapter = Id, Version }, AchievementTrackingCatalog.Json), ct).ConfigureAwait(false);
                                var result = await readWorker(run, ct).ConfigureAwait(false);
                                ThrowReaderError(result.ErrorCode, result.Error);
                                ValidateAnalysis(result, binding.Account, digest, dllHash);
                                return JsonSerializer.Serialize(result, AchievementTrackingCatalog.Json);
                            }
                            finally { lease.Dispose(); Prune(runs, binding.KeepSnapshots ? 3 : 0); }
                        }, deadline.Token).ConfigureAwait(false);
                        // Deserialize per consumer: rebasing metadata or reconciliation must
                        // never mutate another tracker's cached observation.
                        var shared = JsonSerializer.Deserialize<DeathStrandingReadResult>(serialized, AchievementTrackingCatalog.Json);
                        var projection = shared.Projections[roads ? DeathStrandingRoadReader.TrackerId : definition.Id];
                        ThrowReaderError(projection.ErrorCode, projection.Error);
                        var snapshot = roads ? DeathStrandingRoadReader.Project(projection.Snapshot, definition) : projection.Snapshot;
                        if (snapshot.Items?.Count != definition.Items.Count) throw new InvalidDataException("The isolated reader returned inconsistent target coverage.");
                        snapshot.ObservedUtcTicks = read.Ticks;
                        snapshot.Source = DeathStrandingSaveWorker.SourceLabel(request);
                        return new TrackerSourceObservation { Fingerprint = digest, SnapshotJson = JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json) };
                    }
                    catch (InvalidDataException ex) { skipped++; lastInvalid = ex.Message; }
                    finally { if (!binding.KeepSnapshots) Prune(runs, 0); }
                }
                throw new InvalidDataException("No valid checkpoint could be read. " + lastInvalid);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("The save read timed out. Saved tracker progress was preserved."); }
        }
        internal static string AnalysisKey(string storage, string worker, string game, string account, string fingerprint, string dllHash)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("death-stranding-dc.analysis-v7\n"
                + Path.GetFullPath(storage).ToUpperInvariant() + "\n" + Path.GetFullPath(worker).ToUpperInvariant() + "\n"
                + Path.GetFullPath(game).ToUpperInvariant() + "\n" + account + "\n" + fingerprint + "\n" + dllHash)));
        static void ThrowReaderError(string code, string message)
        {
            if (string.IsNullOrEmpty(code)) return;
            if (code == "InvalidSave") throw new InvalidDataException(message);
            if (code == "UnsupportedFormat") throw new NotSupportedException(message);
            throw new DeathStrandingReaderException(code, message);
        }
        internal static void ValidateAnalysis(DeathStrandingReadResult result, string account, string fingerprint, string dllHash)
        {
            var expectedCounts = AchievementTrackingCatalog.Builtins().Where(d => d.Id == AchievementTrackingCatalog.DeathStrandingId
                || DeathStrandingWorldReader.Supports(d.Id) || DeathStrandingOrderReader.Supports(d.Id) || DeathStrandingChipReader.Supports(d.Id) || PixelVault.Tracking.CenturyCargoGoal.Matches(d))
                .Append(DeathStrandingMailCatalogueReader.Definition()).Append(DeathStrandingMailReader.Definition()).Append(DeathStrandingInterviewReader.Definition()).Append(DeathStrandingInterviewCatalogueReader.Definition()).Append(DeathStrandingRoadReader.Definition()).ToDictionary(d => d.Id, d => d.Items.Count, StringComparer.Ordinal);
            expectedCounts.Add(DeathStrandingFacilityReader.TrackerId, DeathStrandingFacilityReader.FacilityCount);
            if (result.Version != 1 || result.Fingerprint != fingerprint || result.GameDllSha256 != dllHash || result.Projections == null
                || !expectedCounts.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(result.Projections.Keys))
                throw new InvalidDataException("The isolated reader returned inconsistent save identity or coverage.");
            foreach (var pair in result.Projections)
            {
                var projection = pair.Value;
                if (projection == null || (projection.ErrorCode != null && projection.ErrorCode != "InvalidSave" && projection.ErrorCode != "UnsupportedFormat"))
                    throw new InvalidDataException("The isolated reader returned an invalid table outcome.");
                if (!string.IsNullOrEmpty(projection.ErrorCode))
                {
                    if (projection.Snapshot != null) throw new InvalidDataException("The isolated reader returned ambiguous table evidence.");
                    continue;
                }
                var snapshot = projection.Snapshot;
                if (snapshot?.Account != account || snapshot.Provider != "steam" || snapshot.GameId != "1850570" || snapshot.TrackerId != pair.Key
                    || snapshot.Items?.Count != expectedCounts[pair.Key])
                    throw new InvalidDataException("The isolated reader returned inconsistent table identity or coverage.");
            }
        }
        async Task<DeathStrandingReadResult> RunWorkerAsync(string run, CancellationToken token)
        {
            var start = new ProcessStartInfo(workerPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = run };
            start.ArgumentList.Add("--death-stranding-read"); start.ArgumentList.Add(Path.Combine(run, "request.json"));
            using var process = Process.Start(start) ?? throw new IOException("The isolated save reader could not start.");
            await WaitWorkerAsync(process, TimeSpan.FromSeconds(25), token).ConfigureAwait(false);
            var resultPath = Path.Combine(run, "result.json");
            if (!File.Exists(resultPath) || new FileInfo(resultPath).Length > 4 * 1024 * 1024) throw new IOException("The isolated save reader exited unexpectedly (code " + process.ExitCode + "). Saved tracker progress was preserved.");
            var result = JsonSerializer.Deserialize<DeathStrandingReadResult>(await File.ReadAllTextAsync(resultPath, token).ConfigureAwait(false), AchievementTrackingCatalog.Json);
            if (result?.Version != 1 || process.ExitCode != 0 && string.IsNullOrEmpty(result.ErrorCode)) throw new InvalidDataException("The save reader returned an invalid result.");
            return result;
        }
        internal static async Task WaitWorkerAsync(Process process, TimeSpan limit, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(limit);
            // Shutdown must initiate termination synchronously; an async continuation may not run after WPF exits.
            using var killOnCancel = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { } // The awaited path below retries/reports termination failures.
            });
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (token.IsCancellationRequested) throw;
                throw new TimeoutException("The isolated save reader timed out. Saved tracker progress was preserved.");
            }
        }
        // Only our known files are removed, then an empty directory. Never recursively delete a computed path.
        internal static void Prune(string root, int keep)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var folder in Directory.EnumerateDirectories(root)
                .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^run-\d{8}T\d{13,}-[0-9a-f]{32}$") && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(keep))
            {
                var full = Path.GetFullPath(folder);
                if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(Path.GetFileName(full), @"^run-\d{8}T\d{13,}-[0-9a-f]{32}$") || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    using (var check = new FileStream(Path.Combine(full, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                    foreach (var name in new[] { "input.dat", "request.json", "result.json", "manifest.json", ".lock" }) File.Delete(Path.Combine(full, name));
                    if (!Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
