# Achievement checklist tracking

October26 reference-content addition: records can carry optional plain text and show a compact **Content** button, including within linked achievement lists. The reader supports UTF-8 Save copy without changing progress. Death Stranding has a separate149-entry English interview catalogue; automatic67/100 remains in the original counter and individual read states remain manual. See [tracker content](TRACKER_CONTENT.md).

October25: desktop trackers now support Manage tracker → Rename tracker for bundled/custom catalogues. Durable display-name metadata preserves stable identities, all progress/notes/source/automatic settings and unsaved drafts. See [Tracker names](TRACKER_RENAMING.md).

Shared storage update: definitions/validation, SQLite persistence, reconciliation, history and provenance now live in `src/PixelVault.Tracking`. Native `AchievementTrackingStore` retains its UI/game integration wrapper. PixelVault Companion 0.2.0 edits a verified separate workspace through revision-checked API commands; native windows have not switched databases and do not synchronize with that copy yet. See [Companion setup](COMPANION_SETUP.md).

October 24: optional map links are now available from achievement detail/guide screens and full trackers. Maps use game/provider identity, with context-prefilled links and independent durable image storage; profile pages also expose Maps and Trackers. Death Stranding has all 56 chip pins across three calibrated regional maps. See [Saved game maps](GAME_MAPS.md).

October 23 browsing follow-up: dashboard/game-list/tracker windows are modeless siblings, so earlier pickers can close while later trackers stay editable. Shared Group by category toggles delivery-type grouping in premium orders and categories in any game. Headers show completed/total active records plus separate capped goals; filtering preserves totals and LL/LLL count each order once. All 42 focused and 1,129 full native tests pass; normal/narrow grouped layouts inspected. See [Dashboard navigation and grouping](TRACKER_DASHBOARD.md).

October 23 dashboard follow-up: Library/Hub compact cover dashboard → per-game tracker list → exact tracker, with same-game switching and a visible achievement-linked shortcut. Cards/list/forms identify native game-file connections and automatic availability/state. See [Tracker dashboard](TRACKER_DASHBOARD.md) for scope and pending reader validation.

October 23: shared dark forms and generic 1–16 named completion-column authoring are implemented for manual and spreadsheet trackers. Main management actions, Refresh/search and responsive details were simplified. See [Completion columns and styling](TRACKER_COMPLETION_COLUMNS.md) for current behavior, spreadsheet layouts and validation. Earlier dated scope below is historical.

First implementation: October 21, 2026. This is an unreleased feature in the native PixelVault app.

## Access and use

- Achievement guide/detail screens include linked trackers below the guide as collapsed, compact checkbox lists. Manual check-offs can be reversed; game-confirmed completions cannot be unchecked in this view. Named columns remain independent and all writes retain account scope, notes and history. See [Compact linked trackers](TRACKER_ACHIEVEMENT_INLINE.md).

- **Import tracker… → Update current catalogue…** reviews custom additions, metadata changes, archives and restorations from XLSX/CSV or JSON, preserving saved counts/notes with separate catalogue undo. See [Catalogue reconciliation](TRACKER_CATALOGUE_RECONCILIATION.md).

- Per-game achievement page: **Trackers**, or right-click an achievement and choose **Track progress…**.
- Achievement Hub: **Trackers** opens all saved checklists; right-click a collection achievement or open its detail window for the linked checklist.
- The same entry points are available when opening achievements from notifications, the game profile or Timeline.
- **New checklist** creates a game-wide checklist from the Hub, or prefills the optional achievement link from the achievement used to open it. Pick a game, title and one item per line. The achievement picker is restricted to that provider/game; changing the game clears the link.
- **Achievement link…** links or unlinks the selected tracker, including the bundled Death Stranding tracker. Choose **No achievement link — game-wide** when the tracker is unrelated to an achievement. Progress and notes stay with the same tracker.
- **Import tracker… → Spreadsheet…** opens the same universal creation form from the Hub, game or achievement context. Achievement context prefills its optional link. Choose an `.xlsx` workbook or CSV, select a worksheet and map item name, category, target count, details, stable item ID and optional current/completed, availability and notes. Review before creating; blank progress stays unknown. Initial personal observations require the connected account.
- **Import tracker… → Definition JSON…** reviews a portable definition with groups, targets and a clearable optional achievement link. Existing IDs require an explicit independent copy. Definitions start with unknown progress and need no account. See [Definition portability](TRACKER_DEFINITION_PORTABILITY.md).
- **Export…** saves a definition JSON or blank XLSX template without personal progress, or explicitly exports the selected account's effective progress/notes to XLSX. See [Spreadsheet progress](TRACKER_SPREADSHEET_PROGRESS.md) for portability limits.
- Check off items, add notes, search names/notes, and filter by category or state. Filtered views do not change overall totals.
- Ready and remaining is the initial filter when imported observations identify available work. Otherwise All remaining includes unknown items.
- A selected item's manual count can be cleared with **Use imported progress**. Notes remain. Count-based items (target greater than one) show a count editor; completion and rank states use large toggle buttons.

