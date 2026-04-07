# PixelVault Performance To Do

Focused backlog for responsiveness, scalability, and long-operation polish in the live native app.

**Refactor contract:** `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md`

**Also use:** `C:\Codex\PixelVaultData\TODO.md` (rolling tasks), `C:\Codex\docs\ROADMAP.md` (sequencing).

**Historical / narrative plans (archived):** `C:\Codex\docs\archive\` — full Library perf phase write-up and service-split task lists live there; do not treat them as an active checklist.

Source notes that fed this list (some content superseded by landed work): `LIBRARY_UI_SCALABILITY_NOTES.txt`, `PERFORMANCE_RESPONSIVENESS_PLAN.txt`, slim `PERFORMANCE_FIX_PLAN.txt`, slim `pixelvault_service_split_plan.txt`.

---

## Review notes (still worth remembering)

- **MainWindow is partial** across many files; the large **`PixelVault.Native.cs`** is real, not a duplicated copy.
- **God-object weight:** imperative Library, settings, metadata, indexing, and covers still converge on `MainWindow` in places—ongoing monolith shrink (`PERFORMANCE_MONOLITH_SLICE_PLAN.md`) rather than a single “perf” ticket.
- **Closure depth:** named hosts (`LibraryBrowserHost`, `GameIndexEditorHost`, etc.) reduce tangle; extend hosts when touching Library orchestration.
- **Diagnostics:** prefer logging over empty `catch` when adding new I/O paths; re-audit if new silent catches appear.

---

## Active backlog

### A. Service / monolith follow-through (highest leverage for *new* work)

**7.** **Metadata + library + game-index glue** — Narrow remaining direct `indexPersistenceService` / `libraryScanner` use from `MainWindow` when the file is already in scope (other-root paths, indexing partials, filename rules, editors). **`IGameIndexService`**, **`GetSavedGameIndexRowsForRoot`**, **`ILibrarySession`** routing for active root: landed; extend only with intentional seams.

**8.** **`IFileSystemService`** — Extended slice landed. **Optional:** stream-based copy if a future path needs it.

**Monolith / extraction** — Continue shrinking `PixelVault.Native.cs` and moving coherent verticals into partials/services (`PERFORMANCE_MONOLITH_SLICE_PLAN.md`, `HANDOFF.md`). No MVVM requirement.

### B. Spot-check when touching related code

- **5.** Long-running workflows: keep heavy work off the UI thread; preserve progress + `BeginInvoke` patterns.
- **6.** Cancellation: largely landed; verify new long paths accept `CancellationToken` and cancel between work items where practical.

### C. Optional polish (from archived perf plan)

- Capture grid: **full** VirtualizingStackPanel / row-recycler parity for every layout, if traces still show N-scaling issues after viewport cancel work.
- Video-heavy folders: poster-only tiles, stricter off-screen unload, concurrency caps—only if repro jank returns.
- **`LogPerformanceSample`:** finer breakdown (filter vs sort vs row build) if needed for the next regression hunt.

---

## Landed (summary — do not track as open tasks)

| Theme | Notes |
|--------|--------|
| Capture grid / virtualization | Viewport-driven work; off-screen thumbnail/video work dropped; validate on mixed media. |
| Folder sort / search | Cached `NewestCaptureUtcTicks` / capture ticks; debounced Library search. |
| Cold index | Batch embedded reads for missing index entries. |
| Caches / thread safety | `LibraryWorkspaceContext` locks; cover HTTP caches locked; image LRU sync. |
| Empty-catch audit | Audited native hot paths — log or narrow. |
| **5a** Game-capture keywords | Mirror flag — no `Dispatcher.Invoke` on metadata path. |
| Cancellation | Library scan, cover refresh, game-index resolve, manual Steam search, etc. |
| **8** File-system seam | `IFileSystemService` in `Services/IO/`; major callers wired. |
| **9** Folder-row recycling | `VirtualizedRowHost.RecycleVisibleRowElements` on folder `tileRows`. |
| **10** Library UI host | `LibraryBrowserHost`, `ILibraryBrowserShell`, orchestration partials. |
| **11** Async-first I/O | Web client, metadata/cover async surfaces, import Steam rename async, etc. |
| Game index routing | **1a–1c** `IGameIndexService`, `GetSavedRowsForRoot`, parser/scanner bridges. |
| Image pipeline | `LibraryBitmapLruCache`, `LibraryImageLoadCoordinator`, `LibraryThumbnailPipeline`. |
| **2026-04 — Library jank / folder open** | Star glyph: `TryGetLibraryFileStarredFromIndex` uses in-memory index (`TryGetLibraryMetadataStarredFromCachedIndex`) — no full dictionary clone per tile. Folder file list: `GetFilesForLibraryFolderEntry` batches `LoadLibraryMetadataIndexForFilePaths` before platform filter to avoid per-file embedded tag reads. Detail pane: masonry row defs built off UI thread; metadata repair capped (140) with deferred background chunks (~36 files, ~100 ms gap) + idle dispatcher refresh when the same folder is still selected. |

Details and release notes: `CHANGELOG.md`, `HANDOFF.md`, `completed-projects/README.md`.

---

## Suggested order (what is actually left)

1. **Item 7 / monolith:** shrink glue and route persistence/scanner calls through sessions/services **when editing those files**.
2. **Spot-check 5–6** on any new long workflow.
3. **Optional:** virtualization depth, video polish, deeper logging (section C).

Prefer **named types + services + clear thread boundaries** over a UI-framework rewrite.
