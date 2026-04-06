# Library & app performance plan

Companion to the performance deep dive: reduce cold-start cost, folder-cache churn, and navigation jank without changing visible behavior.

**Notion:** [PixelVault — Library performance roadmap](https://www.notion.so/33a73adc59b681b0a45ce71c2799a25d) (child of [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6)).

---

## Step 1 — Folder inventory & cache (**done**)

**Goal:** Fewer unnecessary full `LoadLibraryFolders` rebuilds and cheaper work when computing whether the folder cache is still valid.

| Substep | Description | Status |
|--------|-------------|--------|
| **1.1** | **`BuildLibraryFolderInventoryStamp`**: enumerate direct child folders once, sort **folder names** (not full paths), same rolling hash — less allocation and faster compares on long/NAS paths. | **Done** (`IndexDatabaseStorage.cs`) |
| **1.2** | **Narrow folder cache vs metadata maintenance**: `LibraryFolderCacheRwLock` (`ReaderWriterLockSlim`) for folder-cache disk hits/rebuilds; `LibraryMaintenanceSync` only for metadata index + in-process state touched by scan/upsert/remove/photo save. Folder rebuild runs **after** releasing the maintenance lock (with `RebuildLibraryFolderCache(root, null)` reloading index from disk). **Hazard:** briefly, persisted metadata index may be newer than on-disk folder cache until rebuild completes; UI reading only the folder cache may show a stale game list for that window. Concurrent plain cache hits can share `EnterReadLock`. | **Done** (`LibraryScanner.cs`, `ILibraryScanHost`, `MainWindow`) |
| **1.3** | **Incremental invalidation**: folder cache line 3 stores **metadata index revision** (`length|LastWriteTimeUtc.Ticks`); cache hits require revision match (closes stale UI vs index after 1.2 ordering gap). **`BuildLibraryFolderStructuralStamp`** (`count|nameHash`, no mtimes) for future deltas. **Index-only refresh**: when revision matches, full stamp differs **only** in max child-folder mtime field, and cache v3 header present — rebuild file list from persisted metadata index (one-level-under-game-folder rule), no per-folder `EnumerateFiles` sweep. **Tradeoff:** files on disk not yet in the metadata index are omitted until scan. | **Done** (`MainWindow.LibraryFolderCacheIo.cs`, `LibraryScanner.cs`, `GameIndexCore` alias rewrite preserves header) |

---

## Step 2 — Metadata / ExifTool pipeline (**done**)

**Goal:** Metadata reads should not pretend to be async while blocking a thread; batch work off the UI thread. Steam/cover lookups must not block the WPF dispatcher when an async path exists.

| Substep | Description | Status |
|--------|-------------|--------|
| **2.1** | **`IMetadataService` batch async:** `ReadEmbeddedMetadataBatchAsync` and `ReadEmbeddedKeywordTagsBatchAsync` run ExifTool work on the **thread pool** (`Task.Run` with the same `CancellationToken`), not `Task.FromResult(syncWork)` on the **caller's** thread. Callers that `await` from the UI should still use **`ConfigureAwait(false)`** until the boundary returns to the dispatcher. | **Done** (`MetadataService.cs`) |
| **2.2** | **Library Steam enrichment:** Replace **`.GetAwaiter().GetResult()`** on `ResolveBestLibraryFolderSteamAppIdAsync` with a proper **`async Task<int>`** helper (`EnrichLibraryFoldersWithSteamAppIdsAsync`) so a future or revived call path does not capture the UI thread during HTTP. | **Done** (`PixelVault.Native.cs`; previously no live call sites) |
| **2.3** | **`CoverService` / `ICoverService` audit:** Confirmed production used only <code>*Async</code>; **2.5** removed the redundant sync HTTP surface entirely. | **Done** (superseded by **2.5** for API shape) |
| **2.4** | **Remove blocking `GetResult` bridges:** Dropped **`IImportService.RunSteamRename`** (only **`RunSteamRenameAsync`** remains; **`ImportWorkflow`** already awaited it). Removed dead **`MainWindow.RunRename`** partial methods that called the sync API. **`TimeoutWebClient`**: removed **`DownloadString`** / **`DownloadFile`** sync methods — app uses **`*Async`** only (**`CoverService`**). | **Done** |
| **2.5** | **Finish metadata + cover surfaces:** **`MainWindow` / `MediaToolHelpers`** batch helpers call **`ReadEmbeddedMetadataBatchAsync`** / **`ReadEmbeddedKeywordTagsBatchAsync`** + **`ConfigureAwait(false)`** so ExifTool batch work runs on the thread pool even when the caller is synchronous UI glue. **`LibraryScanner`** still calls **`IMetadataService.ReadEmbeddedMetadataBatch`** directly on parallel workers (no extra **`Task.Run`**). **`ICoverService`**: removed all sync HTTP/cover-download wrappers (**`*Async` only**); **`StubCoverService`** updated. | **Done** |

**Verification:** Dotnet trace / PerfView: awaiting `ReadEmbeddedMetadataBatchAsync` from UI should show CPU on **thread pool** during ExifTool, not on **Main** thread simultaneously for the full batch duration. Opening library metadata edit from folder context uses the async-backed batch path via **`MediaToolHelpers`**.

---

## Step 3 — Detail pane & navigation (**done**)

**Goal:** Switching games should not redo layout/metadata passes when inputs are unchanged; visible captures should win decode bandwidth when scrolling.

| Substep | Description | Status |
|--------|-------------|--------|
| **3.1** | **Viewport-first detail decodes:** Virtual rows that **intersect the live ScrollViewer viewport** (not only the wider overscan band) enqueue bitmap decodes on the **priority** lane (`LibraryImageLoadCoordinator`); overscan-only rows use the **normal** lane. Evaluated when a row is **materialized** (`LibraryDetailTileRowIntersectsViewport` + `CreateLibraryDetailTile` `prioritizeImageDecode`). | **Done** (`LibraryVirtualization.cs`, `MainWindow.LibraryBrowserRender.DetailPane.cs`) |
| **3.2** | **Reuse virtualized detail rows:** `DetailRows.RecycleVisibleRowElements = true` (same pattern as folder tiles). **`RepopulateLibraryDetailTilesFromVisibleRows`** runs in **`AfterVisibleRowsRebuilt`** and walks the visible row visual tree so **`DetailTiles`** stays aligned with recycled rows for selection chrome (build path no longer pushes tiles into the list directly). | **Done** (`MainWindow.LibraryBrowserLayout.cs`, `MainWindow.LibraryBrowserShowOrchestration.cs`, `LibraryVirtualization.cs`) |
| **3.3** | **Trim redundant refined pass:** After metadata repair, the second snapshot is applied **only** when packed day-card **layout** (groups + per-day file order) **differs** from the quick snapshot — unchanged behavior, documented here as part of Step 3. | **Done** (`MainWindow.LibraryBrowserRender.DetailPane.cs`) |

---

## Step 4 — SQLite & index I/O (**done**)

**Goal:** Large libraries pay less for open/save and for library detail work that only touches one folder’s files.

| Substep | Description | Status |
|--------|-------------|--------|
| **4.1** | **Desktop SQLite pragmas:** After `journal_mode=WAL`, set **`synchronous=NORMAL`** (appropriate with WAL), **`busy_timeout=5000`**, **`temp_store=MEMORY`**, **`cache_size=-65536`** (~64 MiB page cache, negative = KiB units). **`foreign_keys`** stays **OFF** (cache-style tables). | **Done** (`InitializeIndexDatabase` in `Services/Indexing/IndexPersistenceService.cs`) |
| **4.2** | **Slice metadata reads:** **`LoadLibraryMetadataIndexEntriesForFilePaths`** loads **`photo_index`** rows only for requested absolute paths (batched `IN` queries). Library detail background render uses **`ILibrarySession.LoadLibraryMetadataIndexForFilePaths(detailFiles)`** instead of loading the entire index. | **Done** (`IndexPersistenceService.cs`, `LibrarySession` / `ILibrarySession`, `MainWindow.LibraryBrowserRender.DetailPane.cs`) |
| **4.3** | **Partial persist without wipe:** **`UpsertLibraryMetadataIndexEntries`** uses **`INSERT … ON CONFLICT DO UPDATE`** so detail metadata repair does not **`DELETE`** the whole `photo_index` for the root. **`MergePersistLibraryMetadataIndexEntries`** upserts then **clears** the in-memory metadata cache so the next full **`LoadLibraryMetadataIndex`** reloads a coherent snapshot (never treat a partial dict as complete). | **Done** (same files) |

---

## Step 5 — Startup & housekeeping

**Goal:** First frame wins.

| Substep | Description | Status |
|--------|-------------|--------|
| **5.1** | **Deferred game index:** Remove constructor-time **`GetSavedRowsForRoot`**; after the library window is shown and prefill/refresh kick off, schedule **`GetSavedRowsForRoot`** on **`DispatcherPriority.ApplicationIdle`** (`ScheduleDeferredGameIndexWarmup` → `ILibraryBrowserShell` / `LibraryBrowserShowOrchestration`). | **Done** (`PixelVault.Native.cs`, `MainWindow.LibraryBrowserOrchestrator.FolderData.cs`, `MainWindow.LibraryBrowserShellBridge.cs`, `MainWindow.LibraryBrowserShowOrchestration.cs`) |
| **5.2** | **Thumbnail decode caps:** Folder/detail/banners use viewport- and tile-size–aware helpers (`CalculateLibraryFolderArtDecodeWidth`, `CalculateLibraryDetailTileDecodeWidth`, `CalculateLibraryBannerArtDecodeWidth` in `MainWindow.LibraryImageLoading.cs`; see changelog **0.997**). | **Done** (no further change in this step) |

---

## Verification

- `LibraryFolderCache` PERF logs: `mode=hit` vs `mode=rebuild` on repeated launches with no library changes.
- Dotnet trace: `LoadLibraryFolders`, `ReadEmbeddedMetadataBatch`, `LibraryBrowserRenderFolderList` / detail render.

---

## References in repo

- `Services/Library/LibraryScanner.cs` — `LoadLibraryFolders`, `LoadLibraryFoldersCached`
- `Services/Indexing/IndexPersistenceService.cs` — SQLite pragmas, **`LoadLibraryMetadataIndexEntriesForFilePaths`**, **`UpsertLibraryMetadataIndexEntries`**
- `UI/Library/LibrarySession.cs` — **`LoadLibraryMetadataIndexForFilePaths`**, **`MergePersistLibraryMetadataIndexEntries`**
- `Storage/IndexDatabaseStorage.cs` — `BuildLibraryFolderInventoryStamp`
- `Services/Metadata/MetadataService.cs` — `ReadEmbeddedMetadataBatch` / `ReadEmbeddedMetadataBatchAsync` (thread-pool offload)
- `Services/Covers/CoverService.cs` — **`ICoverService`** network API is **`*Async` only** (Step **2.5**)
- `MediaTools/MediaToolHelpers.cs` — UI-originating Exif batch via **`ReadEmbeddedMetadataBatchAsync`** / **`ReadEmbeddedKeywordTagsBatchAsync`**
- `UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs` — quick / refined snapshots, viewport-aware decode priority
- `UI/LibraryVirtualization.cs` — `LibraryDetailTileRowIntersectsViewport`, `RepopulateLibraryDetailTilesFromVisibleRows`, detail `CreateLibraryDetailTile` decode priority
