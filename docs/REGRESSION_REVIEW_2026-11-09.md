# Regression review follow-up - 2026-11-09

Work against the maintained `pixelvault-test-source/Codex` checkout on `sync/desktop-work-2026-11-06`, not the historical `C:\Codex\src` tree. These corrections do not publish, deploy, or modify live application data.

## Corrections

- Ranked order tracker controls intentionally use compact 94px LL/LLL buttons. Update the two width cases and the obsolete two-child status expectation. Verify both rank labels and accessible completion descriptions. Non-ranked completion controls retain their 108px expectation.
- The local refresh API reads the shared one-second coordinator snapshot. The runtime integration test must wait for the endpoint to observe the completed tick, with a bounded timeout, rather than assert synchronously against an older snapshot.

## Reproduction

Native UI tests use WPF process-global schema resources. A broad parallel run produced an intermittent `XamlParseException` (`IsOpen` missing from the schema dictionary); the same 349 cases pass with a single xUnit worker. Use the serialized command below for reproducible UI regression validation; no production UI behavior was changed for this harness race.

```powershell
dotnet test tests/PixelVault.Native.Tests --no-restore --filter 'FullyQualifiedName~DeathStranding|FullyQualifiedName~Century|FullyQualifiedName~Centurion|FullyQualifiedName~Refresh|FullyQualifiedName~AchievementTracking|FullyQualifiedName~TrackerDeletion' -- xUnit.MaxParallelThreads=1
dotnet test tests/PixelVault.Companion.Tests --no-restore
```

Browser fixtures resolve Playwright through `NODE_PATH` and launch installed Microsoft Edge. On this workstation the existing package is at `C:\Codex\tmp\century-browser-tools\node_modules` (Playwright 1.64.1). No package install or browser download was needed. On another workstation, point `NODE_PATH` at its own Playwright installation.

```powershell
$env:NODE_PATH='C:\Codex\tmp\century-browser-tools\node_modules'
node --test --test-concurrency=1 tests/*.browser.cjs
```

All 24 browser tests passed against local mock servers, including responsive layouts, navigation, photos, maps, Centurion rewards, streamed updates, retries, and saved-progress notifications. The focused native tracker suite passed 35 tests; the broader serialized native selection passed 349 tests.

The standalone JavaScript suite (`node --test tests/*.test.cjs`) passed all 82 tests. The corrected connection-status case also passed independently.

After the snapshot timing correction, the full Companion .NET suite passed all 211 tests (zero failures or skips). Evidence is retained locally under `C:\Codex\artifacts\regression-followup-20261109`.
