# PC foundation fixes — November 15, 2026

Implementation follow-up to [the foundation review](FOUNDATIONS_REVIEW_2026-11-15.md). The thirteen numbered findings are addressed and published in Native **0.129.000**. The requested App Testing script then refreshed the testing folder with the identical executable; all 391 bundled source files match. Existing tracker/achievement work was preserved and its tested Native improvements ship in this release. Companion remains 0.52.3; its canonical-path reader and batching updates await a separate release. Publication did not launch the new GUI or migrate live data. Evidence: `C:/Codex/artifacts/foundations-release-20261115/verification.json`.

Close older Native GUI instances before first opening this release: older index replacement writes are rejected after migration. Current Companion reads remain compatible with the configured `E:\Game Captures` root spelling; case/trailing-separator-only changes need the pending Companion reader update. The Companion service and reader pinned to Native 0.128.021 were left unchanged.

## Changes and regression coverage

| Review finding | Implemented behavior | Main regression coverage |
| --- | --- | --- |
| 1. Wrong-file import undo | Record the actual final path and SHA-256 content identity, including sidecars. Undo verifies identity and refuses ambiguous/replaced files. Preserve Undo Last Import through explicit batch IDs. | `ImportRecoveryTests`, `FileRelocationJournalTests` |
| 2. Reused deleted game IDs | Allocate new opaque GUID-based IDs; preserve existing IDs. Deleting the highest legacy numeric ID cannot transfer its photos to a new game. | `IndexIdentityAndMigrationTests` |
| 3. Stale editor snapshots | Keep a deep baseline and merge only edited fields against current saved rows under a library write guard. Preserve unrelated concurrent changes and new records; report overlapping edits/deletions as conflicts. Resolve-ID actions remain drafts until Save. Merge duplicate game rows only after applying current state. | `IndexEditorChangesTests`, `IndexEditorDuplicateMergeTests`, `PhotoIndexSaveRehomeSqliteIntegrationTests` |
| 4. Failed library metadata edits | Return per-file failures, leave failed library captures in place, retain retry flags, and update/index only successful writes. Editor-opening/read errors and partial save failures reach the UI. | `MetadataReadContractTests`, import failure tests |
| 5. Shifted metadata columns | Replace positional TSV with group-qualified JSON fields and one parser shared by direct/batch reads, including QuickTime ModifyDate, rating and device fields. | `MetadataReadContractTests`, real ExifTool probe |
| 6. Partial sidecars hide embedded fields | Overlay only present fields in unmarked sidecars. Before an app write, seed complete effective metadata and mark its sidecar so explicit clears remain authoritative. | `MetadataReadContractTests`, real ExifTool write/read/clear roundtrip |
| 7. Interrupted file operations | Flush intent before filesystem changes and retain checkpoints until index commits. Recover interrupted media/sidecar pairs and replay pending index mappings after restart. Reconcile folder moves without overwriting authored identity, notes, tags or stars. | `FileRelocationJournalTests`, `ImportRecoveryTests`, `LibraryRelocationIndexRepairTests` |
| 8. Incomplete filename tokens freeze | Guarantee tokenizer progress for an unfinished opening bracket and all supported token prefixes. Also load parser caches outside their internal lock and reject stale cache publication. | `FilenamePatternEditingTests`, `FilenameParserCacheConcurrencyTests` |
| 9. False duplicates | Shortlist by name/size, then verify media and sidecar bytes before duplicate parking. Parking is undoable; replacement uses a recoverable backup. | `ImportRecoveryTests` |
| 10. Inconsistent Windows path identity | Add canonical path comparison keys while retaining display paths. Migrate duplicate keys transactionally; retain conflicting originals in the migration archive. Update Companion/export readers for both schemas. | `IndexIdentityAndMigrationTests`, Companion `IndexRootIdentityTests` |
| 11. Legacy imports resurrect cleared indexes | Record completed migration versions; an intentionally empty index stays empty across restart. | `IndexIdentityAndMigrationTests` |
| 12. Damaged donor blocks healthy library | Perform donor recovery once during migration, read donors without modifying them, and isolate/record per-donor failures. Ordinary reads do not repeat cross-library repair. | `IndexIdentityAndMigrationTests` |
| 13. Empty reads cannot clear tags | Distinguish successful, failed and unavailable extraction. Successful empty fields clear the mirror; unsuccessful/missing results preserve prior tags, stars and stamps. Background repair rechecks row/file revisions before applying extracted metadata. | `MetadataReadContractTests`, six real SQLite scan/upsert failure cases, cache responsiveness regression |

