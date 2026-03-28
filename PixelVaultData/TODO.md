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

## Current Focus
1. Keep moving through Phase 2 UI-thread responsiveness and cancellation work.
- Finish the remaining cancellation and long-operation consistency cleanup before deeper scanner/service work.

2. Keep repo docs and Notion in sync when releases, phase status, or workflow rules change.
- Follow `C:\Codex\docs\DOC_SYNC_POLICY.md` instead of relying on memory.

3. Pull performance-specific work from the dedicated backlog instead of mixing it into this general list.
- Start with capture virtualization, cached sort keys, and debounced Library search.

## As I Think Of It
- Add a recurring automation that checks repo docs vs Notion for drift:
  current build/version, release entry coverage, roadmap phase status, and handoff/current-build mismatches.
- Remove the duplicated `refreshButton.IsEnabled = false;` line in the Library refresh block.
- Add a short comment near the SQLite `PRAGMA foreign_keys=OFF` line explaining the intentional tradeoff.
- Do a targeted audit of `TimeoutWebClient` call sites and note which ones are guaranteed off the UI thread.
- Start a manual golden-path checklist for risky changes:
  Library -> Refresh -> Import one file -> verify SQLite row -> reopen Library
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

