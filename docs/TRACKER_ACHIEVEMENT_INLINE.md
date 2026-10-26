# Compact linked trackers in achievement screens

Implemented October 24, 2026 for App Testing. Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`; published version remains 0.077.000.

## Access and presentation

The achievement guide reader and Hub achievement detail screen show their linked tracker below the guide. Matching uses provider, provider game ID and achievement ID, with the window's account context. Each linked tracker has its own collapsed-by-default expander; no links means no extra section. Selecting another achievement/account resets expansion. Refreshing the same selection preserves the user's expansion choice and guide draft.

The shared view uses 12.5 px item names, 12 px checkbox labels, compact rows and a bounded 264 px list with virtualization. Ordinary items have one checkbox; named milestones share a row with independent labels, including LL/LLL without a separate game-specific form. Archived items are excluded. Header progress retains unique-record counting and capped category goals. A missing account shows Connect account and disables editing rather than inventing progress. Full names and descriptions, including chip coordinates, remain available on hover.

The component reuses scoped tracker checkbox/expander/scrollbar styling without restyling the enclosing guide. Native high contrast, focus cues, selection/scroll retention and keyboard access remain. Full tracker notes, count editing, import/refresh/reconciliation and management remain in the existing forms.

## Checkbox writes and game confirmation

- Checking a manual/incomplete item writes its target count; unchecking writes zero. Named columns remain independent. Ordinary counts are represented as completion only here.
- Writes use existing manual-field history and can be undone from tracker history. They preserve reported counts, availability, evidence, notes, source bindings, automatic settings and provider achievement status.
- A positive game-file observation cannot be unchecked in this compact view. Its checkbox stays checked and disabled, with an explanatory tooltip. Existing manual overrides remain authoritative; checking an overridden game completion restores the reported value by clearing that manual override.
- Provenance comes from the latest non-undone reported-field history record and the registered `ITrackerGameFileSource` adapter identities, with an exact match to current reported fields. It does not trust a source/evidence label or infer origin from the current binding. Removing/replacing a binding does not erase confirmed provenance. Notes and manual histories do not obscure it; lookup is not limited to the last 100 displayed history entries. External spreadsheet/JSON observations remain manually overrideable.
- The compact-write method rechecks the current record, source history and custom item target/archive state inside its SQLite transaction. An automatic observation arriving after rendering cannot be unchecked by a stale control. Errors restore the current checkbox state and appear inline.

The full tracker's explicit manual override/reconciliation/undo behavior is unchanged; this protection applies to the compact completion action.

## Refresh and implementation

Opening, expanding or polling this section reads saved tracker metadata/observations only. It never reads saves, invokes a source adapter, imports progress or enables automation. While loaded and expanded, it checks saved state every five seconds and skips unchanged revisions. It stops when collapsed/unloaded and also refreshes on window activation. It preserves note/count drafts in other forms and does not edit guide drafts.

`AchievementInlineTrackers` is shared by both achievement screens. Guide entry points from the game achievements list, game profile, Hub detail and stored Next Up guides pass explicit store/account context. `AchievementTrackingInline` adds the guarded manual action and history-based provenance lookup without a database schema change. The private guide constructor remains compatible with existing callers/tests.

## Verification

Ten new cases cover manual counts/notes/undo, account and provider separation, multiple/unlinked trackers, no account, archives, native completion after 105 later note edits and source removal, spoofed external labels, reported undo, stale-control races, draft/selection retention, both screen integrations, independent LL/LLL and virtualization of 540 order rows. All 21 focused cases including existing guide/detail tests pass (`inline-achievement-trackers-focused.trx`). Collapsed/expanded, 900 px guide, 640 px detail and order-rank layouts are rendered and inspected under `App Testing\Achievement Trackers QA\Inline achievement tracker`.

Full native suite: **1,157 passed, zero failed/skipped** (`inline-achievement-trackers-full.trx`). A read-only check of the actual memory-chip tracker finds all 56 states and correctly identifies its 22 current native-confirmed completions from matching history. The review build is updated at the path above without stopping running apps. Live progress, guides, notes, game saves and source/automatic settings are retained. Phase E/F remaining scope is unchanged.