Every record uses a 34 px bordered card, half the former ordinary height, with 3 px spacing. Names remain 18 px on one line with full tooltips. Category metadata produces section headers and visible record counts; mixed uncategorized records use Other, while trackers without categories remain ungrouped. Right-aligned completion controls are 25 px high with Not Complete/Completed text. Missing observations remain null, and full provenance/conflicts stay in details. No checkbox glyph is displayed.

Cards use 10 px horizontal/3 px vertical padding. Counts/note markers stay inline; full names/details remain in tooltips and the side panel. Longer or three/four-column milestone sets provide horizontal access without taller records. Selecting a milestone and Use imported progress retain independent values and notes. Keyboard focus/toggle semantics remain available; minimum window height stays 640 px.
- **Source…** connects a progress JSON file, an XLSX/CSV progress sheet with saved header/ID mappings, or the Death Stranding Director's Cut reader for fabrication/construction/maximum upgrades. It displays paths, profile label, adapter/version, last attempt/success, observation time, fingerprint and result. Disconnecting the source keeps all progress and notes.
- **Refresh** rereads the configured file and opens a reconciliation preview. It is disabled until a source is configured. **Cancel refresh** cancels an active read.
- **Automatic refresh…** opts this source/account into refresh on tracker open, after save changes or periodically while PixelVault runs. Default is off. **Pause automatic / Resume automatic** retain the mode. Routine updates apply while manual conflicts, rollbacks and undone batches require review. Recent automatic checks appear in the settings dialog. See [Automatic refresh](TRACKER_AUTOMATIC_REFRESH.md).
- **History / undo** shows the most recent 100 recorded changes. Inspect and undo a manual count, note, import or complete refresh batch. Earlier changes made before this history feature was installed cannot be reconstructed.

## Death Stranding foundation

The bundled definition is `Services/Achievements/TrackingData/DeathStrandingBuildables.json`, embedded as `PixelVault.Tracking.DeathStranding.json`.

- Provider: Steam; game ID: `1850570`; achievement ID: `062` (Homo Faber, verified against saved provider definitions).
- Definition ID: `death-stranding-dc.homo-faber`.
- Exactly 108 entries marked `TrophyTarget` in the installed Director's Cut catalogue, including level and color variants.
- Stable item key: catalogue UUID. Referenced item UUID, numeric ID, item type and catalogue name code are preserved as metadata. Numeric IDs alone are ambiguous between item types.
- Categories: Weapons, Equipment, Vehicles and Tools. Material costs are available in item details.
- Metadata comes from a read-only Decima Workshop v0.1.27 extraction. The source extraction and validation reports remain under `C:\GameDataExtractor\Backup Data\Death Stranding Director's Cut\Reference\Catalogue`.

Personal progress is **not embedded in the definition or source code**. The user-authorized initial snapshot is a private file under `PixelVaultData/achievement-tracking/imports`. It is imported only for the matching Steam account, once per file content digest. The October 21, 2026 12:09 Central save records 35 fabricated, 17 ready and 56 apparently locked. The save checksum, the complete 408-record catalogue sequence, user-provided positive/negative UI checks and the cached Steam counter (35/108) validate the initial interpretation. Availability still reflects the save's inferred blueprint flag; actual fabrication also depends on facility support and materials.

The 108-item checklist concerns fabrication for Homo Faber. Placing and upgrading structures can be added as separate checklists; they are not inferred from fabrication progress.

Three additional bundled catalogues cover **Master Builder** construction (13), **Trail-Blazer** maximum upgrades (10) and **Fount of Knowledge** chip restoration (56). Construction and upgrades now support native save refresh through a validated counted persistent-fact map; chips remain manual. Their achievement entry points select the matching catalogue. Unobserved progress starts unknown, and notes/check-offs/link edits remain independent. See [World trackers](DEATH_STRANDING_WORLD_TRACKERS.md) for current reader validation and remaining chip/order gates.

