# PV-PLN-ACHG-001 manual verification — 2026-08-24

Build under test: local Debug `net8.0-windows/win-x64` build of PixelVault 0.077.000.

## Live provider checks

| Provider | Game | Result |
|----------|------|--------|
| Steam | What Never Was | Game Profile loaded `0 of 6`; **Open achievement guides** reused the fetched rows and displayed all six stable achievement entries. Search narrowed to one row and the Guided filter returned zero for the empty local guide store. |
| RetroAchievements | Golden Sun | Game Profile loaded `0 of 60`; **Open achievement guides** displayed the provider-backed rows with numeric identities. Search for `Washed Away` narrowed the list to one row. |

## UI and data checks

- Visually inspected the 1560 × 1140 Guide window: list, editor fields, source controls, import actions, Save, and Close fit without clipping.
- UI Automation exposed names for search, filter, achievement list, editor fields, missable state, import actions, source action, Save, and Close.
- The smoke pass did not author or import guide text, so it did not alter the user's guide content.
- The test process was closed after verification; the generated local catalog contained no authored guide text and is ignored as runtime data.

## Automated verification

- `dotnet test .\tests\PixelVault.Native.Tests\PixelVault.Native.Tests.csproj --no-restore`: 631 passed, 0 failed, 0 skipped.
- `dotnet build .\src\PixelVault.Native\PixelVault.Native.csproj --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed.
