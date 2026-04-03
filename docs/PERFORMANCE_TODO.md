# PixelVault Performance To Do

Focused backlog for responsiveness, scalability, and long-operation polish in the live native app.

**Refactor contract** (tiered MainWindow bar, background async expectations, file-system scope): `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md`.

Use this alongside:

- `C:\Codex\PixelVaultData\TODO.md` for general rolling tasks
- `C:\Codex\docs\ROADMAP.md` for long-term sequencing

Source notes folded into this list:

- `C:\Codex\docs\LIBRARY_UI_SCALABILITY_NOTES.txt`
- `C:\Codex\docs\pixelvault_service_split_plan.txt`
- `C:\Codex\docs\PERFORMANCE_FIX_PLAN.txt`

## Review notes (consolidated, Mar 2026)

External and in-repo review both agree on the following (0.815 improved runtime behavior but did not remove these structural items):

- **Not duplicate source files:** `MainWindow` is a **`partial` class** split across many `.cs` files under `src/PixelVault.Native/` (e.g. indexing, metadata, import, UI). There is a single primary `PixelVault.Native.cs`; it is large, but it is not “two identical copies” of the same file.
- **God-object weight remains:** `MainWindow` still owns imperative Library UI, settings, metadata flows, indexing, covers, and more. Full **MVVM** is optional; a practical middle path is a **dedicated Library browser type** (see item 10) plus **service seams**, matching the modular-monolith direction in the changelog and split plan.
- **`ShowLibraryBrowser` closure depth:** Many nested `Action` delegates still make debugging and testing harder; extracting a named type does not require MVVM.
- **Silent failures:** Several **`catch { }`** (empty) sites remain across the native project; they hide production failures and slow diagnosis. Prefer logging or a small `TryLog` helper unless swallowing is explicitly safe.
- **Shared cache thread safety:** `fileTagCache`, `fileTagCacheStamp`, and `folderImageCache` / `folderImageCacheStamp` are touched from **background library work** and from **UI-driven metadata** paths. Without **locking** or a **single owning service**, this is a **correctness risk** (rare races, harder-to-repro bugs), not just style. Treat as higher priority than a broad UI rewrite.

## Priority Now

1. True virtualization for the per-folder capture grid.
- Replace the current `thumbScroll` + `thumbContent` queued row append model with a bounded viewport-driven list.
- Goal: a folder with thousands of captures should not leave thousands of tiles alive in the visual tree.
- This is the clearest highest-impact Library performance win.
Status: landed; keep validating scroll stability and off-screen work cancellation on mixed media folders.

2. Cache folder sort keys during library cache/index builds.
- Add a stored `NewestCaptureUtc`-style field to folder models/cache data.
- Stop recomputing newest-file timestamps during every `renderTiles` pass, especially for `Recently Added`.
- Goal: make sort changes, refreshes, and search rerenders avoid repeated file-system walks.
Status: landed for current cache/index paths; legacy cache fill now uses the ordered cached file list instead of rescanning whole folders.

3. Debounce Library search input.
- Add a short debounce window around `searchBox.TextChanged` before calling `renderTiles`.
- Goal: prevent large libraries from re-filtering and re-sorting on every keystroke.
Status: landed; Library filtering now uses a committed debounced search term so still-typing and case-only changes do not trigger pointless rerenders.

4. Add a small Library performance instrumentation pass.
- Log timing for folder-cache rebuild, folder sorting, search/filter passes, and selected-folder capture rendering.
- Goal: make future regressions easier to spot before they become “the Library feels slow.”
Status: mostly in place; extend only if the next debounce pass needs more visibility.

## Next

