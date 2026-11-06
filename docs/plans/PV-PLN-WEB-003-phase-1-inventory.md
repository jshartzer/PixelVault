# PC Companion migration — Phase 1 inventory and reconciliation design

Plan: [PV-PLN-WEB-003](PV-PLN-WEB-003-pc-hosted-companion.md). **Phase 1 completed October 31, 2026. Phase 2 has not started.**

## Result

The PC can provide every currently visible Companion photo directly. No manual tracker conflicts or pending web/group edits were found. The migration still needs to reconcile automatic observations, map installation-specific source IDs and preserve histories/receipts before either database can become the shared authority.

No hosting, application data, account scope, automatic-update setting, scheduled task or photo file was changed by this work. Both production apps continued running. New artifacts are audit utilities, app/metadata snapshots, comparison reports and this design. No photo/video/preview/XMP payloads were copied, and no backup schedule was installed.

## Evidence and current installation

The primary NAS snapshot was captured **10:42:12–10:42:18 a.m. CDT**; the primary PC snapshots followed **10:42:24–10:42:30 a.m. CDT**. Supplemental artwork manifests, map storage, connector library metadata and runtime inventory were collected later in the same session. These are independent live snapshots, not an atomic cutover baseline.

- Published Native: **0.113.000**. The open Native process runs from `C:\Codex\App Testing\PixelVault.exe`; the published shortcut/build identity and the running test installation must both be checked during implementation.
- NAS: **Companion 0.38.0**, healthy, at the existing home LAN address; container `pixelvault-companion`.
- PC connector: **0.24.0**, running from the installed `Companion Connector` folder.
- PC data: `C:\Codex\PixelVaultData`; library: **`E:\Game Captures`**.
- `PixelVault Companion Connector Recovery` is enabled. It uses interactive-user startup/recovery, so it is not a replacement for the proposed boot-time local service.
- `PixelVaultCompanionData` is a dormant historical workspace whose hosting marker points to the NAS. Do not treat its tracker database, owner state or keys as current.
- Tracker account scope is Steam; achievement scope is Steam plus RetroAchievements. The PC pairing and live NAS manifests agree on those account mappings.
- The archived initial workspace has the same copy identity and all **52 original tracker-history rows** still occur identically on both sides. This establishes the available three-way baseline.

