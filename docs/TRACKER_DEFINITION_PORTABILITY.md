# Portable tracker definitions

ACHG-004 Phase F: definition export/import is implemented in App Testing. The catalogue reconciliation work remains in progress separately.

## Use

Choose **Export… → Definition JSON** to share a tracker definition. In the Hub, game or achievement tracker window, choose **Import tracker… → Definition JSON…**, select that file, and review its game, title, optional achievement link, item/group totals, category targets and item preview.

An import opened from a specific achievement prefills that achievement's link. The link can be changed or cleared. Imports opened in a game must match its provider/game; import a different game's definition from the Hub. File game identity stays fixed during review.

The exported tracker identity is retained when it is new. If that identity already exists, import is disabled until **Create an independent copy** is explicitly selected. A copy receives a new tracker ID while preserving item IDs, groups, milestones and targets. It leaves the existing tracker and all personal state intact. Cancel saves nothing. A duplicate appearing after the preview is rejected by storage rather than overwritten.

Definition import does not need a connected account. All imported progress starts unknown; no source or automatic refresh is enabled. Account observations, manual counts, personal notes, history and machine-specific source settings remain separate. A definition's item descriptions are shared metadata, so authored guidance intentionally included there remains in the export.

## Format and compatibility

New exports use an envelope with `Format: "PixelVault.TrackerDefinition"`, `Version: 1`, and `Definition`. Earlier raw definition-only JSON exports are also accepted. Unknown versions, unexpected fields, duplicate JSON fields, progress snapshots and source/account settings are rejected. The importer accepts metadata only and never executes workbook instructions or adapter commands.

Both formats preserve the complete definition: provider/game, title/description, optional achievement link, completion label, stable item/record IDs, milestone columns, category targets, item counts, sort order, details and identifier metadata. Files are bounded to 4 MiB and JSON depth 32; item counts/groups and field lengths are validated before saving. JSON definition import differs from account-bound **Import progress** and from XLSX/CSV creation/refresh.

XLSX templates flatten active milestone items into stable ID rows; JSON retains complete grouped definitions and optional archive flags. Definition import creates an independent definition. [Catalogue reconciliation](TRACKER_CATALOGUE_RECONCILIATION.md) now updates existing custom item metadata with explicit archive/restore and undo choices.

## Verification

Thirteen new cases cover all five bundled definition round trips and legacy compatibility; counted/grouped targets and links; account-free creation; explicit copies retaining existing progress/notes; snapshot/source/duplicate-field/version/size/group rejection; game scope; achievement-prefilled/clearable links; cancellation; unsaved draft protection; shared UI entry and normal/narrow layouts. Tests use temporary stores only.

All 163 tracker cases and four foil cases pass (167 focused). The full native suite passes **1,063/1,063**, with no skips. Results: `tracking-portability-focused.trx`, `foil-definition-import.trx`, `foil-portability-full.trx`. The previous two foil rendering failures are fixed by the shader-model compatibility change documented in [Foil rendering](TRADING_CARD_FOIL.md).

Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`. Published version remains 0.077.000; running app sessions and live tracker state were not changed by this work.
