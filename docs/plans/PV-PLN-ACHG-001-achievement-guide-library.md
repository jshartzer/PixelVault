# PV-PLN-ACHG-001 — Achievement Guide Library

| Field | Value |
|-------|-------|
| **Plan ID** | `PV-PLN-ACHG-001` |
| **Status** | Complete (2026-08-24) |
| **Owner** | PixelVault / Codex |
| **Topic mnemonic** | `ACHG` — Achievement Guides |
| **Parent context** | Existing Steam / RetroAchievements fetch service, achievement modal, and Game Profile achievement grid |
| **Related** | [`PV-PLN-GPRO-001`](PV-PLN-GPRO-001-game-profile-dashboard.md), [`PV-PLN-UI-001`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md), [`docs/DISTRIBUTION_STORAGE.md`](../DISTRIBUTION_STORAGE.md), [`docs/ACHIEVEMENT_GUIDE_IMPORT.md`](../ACHIEVEMENT_GUIDE_IMPORT.md), [`docs/DOC_SYNC_POLICY.md`](../DOC_SYNC_POLICY.md) |

---

## Purpose

PixelVault already fetches and displays Steam and RetroAchievements achievement definitions and player progress. Those rows are currently transient: they are rebuilt from provider responses and are not assigned a durable PixelVault identity. This prevents PixelVault from attaching durable, user-authored completion guides to an individual achievement.

This plan adds a small **Guide** surface that:

- synchronizes provider achievement definitions into a durable local catalog;
- assigns every provider achievement an internal PixelVault `achievement_id`;
- stores an editable guide, source attribution, tags, and a missable flag for each achievement;
- keeps unlock progress live from the existing provider fetch instead of duplicating it;
- accepts validated JSON guide bundles so sourced summaries can be prepared outside the app and imported safely.

The first supported providers are the two PixelVault already understands: **Steam** and **RetroAchievements**.

## Non-goals

- Automatically crawling or scraping arbitrary guide sites in the shipped app.
- Republishing full third-party walkthroughs.
- Persisting player unlock progress or replacing provider APIs as its source of truth.
- Adding Xbox, PlayStation, Epic, or GOG achievement integrations.
- Rich-text/HTML authoring, collaborative editing, ratings, comments, or a public guide marketplace.
- Automatically combining conflicting guides from multiple sources.

---

## Named policies

| Policy ID | Name | Statement |
|-----------|------|-----------|
| **PV-POL-ACHG-ID-001** | Provider identity is canonical | A provider achievement is uniquely identified by `(provider, provider_game_id, provider_achievement_id)`. Display titles are never keys. Steam uses App ID + API name; RetroAchievements uses Game ID + numeric achievement ID. |
| **PV-POL-ACHG-DURABLE-001** | Guides are authored data | Achievement definitions and guides live in a dedicated durable SQLite database under `PixelVaultData/guides/`, never in the per-library `cache/pixelvault-index-*.sqlite` store. |
| **PV-POL-ACHG-SYNC-001** | Sync cannot erase authorship | Provider sync may update official title, description, icon, and ordering. It must never overwrite guide text, attribution, tags, or missable state. Missing provider rows are marked inactive rather than deleted. |
| **PV-POL-ACHG-PROGRESS-001** | Progress remains live | Unlock state and unlock time remain transient fields returned by `GameAchievementsFetchService`; the guide database does not become a second progress tracker. |
| **PV-POL-ACHG-SOURCE-001** | Summaries keep attribution | Imported or edited sourced guidance stores its source URL and optional source title. PixelVault stores concise original summaries rather than copied full walkthroughs. |
| **PV-POL-ACHG-IMPORT-001** | Validate before write | Bulk guide imports use a versioned JSON contract, validate provider/game/achievement identities, preview unmatched rows, and commit accepted entries in one transaction. Direct ad-hoc SQL is not the normal workflow. |
| **PV-POL-ACHG-HIDDEN-001** | Spoilers remain intentional | Hidden achievements may be cataloged, but the UI must not reveal provider-hidden text beyond what the provider response exposes without an explicit user action. |
| **PV-POL-ACHG-RECOVERY-001** | Backups cover authored data | Guide-database writes participate in a rolling backup strategy appropriate for durable user data. A cache clear must not remove guides. |

