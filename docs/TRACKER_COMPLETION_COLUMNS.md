# Shared tracker styling and named completion columns

Implemented for App Testing on October 23, 2026. Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`. Published version remains 0.077.000.

## Creating a tracker

Both **New checklist** and **Import tracker… → Spreadsheet…** offer **Multiple named completion boxes per item**. Enable it, choose 1–16 boxes and name each box. A tracker uses the same named set for all its records. Names must be distinct (ignoring case), nonempty and at most 80 characters. The overall limit is 10,000 boxes. Multiple boxes are binary; count targets remain available for ordinary trackers.

Examples: Solved / Perfect, Discovered / Collected / Restored, or LL / LLL. These use the same definition, progress, grouping and UI code. There is no game-specific completion form. The existing Death Stranding premium-order definition retains its IDs, independent LL/LLL state and category-specific qualification rules.

Each box has its own stable item ID, reported/manual progress, notes, provenance and undo history. The record ID groups related boxes. A new spreadsheet tracker's box IDs are derived deterministically from record ID plus completion name, so row/column reordering does not alter identity. Renaming existing definition columns is not offered by this creation feature; it requires a deliberate migration, not a refresh.

## Spreadsheet layouts

The authoring form supports two layouts:

1. **One row per record:** enable named boxes, name them and optionally map a boolean column for each box. Map a stable item ID as the record ID for future refresh. Each mapped column accepts true/false, yes/no, 1/0, complete/completed or incomplete/not complete. Blanks stay unknown; unmapped boxes are unobserved. Counts other than binary targets are rejected. Current/Completed and row-per-box mappings are inactive in this mode.
2. **One row per box:** leave the named-column option off and map Record ID plus Milestone alongside the ordinary stable item ID and optional progress fields. Every record must contain the complete named set, with a shared name/category and target one. Named sets are inferred and validated before creation. This is also the grouped Excel export layout.

Saved spreadsheet sources can refresh either layout through the existing review/history service. Wide sources save completion names/header mappings, match stable record IDs and retain independent child item IDs (including existing bundled IDs). Missing records and unmapped columns remain unchanged; malformed or unknown IDs fail before writes. Manual overrides and authored notes keep their existing protection. Blank creation templates remain account-free. Creation does not automatically connect a source or enable automatic refresh.

Grouped Excel exports now include Record ID and Milestone so reimport preserves the grouping and child identities. Portable definition JSON continues to preserve named columns without account observations. Existing ordinary Excel exports keep their previous nine columns.

## Shared presentation

Tracker browsing windows are modeless siblings, allowing earlier pickers to close without closing a later tracker. The main form exposes **Group by category** for any categorized definition; toggling retains the selected record and unsaved note/count drafts. Headers show completed/total active records across the full category, plus a separate capped goal if the definition provides one. Paired/named boxes qualify their record once when any is complete. Filtering does not change full-category totals. No category metadata hides the option; mixed blank categories use Other. This is shared presentation, with no game-specific form or storage mutation.

Tracker dialogs share dark dropdowns/popups, tables, checkbox controls, history lists, expanders and scrollbars, with native system controls retained in high contrast. Primary save/create/apply/refresh actions have a distinct teal treatment; Cancel and Close support Escape. Labels/help text have a 14 px minimum.

The main tracker retains compact 34 px records, 18 px names and right-aligned completion indicators. Management actions live in Manage tracker…; creation/import remain visible, Refresh sits beside search/filters, and history stays directly available. Ctrl+F focuses search. Details are resizable and can be hidden; they collapse by default below 920 px of content width. Source/stat summaries use one line with full tooltip details. Optional spreadsheet mappings are collapsed to reduce form height. Long sets of completion columns use the existing horizontal scrolling.

Refresh also serves as the connection recovery/setup entry point. It reloads the saved binding on demand rather than relying on cached form state. Missing account/source reasons appear below the filters; a missing source opens Source setup, while an absent account cannot create a binding. Existing native save connections are retained and reread directly. Current fabrication, construction and maximum-upgrade native reads were verified through a private database copy and the packaged worker; no live progress was applied.

## Validation and remaining work

Eleven new authoring cases cover independent boxes, unknown/zero, stable IDs under reorder, source persistence/refresh/override/undo, account-free blanks, 16-column portability, limits, malformed values/formulas, grouped Excel roundtrip and actual modal manual creation with an optional achievement link. All 1,092 native tests pass, zero failed/skipped (`tracker-form-polish-full.trx`). Current WPF layouts were rendered at normal/narrow widths. Broad OS scaling/high-contrast/keyboard and Excel-produced-file QA remain open.

The first visual pass is complete. Follow-ups remain: staged file/mapping/preview navigation, more concise field-level reconciliation, unified progress/catalogue history, category collapse/progress, configurable header rows and creation-to-source handoff. Phase F stays In Progress; Phase E still needs LL/chip validation.
