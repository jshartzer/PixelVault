# PixelVault To Do

This file is the lightweight rolling backlog.

Use it for:

- small follow-ups
- opportunistic cleanup
- bugs to remember
- quick wins that do not need a full roadmap phase

For longer-term sequencing and major priorities, use:

- `C:\Codex\docs\ROADMAP.md`

## Current Focus
1. Build the Phase 1 safety net from the roadmap.
- Add a small test project and cover the highest-value pure/storage logic first.

2. Keep an eye on thread-blocking or long-operation rough edges as they come up.
- Especially around cover fetches, refresh/rebuild flows, and any `TimeoutWebClient` usage.

## As I Think Of It
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