---

## Data model

### `achievement_definition`

| Column | Purpose |
|--------|---------|
| `achievement_id INTEGER PRIMARY KEY AUTOINCREMENT` | Stable PixelVault-local identity used by guide records and UI selection. |
| `provider TEXT NOT NULL` | Normalized provider slug: initially `steam` or `retroachievements`. |
| `provider_game_id TEXT NOT NULL` | Steam App ID or RetroAchievements Game ID. |
| `provider_achievement_id TEXT NOT NULL` | Steam API name or RetroAchievements numeric achievement ID. |
| `pixelvault_game_id TEXT NOT NULL DEFAULT ''` | Best-effort PixelVault game association; not part of provider identity. |
| `title`, `description`, `icon_url` | Refreshable official display data. |
| `display_order` | Refreshable provider ordering when available. |
| `is_hidden`, `is_active` | Provider state and soft-retirement state. |
| `first_synced_utc_ticks`, `last_synced_utc_ticks` | Catalog provenance and maintenance. |

Unique index: `(provider, provider_game_id, provider_achievement_id)`.

### `achievement_guide`

| Column | Purpose |
|--------|---------|
| `achievement_id INTEGER PRIMARY KEY` | One editable guide per achievement in the first version. |
| `guide_text TEXT NOT NULL DEFAULT ''` | Concise completion steps. |
| `source_url`, `source_title` | Attribution and open-source action. |
| `tags TEXT NOT NULL DEFAULT ''` | Normalized comma-separated tags in v1. |
| `is_missable INTEGER NOT NULL DEFAULT 0` | Fast planning signal. |
| `created_utc_ticks`, `updated_utc_ticks` | Edit history metadata. |

The schema may later grow a separate `achievement_guide_source` table if multiple first-class sources per guide become necessary. That is intentionally deferred.

---

## UX shape

The first Guide window is deliberately compact:

- game title and provider summary at the top;
- achievement list with status/icon/title on the left;
- official achievement title and description on the right;
- editable multiline **Guide** field;
- **Source URL**, optional source title, comma-separated tags, and **Missable** checkbox;
- **Save**, **Open Source**, and **Close** actions;
- clear unsaved-change handling when switching achievements or closing.

Entry points:

1. A **Guide** button in the existing `AchievementsInfoWindow`.
2. A matching action in the Game Profile achievements section once the editor is stable.

The window fetches provider rows through the existing service, synchronizes their definitions, and then joins live progress with stored guide content by provider identity.

---

## JSON import contract

Guide bundles are versioned and provider-keyed. Initial shape:

```json
{
  "schemaVersion": 1,
  "provider": "steam",
  "providerGameId": "1245620",
  "sourceUrl": "https://example.com/guide",
  "sourceTitle": "Example achievement guide",
  "achievements": [
    {
      "providerAchievementId": "ACH_FIND_ALL_ITEMS",
      "guideText": "Collect all items before entering the final area.",
      "tags": ["collectible", "missable"],
      "isMissable": true
    }
  ]
}
```

Import rejects unknown schema versions and provider/game mismatches. It reports unmatched achievement IDs and never falls back to title-only matching without an explicit review step.

---

## Execution roadmap

### Phase A — Stable identity plumbing

- [x] Extend `GameAchievementsFetchService.AchievementRow` with normalized provider, provider game ID, provider achievement ID, and hidden state.
- [x] Populate Steam identities from App ID + API name.
- [x] Populate RetroAchievements identities from Game ID + achievement object key/ID.
- [x] Add unit coverage for provider identity normalization and RetroAchievements ID resolution.

**Exit:** every displayed Steam/RetroAchievements row has a stable provider identity that does not depend on its title.

### Phase B — Durable guide persistence

- [x] Add a dedicated guide-data path under `PixelVaultData/guides/`.
- [x] Add `IAchievementGuideService` and SQLite implementation.
- [x] Create the additive `achievement_definition` and `achievement_guide` schema.
- [x] Implement transactional catalog sync, guide read/save, inactive marking, and rolling backups.
- [x] Add SQLite integration tests proving sync preserves authored guide fields and guide storage is outside cache.

