# PC Companion migration — Phase 2 shared application stores

Plan: [PV-PLN-WEB-003](PV-PLN-WEB-003-pc-hosted-companion.md). **Implemented and rehearsed on isolated data, October 31, 2026. Live cutover has not occurred.**

## Result

Companion now has an explicit `direct-local` storage mode. Its web tracker commands use the same local repository/database as Native. Game metadata comes from the current PC index/inventory; achievements and guides come from the PC's saved provider caches and guide database. Web group edits commit directly to that guide database through shared transactional commands.

The existing NAS deployment, installed PC connector, launchers, shortcuts, startup recovery job and live PC application data were left in place. No build was published or service switched. The new mode remains opt-in and the normal copied-workspace mode remains compatible.

## Storage and concurrency changes

- **Three explicit roots:** `ApplicationDataRoot` for canonical PC data, `DataRoot` for Companion authentication/preferences, and `CacheRoot` for generated browsing assets. Direct mode requires separate absolute roots on local PC filesystems. Missing/incompatible stores fail closed; neither requests nor startup create a replacement canonical database.
- **Schema ownership:** direct web repositories open existing tracking stores in read/write mode without schema initialization. Native/the explicit preparation command owns schema changes. API tests verify tracker and guide `schema_version` values do not change during web saves.
- **Shared tracker commands:** web and Native tracker notes/counts use field versions, durable request IDs, guarded undo and the existing game-confirmed completion rules. Native's full tracker and compact checkboxes now capture versions when rendering. A stale edit cannot overwrite an intervening web edit.
- **Shared achievement organization:** a portable group-command service writes category, DLC and sort order transactionally in the canonical guide DB. Durable receipts make retries safe. A revision trigger also covers Native/import/legacy SQL changes, including an ABA change back to the previous visible value. Native guide drafts retain their original group version; a conflict rolls back the whole save and leaves the draft available.
- **Read-through metadata:** the portable achievement reader is shared with the exporter. The direct web reader uses saved PC account-scoped achievements, guides, registered categories/order/DLC, current game-index rows and native folder inventory. It neither publishes to nor reads a NAS catalogue. In-memory projections use source stamps plus a bounded one-second refresh, avoiding stale caches when WAL files reuse storage or filesystem timestamps lag.
- **Playtime:** Native and Companion share an account-scoped local provider-summary store. Native profiles read saved playtime and save successful provider results there. Updates preserve other games/accounts, retain newer provider responses, and use an atomic file replacement with a bounded cross-process lease. The rehearsal seeds the existing known summaries from the reviewed PC/NAS metadata.
- **Artwork/preferences:** imported achievement art resolves against the application root; generated icons use the separate cache. Library covers/banners resolve from existing approved native cache paths. Web sort/pins/section order remain in Companion state. Tracker icon choices use the shared tracking store.
- **Refresh signals:** direct browser views poll every two seconds through existing draft-preserving refresh logic. Native tracker polling includes metadata changes as well as history revisions. Native guide views refresh saved group/guide changes on activation when no draft is pending.
- **Transport retirement in direct mode:** connector endpoints return `410`; achievement snapshot receipt/outbox paths do not create another authority. Old remote save-refresh endpoints fail clearly. Local monitoring/manual source refresh is Phase 4 work; direct mode currently reads saved observations.

## Reconciliation rehearsal

The [Phase 1 evidence](PV-PLN-WEB-003-phase-1-inventory.md) was reconciled into a **new isolated application root**, never over the live PC database. The staging command accepts only a `phase2-*` directory directly under the protected evidence directory, requires the reviewed comparison identity/account scope, and rejects a stage created by different tool code.

