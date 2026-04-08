# PixelVault Project Context

This document is the durable orientation guide for the PixelVault repo.

Use it for:

- what the app is
- how the workspace is laid out
- which subsystems exist
- how data flows through the app
- which docs own which kinds of facts

Do not use this file as a live build tracker or session handoff.

For those, use:

- `C:\Codex\docs\CURRENT_BUILD.txt`
- `C:\Codex\docs\HANDOFF.md`
- `C:\Codex\docs\CHANGELOG.md`

## Product Summary

`PixelVault` is a native Windows desktop app built in C# and WPF for organizing, tagging, indexing, and browsing game screenshots and clips.

Core jobs:

- ingest new captures from upload folders
- classify likely platform/source from filenames and metadata
- support review-before-processing and manual cleanup flows
- write metadata into files and sidecars through `ExifTool`
- maintain a persistent game index and per-file metadata index
- browse the library through a richer WPF Library surface
- fetch and manage cover art, including SteamGridDB-backed workflows

## Workspace Rules

Work out of:

- `C:\Codex`

Important:

- treat `C:\Codex` as the only real workspace root
- if a shell or tool reports `A:\Codex`, treat that as an environment quirk
- do not treat `A:\` as the live project drive

## Runtime Layout

Primary areas:

- `C:\Codex\src\PixelVault.Native`: live app source
- `C:\Codex\src\PixelVault.LibraryAssets`: portable library for canonical asset records, scan diffs, root health gates, and reconciliation planning (see `README.md` there)
- `C:\Codex\docs`: repo documentation
- `C:\Codex\scripts`: publish and developer utility scripts
- `C:\Codex\dist`: versioned published builds
- `C:\Codex\assets`: shared branding and UI assets
- `C:\Codex\tools`: bundled runtime tools such as `ExifTool` and `FFmpeg`
- `C:\Codex\PixelVaultData`: shared persistent app data, indexes, caches, and logs

Important runtime pointers:

- current published build: `C:\Codex\docs\CURRENT_BUILD.txt`
- desktop shortcut that should track the newest publish: `C:\Codex\PixelVault.lnk`
- publish helper: `C:\Codex\scripts\Publish-PixelVault.ps1`

## Source Layout

The app is still a modular monolith.

`PixelVault.Native.cs` remains the main orchestration shell, but significant logic has already been pulled into dedicated folders and files.

Key source areas:

- `C:\Codex\src\PixelVault.LibraryAssets`: asset lifecycle, `ScanDiffComputer`, `LibraryRootHealthChecker`, `ScanReconciliationPlan` (not yet wired into the WPF app)
- `C:\Codex\src\PixelVault.Native\Import`: intake, rename, move, sort, undo, and import orchestration
- `C:\Codex\src\PixelVault.Native\Indexing`: game-index, folder-cache, and library-index logic
- `C:\Codex\src\PixelVault.Native\Metadata`: metadata builders, tag helpers, and library edit flows
- `C:\Codex\src\PixelVault.Native\MediaTools`: `ExifTool` / `FFmpeg` execution helpers
- `C:\Codex\src\PixelVault.Native\Models`: import, parsing, and index model types
- `C:\Codex\src\PixelVault.Native\Services`: extracted service seams such as covers, metadata, filename parsing, and filename rules
- `C:\Codex\src\PixelVault.Native\Storage`: SQLite/cache path and persistence helpers
- `C:\Codex\src\PixelVault.Native\UI`: extracted windows, editor hosts, virtualization helpers, and UI-specific support code

## Current App Shape

Major user-facing surfaces:

- Library: the main startup surface and primary browse experience
- Settings: utility/import hub
- Path Settings: environment and external-tool configuration
- Game Index editor: master game-record editing
- Photo Index editor: per-file metadata row editing
- intake preview / manual metadata review flows
- workflow progress and status windows

The long-term direction is to keep the WPF shell thinner while moving business logic into explicit services and extracted workflow types.

## Data Model

### File metadata

Per-file metadata is the source of truth for:

- tags
- comments / descriptions
- platform identity written into files
- capture-date overrides written into files

### SQLite runtime store

The live runtime store is a per-library SQLite database under:

- `C:\Codex\PixelVaultData\cache\pixelvault-index-<library>.sqlite`

It backs the live Game Index and Photo Index experience.

### Game Index

The Game Index is the master registry for:

- stable `GameId`
- canonical title
- platform / console
- Steam App ID
- SteamGridDB ID (`STID`)
- folder-path context and related grouping identity

The Game Index is also authoritative for canonical library folder naming when records are saved.

### Photo Index

The Photo Index is the persistent per-file mirror/cache for:

- file path
- capture stamp
- `GameId`
- console label
- tag text and related metadata mirrors

### Folder cache

The folder cache is derived state.

It is useful for Library rendering and performance, but it is not the canonical source of metadata truth.

### Legacy cache files

Older flat files such as:

- `game-index-*.cache`
- `library-metadata-index-*.cache`

are now legacy migration inputs or historical snapshots rather than the primary runtime store.

## Platform And Capture Model

Recognized platform families include:

- `Steam`
- `PC`
- `PS5` / `PlayStation`
- `Xbox`
- custom platform tags through `Platform:<Custom>` or equivalent custom-platform flows

Important rules:

- `Steam` and `PC` are distinct tags
- multiple recognized platform families resolve to `Multiple Tags`
- `Other` should only be used when no recognized family is present

Current supported workflow families include:

- Steam captures
- PS5 captures
- Xbox captures
- manual / unmatched captures
- mixed-media libraries with video sidecars and optional `FFmpeg` poster generation

## Documentation Map

Use the docs by role:

- `C:\Codex\docs\POLICY.md`: durable behavior rules and operating rules
- `C:\Codex\docs\DOC_SYNC_POLICY.md`: repo/Notion sync rules
- `C:\Codex\docs\HANDOFF.md`: short current stop point and immediate context
- `C:\Codex\docs\CHANGELOG.md`: published version history
- `C:\Codex\docs\CURRENT_BUILD.txt`: current published build pointer
- `C:\Codex\docs\ROADMAP.md`: long-term sequencing
- `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md`: refactor contract (tiered goals, service ports, FS/async scope) aligned with extraction and service split
- `C:\Codex\docs\completed-projects\README.md`: **completed initiatives** (e.g. MainWindow extraction A–F)
- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md`: full historical record for MainWindow extraction (complete)
- `C:\Codex\docs\PERFORMANCE_TODO.md`: active responsiveness and scalability backlog
- `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md`: security, edge cases, and hardening backlog (slim); archive snapshot in `docs/archive/`
- `C:\Codex\docs\NEXT_TRIM_PLAN.md`: ranked next steps to shrink/optimize after current publish (horizon doc; avoid duplicating `PERFORMANCE_TODO.md` long-term)
- `C:\Codex\docs\archive\README.md`: frozen snapshots of older perf / service-split plans (not a live backlog)
- `C:\Codex\docs\MANUAL_GOLDEN_PATH_CHECKLIST.md`: short manual verification path for risky changes
- `C:\Codex\docs\XBOX_ACHIEVEMENTS_API_READINESS_CHECKLIST.md`: parking-lot checklist for future Microsoft Xbox achievements integration readiness
- `C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`: service boundaries and safe parallel-work lanes

## Current Direction

The repo direction is:

1. keep adding small safety nets around risky behavior
2. reduce UI-thread blocking and long-operation rough edges
3. keep shrinking orchestration out of `MainWindow`
4. prefer small explicit seams over framework rewrites

That means:

- no large MVVM rewrite before the seams exist
- no broad churn just to “modernize”
- keep shipping behavior stable while the codebase gets easier to work in
