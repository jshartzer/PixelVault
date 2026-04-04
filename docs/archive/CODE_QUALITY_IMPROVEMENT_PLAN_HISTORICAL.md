# PixelVault code quality and hardening plan — archived snapshot

**Frozen:** This is the pre–Apr 2026 version preserved for context. **Active checklist:** `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md`.

---

# PixelVault code quality and hardening plan

Source: architecture/code review (quality, bugs, performance, security, practices).  
Repo is the source of truth; keep `PixelVaultData/TODO.md` and Notion aligned when items are picked up or closed.

**Notion (Project Wiki):** [PixelVault code quality and hardening plan](https://www.notion.so/33573adc59b681ce906cf060554f1b2d)

---

## 1. Code quality and readability

| Issue | Why it matters | Suggested fix |
|--------|----------------|---------------|
| `MainWindow` spans thousands of lines as a partial “god object” | Hard to reason about lifecycle, dependencies, and tests; merge conflicts cluster | Keep extracting vertical slices (import, library UI, settings) into partials or small services with explicit interfaces; thin orchestration in `MainWindow` |
| Steam rename rules split across parser, game-index lookup, and `ImportWorkflow` | One path can be fixed while another regresses | Single documented module or docblock: AppID-prefix files, title+timestamp replacement, already-canonical skip |

---

## 2. Bugs and edge cases

| Issue | Why it matters | Suggested fix |
|--------|----------------|---------------|
| Title-hint rename assumes exact `GameTitleHint` prefix + `_` | Parser vs filesystem mismatch (spaces, Unicode) can skip rename or mis-align | Normalize hint and basename (e.g. existing `NormalizeGameIndexName` / `NormalizeTitle`) before comparing; or document required exact capture match |
| `SteamAppIdLooksLikeFilenamePrefix` accepts any all-digit prefix | Rare filenames starting with long digit runs could be mis-stripped | Require separator after ID (`_` or `-`) and/or reasonable AppID length bounds |
| `BuildLibraryFolderInventoryStamp` enumerates and sorts all library subfolders | Huge or NAS-backed libraries: slow, can block caller | Cache stamp per session, incremental updates, or move work off UI thread if tied to startup UI |

---

## 3. Performance

| Issue | Why it matters | Suggested fix |
|--------|----------------|---------------|
| `TimeoutWebClient.DownloadString` buffers entire body in memory | Large responses spike memory and GC | **Addressed in app:** **`TimeoutWebClient`** **`MaxStringResponseBytes`** / **`MaxFileDownloadBytes`** with stream read caps (defaults **`DefaultMaxStringResponseBytes`** / **`DefaultMaxFileDownloadBytes`**). |
| Custom filename rules compile to `Regex` | Many rules or invalidations cost CPU | Keep caching; consider `RegexOptions.NonBacktracking` where applicable; validate pattern complexity on save |
| Library virtualization `SizeChanged` → `Dispatcher.BeginInvoke` | Rapid resize queues many refreshes | Debounce like Library search (single timer / coalesce) |

---

## 4. Security

| Issue | Why it matters | Suggested fix |
|--------|----------------|---------------|
| Unbounded HTTP response bodies (string downloads) | DoS / accidental huge payloads | Same as performance cap; fail fast over limit |
| User-editable regex conventions on filenames | ReDoS risk on pathological patterns | Limit pattern length; NonBacktracking where possible; optional timeout/cancel on match |
| `Process.Start` / shell open on library-derived paths | Lower risk with file-only paths; odd extensions can surprise | Normalize full path, `File.Exists`, block non-file schemes; prefer `explorer /select` where appropriate |
| SteamGridDB bearer token in headers | Must not appear in logs or crash dumps | Audit `Log` on HTTP failures; redact `Authorization` in diagnostics |

---

## 5. Best practices

| Issue | Why it matters | Suggested fix |
|--------|----------------|---------------|
| Empty `catch { }` in several hotspots | Silent failures, hard production debug | Catch specific types; at minimum log type + message at debug |
| `CoverService` (and similar) use plain `Dictionary` caches | Parallel cover work can race | `ConcurrentDictionary` or lock; or single-threaded access contract |
| Sync-over-async in `TimeoutWebClient` | Deadlock if called from wrong context | Async at call sites where possible; document “thread-pool only” for sync API |
| SQLite | String-built SQL risks injection | Prefer parameterized commands everywhere (audit grep for interpolated SQL) |

---

## Suggested order (optional)

1. HTTP response caps + log redaction (quick wins, security/perf).  
2. Empty-catch audit in `PixelVault.Native` and `MediaToolHelpers` (observability).  
3. Cover cache concurrency if parallel cover paths are confirmed.  
4. Steam rename edge-case hardening (numeric prefix + normalization).  
5. MainWindow decomposition as ongoing refactor when touching an area.

---

## References

- `C:\Codex\docs\PERFORMANCE_TODO.md` — performance-specific backlog  
- `C:\Codex\docs\DOC_SYNC_POLICY.md` — repo / Notion sync when this plan changes status  
