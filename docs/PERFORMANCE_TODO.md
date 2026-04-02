# PixelVault Performance To Do

Focused backlog for responsiveness, scalability, and long-operation polish in the live native app.

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
- Note: **Library folder list** refresh already uses **`Task.Factory.StartNew`** + **`Dispatcher.BeginInvoke`** (`MainWindow.LibraryBrowser.cs`). **Game Index** open loads **`LoadGameIndexEditorRowsCore`** on the thread pool, then **`GameIndexEditorHost.Show(..., preloadedRows)`**. **Reload** uses **`LoadRowsForBackground`** (same core) off the UI thread with a short busy state on the editor (grid + action buttons).
Status: spot-checked (Mar 2026) — **`ShowLibraryMetadataScanWindow`** runs **`libraryScanner.ScanLibraryMetadataIndex`** on the thread pool and marshals progress with **`BeginInvoke`**. Library browser folder snapshot/refresh, selected-folder detail render (**`Task.Run`**), intake badge count, and cover refresh (**`RefreshLibraryCovers`** from **`StartNew`**) follow the same pattern. Import workflows use **`RunBackgroundWorkflowWithProgress`** in **`ImportWorkflow.cs`**. Manual metadata **game title** combo: saved rows + fallback **`LoadGameIndexEditorRowsCore(root, null)`** now load off the UI thread (**`refreshGameTitleChoices`**).

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

8. Add an `IFileSystemService` seam around heavy file-system operations.
- Wrap directory enumeration, file existence, timestamp reads/writes, and path-heavy scans.
- Performance reason: centralizing file I/O makes caching and batched enumeration much easier later.

## Later

9. Recycle row elements on the Library folder side.
- The left-side row virtualizer is already decent, but it still clears and rebuilds row trees instead of reusing them.
- Useful, but clearly lower priority than capture virtualization.

10. Move `ShowLibraryBrowser` orchestration into a dedicated Library UI type.
- This is mostly an **iteration-speed and testability** improvement; it also reduces closure tangle and makes threading/cancellation easier to reason about.
- It becomes more valuable once capture virtualization and search/sort caching are underway (many of those are now landed—bump priority if the next perf pass touches Library UI again).
- Status (Mar 2026): **`LibraryBrowserHost`** (`UI/Library/LibraryBrowserHost.cs`) is the entry point; **`MainWindow.ShowLibraryBrowserCore`** holds the existing implementation on the **`MainWindow`** partial. Next step when touching Library again: pull more logic behind an **`ILibrarySession` / facade** and shrink **`ShowLibraryBrowserCore`**.

11. Convert touched I/O and provider paths to async-first service APIs.
- Especially metadata reads, file-heavy scans, and network/provider work.
- Do this gradually after the service seams exist so the churn stays bounded.

## Suggested Order

1. ~~Capture virtualization~~ (landed—keep validating)
2. ~~Cached sort keys~~ (landed—keep validating)
3. ~~Search debounce~~ (landed)
4. ~~Instrumentation~~ (mostly landed)
5. ~~**Shared cache thread safety (4a)**~~ and ~~**empty-catch audit (4b)**~~ (landed—keep an eye on new caches / catches in future PRs)
6. Background-thread audit (item 5); ~~5a `Dispatcher.Invoke` cleanup~~ (landed)
7. Cancellation cleanup (item 6—largely landed; spot-check new paths)
8. Metadata + library scanner service extraction (item 7)
9. File-system service seam (item 8)
10. Left-side row recycling (item 9)
11. ~~Library UI extraction (item 10)~~ — first slice landed (`LibraryBrowserHost` + `ShowLibraryBrowserCore`); keep iterating when Library changes

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
