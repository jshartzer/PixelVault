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
- Notion: [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) (under **Project Wiki**; mirror of the doc; **status updated Mar 2026**)
- **Done in source:** MainWindow extraction **Phases A–F** (library host + `ILibraryBrowserShell`, settings `SettingsShellHost`, photography partial, settings persistence partial, etc.). See `C:\Codex\docs\completed-projects\README.md`.

## Current Focus
1. **Incremental monolith shrink + service routing** (when touching related files).
- Narrow remaining direct persistence/scanner glue in `MainWindow`; prefer `ILibrarySession` / `IGameIndexService` / dedicated partials per `HANDOFF.md` and `PERFORMANCE_TODO.md` item 7.
- Large perf slices (virtualization, debounced search, batch cold index, cancellation, keyword mirror / 5a) are **landed** — track only **new** regressions or optional polish there.

2. Keep repo docs and Notion in sync when releases, phase status, or workflow rules change.
- Follow `C:\Codex\docs\DOC_SYNC_POLICY.md` instead of relying on memory.

3. Pull performance-specific work from the dedicated backlog instead of mixing it into this general list.
- **`C:\Codex\docs\PERFORMANCE_TODO.md`** is the short active checklist; **`docs/archive/`** holds long historical plans that are mostly complete.

4. Rebuild the Filename Rules screen from a workflow spec instead of adding more controls to the current dense grid.
- Use `C:\Codex\docs\FILENAME_RULES_FORM_SPEC.md` as the source of truth for the next UI pass.

## Code quality and hardening (from review)

**Active checklist:** `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md` (landed vs open is split there). **Original full tables:** `C:\Codex\docs\archive\CODE_QUALITY_IMPROVEMENT_PLAN_HISTORICAL.md`.

Remaining highlights: Steam rename edge cases (`SteamAppIdLooksLikeFilenamePrefix`), optional `SizeChanged` debounce, regex/ReDoS guardrails when editing rules, opportunistic SQLite/`Process.Start`/log-hygiene audits, ongoing MainWindow shrink.

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

