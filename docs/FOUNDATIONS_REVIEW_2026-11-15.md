# PixelVault PC foundation review

Implementation follow-up: the thirteen findings below are addressed and published in Native 0.129.000; see [foundation fixes and validation](FOUNDATIONS_FIXES_2026-11-15.md). The findings retain the original review evidence and historical line references. Publication refreshed App Testing with the identical release; no live index migration was performed during publication.

Reviewed November 15, 2026 against the maintained checkout at C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex. Scope: import and undo, filename parsing and rules, embedded/sidecar metadata, game identity, photo indexing, editor saves, and persistence/recovery.

The highest-value work is a reliability pass across file operations and data ownership. The existing service boundaries, shared filename parser, batched extraction, stable-ID grouping, SQLite storage, and focused tests are useful foundations. Build on them through small changes that make operations recoverable and prevent silent reassignment or overwriting of data.

This review adds documentation and isolated diagnostic artifacts only. Application source, published builds, settings, and the live capture library were not changed. Existing working-tree changes were preserved. Findings describe the maintained source; the published executable was not separately exercised. Reproduced findings below used disposable files/databases or a small metadata harness. Source-confirmed findings were traced through their callers but were not reproduced through the interactive UI.

## Findings

P1 means a high-priority risk of incorrect file or saved-data changes. P2 means a correctness/reliability defect to address in the next foundation work. The order within each priority reflects suggested implementation attention.

### 1. P1 — Import undo can restore the wrong capture

**Reproduced.** Import records an intermediate destination path. Sorting can subsequently rename the incoming file to resolve a collision without updating that undo entry. Undo falls back to finding the original filename elsewhere in the library and does not validate the file's identity.

With an existing Game/clip.png containing "old", importing another clip.png produced Game/clip (1).png. Undo moved the old capture back to the intake source and left the new imported capture in the library.

Change: journal every actual path transition, retain a file identity/fingerprint, and refuse ambiguous recovery. Include media and sidecars in the operation. Acceptance: collision imports undo the imported bytes, preserve the existing library file, and never choose a candidate solely by basename.

Evidence: [intermediate undo entry](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:409), [sort collision](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:729), [filename recovery](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:1756).

### 2. P1 — Deleted game IDs can be reused for unrelated games

**Identity methods reproduced; editor path source-confirmed.** The allocator chooses one greater than the maximum ID in the current row list. Delete the highest-numbered game in Game Index, then add a different game before saving: the new row receives the deleted ID. Alias reconciliation treats an existing matching ID as the same record, leaving old photos associated with the replacement game.

The probe returned deleted_highest_id_reused=G00002 and old_photo_alias_after_replace=G00002 for an unrelated replacement title.

Change: allocate never-reused IDs independently of currently visible rows, with explicit deletion/merge semantics for referenced photos. Existing IDs can remain; this does not require replacing every identity. Acceptance: delete/add/save cannot transfer photo ownership to another game, including across restart and concurrent writers.

Evidence: [allocator](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Indexing/GameIndexCore.cs:31), [editor allocation](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/UI/Editors/GameIndexEditorHost.cs:441), [same-ID alias](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Indexing/GameIndexFolderAlignment.cs:249).

### 3. P1 — Older editor snapshots can overwrite newer changes

**Source-confirmed.** Both index editors are modeless and save their complete retained row lists. The Photo Index save loads the current database but then overwrites every supplied row from the older editor snapshot. Open Photo Index, star a capture or edit its metadata in Library, then save an unrelated Photo Index change: the older row can overwrite the newer values and can even write the old star rating back into the file.

Game Index similarly replaces master rows using the editor snapshot. A newly imported master row can be removed by a later save from an already-open editor.

Change: save explicit changed fields and deletions against row revisions. Merge unrelated changes; report overlapping conflicts. Acceptance: a stale editor preserves another window's star, tags, notes, and newly added games unless that same field is deliberately changed and reconciled.

Evidence: [Photo Index full snapshot save](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/UI/Editors/PhotoIndexEditorHost.cs:384), [replacement and rating writes](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Library/LibraryScanner.cs:371), [Game Index save](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/GameIndexService.cs:52), [master-table replacement](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:658).

### 4. P1 — Failed library metadata edits can move files and report success

**Source-confirmed.** Library editing shares an intake metadata helper that quarantines failed writes. That helper moves any failed file and sidecar into a sibling Errors directory, including captures already in the library. The library workflow ignores the returned failure count and reports completion. Quick-comment saving likewise sets saved=true after the helper returns.

A metadata-write failure for a file that can still be moved can therefore relocate a library capture while the UI reports that the comment or metadata was saved.

Change: keep intake quarantine policy separate from library editing. Return per-file outcomes, keep failed library files in place, update mirrored metadata only after successful writes, and display partial failure honestly. Acceptance: inject an ExifTool failure in a temporary library; the original path/index remain intact and the UI receives a failure result.

Evidence: [unconditional quarantine](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Import/MainWindow.ImportWorkflow.Steps.cs:75), [move into Errors](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:1494), [ignored library result](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/PixelVault.Native.cs:567), [quick comment](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/UI/Library/MainWindow.LibraryCaptureQuickActions.cs:213).

