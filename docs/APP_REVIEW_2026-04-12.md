# PixelVault App Review

Date: 2026-04-12

**Follow-up (repo):** Remediation and sequencing live in **[`docs/plans/open/PV-PLN-RVW-001-post-app-review-hardening.md`](plans/open/PV-PLN-RVW-001-post-app-review-hardening.md)** (**open plans** folder). **`docs/NEXT_TRIM_PLAN.md`** baseline was **fully refreshed 2026-04-12** (version train, measured line counts)—Finding 3 below describes the **pre-refresh** state for audit trail.

Scope:
- app/code review
- policy/history/docs review
- build and test health

## Summary

PixelVault is in solid shape overall.

- `dotnet build` for `src/PixelVault.Native/PixelVault.Native.csproj` passed with `0` warnings and `0` errors.
- Tests passed for both suites:
  - `PixelVault.Native.Tests`: `275/275`
  - `PixelVault.LibraryAssets.Tests`: `8/8`
- The repo has strong operating docs (`POLICY.md`, `HANDOFF.md`, `PROJECT_CONTEXT.md`, `CHANGELOG.md`) and the modular-monolith cleanup is clearly moving in the right direction.

The main review outcome is not that the app is unstable. It is that a few remaining risk areas are now more important than broad structural cleanup.

## Findings

### Finding 1

Priority: `P1`

File:
- `src/PixelVault.Native/Services/FilenameParsing/FilenameParserService.cs:940-945`
- related validation path: `src/PixelVault.Native/Services/FilenameRules/FilenameRulesService.cs:234`

Title:
- Guard custom filename regex execution

Why it matters:

Non-readable filename-rule patterns are passed through as raw regex and later compiled/executed without a timeout or `NonBacktracking`.

Because these rules are user-authored and reused during parsing, one pathological pattern can stall:
- imports
- scans
- the filename convention editor

Recommendation:

- add save-time limits for user-authored regex patterns
- prefer `RegexOptions.NonBacktracking` where compatible
- add execution-time match timeouts
- add tests around pathological or overly-complex rule inputs

### Finding 2

Priority: `P2`

File:
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserPhotoHero.cs:211-217`
- related cache behavior: `src/PixelVault.Native/Services/Covers/CoverService.cs:821-835`

Title:
- Deduplicate hero-banner downloads per title

Why it matters:

The photo workspace starts a new uncancelled background banner fetch on every cache miss.

Combined with hero-cache purge/rewrite behavior in `CoverService`, rapid selection changes can:
- trigger duplicate requests for the same title
- race on the same cache file
- create avoidable background churn in the photo workspace

Recommendation:

- track per-title in-flight banner downloads
- deduplicate or coalesce repeated requests
- cancel or ignore stale selection work where possible

### Finding 3

Priority: `P2`

File:
- `docs/NEXT_TRIM_PLAN.md:1-11`

Title:
- Refresh next-work plan to the current release line

Why it matters (at review time):

The planning doc described the repo as post-`0.854` and said `PixelVault.Native.cs` was about `2.9k` lines. That was stale vs the **`0.075.xxx`** train and extracted tree, and it steered ROI away from real hotspots.

**Status (2026-04-12):** **`docs/NEXT_TRIM_PLAN.md`** was refreshed (current publish pointer, measured hotspots, tier notes). Treat this finding as **addressed** in-repo; keep **`CHANGELOG`** / **`APP_REVIEW`** text as historical audit unless you intentionally rewrite the review body.

## Recommended Next Work

### 1. Regex hardening slice

This is the highest-value next step.

Why:
- highest risk reduction for the least code churn
- directly addresses the only `P1` finding
- easy to bound with tests

Good target areas:
- `src/PixelVault.Native/Services/FilenameParsing/FilenameParserService.cs`
- `src/PixelVault.Native/Services/FilenameRules/FilenameRulesService.cs`
- `tests/PixelVault.Native.Tests/FilenameRulesServiceTests.cs`

### 2. Hero/banner request dedupe

This is the best follow-up after regex hardening.

Why:
- likely to reduce quiet jank and redundant background work
- small enough to implement without broad architectural churn
- well-aligned with the repo’s responsiveness goals

Good target areas:
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserPhotoHero.cs`
- `src/PixelVault.Native/Services/Covers/CoverService.cs`

### 3. Refresh the planning doc, then pick the next real structural hotspot

After the two fixes above, the next structural step should be chosen from current hotspots, not older planning assumptions.

Likely candidates:
- `src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs`
- `src/PixelVault.Native/UI/Editors/FilenameConventionEditorWindow.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs`

## Supporting Notes

What looks strong right now:
- release/build hygiene
- test coverage breadth
- policy and handoff documentation
- incremental extraction strategy
- service/session seams already introduced around library workflows

What still deserves attention:
- user-authored regex safety
- background-work dedupe/cancellation in a few UI paths
- keeping planning docs current enough that they remain decision tools instead of historical snapshots

## Verification

Commands run:

```powershell
dotnet build C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj -c Release
dotnet test C:\Codex\tests\PixelVault.Native.Tests\PixelVault.Native.Tests.csproj -c Release
dotnet test C:\Codex\tests\PixelVault.LibraryAssets.Tests\PixelVault.LibraryAssets.Tests.csproj -c Release
```

Results:
- build passed
- native tests passed: `275/275`
- library assets tests passed: `8/8`

## Notes

This review was based on:
- source inspection
- active project docs and history docs
- build/test verification

It was not a hands-on interactive WPF smoke test, so UI comments are code-based rather than runtime-repro-based.
