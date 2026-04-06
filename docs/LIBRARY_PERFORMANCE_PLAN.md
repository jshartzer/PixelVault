# Library & app performance plan

Companion to the performance deep dive: reduce cold-start cost, folder-cache churn, and navigation jank without changing visible behavior.

**Notion:** [PixelVault — Library performance roadmap](https://www.notion.so/33a73adc59b681b0a45ce71c2799a25d) (child of [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6)).

---

## Step 1 — Folder inventory & cache (**started**)

**Goal:** Fewer unnecessary full `LoadLibraryFolders` rebuilds and cheaper work when computing whether the folder cache is still valid.

| Substep | Description | Status |
|--------|-------------|--------|
| **1.1** | **`BuildLibraryFolderInventoryStamp`**: enumerate direct child folders once, sort **folder names** (not full paths), same rolling hash — less allocation and faster compares on long/NAS paths. | **Done** (`IndexDatabaseStorage.cs`) |
| **1.2** | **Narrow `LibraryMaintenanceSync`**: allow read-only cache hits without blocking behind a long write; document ordering hazards before changing. | Todo |
| **1.3** | **Incremental invalidation**: detect added/removed game folders without full file enumeration when stamp + index generation match (design only until 1.2 is clear). | Todo |

---

## Step 2 — Metadata / ExifTool pipeline

**Goal:** Metadata reads should not pretend to be async while blocking a thread; batch work off the UI thread.

- Replace `Task.FromResult(ReadEmbeddedMetadataBatch(...))` with real background execution and bounded parallelism.
- Audit call sites so **CoverService** / Steam **`.GetAwaiter().GetResult()`** never runs on the dispatcher.

---

## Step 3 — Detail pane & navigation

**Goal:** Switching games should not redo layout/metadata passes when inputs are unchanged.

- Prioritize **visible** detail tiles for decode (viewport-first queue).
- Reuse virtualized row elements where snapshots allow.
- Trim redundant **quick / refined** passes when fingerprints match.

---

## Step 4 — SQLite & index I/O

**Goal:** Large libraries pay less for open/save.

- Pragmas (WAL, synchronous) tuned for desktop cache DB.
- Avoid loading full metadata dictionaries into memory when a screen only needs a slice.

---

## Step 5 — Startup & housekeeping

**Goal:** First frame wins.

- Defer **non-critical** preloads (e.g. full game index) until idle after `ShowLibraryBrowser`.
- Keep thumbnail pipeline caps aligned with real screen size (see `0.997` changelog).

---

## Verification

- `LibraryFolderCache` PERF logs: `mode=hit` vs `mode=rebuild` on repeated launches with no library changes.
- Dotnet trace: `LoadLibraryFolders`, `ReadEmbeddedMetadataBatch`, `LibraryBrowserRenderFolderList` / detail render.

---

## References in repo

- `Services/Library/LibraryScanner.cs` — `LoadLibraryFolders`, `LoadLibraryFoldersCached`
- `Storage/IndexDatabaseStorage.cs` — `BuildLibraryFolderInventoryStamp`
- `Services/Metadata/MetadataService.cs` — `ReadEmbeddedMetadataBatch` / `Async`
- `UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs` — snapshot passes
