# PV-PLN-PERF-001 - App speed and efficiency

Status: complete, shipped in `0.077.000` on 2026-05-07.

Canonical plan:

- `C:\Codex\docs\plans\PV-PLN-PERF-001-app-speed-and-efficiency.md`

Verification evidence:

- `C:\Codex\docs\perf\PV-PLN-PERF-001-step-29-manual-run.md`
- `C:\Codex\docs\perf\PV-PLN-PERF-001-step-29-manual-results.md`
- `C:\Codex\docs\perf\PV-PLN-PERF-001-latest-manual-run.md`

## Outcome

This initiative made PixelVault's high-touch workflows do less repeated work and keep the UI responsive during larger operations.

Shipped outcomes:

- Import prep reuses one source inventory / intake-analysis pass where possible.
- Post-import and metadata-edit refreshes prefer touched-folder cache updates instead of full recursive refreshes.
- Library open/folder projection can paint cached/indexed rows before deeper embedded metadata repair finishes.
- Import progress and high-volume log output are coalesced/batched.
- Detail-pane metadata work is per-selection cancelable and viewport/overscan-first.
- Virtualized row scrolling coalesces ordinary scroll refreshes at render priority, while page jumps still refresh immediately.
- SteamGridDB picker previews and custom art file saves avoid UI-thread image decode/download or disk copy stalls.

## Release notes

See `C:\Codex\docs\CHANGELOG.md` under `0.077.000`.

## Residual follow-ups

The isolated WPF smoke passed launch, 25/100/500-file foreground imports, large-folder detail open, fast detail scroll, and selection-switch diagnostics. RC testing should still use real throwaway inputs for:

- HDR PNG/JXR same-stem import and duplicate parking.
- SteamGridDB picker save with a real token.
- Right-click custom cover/banner/logo save with real local art.
