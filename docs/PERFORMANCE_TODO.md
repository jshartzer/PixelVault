# PixelVault Performance To Do

Focused backlog for responsiveness, scalability, and long-operation polish in the live native app.

Use this alongside:

- `C:\Codex\PixelVaultData\TODO.md` for general rolling tasks
- `C:\Codex\docs\ROADMAP.md` for long-term sequencing

Source notes folded into this list:

- `C:\Codex\docs\LIBRARY_UI_SCALABILITY_NOTES.txt`
- `C:\Codex\docs\pixelvault_service_split_plan.txt`

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

5. Audit long-running UI workflows for real background execution.
- Review scan/rebuild, import/manual import, and cover-fetch paths for any work that can still block the UI thread.
- Keep the current progress-window pattern, but tighten where work starts and where dispatcher marshaling happens.
Status: queued behind the remaining game-capture-keyword threading cleanup.

5a. Remove the remaining synchronous UI hop from game-capture keyword tagging.
- Replace the `Dispatcher.Invoke` call in `ShouldIncludeGameCaptureKeywords` with the same cached-setting pattern used elsewhere in the metadata path.
- Goal: avoid background metadata/tag work synchronously blocking on the UI thread.
Status: next.

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
- This is mostly an iteration-speed improvement, not a direct runtime win.
- It becomes more valuable once capture virtualization and search/sort caching are underway.

11. Convert touched I/O and provider paths to async-first service APIs.
- Especially metadata reads, file-heavy scans, and network/provider work.
- Do this gradually after the service seams exist so the churn stays bounded.

## Suggested Order

1. Capture virtualization
2. Cached sort keys
3. Search debounce
4. Instrumentation
5. Background-thread audit
6. Cancellation cleanup
7. Metadata + library scanner service extraction
8. File-system service seam
9. Left-side row recycling
10. Library UI extraction

## What I Would Prioritize

If we want the best near-term payoff with the least product risk, do these first:

1. Capture virtualization
- Biggest real scalability ceiling today.

2. Cached `Recently Added` sort keys
- Cheap compared with virtualization and likely to make the whole Library feel snappier.

3. Search debounce
- Small change, immediate UX payoff, especially once sort-key work is in.

4. Background-thread audit on scan/import/cover flows
- Less visible when things are healthy, but important for keeping the app from feeling “hung.”

Service extraction matters, but I would treat it as the enabler for the second wave of performance work, not the very first thing to do.