4a. **Serialize or own shared metadata/thumbnail caches (correctness + perf).**
- `fileTagCache` / `fileTagCacheStamp` and `folderImageCache` / `folderImageCacheStamp` must not be mutated concurrently from the UI thread and thread-pool library loads without coordination.
- Options: one **`lock`** per cache group, **`ConcurrentDictionary`**, or move caches behind a small **`IMetadataTagCache`** / file service used only from defined call paths.
- Goal: eliminate data races; make future batching and profiling safe.
- Status: landed for current hotspots — **`LibraryWorkspaceContext`** now locks **`_folderListingSync`** for per-folder image path listings (alongside existing **`_fileTagCacheSync`** for embedded tags). **`CoverService`** Steam / SteamGridDB in-memory response caches use dedicated locks (**`_steamAppNameCacheLock`**, **`_steamSearchIdCacheLock`**, **`_steamSearchResultsCacheLock`**, **`_steamGridDbResponseCacheLock`**) so parallel cover/metadata work cannot corrupt dictionaries. **`MainWindow`** image LRU cache already uses **`imageCacheSync`**; **`FilenameParserService`** already uses **`sync`** for rule/regex caches.

4b. **Tighten empty `catch` blocks in hot and I/O paths.**
- Audit `catch { }` in `PixelVault.Native`, metadata/media helpers, and virtualization; log at debug or warning unless the exception is expected and harmless.
- Goal: production issues remain diagnosable without spamming users.
- Status: landed — removed bare **`catch { }`** in the audited native paths; failures now **`Log(...)`** via **`MainWindow`** where available, or **`Debug.WriteLine`** for **`TimeoutWebClient`** partial download cleanup (no app **`Log`** hook on that type).

5. Audit long-running UI workflows for real background execution.
- Review scan/rebuild, import/manual import, and cover-fetch paths for any work that can still block the UI thread.
- Keep the current progress-window pattern, but tighten where work starts and where dispatcher marshaling happens.
- Note: **Library folder list** refresh already uses **`Task.Factory.StartNew`** + **`Dispatcher.BeginInvoke`** (`MainWindow.LibraryBrowserOrchestrator.cs`). **Game Index** open loads **`LoadGameIndexEditorRowsCore`** on the thread pool, then **`GameIndexEditorHost.Show(..., preloadedRows)`**. **Reload** uses **`LoadRowsForBackground`** (same core) off the UI thread with a short busy state on the editor (grid + action buttons).
Status: spot-checked (Mar 2026) — **`ShowLibraryMetadataScanWindow`** uses **`libraryScanner.ScanLibraryMetadataIndexAsync`** (scheduled off the UI thread) and marshals progress with **`BeginInvoke`**. Library browser folder snapshot/refresh, selected-folder detail render (**`Task.Run`**), and intake badge count follow the same pattern. **Cover refresh** uses **`Task.Run`** + **`RefreshLibraryCoversAsync`**, which **`await`**s **`ICoverService`** **`*Async`** (no sync **`GetResult()`** on that path). Import workflows use **`RunBackgroundWorkflowWithProgress`** in **`ImportWorkflow.cs`**. Manual metadata **game title** combo: saved rows + fallback **`LoadGameIndexEditorRowsCore(root, null)`** now load off the UI thread (**`refreshGameTitleChoices`**).

5a. Remove the remaining synchronous UI hop from game-capture keyword tagging.
- Replace the `Dispatcher.Invoke` call in `ShouldIncludeGameCaptureKeywords` with the same cached-setting pattern used elsewhere in the metadata path.
- Goal: avoid background metadata/tag work synchronously blocking on the UI thread.
Status: landed — `keywordsBox` updates a `volatile` `_includeGameCaptureKeywordsMirror` on Checked/Unchecked and after Settings `BuildUi`; when `keywordsBox` is null (Library-only), behavior stays “include keywords” as before.

6. Add cancellation-token support across long-running workflows.
- Start with library scan, cover fetch, and import-related work where cancellation already exists conceptually.
- Cover refresh, game-index Steam/SteamGridDB resolution, and library metadata scan now cancel active provider or ExifTool work; remaining work is the manual search/provider paths that still only stop between work items.
- Goal: make cancellation more uniform and reduce “wait for current file/batch” rough edges.
Status: landed for the currently known paths, including manual Steam search in the metadata editor.