| Record family | Rehearsal result |
|---|---|
| Manual progress/notes | All 3,416 captured fields retained. Unrelated accounts remain unchanged. |
| Source identities | Five differing NAS binding IDs mapped to equivalent canonical PC IDs after full permission/reader-field comparison. Existing Native-approved sources are checked independently of whether the old transport worker monitored them. |
| Observations | 28 reported values and ten availability differences reconciled from accepted NAS observations. Each imported reported record must match its latest non-undone source-history evidence and approved reader/version. Older observations are rejected, including availability-only changes. |
| Source protection | Nine active reported-field checkpoints retain the original accepted source fingerprints, adapters, evidence and observation dates. They do not replay every historic refresh. All 500 source-confirmed completions checked remain protected, including the 28 recovered completions. |
| Original histories | 3,088 NAS origin records retained with stable origin keys and payload hashes. They are archived evidence, not replayed mutations or newly executable refresh jobs. |
| Active history mapping | 52 identical common events and 21 already-mirrored web events mapped to existing PC history IDs: 73 mapped rows, with no second check-off. |
| Catalogue history | The semantically identical catalogue event maps to the existing PC catalogue/undo identity. Original payloads and dates remain archived. |
| Request receipts | All 21 existing web request receipts copied with their history pointers remapped. Reusing a request with a different payload remains a conflict. |
| Century state | Canonical progress remains 40/51 roads and 146/240 mails, with one unknown mail and both goals locked. NAS verification/binding evidence remains archived and mapped; no unlock or points are created. |
| Owner/preferences | The captured live owner record and both web preferences are retained in isolated Companion state, without the old exported achievement/library/transport tables. Live key material will be carried over during the final cutover; the rehearsal does not replace production keys. |
| Photo removals | All 54 removal identities retained as app metadata for the next phase. No photo, video, original XMP or preview payloads are copied. |

Reconciliation is transactional and records a completed-run receipt. A replay against the same reviewed input creates zero additional state/history/unlocks. Rejected permission changes, unproven observations, older observations and unresolved authored changes roll back without partial writes. The CLI has **no live-apply mode**; Phase 6 must repeat the capture and perform the final controlled application to the existing canonical PC stores.

Do not replace `PixelVaultData` with a rehearsal folder. The rehearsal contains selected snapshots for validation, not every live application asset/file.

## Verification

- **102 Companion tests** pass, including direct-root validation, unavailable-store behavior, account isolation, shared tracker saves/retries/undo, canonical schema ownership, group conflicts/ABA, and local metadata/provider refresh without export.
- **1,386 Native tests** pass. New tests cover stale Native guide drafts after a web group edit and concurrent account-scoped provider-summary writers. The legacy DLC migration fixture now removes the newer revision schema before simulating the older schema.
- **15 audit/reconciliation tests** pass, covering baseline/permission guards, source evidence, rollback, unrelated-account preservation, deterministic operation identities, receipt replay, old-availability rejection, legacy photo-group scope and committed WAL capture.
- **44 affected JavaScript tests** pass; the application script passes syntax checking.
- **29 connector tests** pass against the extracted reader as a compatibility check; the connector is not installed or restarted by this phase. Fourteen focused Companion reader/direct-mode checks also pass after the reader extraction.
- A separate-process rehearsal writer uses the same domain repository as Native. Its write is visible through the web repository; a stale competing edit is rejected; retry creates no duplicate history; guarded undo restores the original note.
- The real captured library loads **316 editions and 7,507 achievements** with zero NAS calls and no Native UI. Domain metadata load measurements are approximately one second cold and tens of milliseconds warm; these are local data-access measurements, not phone/browser rendering benchmarks.
- Snapshot integrity, full-stage replay and the original evidence hashes are checked independently. Logs, the verified stage and detailed proof results remain in the protected evidence area.

## Handoff and remaining phases

The verified rehearsal is under `Companion Backups/pc-migration-inventory-20261031T154013Z/phase2-final`. Tool usage is documented in [CompanionMigrationAudit](../../scripts/CompanionMigrationAudit/README.md). Published versions remain Native 0.113.000, NAS Companion 0.38.0 and PC connector 0.24.0.

**Phase 2 gate is met for the isolated shared store.** Live data reconciliation and deployment remain part of the final cutover. Phase 3 is next: replace the copied screenshot source with direct indexed-PC media access and a rebuildable derivative cache. Preserve the original workspace epoch/opaque photo identities recorded in the migration-origin metadata and all 54 removals. Local readers/startup belong to Phase 4; scheduled app-only backups/restore rehearsal to Phase 5; phone/hosting switch and fresh reconciliation to Phase 6.
