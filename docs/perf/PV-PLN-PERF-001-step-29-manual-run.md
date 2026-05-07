# PV-PLN-PERF-001 Step 29 Manual Run Sheet

Use this sheet to close the live WPF/manual side of Phase H step 29. Automated verification is already complete; this pass is for app behavior that cannot be honestly proven from terminal output alone.

## Current automated status

- `dotnet test C:\Codex\tests\PixelVault.Native.Tests\PixelVault.Native.Tests.csproj --no-restore`: passed, 549/549.
- `dotnet build C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj --no-restore`: passed, 0 warnings/errors.
- `git diff --check`: passed with CRLF normalization warnings only.
- Latest log-derived artifact: `C:\Codex\docs\perf\PV-PLN-PERF-001-step-29-manual-results.md`.

## Safety notes

- Completed run used an isolated sandbox rooted at `C:\Codex\.tmp\pv-perf-step29`.
- Source, destination/library, export, logs, saved covers, and settings were redirected through `PIXELVAULT_DATA_ROOT`.
- Background auto-intake was disabled in the sandbox so foreground Import could be measured without watcher races.
- Prefer top-level source files only unless the test explicitly needs subfolders.

## Start marker

Before opening the app for this pass, note the start time in UTC:

```powershell
Get-Date -AsUTC -Format "yyyy-MM-ddTHH:mm:ssZ"
```

After the pass, export a fresh log slice:

```powershell
& 'C:\Codex\scripts\Export-PixelVaultPerformanceBaseline.ps1' -Since '<START-UTC-HERE>' -OutputPath 'C:\Codex\docs\perf\PV-PLN-PERF-001-step-29-manual-results.md'
```

## Required checks

| Check | Result | Notes |
|------|--------|-------|
| App opens and Library becomes usable without a blank/stuck shell. | Pass | Current Debug build launched as `PixelVault 0.076.000` and remained responsive after a clean rebuild restored missing WPF theme resources. |
| Import 25 copied top-level files. | Pass | Generated 25 PS5-pattern PNGs; foreground Import moved 25/25 files and reported 25 metadata updates. |
| Import 100 copied top-level files. | Pass | Generated 100 PS5-pattern PNGs; foreground Import moved 100/100 files and final sandbox library count reached 125 PNGs. |
| Import 500 copied/staged top-level files. | Pass | Generated 500 PS5-pattern PNGs; foreground Import moved 500/500 files, final sandbox count reached 625 PNGs, and the main window stayed responsive. |
| HDR PNG/JXR same-stem pair import. | Not run live | Automated HDR pair filtering/duplicate coverage remains the guardrail; no valid throwaway JXR writer was available in the sandbox. |
| Large-folder photo view. | Pass | Opened the synthetic large folder; `LibraryDetailRender` initial pass was 290 ms with `quickMediaMapMs=14`. |
| Fast-scroll detail pane. | Pass | Mouse-wheel scroll produced `VirtualizedRowHostScroll host=DetailRows mode=render-coalesced` rows. |
| Large-folder scroll diagnostics. | Pass | Switching folders after scroll logged a clean `LibrarySelection`, page-jump reset, and `LibraryDetailRenderApplied` for the new folder. |
| SteamGridDB picker preview and save. | Not run live | No SteamGridDB token in the isolated sandbox; covered by automated picker preview/save guardrails. |
| Right-click custom cover/banner/logo save. | Not run live | Covered by code/test guardrails for off-dispatcher file copy and prior right-click art behavior. |

## Close criteria

- Required isolated WPF checks passed, with HDR/JXR and SteamGridDB/custom-art live checks documented as RC follow-ups rather than blockers for this performance-plan close.
- `PV-PLN-PERF-001-step-29-manual-results.md` is exported and linked from the plan/handoff.
- `docs/plans/PV-PLN-PERF-001-app-speed-and-efficiency.md` is marked complete with the residual synthetic grouping note captured.
