# Catalogue updates and compact category groups

ACHG-004 Phase F: explicit catalogue updates are implemented for custom trackers. The shared form/layout use definition metadata for every game, without interpreting particular item names or milestone labels.

## Update items

Choose **Import tracker… → Update current catalogue…**, load XLSX/CSV or portable definition JSON, then explicitly choose additions, item metadata updates, missing-item archives and/or restorations. All options default off. Review stable IDs, selected actions and current/incoming metadata before applying. Tooltips expose full values; worksheet/column mapping can be expanded. Cancel writes nothing.

Catalogue metadata is shared by all accounts; saved counts, notes, observations, source bindings and automatic settings stay separate. New IDs have no inferred progress. Reusing a retained ID preserves its saved state. Tracker identity/title, achievement link, milestone columns and category goals are retained. Bundled source catalogues are read-only; import an independent definition copy to customize them.

Spreadsheet mapping requires stable IDs. Existing unmapped metadata and sort order are retained. New items need names; unmapped targets default to one, mapped targets must be positive integers. Mapped blank categories/details clear them. Progress/notes columns are ignored. Optional Record ID and Milestone mappings support complete grouped records using any of the definition's labels. Duplicate/blank IDs, mapped formulas/errors, invalid matrices and exceeded size/item limits block updates. JSON must match provider/game and milestone columns. Whole milestone records must archive/restore together.

Present spreadsheet rows become restoration candidates only when Restore is selected. JSON preserves explicit Archived flags. Optional per-item Archived is omitted when false. Archives leave the active list/item totals but retain all item-state rows. JSON exports retain archives; spreadsheet exports include active items.

## Transactions and undo

Custom definition changes and tracker_catalogue_history commit together. History stores before/after metadata, time, source filename and SHA-256 of the reviewed incoming metadata. No progress/source tables are written. A changed catalogue invalidates its old preview; definition changes also invalidate prepared progress previews. Failures roll back the entire operation.

**Undo last catalogue update** restores only the previous definition, preserving later progress/note edits. It refuses a different later definition. State for added items remains retained if undo removes them from the definition; reintroducing their IDs recovers that state. Source bindings stay unchanged; review source coverage separately after changing a catalogue.

## Shared compact view

Records are **34 px high plus 3 px spacing**, half the former ordinary 68 px card plus 6 px spacing. Names remain 18 px on one line with ellipsis/full tooltip. Counts and note markers stay inline; full details remain in the side panel. Right-aligned completion controls are **25 px high**, with Not Complete/Completed text. Milestone labels/widths come from the definition; long or three/four-column sets have horizontal access without taller rows.

Category metadata produces section headers and visible record counts. Mixed uncategorized records use Other; trackers with no categories have no headers. Search/status/category filters retain overall active totals. Grouping uses the shared collection view and virtualization; headers are not selectable item records. Game choices derive from library/registered metadata instead of an injected fixed game.

## Verification

Eighteen new cases cover account retention, explicit/no-op choices, archives/CSV restoration, independent/stale undo, stale catalogue/progress previews, atomic rollback, bundled protection, invalid input, spreadsheet reordering/formulas, arbitrary grouped extensions, categorized/uncategorized/empty filters, exact row heights/right controls, four milestones, JSON/CSV UI, cancellation and both window widths. Fixtures include unrelated fantasy/racing games across Steam/RetroAchievements, counts and 1–4 milestone shapes. Only temporary stores are changed.

All **181 tracker +4 foil cases pass (185 focused)**, including real copied-save integration. Full suite: **1,081 passed, zero failed/skipped**. Results: tracker-catalogue-focused.trx, tracker-catalogue.trx, tracker-catalogue-full.trx. Catalogue and compact/grouped/ungrouped/multi-milestone layouts were rendered and inspected.

Review: C:\Codex\App Testing\Achievement Trackers\PixelVault.exe; published version remains 0.077.000. No app stopped or live progress/game saves edited. F remains In Progress for header/source-setup improvements and Excel/OS QA; E still needs LL/chip controls.
