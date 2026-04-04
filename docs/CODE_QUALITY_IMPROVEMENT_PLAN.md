# PixelVault code quality and hardening plan

**Status (Apr 2026):** Several original review items are **landed** (HTTP caps, empty-catch audit on audited paths, cover-cache locking, etc.). **Full table snapshot** from the first pass: `docs/archive/CODE_QUALITY_IMPROVEMENT_PLAN_HISTORICAL.md`.

**Notion:** [PixelVault code quality and hardening plan](https://www.notion.so/33573adc59b681ce906cf060554f1b2d) — keep in sync per `DOC_SYNC_POLICY.md` when milestones change.

---

## Addressed / landed (do not track as open)

| Topic | Where / notes |
|--------|----------------|
| Unbounded HTTP string/file download bodies | `TimeoutWebClient` byte caps (`MaxStringResponseBytes`, `MaxFileDownloadBytes`, stream-limited reads). |
| Empty `catch` in audited hot paths | Replaced with logging / narrower handling (`PERFORMANCE_TODO` 4b). Re-audit when adding new I/O. |
| `CoverService` dictionary races | Locks on Steam/SteamGridDB caches (e.g. `_steamGridDbResponseCacheLock`, related cache fields in `CoverService.cs`). |
| Game-capture keyword `Dispatcher.Invoke` | Mirror flag on settings / `MainWindow` (`PERFORMANCE_TODO` 5a). |

---

## Active backlog

### Structure and docs

| Issue | Why it matters | Next step |
|--------|----------------|-----------|
| `MainWindow` / `PixelVault.Native.cs` still central | Conflicts and reasoning cost | Incremental extractions when touching an area (`HANDOFF.md`, `PERFORMANCE_MONOLITH_SLICE_PLAN.md`, `ARCHITECTURE_REFACTOR_PLAN.md`). |
| Steam rename rules scattered | One path fixed, another regresses | Single docblock or small doc: AppID prefix, `gameTitleHint` + `_`, canonical skip; align with `SteamImportRename.cs` tests. |

### Bugs / edge cases

| Issue | Why it matters | Next step |
|--------|----------------|-----------|
| `TryBuildSteamRenameBase` title-hint branch | Unicode/spacing vs filesystem | Normalize hint + basename before compare, or document exact capture contract. |
| `SteamAppIdLooksLikeFilenamePrefix` | Long digit-prefix filenames | Require separator after AppID and/or length bounds (`SteamImportRename.cs`). |
| `BuildLibraryFolderInventoryStamp` | NAS / huge trees | Session cache, incremental stamp, or ensure caller never blocks UI for full enumeration. |

### Performance / UX polish

| Issue | Why it matters | Next step |
|--------|----------------|-----------|
| Library virtualization `SizeChanged` → `BeginInvoke` | Resize storms | Debounce like Library search (one timer / coalesce). |
| Filename rules → `Regex` | CPU + complexity | Keep cache; `RegexOptions.NonBacktracking` where safe; validate length/complexity on save. |

### Security / correctness

| Issue | Why it matters | Next step |
|--------|----------------|-----------|
| User-defined regex (ReDoS) | Pathological patterns | Length limits, NonBacktracking, optional match timeout. |
| `Process.Start` / open-folder on derived paths | Odd extensions / schemes | Normalize, `File.Exists`, reject non-file URIs; prefer `explorer /select` where appropriate. |
| SteamGridDB token | Must not leak in logs | Confirm no diagnostic path logs raw headers; sanitize `ex` if it ever includes request metadata. |
| SQLite | Injection risk | Prefer parameters everywhere; occasional grep for interpolated SQL when editing storage. |
| `TimeoutWebClient` sync wrappers | Wrong context → deadlock | Prefer async call sites; document “background-only” for any remaining sync entry points. |

---

## Suggested order (remaining)

1. **Steam rename** — separator/length on `SteamAppIdLooksLikeFilenamePrefix`; optional normalization for title-hint (good test ROI).  
2. **`SizeChanged` debounce** — if resize jank shows up in traces.  
3. **Regex / ReDoS** — when touching `FilenameRulesService` or convention editor save path.  
4. **MainWindow shrink** — only with functional work in that file.  
5. **SQLite / Process.Start / log redaction** — opportunistic audits when those subsystems change.

---

## References

- `C:\Codex\docs\PERFORMANCE_TODO.md` — responsiveness + monolith follow-through  
- `C:\Codex\docs\archive\README.md` — other historical plan snapshots  