## Persistence and recovery contracts

- Related editor/scanner writes use a reentrant per-library process mutex. The lock order is maintenance lock, process guard, then metadata/folder cache locks. Background metadata extraction runs outside these locks and revalidates before committing.
- File relocation journals retain content identity and exact source/target paths. Prepared pairs roll back; completed moves retain recovery information until dependent index saves finish. Recovery does not select files by basename. Acknowledged empty journals are removed.
- Stored game membership paths survive temporary missing files so interrupted moves can be reconciled. Explicit scan/reconciliation workflows own pruning; persistence reads must not erase recovery evidence.
- Required pre-upgrade SQLite backups live under the configured cache root's `index-migration-backups` directory. Each includes a JSON manifest identifying the original database/library. Backup creation and integrity verification must succeed before canonical-key schema changes. These backups are separate from rolling-backup pruning.
- Canonical migration preserves conflicting originals in `index_migration_archive`; donor failures are recorded in `index_recovery_issue`. Legacy replacement inserts are rejected after migration. **Returning to an older Native release requires restoring the matching pre-upgrade database backup first.** Restore only with all library writers stopped and retain the upgraded database separately to preserve later edits. Companion and export readers in this checkout support either schema.

## Validation

Integrated verification results are recorded in `C:/Codex/artifacts/foundations-fixes-20261115`.

- Companion full suite: **219 passed**, zero failures/skips (`companion-full.trx`).
- Connector full suite: **29 passed**, zero failures/skips (`connector-foundations-final.trx`).
- Export Release build: succeeded; zero errors, one existing NU1510 warning (`export-final-build.log`).
- Native full suite: **1,650 passed / 3 pre-existing failures / 0 skipped**, 1,653 total (`native-final.trx`). No task-related failures remain. Final focused import/parser selection: 91 passed (`import-final.trx`); index/relocation selection: 88 passed (`index-relocation-final.trx`).
- Real bundled ExifTool roundtrip preserves embedded tags, comment, capture date and device fields through a star sidecar write; clearing tags/comment remains clear while other fields survive. Evidence: `C:/Codex/artifacts/foundations-review-20261115/metadata-probes/fixed-roundtrip-output.txt`.

The three existing Native failures are two toolbar-spacing assertions in `TrackerDashboardTests` and one completed-group assertion in `AchievementGroupingTests`. They were independently reproduced on an isolated unmodified baseline before this work; their tests and relevant implementation source remain unchanged. Baseline evidence: `C:/Codex/artifacts/tracker-achievement-efficiency-20261115/baseline/baseline-verification.json`. Current hash comparison: `C:/Codex/artifacts/foundations-fixes-20261115/baseline-source-equivalence.json`. The final verification summary checks that the failed test names exactly match that baseline (`verification.json`).

Tests use temporary libraries/databases and fault injection; there was no live-library migration or interactive desktop acceptance run in this implementation pass. Fingerprint verification adds file I/O, particularly for large videos moving through multiple stages; large-video throughput has not been benchmarked. The review's broader ideas for a metadata-origin inspector, filename-rule explanations, indexed duplicate candidate lookup and a health/repair preview remain product follow-ups, distinct from its thirteen defects.