## Reusable model and ownership

The Growth of a Legend tracker (Steam 1850570 / 019) adds 540 paired LL/LLL order rows. Category totals target 20 unique qualifying orders each; either rank qualifies, and both ranks still count once. LL and LLL check-offs/notes remain independent. A private account-matched dated snapshot supplies 41 calibrated LLL observations; all LL and unobserved LLL entries remain unknown. Native order refresh is not enabled. See [Order tracker and research](DEATH_STRANDING_ORDER_RESEARCH.md).

- `AchievementTrackerDefinition`: stable definition ID, provider, provider game ID, game title, optional achievement ID, title, description, completion label and item list. Optional milestone columns (up to four) and category targets support grouped checklists.
- `AchievementTrackerItem`: stable ID, name, category, detail, target count, sort order and adapter identifiers; grouped rows use a shared GroupId and distinct Milestone. Validation requires one binary item per milestone with matching row name/category.
- `AchievementTrackerSnapshot`: versioned, account-bound observations with source/evidence text and observation time.
- `AchievementTrackerState`: nullable imported count, nullable manual override, independent availability, note and provenance timestamps. The effective count is manual when present, otherwise imported. Missing counts remain unknown.
- Definitions and observations do not reference Steam or RetroAchievements fetch/cache objects. They can be reused for other games or checklist kinds without changing the UI or storage model.

Durable storage is `PixelVaultData/achievement-tracking/pixelvault-tracking.sqlite` (WAL, full synchronization, transactional imports). It is constructed in `MainWindow.ServiceComposition.cs`, and the same store is passed to all UI entry points. Custom definitions are shared metadata; progress and notes are keyed by tracker and provider account. RetroAchievements usernames normalize case; Steam identities match exactly. Blank accounts cannot write progress. The directory is gitignored to keep personal notes and account-bound observations out of source history.

Optional achievement-link edits persist in `tracker_link`, separately from bundled/custom definitions. A nullable override permits an explicit unlink of a built-in definition. An absent row keeps the definition's default link. Link changes only affect organization metadata and never reset item state. Cached provider achievements supply names and choices without a provider fetch.

Imports update reported counts, availability and provenance; optional source notes only fill untouched empty notes. They preserve manual overrides and authored/cleared notes, skip repeated imports, ignore older observations, and reject mismatched accounts/games/checklists, unknown or duplicate item IDs, negative counts, invalid availability and invalid times. Provider achievement status, provider counters, guide notes, pins and point awards are separate and are never changed by checklists.

## Progress import format

**Import progress** accepts JSON matching the selected checklist and connected account, up to 4 MiB. A snapshot may cover a subset of known items; absent items are not reset.

```json
{
  "Version": 1,
  "TrackerId": "definition-id",
  "Provider": "steam",
  "GameId": "provider-game-id",
  "Account": "connected-account-id",
  "Source": "Save snapshot or another observation source",
  "Evidence": "How the observation was validated",
  "ObservedUtcTicks": 0,
  "Items": [
    { "ItemId": "stable-item-id", "Current": 1, "Availability": 1 }
  ]
}
```

Replace `ObservedUtcTicks` with a valid UTC .NET timestamp (`DateTime.UtcNow.Ticks` when observing now). Availability values are 0 Unknown, 1 Available, 2 Locked. Current may be null for unavailable progress. New adapters should emit this format with stable, game-specific identifiers and validated source-account identity.

Raw `.dat` saves are not generic JSON imports. The **Death Stranding Director's Cut save** source reads a copied checkpoint directly through its isolated worker, then uses the shared reconciliation service. Other games need compatible adapters or exported JSON/spreadsheet observations. Optional app-owned automatic refresh supports JSON, spreadsheets and Death Stranding sources. Refreshing provider achievements does not refresh tracker observations. JSON progress imports remain distinct from portable definition exports.

The [Death Stranding save adapter](DEATH_STRANDING_SAVE_ADAPTER.md) documents setup, account/format checks, newest-save selection, explicit checkpoint overrides, isolated decoding, dependency licensing and private snapshot retention. It requires the installed game's compression DLL; it does not require the earlier research scripts or Codex runtimes.

## Refresh and reconciliation