7. Isolate metadata and library scan work behind services.
- Prioritize `IMetadataService` and `ILibraryScanner` extractions from the service split plan.
- Performance reason: once ExifTool/process work and library scanning stop living directly in `MainWindow`, it becomes much easier to batch, cache, profile, and move work off the UI thread safely.
- Status (Mar 2026) — **`LoadGameIndexEditorRowsCore`** uses **`ILibraryScanner.EnsureGameIndexFolderContext`** (cached folders + forced rebuild when empty + log via host). Library browser folder refresh and **`SaveGameIndexEditorRows`** folder snapshot for alias alignment call **`libraryScanner` / `librarySession.Scanner`** **`LoadLibraryFoldersCached`** directly. **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`** owns the manual-metadata **finish** loop (normalize, match row, set Steam AppID, call injected save); **`MainWindow`** still supplies resolve/platform/grouping/save funcs via **`ImportServiceDependencies`** until a dedicated game-index service absorbs them. **Next:** remaining game-index / metadata orchestration that still bypasses **`ILibraryScanner`** where a seam exists.

8. Add an `IFileSystemService` seam around heavy file-system operations.
- Wrap directory enumeration, file existence, timestamp reads/writes, and path-heavy scans.
- Performance reason: centralizing file I/O makes caching and batched enumeration much easier later.
- Status (Mar 2026) — **`IFileSystemService`** + **`FileSystemService`** in **`Services/IO/`** (`FileExists`, **`DirectoryExists`**, **`EnumerateDirectories`**, **`EnumerateFiles`**, **`ReadAllLines`**, **`WriteAllLines`**, **`DeleteFile`**, **`MoveFile`**, **`CopyFile`**, **`CreateDirectory`**, **`GetLastWriteTime`**). **`LibraryScanner`**, **`ImportService`**, **`CoverService`** (optional dep), migration **`Copy*`** helpers on **`MainWindow`**. Tests: **`FileSystemServiceTests`**. **Next:** stream-based copy only if needed.

## Later

9. Recycle row elements on the Library folder side.
- The left-side row virtualizer is already decent, but it still clears and rebuilds row trees instead of reusing them.
- Useful, but clearly lower priority than capture virtualization.
- Status: landed (Mar 2026) — **`VirtualizedRowHost.RecycleVisibleRowElements`** + per-row-index cache in **`UI/LibraryVirtualization.cs`**; enabled for the Library **folder** scroll host only (**`tileRows`**). **Detail** pane keeps recycling off because **`BeforeVisibleRowsRebuilt`** / **`detailTiles`** expect each visible rebuild to run **`Build()`**. Cache clears on **`SetVirtualizedRows`** and when the model has zero rows.

10. Move `ShowLibraryBrowser` orchestration into a dedicated Library UI type.
- This is mostly an **iteration-speed and testability** improvement; it also reduces closure tangle and makes threading/cancellation easier to reason about.
- It becomes more valuable once capture virtualization and search/sort caching are underway (many of those are now landed—bump priority if the next perf pass touches Library UI again).
- Status (Mar 2026): **`LibraryBrowserHost`** (`UI/Library/LibraryBrowserHost.cs`) is the entry point (receives **`ILibrarySession`**). **`MainWindow.ShowLibraryBrowserCore`** lives in **`UI/Library/MainWindow.LibraryBrowserOrchestrator.cs`** (dedicated partial file for Library browser UI; same **`MainWindow`** type, no behavior change). **E2 slice:** **`ILibrarySession`** / **`LibrarySession`** surface workspace + scanner + file seam + root. Next: optional further shrink (named sub-handlers / less closure depth) when Library UI is touched again.

11. Convert touched I/O and provider paths to async-first service APIs.
- Especially metadata reads, file-heavy scans, and network/provider work.
- Do this gradually after the service seams exist so the churn stays bounded.
- Status (Mar 2026) — **`TimeoutWebClient.DownloadStringAsync` / `DownloadFileAsync`** (true async HTTP + file write); sync **`DownloadString` / `DownloadFile`** delegate to them; response bodies are capped (**`MaxStringResponseBytes`** default 4 MiB, **`MaxFileDownloadBytes`** default 48 MiB; `≤ 0` disables). **`IMetadataService`** — **`ReadEmbeddedKeywordTagsBatchAsync`** / **`ReadEmbeddedMetadataBatchAsync`** (**`Task.FromResult`** wrappers; ExifTool remains synchronous—call off UI). **`LibraryScanner`** metadata upsert uses **`ReadEmbeddedMetadataBatch`** directly (no **`GetResult()`** on the async wrapper). Manual metadata **Steam search** uses **`SearchSteamAppMatchesAsync`** + **`Task.Run`**. Library **detail** render **`await`**s **`ReadEmbeddedMetadataBatchAsync`** inside **`Task.Run`**. Library **cover refresh** **`await`**s **`ICoverService`** **`*Async`**. **Game index** “Resolve IDs” uses **`ResolveMissingGameIndexSteamAppIdsAsync`** / **`ResolveMissingGameIndexSteamGridDbIdsAsync`** (**`await`** **`ResolveBestLibraryFolder*Async`** + cover **`TryResolve*Async`**). **Import-and-edit** Steam title refresh when the loaded title is unchanged is **`IImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`**, which **`await`**s **`ICoverService.SteamNameAsync`** per eligible row. Network work stays async, but **`ManualMetadataItem.GameName`** assignments must run on the **UI synchronization context** captured when the method starts: **`ImportService`** uses default **`ConfigureAwait(true)`** after **`SteamNameAsync`** (do not resume with **`ConfigureAwait(false)`** before mutating live grid rows); **`MainWindow`** **`await`**s the service with **`ConfigureAwait(true)`**. Callers without a UI context must **`Dispatcher`**-marshal row updates. **Next:** any remaining sync **`CoverService`** entry points still worth migrating when touched.

## Suggested Order

1. ~~Capture virtualization~~ (landed—keep validating)
2. ~~Cached sort keys~~ (landed—keep validating)
3. ~~Search debounce~~ (landed)
4. ~~Instrumentation~~ (mostly landed)
5. ~~**Shared cache thread safety (4a)**~~ and ~~**empty-catch audit (4b)**~~ (landed—keep an eye on new caches / catches in future PRs)
6. Background-thread audit (item 5); ~~5a `Dispatcher.Invoke` cleanup~~ (landed)
7. Cancellation cleanup (item 6—largely landed; spot-check new paths)
8. Metadata + library scanner service extraction (item 7)
9. ~~File-system service seam (item 8)~~ — extended slice landed; optional **`CopyFile`** later
10. ~~Left-side row recycling (item 9)~~ — landed (`RecycleVisibleRowElements` on folder **`tileRows`**)
11. ~~Library UI extraction (item 10)~~ — landed (`LibraryBrowserHost` + `ShowLibraryBrowserCore` in **`MainWindow.LibraryBrowserOrchestrator.cs`**); optional deeper splits when Library changes
12. ~~Async-first I/O (item 11)~~ — landed for **`TimeoutWebClient`**, **`IMetadataService`** / **`ICoverService`** `*Async`**, **`LibraryScanner`** batch reads (**`ReadEmbeddedMetadataBatchAsync`** + **`SemaphoreSlim`**), and library **cover refresh** (**`RefreshLibraryCoversAsync`** **`await`** chain).
13. ~~`IFileSystemService` seam (item 8)~~ — landed (**`LibraryScanner`**, **`ImportService`**, extended read/write/move/timestamp helpers).

## What I Would Prioritize

If we want the best near-term payoff with the least product risk, do these first:

1. ~~**Shared cache correctness (4a)**~~ (landed for folder listings + cover HTTP caches; re-check when adding caches)  
   - Prevents rare corruption/weird Library state under load; unlocks safer parallel work later.

2. ~~**Game-capture keyword `Dispatcher.Invoke` removal (5a)**~~ (landed)  
   - Stops background metadata from stalling on the UI thread.

3. ~~**Empty-catch audit (4b)**~~ (landed in audited paths)  
   - Cheap; improves supportability when something regresses.

4. ~~**Library UI extraction (10)**~~ — first slice landed; extend the host/facade when the next Library pass needs it.

5. **Service extraction (7–8)** as the enabler for a second wave of batching and async APIs.

Full **MVVM** is not required for these wins; prefer **named types + services + clear thread boundaries** unless you explicitly want a UI-framework rewrite.
