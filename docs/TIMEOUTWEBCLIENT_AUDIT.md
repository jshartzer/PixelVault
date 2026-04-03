# TimeoutWebClient Audit

Audit date: 2026-03-27

Updated: 2026-03-29

Scope:

- `C:\Codex\src\PixelVault.Native\Infrastructure\TimeoutWebClient.cs`
- cover / Steam lookup call paths that still rely on the synchronous wrapper

## Current Reality

`TimeoutWebClient` is still synchronous internally.

It wraps `HttpClient`, but both `DownloadString` and `DownloadFile` block with `GetAwaiter().GetResult()`.

That means the core rule is:

- do not call `TimeoutWebClient` work directly from the WPF UI thread

What changed on March 29, 2026:

- `TimeoutWebClient` now accepts a `CancellationToken`
- cover-refresh and game-index ID-resolution workflows now pass their workflow tokens into Steam and SteamGridDB requests
- canceling those workflows can stop the active lookup or download instead of waiting only for the next title boundary

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
- background `Task.Run` → `RefreshLibraryCoversAsync(...)` (`await` chain)
- `ResolveBestLibraryFolderSteamAppIdAsync` / `ResolveBestLibraryFolderSteamGridDbIdAsync`
- `TryDownloadSteamCoverAsync` / `TryDownloadSteamGridDbCoverAsync` → `CoverService.*Async`
- `TimeoutWebClient.DownloadStringAsync` / `DownloadFileAsync` (sync wrappers unused on this path)

Status:

- safe in current code

Reason:

- the refresh workflow runs in a background task with progress marshaled back to the UI
- the cover-refresh cancel action now cancels the active Steam / SteamGridDB request as well as the outer loop

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
- the workflow cancellation token now reaches the in-flight Steam / SteamGridDB request path

## Important Non-Issues

### Library tile rendering

Library folder tiles use **`GetLibraryArtPathForDisplayOnly`** (no download); the selected-folder banner runs **`GetLibraryArtPathForDisplayOnly`** + **`File.Exists`** on the thread pool, then **`QueueImageLoad`** on the dispatcher. **`ResolveLibraryArtAsync(folder, false)`** is a completed task (**`Task.FromResult`**) over the same path.

Status:

- safe in current code

Reason:

- the UI render path passes `allowDownload = false`, so it only uses custom cover paths, cached cover paths, or preview-image fallback paths
- it does not trigger network fetches while painting the Library

## Remaining Risks

1. `TimeoutWebClient` itself still invites misuse
- It looks simple to call, but it is still blocking.
- The new comment in `TimeoutWebClient.cs` should help, but this remains a convention risk.

2. Manual Steam search still has no user-driven cancel affordance
- The manual Steam AppID search runs on a background task, which keeps the UI responsive.
- It still does not expose a cancel path to the user if a provider call stalls or they change their mind mid-search.

3. **`ResolveLibraryArtAsync(folder, true)`** must stay off the UI thread (cover refresh already **`await`**s it)
- Current audited usages are backgrounded.
- Future callers should not use the download-enabled path from direct UI rendering or click handlers without a worker/task boundary.

## Conclusion

No current high-priority UI-thread blocking issue was found in the audited `TimeoutWebClient` call paths.

The current risk is not “this is still blocking the UI today.”

The remaining risk is:

- future direct calls from UI code
- download-enabled helper paths being reused without a background task boundary
- long-running workflows that still have cancellation only around the outer loop rather than the active provider call

## Suggested Follow-Up

1. Keep new Steam / cover call sites inside background workflows only
2. Extend the same request-level cancellation pattern to the remaining provider-backed workflows that still only poll between work items
3. If the web layer gets touched again, prefer introducing async-first service APIs behind `CoverService`
