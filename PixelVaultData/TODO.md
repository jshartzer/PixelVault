# PixelVault To Do

This file is the lightweight rolling backlog.

Use it for:

- small follow-ups
- opportunistic cleanup
- bugs to remember
- quick wins that do not need a full roadmap phase

For longer-term sequencing and major priorities, use:

- `C:\Codex\docs\ROADMAP.md`

For the focused performance backlog, use:

- `C:\Codex\docs\PERFORMANCE_TODO.md`

For **code quality, security hardening, and structural improvements** from the March 2026 review, use:

- `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md` (full checklist: quality, bugs, performance, security, practices)
- Notion: [PixelVault code quality and hardening plan](https://www.notion.so/33573adc59b681ce906cf060554f1b2d) (under **Project Wiki**; mirror of the doc; update both when status changes per `DOC_SYNC_POLICY.md`)

For **splitting `MainWindow` / shrinking `PixelVault.Native.cs`**, use:

- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` (phases A–F; ties to `ROADMAP.md` Phase 3)
- Notion: [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) (under **Project Wiki**; mirror of the doc)
- **Phase B (low-risk extractions) is done in source:** B3 changelog window, B1 `UiBrushHelper.FromHex`, B2 `WorkflowProgressWindow` factory. **Next extraction slice:** Phase C → `UI/Intake/`.

## Current Focus
1. Keep moving through Phase 2 UI-thread responsiveness and cancellation work.
- Cover refresh, game-index ID resolution, and library scan now cancel active provider or ExifTool work instead of waiting only between titles or batches.
- Debounced Library search is in and the quick live validation pass looked good.
- Manual Steam search now supports in-flight cancellation from the metadata editor.
- Library metadata index rows now cache capture timestamps, and the Library detail path batch-backfills missing capture times on first selection.
- Next cleanup: remove the remaining `Dispatcher.Invoke` path from game-capture keyword tagging, then audit the remaining scan/rebuild/import/manual-import paths for work that still starts too close to the UI thread.

2. Keep repo docs and Notion in sync when releases, phase status, or workflow rules change.
- Follow `C:\Codex\docs\DOC_SYNC_POLICY.md` instead of relying on memory.

3. Pull performance-specific work from the dedicated backlog instead of mixing it into this general list.
- Capture virtualization, cached sort keys, debounced Library search, manual provider-search cancellation cleanup, and cached library capture timestamps are in; next up is the keyword-threading cleanup, then the broader background-thread audit.

4. Rebuild the Filename Rules screen from a workflow spec instead of adding more controls to the current dense grid.
- Use `C:\Codex\docs\FILENAME_RULES_FORM_SPEC.md` as the source of truth for the next UI pass.

## Code quality and hardening (from review)

Work items live in `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md`. Short picks:

- Cap `TimeoutWebClient` / HTTP string download size; avoid buffering unbounded bodies.
- Audit empty `catch` blocks; log or narrow exception types.
- Make `CoverService` caches thread-safe if cover work runs in parallel (`ConcurrentDictionary` or locking).
- Tighten Steam rename “numeric prefix” detection (separator + length); consider normalized title vs hint.
- Debounce Library virtualization `SizeChanged` refresh (similar to search debounce).
- ReDoS guardrails for user-defined filename convention regexes.
- Redact SteamGridDB token from any HTTP error logging.
- Continue peeling `MainWindow` into partials/services; document Steam rename rules in one place.

## As I Think Of It
- Add a recurring automation that checks repo docs vs Notion for drift:
  current build/version, release entry coverage, roadmap phase status, and handoff/current-build mismatches.
- Remove the duplicated `refreshButton.IsEnabled = false;` line in the Library refresh block.
- Add a short comment near the SQLite `PRAGMA foreign_keys=OFF` line explaining the intentional tradeoff.
- Do a targeted audit of `TimeoutWebClient` call sites and note which ones are guaranteed off the UI thread.
- When adding new extracted services or helper-heavy files, consider enabling nullable for those files/projects first instead of waiting for a repo-wide pass.
- If more WinForms usage gets added beyond the current narrow cases, reassess whether that stack should be consolidated.

## Recently Completed
1. Expanded FFmpeg-backed video handling.
- Added cached clip metadata probing plus richer Library clip actions so videos are more first-class than simple poster-backed tiles.

2. Stress-tested the Library virtualization and lazy-loading paths.
- Hardened resize-heavy folder/detail browsing so layout-only changes preserve scroll position more reliably, and added a repeatable mixed-media stress dataset generator for future verification.

## Working Order
- Pure models and helpers first.
- Storage and indexing second.
- Media-tool wrappers third.
- Import orchestration fourth.
- UI wiring last.

