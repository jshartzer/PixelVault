# TimeoutWebClient Audit

Audit date: 2026-03-27

Scope:

- `C:\Codex\src\PixelVault.Native\Infrastructure\TimeoutWebClient.cs`
- cover / Steam lookup call paths that still rely on the synchronous wrapper

## Current Reality

`TimeoutWebClient` is still synchronous internally.

It wraps `HttpClient`, but both `DownloadString` and `DownloadFile` block with `GetAwaiter().GetResult()`.

That means the core rule is:

- do not call `TimeoutWebClient` work directly from the WPF UI thread

## Audited Call Paths

### Manual Intake Steam search

Path:

- `steamSearchButton.Click`
- `Task.Factory.StartNew(...)`
- `SearchSteamAppMatches(...)`
- `CoverService.SearchSteamAppMatches(...)`
- `TimeoutWebClient.DownloadString(...)`

Status:

- safe in current code

Reason:

- the actual lookup runs on a background task and only marshals the result back to the dispatcher

### Library cover refresh / ID resolution

Path:

- `runScopedCoverRefresh(...)`
- background `Task.Factory.StartNew(...)`
- `RefreshLibraryCovers(...)`
- `ResolveBestLibraryFolderSteamAppId(...)` / `ResolveBestLibraryFolderSteamGridDbId(...)`
- `TryDownloadSteamCover(...)` / `TryDownloadSteamGridDbCover(...)`
- `CoverService.*`
- `TimeoutWebClient.DownloadString(...)` / `DownloadFile(...)`

Status:

- safe in current code

Reason:

- the refresh workflow runs in a background task with progress marshaled back to the UI

### Game Index `Resolve IDs`

Path:

- `resolveIdsButton.Click`
- `RunBackgroundWorkflowWithProgress(...)`
- `ResolveMissingGameIndexSteamAppIds(...)`
- `ResolveMissingGameIndexSteamGridDbIds(...)`
- cover-service Steam / SteamGridDB lookups
- `TimeoutWebClient`

Status:

- safe in current code

Reason:

- the resolve workflow is already backgrounded and returns only progress / completion state to the UI

## Important Non-Issues

### Library tile rendering

`ResolveLibraryArt(folder, false)` is used while rendering Library tiles and folder previews.

Status:

- safe in current code

Reason:

- the UI render path passes `allowDownload = false`, so it only uses custom cover paths, cached cover paths, or preview-image fallback paths
- it does not trigger network fetches while painting the Library

## Remaining Risks

1. `TimeoutWebClient` itself still invites misuse
- It looks simple to call, but it is still blocking.
- The new comment in `TimeoutWebClient.cs` should help, but this remains a convention risk.

2. `ResolveLibraryArt(folder, true)` would block if reused on the UI thread
- Current audited usages are backgrounded.
- Future callers should not use the download-enabled path from direct UI rendering or click handlers without a worker/task boundary.

3. There are unused local helper constructors still sitting in `PixelVault.Native.cs`
- `CreateSteamWebClient()`
- `CreateSteamGridDbWebClient()`
- These are not part of the current audited live paths, but they are worth removing later so nobody accidentally revives direct in-window network calls.

## Conclusion

No current high-priority UI-thread blocking issue was found in the audited `TimeoutWebClient` call paths.

The current risk is not “this is still blocking the UI today.”

The real risk is:

- future direct calls from UI code
- download-enabled helper paths being reused without a background task boundary

## Suggested Follow-Up

1. Remove the unused `CreateSteamWebClient` and `CreateSteamGridDbWebClient` helpers from `PixelVault.Native.cs`
2. Keep new Steam / cover call sites inside background workflows only
3. If the web layer gets touched again, prefer introducing async-first service APIs behind `CoverService`