The source contract is in `Services/Achievements/TrackerRefresh.cs`. `ITrackerDataSource` supplies an adapter ID/version, capabilities, configuration validation and a cancellable observation method. It returns a snapshot and fingerprint; adapters do not write tracker state. The initial `pixelvault.json-file` v1 adapter reads a bounded, stable UTF-8 JSON file, computes SHA-256 and never executes commands or follows instructions in that file. Configuration is local/account-specific and separate from shared definitions. One active source/profile label is supported per tracker/account; it is not a separate parallel progress slot.

The review shows previous/new reported counts, current manual overrides, incoming availability, changed/unchanged/unknown totals, older/equal observations and manual conflicts. Defaults update reported observations while preserving manual values and notes. Per-item **Use source** and the explicit bulk action clear only the selected observed manual counts. Items absent from a partial snapshot remain untouched. The detail view displays both values when a manual override conflicts with the source.

Older timestamps and equal-time conflicting values are skipped unless **Allow replacing newer observations…** is checked. This explicit choice permits reconciliation after a rollback without weakening automatic import's stale-observation guard. Manual source refreshes show a visible review; opted-in background reads only apply routine observations and retain a review barrier for conflicts, rollbacks and undone batches. The source timestamp is supplied by the snapshot, not substituted with the JSON file's modification time.

A changed tracker, changed source binding, cancelled review or newer refresh attempt invalidates an older preview. Cancellation, invalid identities, unsupported adapters, unstable/corrupt files and database write failure preserve prior progress and last-success metadata. The source records attempts and successful fingerprints separately; duplicate files do not create duplicate undo batches. Reading the source never implicitly clears manual overrides. Unsaved note/count drafts must be saved before opening source/history actions, so a reload cannot discard them.

`tracker_source` stores the account-scoped binding and latest read status. `tracker_history` stores transactionally committed batches, source fingerprint/parser version and prior/after values for the affected fields. Manual progress, notes and reported observations use separate field masks. Undo first checks every affected current value against the recorded result and refuses to overwrite a conflicting later edit; then restores the entire batch atomically. Unrelated newer fields and accounts remain untouched. History remains in the database; the UI lists the latest 100 entries. No-change reads update source diagnostics without creating a meaningless undo batch.

Bootstrap JSON files in the private imports directory retain their automatic, account-matched, once-per-digest behavior. Undoing a bootstrap import does not silently replay it on the next UI reload; an explicit reimport can restore it. Interactive JSON imports use the same reconciliation screen as Refresh. Old definitions, progress, notes and prior digest records migrate by adding new tables; no existing state is rewritten just to enable the feature.

Progress source metadata is bounded to 1,000 source-name characters and 4,000 evidence characters, in addition to the 4 MiB file/snapshot cap.

## Spreadsheet creation rules

The first row contains headers. Each later nonblank row is an item. Item name is required; target defaults to one and category to Checklist. Optional explicit IDs must be unique (repeated names are allowed). When IDs are absent, row-based IDs are assigned for this one-time creation; these are not promised as durable matching keys for a future reimport.

Files must be `.xlsx` or comma-separated CSV, up to 16 MiB, 128 columns and 10,000 items per selected worksheet. XLSX supports shared/inline text, sparse cells and worksheet relationships. The reader never evaluates formulas or follows external workbook links. Mapped formula/error cells must first be replaced by values. Legacy `.xls` files must be saved as `.xlsx` or CSV. Invalid counts, duplicate IDs and invalid mapped rows block creation with row-specific errors. Preview shows the first 25 items while validation covers all rows.

Spreadsheet creation now optionally imports mapped current/completed counts, availability and notes atomically with the definition. Blank templates remain account-free; personal observations require an account. Existing trackers can connect a spreadsheet source with saved headers and stable IDs, then use Refresh and optional automation. Missing rows remain unchanged; metadata is not merged. Imported nonempty notes only fill untouched empty notes. Export… separates definitions/templates from account progress. Configurable header rows, catalogue merges and portable definition import remain planned. See [Spreadsheet progress](TRACKER_SPREADSHEET_PROGRESS.md).

## Feature roadmap and research