### 5. P2 — Batch extraction shifts rating and camera fields

**Reproduced with real ExifTool and the unchanged metadata service.** The output has eight date fields at columns 15–22, but parsing consumes only 15–21. Rating, camera make, and camera model are then read one column too early. QuickTime ModifyDate is omitted from date parsing.

For a fixture with Rating=5, Make="Camera Corp", Model="Nintendo Switch 2", batch extraction returned Rating=null, CameraMake="5", CameraModel="Camera Corp". The direct rating reader returned 5. This prevents reliable star reconciliation and can break device-based platform recognition.

Change: correct the mapping immediately, then use named, group-qualified fields in structured output so adding a field cannot silently shift the rest. Direct and batch extraction should share a parser. Acceptance: real fixtures return matching direct/batch values for ratings, device information, dates, comments, and tags.

Evidence: [requested columns](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Metadata/MetadataService.cs:591), [incorrect positions](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Metadata/MetadataService.cs:644).

### 6. P2 — Partial sidecars hide embedded metadata

**Reproduced.** Any existing sidecar replaces the media file as the complete metadata read target. Starring a video/JXR can create a sidecar containing only a rating; subsequent reads ignore the underlying file's embedded tags, comments, dates, and device information.

A synthetic MP4 returned its embedded Steam tag and comment when read directly, but empty tags/comment when its rating-only sidecar was selected. The metadata remains physically embedded; the defect is in effective reads and the values shown to editing workflows.

Change: define field-level precedence and explicit clearing, or create complete sidecars from the effective metadata before partial updates. Acceptance: starring or editing only a comment preserves every unrelated embedded field, while explicitly clearing a field still works.

Evidence: [whole-sidecar precedence](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/MediaTools/MediaToolHelpers.cs:20), [rating-only sidecar](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Metadata/MetadataService.cs:442).

### 7. P2 — Cancellation loses recovery information for completed moves

**Reproduced.** Move entries accumulate locally until the entire move loop returns. Cancellation before the next file throws away the returned-result opportunity; the workflow only writes the manifest after the call and another cancellation check. Cancelling after the first of two moves left one file moved, one in source, and no returned move result to persist.

Change: persist intent before each filesystem mutation and record completion immediately afterwards. Cancellation must retain partial progress. Use the same mechanism for game-folder alignment, whose media, sidecar, photo-index, and game-index updates currently occur in separate steps without an operation-wide recovery record.

Acceptance: inject cancellation/failure after each stage, restart, and recover deterministically without losing or reassigning media.

Evidence: [move loop and local journal](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:353), [late manifest write](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Import/ImportWorkflow.cs:576), [folder alignment moves](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Indexing/GameIndexFolderAlignment.cs:166), [separate master save](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/GameIndexService.cs:64).

### 8. P2 — An unfinished filename-rule token can freeze the editor

**Bounded compiled-method probe plus source confirmation.** The readable-pattern tokenizer fails to advance on an opening bracket without a closing bracket. It repeatedly appends empty segments. The editor builds the pattern synchronously during text changes, so starting a token by typing "[" can enter the loop before regex timeouts apply. A valid control completed; the malformed input did not complete within the probe's bound.

Change: guarantee progress in the tokenizer and treat incomplete tokens as normal editing state. Bound preview work. Acceptance: every prefix of each supported token returns promptly with a valid result or an incomplete-input diagnostic.

Evidence: [tokenizer](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/FilenameParsing/FilenameParserService.cs:1368), [editor pattern building](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/UI/Editors/FilenameConventionEditorWindow.cs:681).

### 9. P2 — Duplicate detection mistakes equal-sized files for identical captures

**Reproduced.** The duplicate key contains only filename and byte length. Same-name files containing "AAAA" and "BBBB" were classified as duplicates: moved=0, parkedDuplicates=1, undoEntries=0. The incoming file survives in Import Duplicates but is excluded from the normal library import.

Change: use name/size to shortlist candidates and compare content before treating them as duplicates. Record parking in recovery history. Acceptance: different bytes of equal length import independently; identical captures take the duplicate path without losing recovery visibility.

Evidence: [duplicate decision](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:365), [key construction](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Intake/ImportService.cs:573).

### 10. P2 — Database path identity disagrees with Windows path identity

**Reproduced.** In-memory keys are case-insensitive, while SQLite root/path keys and targeted comparisons are case-sensitive. An exact-path read returned one row; the same path uppercased returned zero. Uppercasing the library root also returned zero. Upserting the uppercased path created two SQL rows for the same Windows file.

Change: establish a stable library identity and canonical path comparison key, preserving the original path for display. Migrate existing duplicate keys deliberately. Acceptance: case variants resolve to one record and cannot hide or duplicate stored metadata.

Evidence: [schema](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:343), [targeted lookup](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:797), [upsert key](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:830).

### 11. P2 — Legacy migration resurrects intentionally emptied indexes

**Reproduced.** Migration uses an empty-table check each time the database opens instead of a persisted migration state. With a legacy cache present: initial migration loaded one photo; explicitly saving an empty index followed by reload restored that photo.

