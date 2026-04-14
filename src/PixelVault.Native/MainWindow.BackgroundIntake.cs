using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        BackgroundIntakeAgent _backgroundIntakeAgent;

        internal bool IsForegroundIntakeBusy => _foregroundIntakeBusyGate.IsBusy;

        void BeginForegroundIntakeBusy() => _foregroundIntakeBusyGate.Enter();

        void EndForegroundIntakeBusy() => _foregroundIntakeBusyGate.Leave();

        void InitializeBackgroundIntakeAgent()
        {
            _backgroundIntakeAgent = new BackgroundIntakeAgent(this);
            _backgroundIntakeAgent.ApplySettingsAndStart();
            Closed += (_, __) => _backgroundIntakeAgent.Dispose();
        }

        /// <summary>Background intake only; prefix <c>[BGINT]</c> for grepping. No-op unless verbose logging is enabled in Path Settings.</summary>
        internal void LogBackgroundIntakeVerbose(string message)
        {
            if (!backgroundAutoIntakeVerboseLogging) return;
            try
            {
                Log("[BGINT] " + message);
            }
            catch
            {
            }
        }

        internal sealed class BackgroundIntakeAgent : IDisposable
        {
            const int DebounceMilliseconds = 500;
            const int MaxBatchFiles = 80;
            static readonly TimeSpan StabilityMaxWait = TimeSpan.FromMinutes(10);

            readonly MainWindow _host;
            readonly object _pendingSync = new object();
            readonly HashSet<string> _pendingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
            DispatcherTimer _debounceTimer;
            CancellationTokenSource _runCts = new CancellationTokenSource();
            volatile bool _flushRunning;

            public BackgroundIntakeAgent(MainWindow host)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
            }

            void V(string message) => _host.LogBackgroundIntakeVerbose(message);

            static string SummarizePaths(IReadOnlyList<string> paths, int maxNames = 10)
            {
                if (paths == null || paths.Count == 0) return "(none)";
                var parts = paths.Take(maxNames).Select(p => Path.GetFileName(p) ?? p).ToList();
                var s = string.Join(", ", parts);
                if (paths.Count > maxNames) s += " (+" + (paths.Count - maxNames) + " more)";
                return s;
            }

            public void ApplySettingsAndStart()
            {
                if (_host.Dispatcher.CheckAccess())
                    ApplySettingsAndStartCore();
                else
                    _host.Dispatcher.Invoke(ApplySettingsAndStartCore);
            }

            void ApplySettingsAndStartCore()
            {
                V("ApplySettingsAndStart: restarting agent.");
                StopCore();
                if (!_host.backgroundAutoIntakeEnabled)
                {
                    V("Background auto-intake is disabled in settings; not watching.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(_host.libraryRoot))
                {
                    V("Library root is empty; background intake will not watch (set Library folder in Path Settings).");
                    return;
                }

                if (_host.backgroundAutoIntakeVerboseLogging)
                    _host.Log("Background intake: verbose logging on — filter the main log for [BGINT].");

                _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds) };
                _debounceTimer.Tick += (_, __) =>
                {
                    _debounceTimer.Stop();
                    BeginFlushPending();
                };

                var sourceRoots = _host.GetSourceRoots();
                V("Configured source roots count=" + sourceRoots.Count + " libraryRoot=" + (_host.libraryRoot ?? string.Empty));
                foreach (var root in sourceRoots)
                {
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    {
                        V("Skipping watch (missing or empty path): '" + (root ?? string.Empty) + "'");
                        continue;
                    }
                    try
                    {
                        V("Attaching watcher TopDirectoryOnly filter=* path=" + root);
                        var w = new FileSystemWatcher(root)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                            IncludeSubdirectories = false,
                            Filter = "*",
                            EnableRaisingEvents = true
                        };
                        w.Created += (_, e) => SchedulePath(e.FullPath);
                        w.Changed += (_, e) =>
                        {
                            try
                            {
                                if (string.IsNullOrWhiteSpace(e.FullPath) || !File.Exists(e.FullPath)) return;
                            }
                            catch
                            {
                                return;
                            }
                            SchedulePath(e.FullPath);
                        };
                        w.Renamed += (_, e) => SchedulePath(e.FullPath);
                        w.Error += (_, e) =>
                        {
                            try
                            {
                                var ex = e.GetException();
                                _host.Log("Background intake watcher error: " + ex.Message);
                                _host.LogBackgroundIntakeVerbose("Watcher Error event: " + ex);
                            }
                            catch
                            {
                            }
                        };
                        _watchers.Add(w);
                    }
                    catch (Exception ex)
                    {
                        _host.Log("Background intake: could not watch '" + root + "': " + ex.Message);
                        V("Watcher attach failed for '" + root + "': " + ex);
                    }
                }

                if (_watchers.Count > 0)
                    _host.Log("Background auto-intake: watching " + _watchers.Count + " source folder(s).");
                else
                    V("No FileSystemWatchers started (check source folders exist and are configured).");
            }

            void SchedulePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                _ = _host.Dispatcher.BeginInvoke(new Action(() => SchedulePathCore(path)));
            }

            void SchedulePathCore(string path)
            {
                if (!_host.backgroundAutoIntakeEnabled) return;
                try
                {
                    path = Path.GetFullPath(path);
                }
                catch
                {
                    return;
                }
                if (!File.Exists(path))
                {
                    V("SchedulePath ignored (file missing): " + path);
                    return;
                }
                if (!IsMedia(Path.GetFileName(path)))
                {
                    V("SchedulePath ignored (not a media extension): " + path);
                    return;
                }
                lock (_pendingSync)
                {
                    _pendingPaths.Add(path);
                    var n = _pendingPaths.Count;
                    if (_debounceTimer != null)
                    {
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                    V("Queued '" + Path.GetFileName(path) + "' pendingUnique=" + n + " debounceMs=" + DebounceMilliseconds);
                }
            }

            static bool IsMedia(string path) => MainWindow.IsMedia(path);

            void BeginFlushPending()
            {
                if (_flushRunning)
                {
                    V("BeginFlushPending skipped (previous flush still finishing).");
                    return;
                }
                if (_host.IsForegroundIntakeBusy)
                {
                    V("BeginFlushPending deferred: foreground import/intake in progress.");
                    lock (_pendingSync)
                    {
                        if (_pendingPaths.Count > 0 && _debounceTimer != null)
                        {
                            _debounceTimer.Stop();
                            _debounceTimer.Start();
                        }
                    }
                    return;
                }
                List<string> batch;
                lock (_pendingSync)
                {
                    if (_pendingPaths.Count == 0) return;
                    batch = _pendingPaths.Take(MaxBatchFiles).ToList();
                    foreach (var p in batch) _pendingPaths.Remove(p);
                }
                V("Debounce fired: processing batch size=" + batch.Count + " files=" + SummarizePaths(batch));
                _flushRunning = true;
                var ct = _runCts.Token;
                Task.Run(() => FlushPendingAsync(batch, ct), ct).ContinueWith(
                    t =>
                    {
                        _flushRunning = false;
                        if (t.IsFaulted)
                        {
                            try
                            {
                                var baseEx = t.Exception?.GetBaseException();
                                _host.Log("Background intake: " + (baseEx?.Message ?? "failed"));
                                _host.LogBackgroundIntakeVerbose("Flush task faulted: " + baseEx);
                            }
                            catch
                            {
                            }
                        }
                        _ = _host.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            lock (_pendingSync)
                            {
                                if (_pendingPaths.Count > 0 && _host.backgroundAutoIntakeEnabled && _debounceTimer != null)
                                {
                                    _debounceTimer.Stop();
                                    _debounceTimer.Start();
                                }
                            }
                        }));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            async Task FlushPendingAsync(List<string> batch, CancellationToken cancellationToken)
            {
                if (batch == null || batch.Count == 0) return;
                if (!_host.backgroundAutoIntakeEnabled || string.IsNullOrWhiteSpace(_host.libraryRoot))
                {
                    V("FlushPendingAsync aborted (disabled or no library root).");
                    return;
                }
                if (_host.IsForegroundIntakeBusy)
                {
                    V("FlushPendingAsync re-queue: foreground import/intake became busy after dequeue.");
                    _ = _host.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        lock (_pendingSync)
                        {
                            foreach (var p in batch)
                                _pendingPaths.Add(p);
                            if (_debounceTimer != null)
                            {
                                _debounceTimer.Stop();
                                _debounceTimer.Start();
                            }
                        }
                    }));
                    return;
                }

                var quietMs = Math.Max(1, _host.backgroundAutoIntakeQuietSeconds) * 1000;
                V("Stability phase: quietMs=" + quietMs + " maxWait=" + StabilityMaxWait + " candidateFiles=" + batch.Count);
                var stable = new List<string>();
                foreach (var path in batch.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(path))
                    {
                        V("Stability skip (missing): " + path);
                        continue;
                    }
                    var name = Path.GetFileName(path) ?? path;
                    V("Stability wait start: " + name);
                    var ok = await SourceFileStabilityProbe.WaitUntilStableAsync(path, quietMs, StabilityMaxWait, cancellationToken).ConfigureAwait(false);
                    var stillThere = File.Exists(path);
                    V("Stability wait end: " + name + " ok=" + ok + " stillExists=" + stillThere);
                    if (!ok || !stillThere) continue;
                    stable.Add(path);
                }

                if (stable.Count == 0)
                {
                    V("No files passed stability; nothing to analyze.");
                    return;
                }

                V("Passed stability: count=" + stable.Count + " " + SummarizePaths(stable));
                var rules = _host.filenameParserService.GetConventionRules(_host.libraryRoot);
                V("Loaded convention rules count=" + (rules?.Count ?? 0));
                var analyses = _host.intakeAnalysisService.AnalyzeFiles(stable, cancellationToken);
                var eligible = new List<string>();
                foreach (var path in stable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fname = Path.GetFileName(path) ?? path;
                    if (analyses == null || !analyses.TryGetValue(path, out var a) || a == null)
                    {
                        V("Skip eligibility: " + fname + " reason=no_analysis_entry");
                        continue;
                    }
                    var parsed = a.Parsed ?? new FilenameParseResult();
                    var rule = AutoIntakePolicy.TryResolveMatchedRule(rules, parsed);
                    if (AutoIntakePolicy.IsEligibleForBackgroundAutoImport(a, rule))
                    {
                        var mode = rule != null && rule.IsBuiltIn ? "built-in" : "custom-trusted";
                        V("Eligible: " + fname + " conventionId=" + (parsed.ConventionId ?? "") + " mode=" + mode + " canUpdateMetadata=" + a.CanUpdateMetadata);
                        eligible.Add(path);
                        continue;
                    }
                    var why = AutoIntakePolicy.TryGetIneligibilityReason(a, rule);
                    V("Not eligible: " + fname + " reason=" + (why ?? "unknown") + " matchedConvention=" + parsed.MatchedConvention + " canUpdateMetadata=" + a.CanUpdateMetadata);
                }

                if (eligible.Count == 0)
                {
                    V("No eligible paths after policy; headless import not run.");
                    return;
                }

                V("Starting headless standard import for count=" + eligible.Count + " " + SummarizePaths(eligible));
                HeadlessStandardImportOutcome outcome;
                try
                {
                    outcome = await _host.RunHeadlessStandardImportForTopLevelPathsAsync(eligible, cancellationToken, progress: null).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    V("Headless import threw: " + ex);
                    _ = _host.Dispatcher.BeginInvoke(new Action(() => TryNotifyBackgroundOutcome(0, ex.Message)));
                    return;
                }

                var moved = outcome?.MoveResult?.Moved ?? 0;
                var skipped = outcome?.MoveResult?.Skipped ?? 0;
                var renamed = outcome?.MoveResult?.RenamedOnConflict ?? 0;
                V("Headless import finished: moved=" + moved + " skipped=" + skipped + " renamedOnConflict=" + renamed);
                if (moved > 0)
                    _host.RecordBackgroundIntakeBatch(outcome);
                _ = _host.Dispatcher.BeginInvoke(new Action(() => TryNotifyBackgroundOutcome(moved, null)));
            }

            void TryNotifyBackgroundOutcome(int moved, string errorMessage)
            {
                if (!_host.backgroundAutoIntakeEnabled) return;
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    _host.Log("Background auto-intake failed: " + errorMessage);
                    if (_host.backgroundAutoIntakeToastsEnabled)
                        TryLibraryToast("Background auto-intake failed: " + errorMessage, MessageBoxImage.Warning);
                    return;
                }
                if (moved <= 0) return;
                _host.Log("Background auto-intake: moved " + moved + " file(s) into the library pipeline.");
                if (!_host.backgroundAutoIntakeToastsEnabled) return;
                if (!_host.backgroundAutoIntakeShowSummary) return;
                var msg = "Background import moved " + moved + " file(s). Command palette → Background imports to review or undo.";
                TryLibraryToast(msg, MessageBoxImage.Information, () => _host.EnsureBackgroundIntakeActivityWindow());
            }

            void TryLibraryToast(string msg, MessageBoxImage icon, Action review = null)
            {
                try { _host.TryLibraryToast(msg, icon, review); } catch { }
            }

            void StopCore()
            {
                int pendingLeft;
                lock (_pendingSync) pendingLeft = _pendingPaths.Count;
                V("StopCore: cancelling run, disposing " + _watchers.Count + " watcher(s), pendingPathsBeforeClear=" + pendingLeft);
                try { _runCts?.Cancel(); } catch { }
                foreach (var w in _watchers)
                {
                    try
                    {
                        w.EnableRaisingEvents = false;
                        w.Dispose();
                    }
                    catch { }
                }
                _watchers.Clear();
                if (_debounceTimer != null)
                {
                    try { _debounceTimer.Stop(); } catch { }
                    _debounceTimer = null;
                }
                lock (_pendingSync) _pendingPaths.Clear();
                _runCts = new CancellationTokenSource();
            }

            public void Dispose()
            {
                if (_host.Dispatcher.CheckAccess())
                    StopCore();
                else
                {
                    try { _host.Dispatcher.Invoke(StopCore); } catch { }
                }
            }
        }

        internal void RecordBackgroundIntakeBatch(HeadlessStandardImportOutcome outcome)
        {
            if (outcome?.MoveResult?.Entries == null || outcome.MoveResult.Entries.Count == 0) return;
            if (outcome.MoveResult.Moved <= 0) return;

            var batch = new BackgroundIntakeActivityBatch { Id = Guid.NewGuid(), CompletedUtc = DateTime.UtcNow };
            foreach (var e in outcome.MoveResult.Entries)
            {
                if (e == null) continue;
                var snap = BackgroundIntakeActivitySession.CloneEntry(e);
                var ruleLabel = "—";
                try
                {
                    var pr = filenameParserService.Parse(e.ImportedFileName, libraryRoot);
                    if (pr != null && pr.MatchedConvention)
                        ruleLabel = string.IsNullOrWhiteSpace(pr.ConventionName) ? (pr.ConventionId ?? "—") : pr.ConventionName;
                }
                catch
                {
                }

                var resolved = importService.TryResolveUndoEntryCurrentPath(snap);
                if (string.IsNullOrWhiteSpace(resolved)) resolved = snap.CurrentPath ?? string.Empty;

                batch.Rows.Add(new BackgroundIntakeActivityRow
                {
                    UndoSnapshot = snap,
                    FileLabel = e.ImportedFileName ?? string.Empty,
                    SourceFolder = e.SourceDirectory ?? string.Empty,
                    RuleLabel = ruleLabel,
                    ResolvedLibraryPath = resolved
                });
            }

            if (batch.Rows.Count == 0) return;
            _backgroundIntakeActivitySession.AddBatch(batch);
            _ = Dispatcher.BeginInvoke(new Action(delegate
            {
                ReloadBackgroundIntakeActivityWindowIfOpen();
                ReloadSystemTrayStatusFlyoutIfOpen();
            }));
        }
    }
}