**Exit:** provider rows can be synchronized and guide records survive process restart and provider refresh.

### Phase C — Guide editor

- [x] Build the first `AchievementGuideWindow` using existing WPF visual/accessibility conventions.
- [x] Join fetched progress rows to stored guide records.
- [x] Add dirty-state prompts, source URL validation, tag normalization, and open-source behavior.
- [x] Wire a Guide entry point into `AchievementsInfoWindow`.
- [x] Add the Game Profile entry point after modal verification; reuse the already-fetched rows rather than issuing a second provider request.

**Exit:** a user can open a game, select any achievement, edit/save its guide, close PixelVault, and see the same guide after restart.

### Phase D — Sourced bundle import

- [x] Define DTOs and versioned JSON validation.
- [x] Add file/clipboard import with preview counts: matched, changed, unchanged, unmatched, and invalid.
- [x] Commit accepted guide updates transactionally.
- [x] Preserve per-entry source overrides while supporting bundle-level attribution defaults.
- [x] Add fixtures representing Steam and RetroAchievements imports.

**Exit:** a guide bundle prepared from user-supplied source links can be imported without manual SQLite edits.

### Phase E — Polish and verification

- [x] Filter achievement list by guided/unguided, missable, locked, and text search.
- [x] Add keyboard navigation, automation names, and Escape behavior.
- [x] Document backup/import behavior and copyright/source expectations.
- [x] Run full tests, build, `git diff --check`, and manual Steam + RetroAchievements smoke checks.

---

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Provider titles/localization change | Join only on provider identity; refresh display data independently. |
| RetroAchievements response shape varies between object and array | Read object property key first, then explicit `ID` fields; reject rows with no stable ID from guide synchronization while still allowing display. |
| A cache-clean action removes user guides | Keep the guide DB outside `cache/`; include it in durable data documentation and backups. |
| Provider temporarily omits achievements | Soft-mark inactive only after a successful complete sync; never cascade-delete guides. |
| Guide page cannot be accessed automatically | Retain manual editing and accept user-provided excerpts/notes; import is a convenience, not a dependency. |
| Imported content maps to the wrong platform version | Require provider + provider game ID + provider achievement ID and preview mismatches. |
| App is closed during a write | Use SQLite transactions, WAL/busy timeout as appropriate, and rolling backups. |

---

## Definition of done

`PV-PLN-ACHG-001` is complete when:

1. Steam and RetroAchievements rows expose stable provider achievement identities.
2. Achievement definitions and authored guides persist in a durable non-cache SQLite database.
3. Provider refresh updates official metadata without overwriting or deleting guides.
4. The Guide editor is reachable from the existing achievement experience and supports edit/save/reopen.
5. Versioned JSON bundles can be validated, previewed, and imported transactionally.
6. Automated tests cover identity, schema upgrade, sync preservation, guide editing persistence, and import matching.
7. Documentation and manual verification cover backup, cache clearing, Steam, and RetroAchievements behavior.

---

## Execution log

| Date | Entry |
|------|-------|
| 2026-08-24 | Plan created and activated. Phase A begins with provider identity plumbing; Phase B follows immediately with a durable guide database outside the cache store. |
| 2026-08-24 | Phase A complete. Phase B durable SQLite catalog/guide store, preservation tests, and rolling backups implemented. Phase C first editor slice wired into the existing Achievements modal; Game Profile entry remains. |
| 2026-08-24 | Phase C complete: the Game Profile achievement section now offers Open Guide after a successful fetch and reuses that result without another provider request. |
| 2026-08-24 | Phase D complete: versioned JSON guide bundles support file/clipboard preview and transactional import, bundle/per-entry attribution, mismatch reporting, and Steam/RetroAchievements fixtures. |
| 2026-08-24 | Phase E and the plan complete: search/status filters, keyboard/accessibility behavior, import and storage documentation, 631 automated tests, a zero-warning build, clean `git diff --check`, and live Steam plus RetroAchievements Game Profile/Guide smoke checks passed. Evidence: [`docs/achievement-guides/PV-PLN-ACHG-001-manual-results.md`](../achievement-guides/PV-PLN-ACHG-001-manual-results.md). |