Change: persist migration versions/completion and import legacy inputs once. Treat an intentionally empty table as valid. Acceptance: clearing an index remains cleared across reopening, while first-time migration still works.

Evidence: [open-time migration](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:269), [empty-table triggers](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:1236).

### 12. P2 — An unrelated damaged database can block a healthy library

**Reproduced.** Ordinary game-index loading searches sibling index databases for missing external IDs. Donor files are opened writable and initialized without per-file error isolation. Adding an invalid, unrelated old-library SQLite file caused the otherwise healthy active library load to fail with SQLite Error 26.

Change: move cross-library recovery out of ordinary reads; use selected read-only donors, isolate failures, and record where recovered values came from. Acceptance: a damaged donor cannot prevent active-library browsing or modify other libraries.

Evidence: [ordinary load invokes recovery](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:1526), [donor scan/open](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Indexing/IndexPersistenceService.cs:1324).

### 13. P2 — Successful empty metadata cannot clear old indexed tags

**Source-confirmed.** Empty extracted tags fall back to the previous indexed tags, so removing all tags externally does not clear them on rescan. The extraction API also returns empty snapshots for some unavailable-input/tool cases, leaving callers unable to distinguish successfully empty metadata from an unavailable read.

Change: return read status and per-field presence. Successful empty values should clear mirrored fields; failures should preserve prior data and expose an error. Keep any deliberate index-only override separate from the file mirror. Acceptance: removing the final tag reconciles correctly, while a failed read does not erase known metadata.

Evidence: [fallback to cached tags](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Indexing/LibraryMetadataIndexing.cs:116), [empty default snapshots](C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex/src/PixelVault.Native/Services/Metadata/MetadataService.cs:544).

## Suggested implementation sequence

1. **Correct the narrow defects and add regression fixtures.** Fix filename-token progress, batch-field extraction, game-ID allocation, wrong-file undo, and library-edit failure reporting. Add stale-editor protection before widening background writes. These are separate reviewable changes; the smaller parser fixes can proceed alongside the higher-risk identity/recovery work.
2. **Make file operations recoverable.** Introduce one planned operation covering rename, metadata writes, media/sidecar moves, duplicate parking, sorting, indexing, and undo. Persist checkpoints and expose partial completion/retry results. Reuse it for game-folder alignment instead of building another relocation path.
3. **Unify metadata semantics.** Use one contract for read status, field provenance, explicit changes/clears, and per-file write outcomes. Read embedded and sidecar fields consistently, preserve untouched values, and make the photo index a predictable mirror plus explicitly identified user overrides.
4. **Strengthen storage boundaries.** Separate the durable game registry and authored collection state from rebuildable photo/folder projections. Add canonical keys, row revisions, explicit migrations, and atomic related SQL changes. Ordinary reads should not launch cross-library repair or mutate donor databases.
5. **Improve scale and explainability after correctness.** Query/update affected rows instead of repeatedly replacing complete tables. Replace repeated recursive duplicate discovery with an indexed candidate lookup plus verified content identity. Carry the original parse decision through import rather than repeatedly inferring identity from renamed files. Extend Setup & health with a read-only consistency report and a preview of specific repairs.

Useful user-facing improvements within these changes are a clear per-file import outcome with retry/undo status; a short explanation of which filename rule supplied the game/platform/date; and a metadata inspector showing effective values, their origin, and explicit overrides. Keep the current visual design and established workflows while making their behavior more predictable.

## Validation and limits

The existing focused native selection passed **204 tests, zero failures or skips**, including import, filename parsing/rules, intake analysis, metadata helpers, index persistence/assignment, and rehoming. Passing those tests does not cover the new failure cases above. The review did not run the entire application/Companion suite or an interactive UI walkthrough.

Additional probes used dummy media and temporary SQLite databases. Index probes were rerun against the Debug assembly produced by the focused build. Metadata probes linked the unchanged MetadataService source with a minimal host and used the bundled ExifTool. Import probes exercised the compiled service. These establish the specific component behaviors described; they are not a claim of live-library corruption.

- [Focused test results](C:/Codex/artifacts/foundations-review-20261115/foundations-review.trx)
- [Import probe results](C:/Codex/artifacts/foundations-review-20261115/import-probes/result.txt) and [probe script](C:/Codex/artifacts/foundations-review-20261115/import-probes/probe.ps1)
- [Metadata probe results](C:/Codex/artifacts/foundations-review-20261115/metadata-probes/result.txt) and [probe source](C:/Codex/artifacts/foundations-review-20261115/metadata-probes/Program.cs)
- [Index probe results](C:/Codex/artifacts/foundations-review-20261115/index-probes/result.txt) and [probe source](C:/Codex/artifacts/foundations-review-20261115/index-probes/Program.cs)

The most valuable additions to regression coverage are failure injection between filesystem/sidecar/database steps, stale-editor saves, deletion followed by ID allocation, equal-sized different-content captures, every incomplete rule-token prefix, real ExifTool direct/batch parity, partial-sidecar preservation, valid empty metadata, path variants, migration after intentional clearing, and damaged recovery donors.
