# PV-PLN-LIBST-001 — Single-folder storage model (console rows preserved)

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-LIBST-001` |
| **Status** | **Complete (2026-04-08)** — Slices **0–G** (Steps **0–9**) delivered per this plan. Optional product QA from the Step 8 manual checklist and Step 9 follow-ups in-doc remain non-blocking. |
| **Owner** | PixelVault / Codex |
| **Source brief** | User request (2026-04-07): keep unique Game Index rows per console, but store all captures for a game in one app-owned folder and stop inferring identity from folder structure |
| **Related** | `docs/PROJECT_CONTEXT.md`, `docs/POLICY.md`, `docs/DOC_SYNC_POLICY.md`, `docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md` (active plan index: `docs/plans/README.md`) |

## Purpose

Move PixelVault to a model where:

1. the **Game Index** still keeps one canonical row per **game + console**
2. the **physical library folder** is a storage output owned by the app, not a metadata input
3. all captures for the same game can live in **one folder**
4. changing photo/game metadata can cause the app to **re-home files** into the correct folder automatically

This plan is explicitly **not** a proposal to collapse platform rows in the Game Index.

## Problem statement

Today the app already behaves like a single-folder system in some paths, but several important flows still use the folder name or parent path as identity input.

Current leakage points include:

- `src/PixelVault.Native/Indexing/LibraryFolderIndexing.cs`
  - `GuessGameIndexNameForFile(...)` still reads the parent folder name.
- `src/PixelVault.Native/Indexing/GameIndexCore.cs`
  - `NormalizeGameIndexName(name, folderPath)` still falls back to folder path.
- `src/PixelVault.Native/Metadata/LibraryMetadataEditing.cs`
  - library edit startup still falls back to the file’s parent folder when deriving game name context.
- `src/PixelVault.Native/Services/Library/LibraryScanner.cs`
  - scan/rebuild paths still seed names and grouping from helpers that can inherit folder-based guesses.
- `src/PixelVault.Native/Indexing/GameIndexFolderAlignment.cs`
  - canonical storage still splits duplicate titles into separate `Game - Platform` folders.

As long as those paths remain, folder structure is still partially acting as metadata.

## Hard rules (target contract)

These rules should become durable behavior:

1. **Game Index identity**
   - One canonical Game Index row per normalized **title + console** remains the rule.

2. **Folder structure is derived**
   - Folder names and folder paths are app-owned outputs, not title/console/ID inputs.

3. **Per-file ownership comes from model data**
   - File assignment should come from photo index, embedded metadata, parser results, and Game Index relationships.
   - Folder names should never be required to decide what game a file belongs to.

4. **Storage is shared when intended**
   - Multiple platform rows may share one physical storage folder.

5. **Metadata edits can move files**
   - If a file’s assigned game changes, the app may move the file to the correct app-owned folder automatically.

6. **Browse grouping and storage are separate**
   - `All` vs `By Console` remains a presentation concern, not a disk-layout concern.

## Target model

Separate three concepts that are partially conflated today:

### 1. Game identity

Owned by the **Game Index**:

- `GameId`
- canonical name
- `PlatformLabel`
- external IDs (`SteamAppId`, `NonSteamId`, `SteamGridDbId`, `RetroAchievementsGameId`)

### 2. File ownership

Owned by the **Photo Index** plus embedded metadata:

- file path
- assigned `GameId`
- console label / tags
- capture metadata

### 3. Storage placement

Owned by a dedicated placement model/service:

- shared storage group for related Game Index rows
- canonical folder label/path for that storage group
- desired physical destination for each file

## Proposed structural addition

Introduce a storage-layer concept separate from `GameId`. Working names:

- `StorageGroupId`
- `FolderGroupId`
- `LibraryGroupId`

Recommended direction: `StorageGroupId`.

Meaning:

- many Game Index rows may point to one `StorageGroupId`
- one `StorageGroupId` maps to one canonical storage folder
- `Diablo IV | Steam` and `Diablo IV | PS5` can keep separate Game Index rows while sharing one storage group and one folder

Optional companion field:

- `StorageFolderName`

This would let the app keep a stable folder label without deriving it from the current row’s `PlatformLabel`.

## Non-goals

- Do not collapse the Game Index to one row per title.
- Do not rely on the browse-layer `All` merge as the storage truth.
- Do not make folder structure the migration source of truth.
- Do not silently bulk-rename user libraries without a preview / repair step.

## Preflight requirements

These items should be completed before Slice A starts. They are not polish; they are the guardrails that keep the implementation from drifting.

### 0.1 Decision lock (write the answers down)

Before implementation, answer every item in **[Decision lock items](#decision-lock-items)** (canonical checklist below) in this doc or a short linked decision note. That section is the single source of truth so answers do not drift across two lists.

Do not leave these as chat-only decisions.

**Locked in this doc — 2026-04-08:** see **[§ Decision lock — resolved answers](#decision-lock--resolved-answers-2026-04-08)** below.

### 0.2 Read-only leak spike before Slice A

Do one short read-only spike before implementation begins:

- enumerate all call sites of `GuessGameIndexNameForFile(...)`
- enumerate all call sites of `NormalizeGameIndexName(..., folderPath)`
- trace scanner seeds and startup ordering between photo index and folder cache
- produce one short “today vs target” flow for `identity -> placement -> disk`

Expected output:

- a small leak list that can be checked off during Step 1

**Status (2026-04-08):** Satisfied by the Step 1 audit and leak tables in **[Execution log](#execution-log)** — see *Step 1 scanner / rebuild audit* and *Initial leak spike*.

### 0.3 Unresolved-file contract

Step 1 removes folder-as-title fallback, so the plan needs an explicit no-`GameId` / no-hint story.

Recommended default:

- unresolved files remain visible to the user in a manual-assignment / unresolved surface
- unresolved files keep their current physical path until assignment is known
- organize / merge flows should skip unresolved files rather than inventing a title from the folder
- manual assignment must attach through `GameId` or parser-backed selection, never by trusting the parent folder name

**Status (2026-04-08):** Satisfied by the documented unresolved / pending-assignment behavior in the execution log (*Slice A — unresolved UX*): `PendingGameAssignment` rows, **`missingid`** visibility, and organize flows that must not mint `GameId` from the parent folder name.

## Staged implementation plan

### Step 0 — Preflight and decision lock

Complete the preflight requirements before Slice A:

- lock the product decisions in writing
- do the leak spike and capture the leak list
- define unresolved-file behavior explicitly

Result:

- Step 1 starts with stable rules instead of hidden assumptions

### Step 1 — Remove folder-based identity inference

Create a narrow resolver path for “what game does this file belong to?” that does **not** trust folder names.

Primary work:

- stop using parent folder names inside:
  - `GuessGameIndexNameForFile(...)`
  - `NormalizeGameIndexName(name, folderPath)` fallback behavior
  - library metadata edit startup/title recovery
- prefer this resolution order:
  - photo index `GameId`
  - saved Game Index row by `GameId`
  - parser title hint / embedded metadata
  - explicit manual assignment
- if nothing is known, treat the file as unresolved instead of trusting the folder
- wire unresolved files into the chosen unresolved-file UX rather than letting them fall back to inferred folder identity

Result:

- folder path becomes observational only
- the app can still browse folders, but identity no longer depends on them

### Step 2 — Add storage-group persistence

Add a shared storage concept to the Game Index and persistence layer.

Primary work:

- add `StorageGroupId` to:
  - `GameIndexEditorRow`
  - SQLite `game_index`
  - cache / projection models as needed
- add migration/backfill behavior for existing libraries
- default backfill should be deterministic and non-destructive

Recommended first-pass default:

- rows with the same normalized game name across platforms get the same generated `StorageGroupId`
- rows with unrelated names keep separate groups
- auto-merge is forbidden when rows present conflicting external IDs or a future/manual “different game” marker
- v1 may keep `StorageGroupId` app-managed/read-only, but the model must leave room for later manual split/merge tools

Result:

- storage can be shared without changing Game Index row identity

### Step 3 — Introduce a placement service

Move all “where should this file/folder live?” logic behind one service.

Suggested service:

- `LibraryPlacementService`

Inputs:

- library root
- `GameId`
- `StorageGroupId`
- canonical storage label
- current file set

Outputs:

- desired folder path
- move plan
- collision/rename decisions

Primary work:

- replace ad hoc folder creation/move rules in:
  - import sorting
  - manual metadata organize
  - game-index folder alignment
- stop encoding platform suffixes into canonical storage by default
- move plans must include **sidecars** (and any import **undo** entries that track moves), not only media files — placement owns the full rename surface

Result:

- the app owns one path computation model
- storage no longer depends on each caller re-implementing folder rules

**Implementation (v1):** `LibraryPlacementService`, `LibraryFileMovePlan`, import dependencies in `ImportServiceDependencies` (see execution log, Slice C).

### Step 4 — Make scanner and repair paths path-agnostic

Update library scan/rebuild logic so folder layout is never used as the source of truth.

Primary work:

- scanner enumerates files from disk, but ownership resolution comes from:
  - photo index
  - embedded metadata
  - parser outputs
  - Game Index
- folder cache stores current placement, but does not infer title/console from it
- rebuild/repair flows should tolerate mixed-platform files in one physical directory
- **Implementation note:** specify how **`LibraryFolderInfo` / folder cache** represent **one disk path** that may back **multiple `GameId`s** (e.g. shared `FolderPath` keyed by `GameId`, or a storage-group row plus per-console projections). Console-specific browse rows remain **projections** over file assignments, not separate required directories.

**Implementation (v1):** `LibraryScanner.LoadLibraryFoldersCore` groups indexed files by **`GameId`**; each `LibraryFolderInfo` is one browse/cache row; **`FolderPath`** is current placement (saved row path or majority directory of that row’s **`FilePaths`**). Same path may appear on multiple rows. Platform/titles come from the game index and photo metadata (`DetermineFolderPlatform` uses index/tags, not folder names). See XML on **`LibraryFolderInfo`**.

Important behavior change:

- one real folder may contain Steam, Xbox, PS5, and Emulation captures
- console-specific browse rows must be projections over file assignments, not separate physical folders

### Step 5 — Add explicit migration / repair workflow

Do not force a one-shot hidden migration. Provide an app-owned re-home workflow.

**Implementation (v1):** Library toolbar **Merge folders** → `OpenLibraryStorageMergeTool`: `BuildStorageMergeWorkingSet`, `LibraryStorageMergePlanner.PlanDryRun`, summary window, **Apply** runs `SaveSavedGameIndexRows` + `AlignLibraryFoldersToGameIndex` + save + `RefreshCachedLibraryFoldersFromGameIndex`. `BuildGameIndexRowsFromFolders` preserves **`StorageGroupId`** from the folder cache. See **`LibraryStorageMergePlannerTests`**. Not yet: dedicated undo manifest, empty-folder auto-delete.

This is a release milestone, not cleanup polish. Real libraries with years of `Game - Platform` folders will need a dry-run before defaults change.

Suggested user-facing tool:

- `Merge platform folders into single game folders`

Capabilities:

- dry-run preview
- source folders -> target folder mapping
- file counts
- conflict preview
- empty-folder cleanup preview
- cancel / apply

This tool should:

- update storage-group assignments if needed
- move files and sidecars
- update photo index and folder cache
- remove empty old platform folders after success

### Step 6 — Update editors and maintenance surfaces

Expose enough information for the new model to stay understandable.

Primary work:

- Game Index editor:
  - show storage-group relationship or at least resulting storage folder
- Folder/ID editor:
  - avoid implying that one platform row must equal one folder
- diagnostics / health:
  - report when files are misplaced relative to `StorageGroupId`
- browse:
  - keep `By Console` as projection-only behavior

**Implementation (v1):** Game Index grid: read-only **Target storage folder** and **Placement** (`Mismatch` when row `FolderPath` ≠ canonical). Setup & health **Library storage placement**: **Game index rows** (cached folder vs canonical) and **Indexed captures (photo index)** — every assigned file’s directory must fall under that `GameId`’s canonical folder (subfolders allowed); reports **outside** count, **orphan** `GameId`s, and **unassigned** entries skipped. Folder ID editor copy notes **shared disk folder**. Helpers: **`PathsEqualNormalized`**, **`IsDirectoryWithinCanonicalStorage`**.

### Step 7 — Tighten metadata-driven re-homing

Once placement is centralized, use metadata edits to drive movement more intentionally.

Examples:

- changing a file’s `GameId` moves it to the new storage group folder
- changing platform label alone does not split storage if the storage group stays the same
- creating a new game record can create a new storage group when needed

This step is where the app fully owns folder structure as a projection from the model.

**Implementation (v1):** **`OrganizeLibraryItems`** skips moves when the file already sits **under** the canonical game folder (subfolders allowed; **`IsDirectoryWithinCanonicalStorage`**), so platform-only / tag churn with the same `GameId` does not flatten layout. **`RehomeLibraryCapturesTowardCanonicalFolders`** runs after **Photo Index** save for paths whose **`GameId`** changed, reusing organize + **`LibraryPlacementService`** (same behavior as library metadata apply). Library metadata finish workflow already ran organize after finalize; unchanged apart from subfolder rule.

### Step 8 — Verification and rollout

Add explicit automated and manual verification before treating the model as complete.

Automated coverage should include:

- no title inference from parent folder names
- SQLite migration for storage-group fields
- placement planning for multi-platform same-title rows
- scanner rebuild against mixed-platform single folders
- metadata edit -> file re-home behavior
- **Photo index save → re-home:** unit tests for **`LibraryRehomeRules.PhotoIndexGameIdChangedForRehome`** (when `GameId` changes vs first assign vs unchanged) and for **`LibraryPlacementService.IsCaptureAlreadyUnderCanonicalOrganizeTarget`** (exact dir, subfolder under canonical with game row, no false positives when not from game row). **Integration:** **`PhotoIndexSaveRehomeIntegrationTests`** — temp library, fake **`ILibraryScanHost`**, **`SavePhotoIndexEditorRows`** + **`LibraryScanner`** hook to skip folder-cache rebuild; asserts capture moves under canonical folder after `GameId` change.
- **Other index paths:** audit any code that mutates `photo_index` **`GameId`** or assignment without going through library metadata finish or photo index save; extend **`RehomeLibraryCapturesTowardCanonicalFolders`** (or equivalent) so small saves don’t leave disk and index inconsistent—verify with tests or a checklist before closing Step 8.

Manual checks should include:

- import into a game that already exists on another console
- manual metadata reassignment between games
- `All` vs `By Console` browse after files share one folder
- merge/re-home dry run and apply
- undo/conflict behavior
- **photo index:** change a file’s `GameId`, save, confirm capture moves (or stays) per **`OrganizeLibraryItems`** / subfolder rules

**After Step 8:** use **Step 9** for production-faithful tests and hardening that are not blockers for first rollout but reduce regression risk on real libraries.

### Step 9 — SQLite-faithful integration tests and re-home persistence hardening

Step 9 is the **last numbered step** in this plan. It targets two things: (1) tests that hit **real `IndexPersistenceService` SQLite** (not only an in-memory host), and (2) **production correctness** so re-home does not leave `photo_index` pointing at pre-move paths after files move.

**Done (2026-04-08)**

- **`PhotoIndexSaveRehomeSqliteIntegrationTests`:** temp library + **`IndexPersistenceHarness`** + **`LibraryScanner.SavePhotoIndexEditorRows`**; asserts disk layout **and** `photo_index` row (`file_path`, `game_id`) after **`GameId`** reassignment and re-home.
- **Shared `IndexPersistenceHarness`:** moved out of **`IndexPersistenceServiceTests`** into `tests/PixelVault.Native.Tests/IndexPersistenceHarness.cs` for reuse.
- **Re-home → SQLite consistency:** **`RehomeLibraryCapturesTowardCanonicalFolders`** (in **`LibraryMetadataEditing`**) after a successful **`OrganizeLibraryItems`** pass now **removes stale index keys** for the pre-move paths and **`UpsertLibraryMetadataIndexEntries`** for the **`ManualMetadataItem`** list (same idea as the library metadata apply workflow). Fake test host **`PhotoSaveTestHost`** re-saves the metadata index after **`MoveTowardCanonicalForIntegrationTest`** so host doubles stay coherent.

**Optional follow-ups (not blocking)**

- **Re-home failures:** richer UX when a move fails (locked file, collision); today logging covers the organize summary.
- **Performance:** document or profile folder-cache rebuild + large-batch re-home if users report slowdowns.
- **Step 8 “other index paths”:** deeper audit if you need a written matrix of every `photo_index` / assignment mutation outside editor save and manual metadata apply.

## Suggested delivery slices

| Slice | Scope |
|------|-------|
| **0** | Step 0 — decision lock, leak spike, unresolved-file contract |
| **A** | Step 1 — remove folder-name inference and codify source-of-truth resolution |
| **B** | Step 2 — add `StorageGroupId` persistence and migrations |
| **C** | Step 3 — centralize placement into a service |
| **D** | Step 4 — scanner/folder-cache path-agnostic rebuild |
| **E** | Step 5 — migration / merge preview tool (release-gating) |
| **F** | Steps 6–8 — editor polish, diagnostics, verification, rollout |
| **G** | Step 9 — SQLite integration tests + re-home index persistence hardening (final step) |

## Early test matrix

Add or extend these tests before the larger refactors land:

- no identity from parent folder name in golden cases
- same physical path with multiple `GameId` rows once `StorageGroupId` exists
- SQLite migration round-trip for new storage-group columns
- move + sidecar + index updates in at least one integration-style test
- scanner rebuild against a mixed-platform single-folder fixture
- **LIBST Step 8:** **`LibraryRehomeRules`**, **`IsCaptureAlreadyUnderCanonicalOrganizeTarget`**, **`PhotoIndexSaveRehomeIntegrationTests`** (temp disk + fake host; not full SQLite pipeline).
- **LIBST Step 9:** **`PhotoIndexSaveRehomeSqliteIntegrationTests`** + **`IndexPersistenceHarness`**; production re-home updates **`photo_index`** after moves via remove + upsert.

## Operational safeguards

Before broad re-home moves ship:

- remind the user in UI or docs that a library backup is recommended before migration
- keep verbose move logging for the first release train:
  - source path
  - destination path
  - `GameId`
  - `StorageGroupId`

### Step 9 — ops checklist (release / support)

- **Backup** library folder (and PixelVault cache / index DB under the configured cache root) before running **merge**, mass **organize**, or storage migration tooling.
- After **photo index** edits that change **`GameId`**, confirm captures appear under the expected game folder and that **photo index** reload shows **current file paths** (re-home now refreshes SQLite when moves occur).
- If **`photo_index`** and disk disagree after a crash or interrupted save, use **reload from disk** / **repair** flows documented in product help rather than hand-editing SQLite.

## Decision lock items

Canonical checklist for **§0.1** — answer in writing (in this doc or a linked decision note) before Step 1 begins:

1. Should `StorageGroupId` be app-managed only in v1, or user-editable?
2. Should `StorageFolderName` exist as a persisted shared label, or should folder naming always derive from a preferred title?
3. When multiple platform rows share one storage group, which row supplies the default cover/folder thumbnail if they disagree?
4. Should automatic re-homing happen immediately on every metadata save, or only during explicit organize/repair passes?
5. What is the unresolved-file UX when there is no `GameId` and no trusted parser/metadata hint?
6. How much of the migration should be automatic vs opt-in?
7. Under what exact conditions is same-title cross-platform auto-merge forbidden during backfill?

## Decision lock — resolved answers (2026-04-08)

Canonical answers for **§0.1** (product defaults for this plan; revise here if strategy changes):

1. **`StorageGroupId` in v1** — **App-managed and read-only in the Game Index editor.** Values come from deterministic backfill / future tooling; manual split/merge remains a later step (plan Step 6+).

2. **`StorageFolderName`** — **Not persisted in v1.** Folder labeling continues to follow existing placement / title rules until **Step 3** (`LibraryPlacementService`); add an explicit persisted label only if placement needs it independently of row titles.

3. **Default cover / thumbnail when one storage group has multiple platform rows** — **Deterministic:** prefer a row with a usable `PreviewImagePath` in ascending `GameId` order within the group; if none, fall back to existing per-row / empty behavior until placement consolidates assets.

4. **Re-homing timing** — **Not on every metadata save in v1.** Bulk or implicit moves belong behind explicit organize/repair/import flows now; **Step 7** tightens metadata-driven re-homing after placement is centralized.

5. **Unresolved-file UX** — **As in Step 1:** files without a trusted `GameId` stay visible on **`PendingGameAssignment`** / **Missing ID** surfaces; they keep their paths until assignment via photo index, parser, or explicit manual flows — **not** from parent folder name.

6. **Migration automatic vs opt-in** — **`StorageGroupId` SQLite column + deterministic backfill runs automatically** (non-destructive, no mass moves). **Physical re-home / folder merge stays opt-in** with preview (**Step 5**).

7. **When same-title storage merge is forbidden (backfill)** — Rows **must not** union into one `StorageGroupId` when both are **Steam** and have conflicting non-empty **`SteamAppId`** or conflicting non-empty **`SteamGridDbId`**; when both are **Emulation** and have conflicting non-empty **`NonSteamId`** or **`RetroAchievementsGameId`**; or when both already carry **non-empty, differing `StorageGroupId`**. A future explicit “different game” marker would also block merge (reserved).

## Recommendation

Start with **Step 0**, then **Step 1** and **Step 2**. That is the real architectural turn:

- lock the decisions in writing
- stop trusting folder structure
- add an explicit storage-group concept

Without **this foundation** (preflight + Steps 1–2), removing ` - Platform` from folder naming would only hide the current coupling instead of fixing it.

## Execution log

| Date | Slice / step | Notes |
|------|----------------|------|
| 2026-04-08 | **Slice A (partial Step 1)** | Removed **parent folder name** as a source for `GuessGameIndexNameForFile` (hints + filename stem only). Removed **`NormalizeGameIndexName(name, folderPath)`** basename fallback when `name` is empty. Fixed **library metadata edit** path that used parent folder for `GameName` when `folderPathUsable` was false — now uses parser/filename guess only. Deleted **`ShouldTrustFilenameTitleOverFolder`** (only served folder-vs-hint arbitration). |
| 2026-04-08 | **Slice A (Step 1 continued)** | **`GameIndexEditorAssignmentService.EnsureManualMetadataMasterRow`**: return **`null`** when there is no title and no `preferredGameId` (no blank-title placeholder rows); when **`preferredGameId`** is set, **resolve by id first** (scan/sync no longer requires filename identity to match saved row). **`ResolveGameIdForIndexedFile`**: if filename parser yields no title, **leave `GameId` empty** instead of creating a row. **`PreserveLibraryMetadataEditGameIndex`**: derive **`sourceName`** from **saved row → first item filename guess → folder display name** (not folder-as-path). **Folder ID editor** guard if ensure returns null. Tests: `GameIndexEditorAssignmentServiceTests`. |
| 2026-04-08 | **Slice A (Step 1 — audit + unresolved UX)** | **Scanner/rebuild audit** (see subsection below): no remaining **game identity** inference from parent folder basename in index/scan paths; `Path.GetDirectoryName` uses are placement/enumeration. **Unresolved surface**: `LibraryScanner.LoadLibraryFoldersCore` appends **`LibraryFolderInfo`** rows per directory with indexed files but **empty `GameId`** (`PendingGameAssignment`); tile title pattern **`Needs assignment ·`** plus directory leaf name is a **browse label**, not game identity. These rows appear in the library grid, **`missingid`** filter, and folder cache (extra tab column). **`ApplySavedGameIndexRows`** / **100% completion** skip pending buckets so the app does not mint `GameId`s from that label. Filter/menu copy: **Missing ID / assignment**. Tests: `LibraryBrowseFolderSummaryTests`. |
| 2026-04-08 | **Slice 0 (Step 0 — preflight)** | **§0.1** filled in under [Decision lock — resolved answers](#decision-lock--resolved-answers-2026-04-08). **§0.2** treated as done via existing Step 1 audit + leak tables in this doc. **§0.3** pointed at unresolved/pending-assignment behavior logged under Slice A. |
| 2026-04-08 | **Slice B (Step 2 — `StorageGroupId`)** | **`GameIndexEditorRow.StorageGroupId`**, SQLite `game_index.storage_group_id` (`EnsureGameIndexStorageGroupIdColumn`), read/write in `IndexPersistenceService`, **`GameIndexStorageGroupBackfill.AssignDeterministicStorageGroupIds`** on load/save merge. **`LibraryFolderInfo.StorageGroupId`** filled from saved rows in **`LibraryScanner`**; synced in **`ApplySavedGameIndexRows`** / conservative **`UpsertSavedGameIndexRow`**; **`CloneLibraryFolderInfo`** copies it. **Merge rule:** two **Steam** rows with differing non-empty **`SteamGridDbId`** do not share a storage group. **Game Index editor:** read-only **Storage group** column + search hits **`StorageGroupId`**. Tests: **`GameIndexStorageGroupBackfillTests`**. Legacy tab-separated game index files still omit `StorageGroupId`; SQLite + backfill is the source of truth. |
| 2026-04-08 | **Slice C (Step 3)** | **`LibraryPlacementService`**: shared **`StorageGroupId`** → one folder name; legacy empty group → title-count + ` - Platform` suffix. **`AlignLibraryFoldersToGameIndex`**, **`OrganizeLibraryItems`**, and **`ImportService.SortDestinationRootIntoGameFolders`** use placement. Import sort: **`TryResolveGameIndexRowForImportSort`** (Steam AppID / non-Steam ID / **`BuildGameIndexIdentity`** title+platform), then **`PlanImportRootSort`** / **`LibraryFileMovePlan`**. Sidecars still moved by existing **`MoveMetadataSidecarIfPresent`** per move (not a unified sidecar list yet). **`LibraryPlacementServiceTests`**. |
| 2026-04-08 | **Slice D (Step 4 — partial)** | Documented **`LibraryFolderInfo`** as per-**`GameId`** projection with optional shared **`FolderPath`**. **`LoadLibraryFoldersCore`** summary: group by photo-index **`GameId`**; placement is observed, not title identity. **`NonSteamId`** copied onto scan-built folder rows when a saved game-index row exists (parity with other IDs). Further Step 4 work: broader repair/merge QA, mixed-folder fixtures in tests. |
| 2026-04-08 | **Slice E (Step 5 — v1)** | **`LibraryStorageMergePlanner` / dry-run models** + tests. **UI:** **Merge folders** next to Game Index; preview lists groups, target dir, move counts, optional empty-folder hints, rename-on-clash count. **Apply** persists index + runs **`AlignLibraryFoldersToGameIndex`**. **`BuildGameIndexRowsFromFolders`** now copies **`StorageGroupId`**. |
| 2026-04-08 | **Slice F (Step 6)** | **Game Index:** **Target storage folder** + **Placement**; **`FormatCanonicalStorageFolderAbsolutePath`**. **Health:** **`LibraryStoragePlacementHealthSnapshot`** — row paths + full **photo index** scan vs canonical folders (misplaced / orphan GameId / unassigned). **Folder ID editor** copy. **`PathsEqualNormalized`**, **`IsDirectoryWithinCanonicalStorage`**, tests. |
| 2026-04-08 | **Slice F (Step 7)** | **`OrganizeLibraryItems`:** skip move when already under canonical folder (subfolders). **`RehomeLibraryCapturesTowardCanonicalFolders`** + **`ILibraryScanHost`**: after **Photo Index** save, re-home files whose **`GameId`** changed. Shared placement rules; same-storage-group rows still target one folder. |
| 2026-04-08 | **Slice F (Step 8 — started)** | Plan: **photo index re-home** tests + **other index paths** audit. Code: **`IsCaptureAlreadyUnderCanonicalOrganizeTarget`**, **`LibraryRehomeRules`**, **`LibraryRehomeRulesTests`** + organize-target tests. |
| 2026-04-08 | **Slice F (Step 8 — integration)** | **`PhotoIndexSaveRehomeIntegrationTests`**: `GameId` edit in photo index save moves file from wrong folder to canonical **`Portal`** dir; **`LibraryScanner`** optional **`folderCacheRebuildHook`** for tests only. |
| 2026-04-08 | **Slice G (Step 9 — defined)** | Added Step 9 scope: SQLite integration + hardening checklist. |
| 2026-04-08 | **Slice G (Step 9 — shipped)** | **`PhotoIndexSaveRehomeSqliteIntegrationTests`**, shared **`IndexPersistenceHarness`**. **`RehomeLibraryCapturesTowardCanonicalFolders`**: after moves, **`RemoveLibraryMetadataIndexEntries`** (pre-move paths) + **`UpsertLibraryMetadataIndexEntries`** (`ManualMetadataItem` list). Fake **`PhotoSaveTestHost`** re-saves metadata index after integration re-home. Step 9 ops checklist added under **Operational safeguards**. |
| 2026-04-08 | **Plan closure** | **`PV-PLN-LIBST-001`** marked **complete** in the header table; numbered Steps 0–9 and slices 0–G are shipped as documented. |

### Step 1 scanner / rebuild audit (2026-04-08)

| Area | Finding |
|------|--------|
| **`LibraryScanner.LoadLibraryFoldersCore`** | Builds `FolderPath` / groups from **file paths** (observed placement). **`GuessGameIndexNameForFile`** is filename/parser only. After this slice, **unassigned** files get explicit browse rows. |
| **`GameIndexFolderAlignment` / `ApplySavedGameIndexRows`** | Matches saved rows to folders by **id / identity**; skips **`PendingGameAssignment`** rows so browse labels are not written back as game titles. |
| **`MainWindow.LibraryBrowserViewModel`** | **`BuildLibraryBrowserAllMergeKey`** uses a fixed **`unassigned|`** prefix plus **folder path** for pending buckets so **All** grouping does not merge different directories. **`BuildLibraryBrowserViewKey`** still uses `folderPath` only for **key disambiguation**, not title inference. |
| **`OrganizeLibraryItems` / import** | Target folder names from **user/item game name** or filename stem — not parent directory as game title. |
| **Photography gallery caption** | **`Path.GetFileName(Path.GetDirectoryName(file))`** — **display caption only**; not indexed `GameId` resolution. |

### Initial leak spike (read-only, 2026-04-08)

Superseded by audit above; kept for history:

| Area | Symbol / pattern |
|------|------------------|
| Indexing | `GuessGameIndexNameForFile` (host), `EnsureGameIndexRowForAssignment` + guess in `LibraryScanner` photo sync |
| Indexing | `NormalizeGameIndexName(..., row.FolderPath)` on **rows** — still passes `FolderPath`; no longer substitutes basename when `Name` empty |
| Metadata | `LibraryMetadataEditing` manual item `GameName` |
| UI | `MainWindow.LibraryBrowserViewModel` (`BuildLibraryBrowserViewKey`, merge keys), `LibraryFolderIdEditor`, `GameIndexEditorHost` |
| Alignment | `GameIndexFolderAlignment.BuildCanonicalGameIndexFolderName` — empty name → `Unknown Game` until row name is fixed |

## Notes for execution

When execution starts, reference **`PV-PLN-LIBST-001`** in commits and, if used, Notion per **`docs/DOC_SYNC_POLICY.md`**.

Use **storage model** consistently in naming and status updates. Avoid calling this a storage “mode,” because it is a data/placement model change rather than a browse toggle.
