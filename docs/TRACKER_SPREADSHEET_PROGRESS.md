# Spreadsheet progress and saved refresh mappings

ACHG-004 Phase F is in progress in App Testing. This increment adds progress-aware creation, a reusable spreadsheet source and separate definition/account exports. It uses the same universal game/optional-achievement form, account isolation, reconciliation, history and undo as other tracker sources.

## Use

1. Choose **Import tracker… → Spreadsheet…** from the Hub, game or achievement tracker view. Achievement entry prefills the game and optional achievement link.
2. Choose an XLSX/CSV file and worksheet. Map names and any optional category, target, details, stable ID, current count, completed, availability and notes columns. Review the first 25 rows; validation covers the entire worksheet.
3. Create the tracker. Definition and initial observations commit together. Personal observations require a connected account; a blank template can create an unknown checklist without one.
4. To reuse a workbook, choose **Source… → Excel workbook / CSV progress**, load it, map a stable ID plus Current or Completed, and save the source. Creation alone does not connect or enable a source.
5. Choose **Refresh**, review the incoming counts, availability and note actions, then apply or cancel. Existing **Automatic refresh…** modes are available through explicit opt-in; the source defaults to manual use.

Mappings are saved with the local tracker/account source binding as worksheet/header names. Column and row reordering therefore preserve item identities. Renamed, missing or ambiguous headers require remapping. Imported progress matches the existing definition's stable IDs and target counts. Name/category/details/target metadata is not changed by a progress refresh.

## Values and reconciliation

| Field | Meaning |
| --- | --- |
| Current blank | Unknown/null, distinct from an explicit zero |
| Current | Whole number 0–1,000,000 |
| Completed | true/false, yes/no, 1/0, complete/completed/incomplete/not complete |
| Completed without Current | True supplies the item's target; false supplies zero |
| Both count and completed | Must agree with whether the target is reached |
| Availability | Unknown, Available or Locked; blank is Unknown |
| Notes | Optional text, at most 20,000 characters |

Missing rows leave existing items untouched. An explicitly present blank count is an unknown observation. Duplicate/blank IDs and IDs outside the existing definition block refresh. New one-time definitions may omit ID mapping and receive row IDs; refresh always requires explicit stable IDs.

Reported counts and availability update through the guarded preview. Manual counts remain separate until the user chooses Use source. Incoming nonempty notes only fill untouched empty notes; existing notes and notes deliberately cleared by an active manual note edit remain protected. The preview lists note additions and exposes incoming text by tooltip. Later imported notes do not replace earlier saved notes. Undo checks affected fields and preserves independent later edits.

The source is `pixelvault.spreadsheet` version 1. It reads a bounded, stable, read-only XLSX/CSV, computes SHA-256 and uses file last-write UTC as observation time. Older or equal-time conflicting observations require explicit reconciliation. File content deduplication, changed-binding/state guards, cancellation, history and optional scheduling use the existing shared services. No formulas or workbook instructions are executed. Save/close an open workbook if its writer prevents a stable read.

## Exports

**Export…** offers:

- **Definition JSON:** definition metadata, stable IDs, groups, category targets and optional achievement link; no account progress, notes, machine paths or source configuration.
- **Blank Excel template:** item IDs/names/categories/targets/details with blank personal columns.
- **Account progress Excel:** the selected account's effective current counts, completion state, availability and notes. This includes personal data and manual overrides as effective values, without exporting their underlying history/source configuration.

XLSX cells are plain inline values, including text beginning with `=`. Blank count/Completed exports remain blank for unknown progress. Templates freeze/filter the header and use readable column widths. Spreadsheet reimport does not recreate manual-vs-reported provenance from an account export.

## Remaining Phase F work

[Explicit catalogue reconciliation](TRACKER_CATALOGUE_RECONCILIATION.md) and [portable definitions](TRACKER_DEFINITION_PORTABILITY.md) are implemented. Header-row choice and source-setup improvements remain planned. Progress Refresh does not merge metadata; use Update current catalogue… for that separate reviewed operation. XLSX flattens active items; JSON retains groups and archives. Excel-produced-file and broader keyboard/OS scaling/high-contrast QA remain open. E separately needs LL/chip controls.

## Verification

October 23 follow-up: complete definition JSON round trips are implemented. All 163 tracker tests plus four foil cases pass (167 focused), and the full suite passes 1,063/1,063 with no skips. The prior two shader failures are fixed. Source/import/review and new definition-import layouts inspected; prior figures below are the earlier baseline.

All 150 focused tracker cases pass, including 16 new spreadsheet progress cases. They cover blanks/zero/targets, contradictory/invalid rows, account isolation, atomic creation, blank account-free templates, stable-ID/column reordering, partial/missing/duplicate IDs, preserved/cleared notes, manual conflicts, stale/equal-time notes, cancellation, deduplication, stale previews, scoped undo, XLSX round trips and source/form rendering. Real copied-save worker integration remains passing.

Full suite: 1,047 passed, two previously documented unrelated TradingCardFoil shader failures, 1,049 total. Results: `tracking-phase-f.trx` and `tracking-phase-f-full.trx` under the native tests' TestResults directory. Populated import/source/review and minimum-width tracker layouts were rendered and inspected.

Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`. Published version remains 0.077.000; existing running app sessions were not stopped.