The full draft is [PV-PLN-ACHG-004 — Game progress trackers](plans/PV-PLN-ACHG-004-game-progress-trackers.md), mirrored in [Notion](https://app.notion.com/p/40073adc59b6819494bccc9055d72943). Phases B–D are implemented for review: refresh/reconciliation/history, the Death Stranding save adapter and opt-in automatic refresh. Phase E remains in progress for chip/LL controls; construction/maximum-upgrade native refresh is implemented. Phase F now implements spreadsheet progress, saved refresh mappings and exports; metadata reconciliation and full portability remain open. Broader game support follows the same observation contract.

[Death Stranding research](DEATH_STRANDING_TRACKER_RESEARCH.md) records the 56 named memory-chip definitions and structure build/maximum-level facts. Controlled Postbox maximum-upgrade evidence now supplies one dated private tracker observation; full native fact parsing and chip progress remain unverified. See [World tracker evidence](DEATH_STRANDING_WORLD_TRACKERS.md).

## Verification and review

Latest catalogue/grouping checks: all 181 tracker +4 foil cases pass (185 focused); full native suite 1,081 passed, zero failed/skipped. Eighteen new cases cover account retention, atomic/stale updates, archive/restore, undo and compact category layouts using unrelated fantasy/racing games and arbitrary milestones. Review build updated without a version bump or live-state edits. Prior results below are historical.

October 23 shader/portability follow-up: the two foil failures are fixed, preserving their assertions. The shader is now compiled to software-compatible PS 2.0 with moving reflection and honeycomb texture intact. All 163 tracker tests and four foil tests pass (167 focused); full native suite **1,063 passed, zero failed/skipped** in `foil-portability-full.trx`. Thirteen new cases verify complete versioned/legacy definition export/import, copies, isolation, invalid input and shared UI/context/draft behavior. Review build updated without a version bump or stopping existing sessions; no live tracker state changed. Phase F's definition round trip is implemented; catalogue merging and broader QA remain open.

October 22 continuation: all 150 focused cases pass, including 16 new spreadsheet progress/source/export cases and real copied-save integration. Full native suite: 1,047 passed plus the two known unrelated TradingCardFoil shader failures (1,049 total), `tracking-phase-f-full.trx`. Spreadsheet creation/source, reconciliation and minimum-width tracker renders inspected. Review build updated without stopping existing app sessions or changing 0.077.000. Latest private sync records 44/108 fabrication, 10/13 construction, 1/10 upgrades and 41 calibrated LLL orders (new #480); manual values/notes preserved after backups. LL/chip controls remain open. Prior results below are historical.

Current save reconciliation: 43/108 fabricated, 10/13 construction, 1/10 maximum upgrades and 40 LLL orders. LL and chip restoration remain unverified. Consistent SQLite backups preceded updates; manual overrides and notes were verified unchanged. Construction and upgrade native sources are now configured, automatic modes left off, and actual packaged-worker refreshes return unchanged after the sync. All 134 focused tests pass; full suite 1,031 passed plus the two known shader failures (1,033 total), `tracking-current-world-full.trx`. Twelve new world-reader cases validate supported layout, malformed/unsupported inputs, exclusions and real worker reconciliation across 24 copied saves. Earlier results below are historical baselines.

`AchievementTrackingTests` covers the 108-entry definition, account isolation and normalization, persistence, unknown state, atomic validation, duplicate/stale imports, manual override and note preservation, reset behavior, custom game/count targets, seed matching, all UI entry points, toggle/note persistence and minimum-width layout. World/order cases verify separate cards, larger targets, rank independence and reset to Unknown. The optional local render fixture accepts `PIXELVAULT_TRACKING_REVIEW_SEED` and `PIXELVAULT_TRACKING_REVIEW_OUTPUT`, uses a temporary store, and does not change the live database.

- 122 focused tracker/import/refresh/history/save-reader/automation/world/order-catalogue tests passed, including five new premium-order cases and real-save manual/automatic fabrication integration.
- Full readability runs each report 1,018 passed / 3 failed (1,021 total): the two known `TradingCardFoilTests` failures plus a temporary SQLite cleanup file-lock race. The first run's cleanup failure is in `AchievementPinUiTests`; the second's is in `TrackerAutomationTests.Dispose`, after their behavior assertions. Both cases pass separately in `tracking-readability-cleanup-check.trx`. Results: `tests/PixelVault.Native.Tests/TestResults/tracking-readability-full.trx` and `tracking-readability-full-rerun.trx`; all 122 focused cases pass in `tracking-readability-focused.trx`. No production storage changes were made for this presentation update.
- Native tracker, spreadsheet, source, reconciliation and history/undo renders inspected. Minimum-width tracker layout remains covered by tests.
- Latest compact-row check: all 39 achievement-tracker/world/order UI and persistence cases pass in `tracking-compact.trx`, including right-column placement, compact dimensions, Not Complete labels, independent ranks/reset, notes and normal/narrow renders. No further full-suite run was needed for this row-only presentation revision.
- Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`.
- Published build pointer, version and running app sessions are unchanged.