Private evidence is retained under `C:\Codex\Companion Backups\pc-migration-inventory-20261031T154013Z\`, with restricted access and Git exclusion. `comparison.json` contains item-level details and deterministic proposals. `capture-manifest.json`, `nas/snapshot-manifest.json` and the final evidence seal record integrity/timing. They contain private data and belong outside public source packages.

## Comparison findings

| Area | Finding | Required treatment |
|---|---|---|
| Manual tracker progress and notes | **3,416 fields match across 1,708 item-state rows.** Zero manual-field conflicts. | Keep canonical PC values; repeat the comparison at cutover for newly authored edits. |
| Custom definitions and presentation metadata | Five stored custom definitions, four links, one title override and two icon overrides match. | Reuse the PC records. Bundled definitions continue to come from the shared code. |
| Automatic observations | 628 rows differ in provenance/source fields. Within those, **28 reported values differ**: 25 Homo Faber and 3 Master Builder items are PC `0`, NAS `1`. Ten availability values differ. | Preserve both evidence streams. Reconcile through approved source bindings and the existing validation rules; do not replace the NAS with stale PC observations or take numeric maxima. |
| Source bindings | Ten per-account bindings exist on each side. Five match exactly; five differ **only in their installation-specific `Id`**. | Keep PC binding IDs and create explicit NAS-to-PC aliases after comparing every permission/reader field. |
| Automatic settings | Seven PC settings rows; two NAS settings rows. Five PC-only configurations are enabled, unpaused, five-minute refreshes. | Preserve PC settings. Retire duplicate execution ownership only when the new local worker is active. |
| Century observations | Both sides agree on **40/51 roads** and **146/240 mails**, with one mail unknown and both goals locked. Differences are verification timestamps and binding IDs. | Remap provenance; retain verification evidence and unknown state. Do not create unlocks or points. |
| Tracker history | PC 147 rows; NAS 1,122; 52 exact common rows. **1,070 NAS rows need origin-preserving mapping.** | These are historical records, not 1,070 pending edits. Map origins, mirrored web events, undo references and source lineage before importing. |
| Other tracker evidence | Ten import receipts match. NAS also holds 21 client-request receipts, one distinct catalogue-history row and 200 automation-result rows. | Preserve receipt identity and history references; retain diagnostics as history rather than replaying them. |
| Pending edits/reviews | Zero pending tracker-field conflicts, title conflicts, achievement group edits, group conflicts, unfinished save jobs or save-review jobs. PC automation has no pending review. | No authored-value choice is required from the user for this capture. Recheck immediately before cutover. |
| Refresh-job archive | 1,616 terminal jobs: 1,068 complete and 548 historical failures. Current connector error fields are empty. | Retain relevant audit evidence. Do not restart terminal jobs as migration work. |
| Achievements and guides | All **7,507 definition identities**, group assignments and exported guide/source/tag fields match. Saved progress/counter fields match for 7,182 rows with provider snapshots; the other 325 are unknown on the NAS and have no PC provider snapshot. | Read definitions/guides and saved progress directly from the canonical PC services. Preserve unknowns. |
| Imported achievement artwork | 489 approved import identities and image hashes match. NAS omits rich description/title/dimension metadata present locally. | Use the richer PC manifests and existing local images. No artwork transfer is needed for these imports. |
| Game library and playtime | 316 platform-edition identities match. The NAS has 97 saved playtime summaries. One row's playtime/last-played values differ from the later connector-cache capture. | Carry last-known summaries into a canonical local provider-summary store, then refresh locally. Treat the live-capture difference as provider data to reconcile. |
| Web-specific state | One owner record, one key file and two saved preferences (`library`, `game-sections`). | Preserve live NAS authentication material and preferences; do not substitute the dormant PC copy. |

### Photo coverage

- **282 catalogue files; 5,797 visible photos. All 5,797 resolve to an existing, currently listed and indexed PC original of the recorded byte length.**
- No visible NAS-only identity, missing current original, unavailable library root or PC-only current photo was found.
- Indexed capture dates, editions and all 18 authored photo-group memberships match. Legacy console-edition group assignments must be resolved across the consolidated storage family; inspecting only a photo's current edition misses valid assignments.
- All **18 published XMP sidecars** exist locally with the recorded sizes.
- **54 removal-overlay identities** must survive migration. Fifty still occur in raw catalogue rows; four exist only in retained removal metadata. Raw catalogue counts therefore differ from visible counts. Do not resurrect them.
- This was a metadata, identity, existence and byte-length check. It did not read/hash photo or sidecar payloads and does not establish a new full byte-integrity audit. Existing drive backups remain responsible for media recovery.

## Canonical data and write-ownership map

The map below covers the persisted table families and file-backed state found during inventory. A separate database for web authentication/preferences is compatible with direct data access; a separate authoritative tracker, achievement or photo copy is not.

| Data/fields | Current sources | Canonical destination and write owner | Backup treatment |
|---|---|---|---|
| Custom tracker definitions, items/private content, title, icon, achievement link | PC and NAS tracking stores; shared bundled catalogue | Existing PC `achievement-tracking/pixelvault-tracking.sqlite`, shared tracker domain operations for Native/web. | Include authored records and catalogue history. |
| Item manual values, notes and per-account state | Both tracking stores | Same PC tracking store; versioned field commands, with drafts/conflicts retained. | Include all accounts' durable state. |
| Source observations, availability, provenance, permissions, bindings and source status | Both tracking stores and approved connector configuration | PC tracking store; one approved local source worker per source. Permissions never inferred from new IDs. Runtime health re-established locally. | Include bindings, evidence, source receipts and durable observations; source files remain in place. |
| Automatic settings/results and Century state | Both tracking stores | Preserve PC settings; reconcile approved observations with source-ID aliases. Local workers update through shared domain logic. | Include settings, verified/unlock evidence and retained histories. |
| Tracker history/imports/client requests/web-sync receipts | Both tracking stores | Origin-to-canonical mappings in the PC tracking store; shared history/undo/retry logic. Existing `tracker_web_sync` becomes retired migration evidence. | Include origin mappings, request fingerprints, undo links and import receipts. Never replay history as mutations. |
| Achievement definitions, guides, sources, tags, missability, categories/order/DLC, pins and score awards | PC `guides/pixelvault-guides.sqlite`; NAS exported achievement tables | PC guide domain services and shared local DB. Preserve score/award history independently of provider progress. | Include all durable guide/organization/pin/award tables. |
| Official earned state, dates, counters, rarity and account caches | PC `achievement-hub/*.json`; NAS achievement snapshots | PC account-scoped provider cache/service; atomic reads/writes shared with Native. | Include small saved snapshots needed for recovery; never convert unknown to locked/earned. |
| Playtime and last-played | NAS `library/catalog.json` and PC connector `library-cache/catalog.json`; provider refresh worker | Canonical local provider-summary storage under the PC app root, consumed by Native/web. Carry existing known values before retiring the old catalogue. | Include summaries and provider-account mappings; keep credentials in protected storage. |
| Game identity/editions/storage groups, favorites/showcase/completion/notes, filename conventions and samples | Current `cache/pixelvault-index-e_game_captures.sqlite` and folder inventory | Existing PC index/services; Native intake/indexing retains write ownership until shared commands are extracted. Companion reads projections directly. | **Include this SQLite DB despite its `cache` folder name:** it contains authored state. Folder inventory itself is rebuildable. |
| Photo IDs, dates, tags, stars and library membership | PC photo index/inventory; NAS metadata | PC index and shared identity resolver; preserve a migration ID map for path-derived identities and renames. | Include index and ID/removal mappings, not media bytes. |
| Authored photo groups and view metadata | PC `capture-groups.sqlite`; NAS projected group labels | PC `CaptureGroupStore` and storage-family scope resolution. | Include all three group tables. |
| Removal overlays and archived photo identity records | NAS `screenshots/.../removals/*.json` | Canonical PC app metadata keyed by existing scoped photo identities. | Include all 54 removals and their identity metadata; exclude retained raw media. |
| Original photos/videos/XMP | Existing PC library drives; NAS copies | Existing configured PC roots, read in place. No Companion original store. | **Excluded from new app backups.** Existing drive backups cover these files. |
| Thumbnails/previews/provider image caches | PC caches and NAS derivatives | Bounded rebuildable PC cache, reusing compatible existing derivatives. | Exclude generated cache payloads. |
| Custom covers, imported achievement art and maps | PC `saved-covers`, `achievement-artwork`, `game-maps/maps.sqlite`; NAS imported art | Existing PC app stores and artwork/map services. Preserve selected custom assets and rich manifests. | Include irreplaceable custom assets/map records; exclude provider-download caches. |
| Owner/password hash/protected keys, approved web-account scope, web sort/pins/section order | Live NAS `companion.sqlite`, key directory and manifests | PC Companion state root. Owner service alone writes auth; web preference services write presentation settings. | Include protected live state and key recovery material. Do not use historical PC auth data. |
| Pairing/publication receipts, connector heartbeat/activity and refresh-job archive | NAS connector/activity/publication/title/achievement-receipt/job tables; PC connector checkpoints | Keep historical evidence during transition. Replace network workers with local services, then retire transport credentials/leases/publication state. | Preserve the migration baseline and useful request/origin records; do not restore old live leases or jobs. |
| PC settings, startup configuration and app paths | PC INI, launcher and recovery task; NAS hosting config | Existing PC settings plus explicit new hosting/service configuration, changed only in later phases. | Include settings and service recovery instructions; keep secrets protected. |

## Reconciliation contract for Phase 2

1. **Stage against copies first.** Use the captured baseline and repeat the capture before cutover. Validate every account scope and source guard. A missing baseline must force review, not select the newest timestamp.
2. **Keep canonical PC identities.** Map the five NAS-only binding IDs to their equivalent PC IDs after confirming all other binding fields match. Map Century `BindingId` references and transport source revisions through that table.
3. **Keep current manual values.** They match in this capture. New one-sided authored edits can use the proven baseline; independently changed notes/progress require explicit conflict resolution. Preserve source-confirmed completion protection.
4. **Preserve historical origins without replay.** Create a unique origin mapping from `(installation/workspace, table, old ID, payload hash)` to canonical IDs. Map the 52 exact common events directly. Detect the 21 web events already mirrored into PC history using their sync receipts; preserve both origins without adding another check-off. Map client-request history pointers and catalogue undo references.
5. **Separate archived observations from current state.** Preserve all 1,070 nonidentical NAS history rows, but do not blindly append them as newest live observations. Repository source protection and field versions depend on history ordering/IDs. Establish a validated final observation consistent with the remapped binding and retained state; archival imports must not invalidate that evidence or make a repeated operation create another history entry.
6. **Resolve the 28 automatic value differences and ten availability differences through existing source validation.** Compare retained observation fingerprints, reader versions and save provenance; preserve the NAS-confirmed values/evidence until a validated current local read can establish the canonical result. Do not silently regress progress, grant new source access, select the larger number or trigger a save read in this inventory step.
7. **Retain known summaries and web state.** Move the live owner/key/preferences and last-known playtime into their designated local stores. Preserve all unknown achievement progress and all 54 removals. Keep the original recovery evidence.
8. **Prove idempotency and failure recovery on isolated copies.** A second apply must create zero additional state/history/unlocks; interrupted reconciliation must resume from per-operation receipts. Recompute final account/table/identity audits before enabling a shared writer. The dry-run utility is not an apply tool.

## Tools, verification and gate

[Inventory utilities](../../scripts/CompanionMigrationAudit/README.md) use read-only/query-only source connections, SQLite backup snapshots and metadata-only file checks. `compare.py` creates a non-executable reconciliation proposal with deterministic IDs and before/incoming hashes; it has no apply command. The private report includes per-table column/row coverage, detailed discrepancies and proposed preservation operations.

Nine focused tests cover independent edits/note clearing, missing baselines, deterministic/account-scoped operation IDs, unavailable drives versus removal intent, photo identity/path containment, legacy group projection, terminal job handling and committed WAL inclusion without source writes. Repeated full dry runs and evidence checks are recorded with the private report.

**Phase 1 gate: met.** Every inventoried durable field family has a destination and owner. No unresolved authored-value conflict or visible NAS-only original was found. Remaining differences are classified and have an explicit implementation treatment. The 28 source-value differences, source/history mappings and live-owner migration remain required work before cutover; completing this inventory does not complete reconciliation or authorize a hosting switch.
