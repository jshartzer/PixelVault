# PV-PLN-LIBST-001 — Single-folder storage model (console rows preserved)

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-LIBST-001` |
| **Status** | Draft |
| **Owner** | PixelVault / Codex |
| **Source brief** | User request (2026-04-07): keep unique Game Index rows per console, but store all captures for a game in one app-owned folder and stop inferring identity from folder structure |
| **Related** | `docs/PROJECT_CONTEXT.md`, `docs/POLICY.md`, `docs/DOC_SYNC_POLICY.md`, `docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md` |

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

### 0.2 Read-only leak spike before Slice A

Do one short read-only spike before implementation begins:

- enumerate all call sites of `GuessGameIndexNameForFile(...)`
- enumerate all call sites of `NormalizeGameIndexName(..., folderPath)`
- trace scanner seeds and startup ordering between photo index and folder cache
- produce one short “today vs target” flow for `identity -> placement -> disk`

Expected output:

- a small leak list that can be checked off during Step 1

### 0.3 Unresolved-file contract

Step 1 removes folder-as-title fallback, so the plan needs an explicit no-`GameId` / no-hint story.

Recommended default:

- unresolved files remain visible to the user in a manual-assignment / unresolved surface
- unresolved files keep their current physical path until assignment is known
- organize / merge flows should skip unresolved files rather than inventing a title from the folder
- manual assignment must attach through `GameId` or parser-backed selection, never by trusting the parent folder name

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

Important behavior change:

- one real folder may contain Steam, Xbox, PS5, and Emulation captures
- console-specific browse rows must be projections over file assignments, not separate physical folders

### Step 5 — Add explicit migration / repair workflow

Do not force a one-shot hidden migration. Provide an app-owned re-home workflow.

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

### Step 7 — Tighten metadata-driven re-homing

Once placement is centralized, use metadata edits to drive movement more intentionally.

Examples:

- changing a file’s `GameId` moves it to the new storage group folder
- changing platform label alone does not split storage if the storage group stays the same
- creating a new game record can create a new storage group when needed

This step is where the app fully owns folder structure as a projection from the model.

### Step 8 — Verification and rollout

Add explicit automated and manual verification before treating the model as complete.

Automated coverage should include:

- no title inference from parent folder names
- SQLite migration for storage-group fields
- placement planning for multi-platform same-title rows
- scanner rebuild against mixed-platform single folders
- metadata edit -> file re-home behavior

Manual checks should include:

- import into a game that already exists on another console
- manual metadata reassignment between games
- `All` vs `By Console` browse after files share one folder
- merge/re-home dry run and apply
- undo/conflict behavior

## Suggested delivery slices

| Slice | Scope |
|------|-------|
| **0** | Step 0 — decision lock, leak spike, unresolved-file contract |
| **A** | Step 1 — remove folder-name inference and codify source-of-truth resolution |
| **B** | Step 2 — add `StorageGroupId` persistence and migrations |
| **C** | Step 3 — centralize placement into a service |
| **D** | Step 4 — scanner/folder-cache path-agnostic rebuild |
| **E** | Step 5 — migration / merge preview tool (release-gating) |
| **F** | Steps 6–8 — editor polish, diagnostics, hardening, verification |

## Early test matrix

Add or extend these tests before the larger refactors land:

- no identity from parent folder name in golden cases
- same physical path with multiple `GameId` rows once `StorageGroupId` exists
- SQLite migration round-trip for new storage-group columns
- move + sidecar + index updates in at least one integration-style test
- scanner rebuild against a mixed-platform single-folder fixture

## Operational safeguards

Before broad re-home moves ship:

- remind the user in UI or docs that a library backup is recommended before migration
- keep verbose move logging for the first release train:
  - source path
  - destination path
  - `GameId`
  - `StorageGroupId`

## Decision lock items

Canonical checklist for **§0.1** — answer in writing (in this doc or a linked decision note) before Step 1 begins:

1. Should `StorageGroupId` be app-managed only in v1, or user-editable?
2. Should `StorageFolderName` exist as a persisted shared label, or should folder naming always derive from a preferred title?
3. When multiple platform rows share one storage group, which row supplies the default cover/folder thumbnail if they disagree?
4. Should automatic re-homing happen immediately on every metadata save, or only during explicit organize/repair passes?
5. What is the unresolved-file UX when there is no `GameId` and no trusted parser/metadata hint?
6. How much of the migration should be automatic vs opt-in?
7. Under what exact conditions is same-title cross-platform auto-merge forbidden during backfill?

## Recommendation

Start with **Step 0**, then **Step 1** and **Step 2**. That is the real architectural turn:

- lock the decisions in writing
- stop trusting folder structure
- add an explicit storage-group concept

Without **this foundation** (preflight + Steps 1–2), removing ` - Platform` from folder naming would only hide the current coupling instead of fixing it.

## Notes for execution

When execution starts, reference **`PV-PLN-LIBST-001`** in commits and, if used, Notion per **`docs/DOC_SYNC_POLICY.md`**.

Use **storage model** consistently in naming and status updates. Avoid calling this a storage “mode,” because it is a data/placement model change rather than a browse toggle.
