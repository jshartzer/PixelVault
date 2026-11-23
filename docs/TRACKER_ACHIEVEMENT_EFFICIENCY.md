# Tracker and achievement efficiency — November 15, 2026

Implemented in `<source-root>`. The Native changes are published in **0.129.000**, together with the foundation fixes, and the requested App Testing script copied the identical release. Companion remains 0.52.3; Companion-side batching/health updates await a separate release. Existing unrelated edits were preserved. Publication evidence: `<runtime-root>\artifacts\foundations-release-20261115\verification.json`.

## Implemented changes

1. **Tracker history:** additive per-item field-version, note-authorship and typed-provenance projections replace repeated audit scans. Updates and history commit together. SQLite triggers invalidate derived state when older clients or direct writers change history; readers check revisions inside their snapshot. Initial backfill retains exact legacy client versions, and undo retains conflict checks. History remains intact. Native history loads fifty summaries at a time and fetches selected detail asynchronously. New audit batches use compact JSON; old batches are not rewritten.
2. **Achievement reads:** bulk score hydration uses read-only connections after any necessary legacy initialization/migration. Final awards and current canonical rarity remain authoritative. Duplicate observations are skipped under the cross-process lease. Partially initialized databases are detected before reading score tables; corrupt, oversized or unsupported existing snapshots cannot be silently overwritten.
3. **Shared save reading:** Companion groups queued Death Stranding trackers by source into a bounded batch in one isolated Native worker. The existing analysis cache can serve multiple projections. Ready tasks drain without a two-second wait between each tracker. Independent binding/account validation, authority checks, previews, transactions, undo, error handling and cancellation remain in place. Unfinished cancelled work returns to the queue.
4. **Native tracker UI:** virtualized rows create their controls when realized, retain unchanged row content and preserve the current design. Search, filters and grouping retain unsaved notes, counts and item settings by tracker/item identity, including temporarily hidden items. Drafts keep their original field versions, so external changes still cause a save conflict rather than being overwritten.
5. **Interrupted reviews:** Native attempts record process identity and process start time. Dead readers can retry; abandoned Native previews become an explicit review requirement. The tracker offers recreation or discard without applying observations. Durable Companion jobs carry a separate ownership marker and survive service restarts; unknown process liveness is not treated as proof of abandonment.
6. **Source health:** Companion derives outstanding failures from individual persisted source/provider states. One successful tracker no longer hides another failure. Browser status identifies the affected game and exposes last-success/retry information and interrupted reviews.
7. **Achievement detail freshness:** Hub refresh independently checks detail freshness, so a recent notification-only snapshot does not suppress missing rarity/counter details.
8. **Source location:** README, policy, project context, versioning instructions and current-build markers agree on the maintained worktree and runtime root. Five release/build preparation entrypoints reject the stale `<runtime-root>` source checkout before producing output.

## Validation

- Native Release build succeeds. Final full serial suite: **1,579 passed, three baseline failures**, 1,582 total.
- Exact archived HEAD `9363b7683e9f23457104144f232619e116c02c91` reproduces all three failures: two toolbar-spacing cases and one completed-achievement grouping case. Their tests and implementation files are byte-identical to the maintained source; this change does not alter their behavior.
- Companion full fresh-build suite: **215 passed, zero failures**.
- Additional checks include 21 JavaScript unit tests, three notification browser tests, seven rendered native layout checks, and ten repeated score-migration concurrency runs (six concurrent hydrations per run).
- Native batch/cache tests confirm two fixture projections share one analysis. Regression coverage includes mixed old/new history, old-client writes, deletion cleanup, rollback, stale drafts, partial worker failure, cancellation, rebinds, abandoned reviews and durable review ownership.
- All twelve changed PowerShell files parse; all five stale-checkout entrypoints reject invocation before build/output changes.

Evidence is in `<runtime-root>\artifacts\tracker-achievement-efficiency-20261115`: final Native and Companion TRX files, isolated baseline TRX/source-hash proof, history benchmark JSON, UI benchmark output and rendered PNGs under `ui`.

## Measurements and practical limits

These are local synthetic component probes, not whole-app or physical-phone timings. Concurrent test workloads can affect timings.

| Component / fixture | Before | After |
| --- | --- | --- |
| Warm tracker editor, 1,000 items and 46 MiB history | 202–239 ms / about 105 MiB allocated | 17.5 ms / 8.27 MiB allocated |
| Single note save in the same fixture | 333–462 ms / 181–186 MiB allocated | 6.4 ms / 0.84 MiB allocated |
| Warm achievement load, 10,000 achievements / 4.86 MB snapshot | median about 1,033 ms, database writes | median about 143 ms, read-only |
| Repeated tracker row refresh, 1,452 records, no window layout | median about 332 ms / 84 MiB allocated | median about 13.3 ms / 3.57 MiB allocated |

The first history projection backfill took approximately 456 ms for the 46 MiB fixture. Maintaining projections added about 30 ms to a 1,000-item preview/apply operation (97.5 ms versus 67.8 ms using the legacy-schema fallback). This is a deliberate tradeoff for substantially cheaper ordinary reads and edits. First backfill holds a write transaction, and old-version writers invalidate the projection; account for this during eventual rollout. No live database was migrated or benchmarked by this implementation pass.

The future deployment must package the updated Native reader with Companion because the worker protocol now includes `read-batch`. Preserve the existing service identity, data roots and source permissions. Published versions, shortcuts, services and live progress were not changed by this work.
