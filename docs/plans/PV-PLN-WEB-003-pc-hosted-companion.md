# PV-PLN-WEB-003 — PC-hosted Companion with direct local data

Status: **PC cutover deployed; actual phone login/library/tracker/photo loading pass. Performance and normal-use observation remain open.** Companion 0.39.0 and Native 0.114.000 use canonical PC data and in-place photos. Full production owner/state/configuration is covered by daily app-only backups. [Cutover and current acceptance](PV-PLN-WEB-003-phase-6-cutover.md). [Notion roadmap mirror](https://app.notion.com/p/40a73adc59b68164a9d9cf5fbdd591a2).

Related: [original Companion plan](PV-PLN-WEB-001-nas-web-companion.md), [photo library](PV-PLN-WEB-002-companion-screenshot-library.md), [current PC connection](../COMPANION_CONNECTOR.md).

## Outcome

Run Companion on the always-on Windows PC. The phone connects to that PC, and Companion uses the PC's existing application stores and capture library directly. Eliminate the separate NAS runtime dataset and routine PC-to-NAS publication. Keep application-data backups; rely on the existing drive backups for photo originals and sidecars.

This is a storage integration project as well as a hosting move. Merely moving the current copied Companion workspace to Windows does not meet the goal.

## User requirements

- The PC is always on; access while the PC is off is unnecessary.
- Companion reads the PC's actual data, including photos, rather than maintaining another synchronized library.
- Do not create or schedule additional photo backups. Existing drive backups cover those files.
- Preserve app-data backups and all existing progress, notes, history, account boundaries and settings.
- The user authorized the inventory, then Phase 2 and Phase 3 implementation/rehearsal on October 31. Hosting changes, live reconciliation and NAS cleanup have not started.

## Target layout

| Area | Proposed location and behavior |
|---|---|
| Companion server | Versioned Windows installation on this PC, running independently of the Native UI. |
| Shared application data | Existing resolved PC data root, currently `C:\Codex\PixelVaultData`. One canonical store per kind of data. |
| Companion-specific state | `C:\Codex\PixelVaultCompanionData`, retaining owner login, keys, approved-account scope and web preferences. No duplicate tracker or achievement authority. |
| Photo originals and XMP | Existing configured library roots, currently `E:\Game Captures`. Read in place, with no Companion original-file store. |
| Thumbnails and browsing previews | Bounded, rebuildable local cache, proposed `C:\Codex\PixelVaultCompanionCache`. Excluded from app backups. |
| Application backups | Versioned app-only snapshots under the existing protected `C:\Codex\Companion Backups` area; a separate-device copy on the NAS is appropriate. NAS backup unavailability must not block Companion. |
| Phone connection | A stable PC LAN address/name on the existing Companion port, verified during implementation. Preserve the current home-network-only access scope and authentication. |

The phone continues to use the web API. It never opens SQLite files or Windows shares directly. Native and Companion use shared domain repositories on the PC. Trackers use one local SQLite database, with transactions, version checks and tested cross-process access. Only one designated component performs schema migrations. Other mutable stores need the same concurrency review before direct web writes are enabled.

## What the current implementation requires us to change

- `TrackerWorkspace.cs` explicitly accepts an `isolated-copy` manifest and opens a tracker database under the Companion root. Add an explicit direct-local mode with separate application and Companion state roots; never infer a mode from folder contents.
- `TrackerRepository` is already shared by Native and Companion; Native's `AchievementTrackingStore` extends it. Reuse this foundation, including source protections, account scope, history and retry receipts. Sharing a filename alone is insufficient: audit Native commands as well as web commands for concurrent stale edits.
- `GameLibrary` reads an exported catalogue and copied artwork; `AchievementStore` keeps exported achievements and group synchronization state in `companion.sqlite`. Replace these authority paths with local adapters to the PC's real indexes, achievement cache and guide/group services.
- `ScreenshotLibrary` serves copied manifests, originals and derivatives. Replace its source layer with indexed PC assets and an on-demand derivative cache while retaining the existing gallery/viewer API where practical.
- The connector contains both network replication and useful local save-monitoring/read-worker behavior. Retire replication only after local background workers replace the useful work, including when Native is closed.
- Current PC launchers defer to the NAS through a hosting marker. Replace that routing only during cutover, after validating the new configuration.

## Phase 1 — Inventory and design the reconciliation

**Completed:** [inventory, comparison findings, canonical source map and reconciliation contract](PV-PLN-WEB-003-phase-1-inventory.md). No live data or hosting configuration was changed; the dry-run tool has no apply command.

1. Inventory the current NAS workspace and actual PC stores, versions, account mappings, pending edits/conflicts, save bindings, automatic settings, library roots and startup jobs. The dormant PC Companion workspace is historical, not a current source.
2. Produce a source map for trackers, Century observations/unlocks, guides and achievement groups, official achievement caches, playtime, game identities/artwork, photo groups, preferences, owner credentials and keys. Record the canonical PC destination and write owner for each.
3. Compare the two live datasets using stable account/game/tracker/item identities and existing synchronization baselines. Identify web-only changes, independent desktop edits, queued group edits, differing links/icons/titles, custom definitions and private content. Do not assume the existing partial synchronization has reconciled everything.
4. Build an idempotent reconciliation tool with a dry-run report and deterministic receipts. Preserve histories from both sides with origin labels and ID mapping; replaying the tool must not duplicate edits, history or unlocks. Do not choose a winner solely by timestamp. Conflicting authored values require an explicit recorded resolution before final cutover.
5. Compare NAS photo metadata to the current PC inventory. Classify intentionally removed, temporarily unavailable and genuinely NAS-only assets. Preserve existing removals and stable photo identities; report any missing original that prevents direct access. Do not copy the whole NAS photo store back to the PC.

**Gate:** Every durable field has a destination; all discrepancies are classified; unresolved authored conflicts and NAS-only originals are visible in the report.

## Phase 2 — Use the PC application stores directly

**Implemented and rehearsed:** [shared stores, guarded commands, isolated reconciliation and validation](PV-PLN-WEB-003-phase-2-shared-stores.md). The direct-local mode is opt-in; production remains on the NAS. Live reconciliation is repeated at Phase 6 cutover.

1. Separate `ApplicationDataRoot`, Companion state and cache configuration. Validate the intended roots explicitly and fail clearly if the canonical store is unavailable; never create an empty replacement or silently fall back to an old copy.
2. Reconcile trackers into the canonical PC tracking database using the reviewed mapping. Keep unrelated accounts intact, while the web API exposes only the existing approved accounts.
3. Connect Companion tracker queries and commands to the shared repository. Verify local SQLite WAL/locking, bounded busy retries, per-field conflicts, undo, retry idempotency and one schema-migration owner. Preserve source-confirmed completion and manual-override rules.
4. Extract portable local readers/services for game indexes, saved achievements, guides/groups, playtime and artwork from the existing Native/export code. Route web group edits through the same durable domain operations as Native; retire the group outbox after its pending work is reconciled.
5. Retain web-only presentation preferences in Companion state. Reconcile any preference intended to be shared, such as tracker icons, instead of silently overwriting one side.
6. Add revisions/change signals so Native and browser views refresh from committed local changes without discarding drafts. Derived in-memory/read caches are acceptable; another independently writable dataset is not.

**Gate:** A web edit and a Native edit affect the same saved record, with correct history and conflict behavior. Local saved data remains usable without the NAS and without launching the Native UI.

## Phase 3 — Serve the existing capture library

**Implemented and rehearsed:** [direct indexed photos, durable identities/removals, checked streams and rebuildable previews](PV-PLN-WEB-003-phase-3-local-photos.md). All 5,797 visible photos and 54 removals match; sampled originals/XMP and regenerated HDR previews are validated. Production remains on the NAS.

1. Read the authoritative PC inventory, photo index, storage-group and capture-group data directly. Reuse existing stable identities, indexed capture dates, console editions, group membership and ordering.
2. Resolve an authenticated opaque photo ID to an approved local original or XMP sidecar. Stream the file in place, retaining exact bytes, filename, range requests and version validation. Keep full Windows paths private. Check resolved path containment and reject traversal or reparse-point escapes outside approved roots.
3. Keep lightweight thumbnails and full-frame browsing previews. Reuse compatible PC cached derivatives where possible; otherwise generate only the requested sizes, with bounded background work and cache eviction. Preserve the existing JXR/HDR conversion behavior.
4. Use index revisions and settled file checks for new/changed captures; regenerate only affected derivatives. Do not repeatedly hash or export the full collection. A changed file invalidates its prior cache/version; an in-progress import cannot publish partial content.
5. Preserve intentional deletion/removal behavior. An unavailable drive is an availability problem, not evidence to delete catalogue entries or state. A local rename/move must update the source mapping without creating a second photo identity; retain an explicit migration map where the old identity was path-derived.
6. Keep the current galleries, recent strip, timeline, filters, group views, original downloads and metadata downloads. No photo editing/deletion feature expansion is included.

**Gate:** A new indexed capture appears without an upload. Original downloads come from the existing library, and deleting the derivative cache does not lose a photo, group or authored field. No originals are copied into Companion storage.

## Phase 4 — Run the local server and readers reliably

**Windows hosting and credentials verified; production acceptance pending:** [local runtime and Windows acceptance](PV-PLN-WEB-003-phase-4-runtime.md). Ten real save readers, authenticated library/original/thumbnail access, phone health while locked, crash recovery and pre-sign-in boot startup pass. Both approved provider credentials also passed authenticated packaged-reader fetches under the actual virtual account. Earlier notes that provider keys were excluded were incorrect; matching keys already existed in the selected settings. An initial post-reboot connection request timed out before successful follow-up/repeat, with cause unconfirmed. Real automatic jobs stay paused and the NAS stays live.

1. Install the PC server with automatic startup and restart recovery, independent of an open terminal or Native window. Target startup at boot, including before interactive sign-in; validate the Windows service identity, user-profile dependencies, file permissions and existing credential access in that context.
2. Move approved save monitoring, manual refresh requests, stable-save checks, isolated reader workers and required provider refresh jobs into local background services. Preserve all existing approved paths/accounts, pause controls, provenance, older-save reviews and authority rules. Do not run duplicate Native/background observations for the same source.
3. Keep readers isolated from HTTP requests and constrain work so photo conversion or a failed reader cannot stall tracker edits. Maintain useful health and bounded logs without exposing secrets or local paths to the browser.
4. Bind to loopback and the selected home LAN interface, with a narrowly scoped firewall rule. Verify the actual current PC address and arrange a stable address/name. Update bookmarks, the phone home-screen entry and desktop Companion destination during cutover.
5. Preserve the existing owner account and protected keys. A new host may require signing in again. Reuse existing secret storage where compatible; never place tokens, passwords or private mappings in source or the plan.

**Gate:** Companion and approved background updates recover after a server crash and a PC restart, work while locked and with Native closed, and do not depend on NAS availability.

## Phase 5 — Keep application-data backups only

**Implemented and rehearsed:** [app-only snapshots, daily task and isolated restore](PV-PLN-WEB-003-phase-5-app-backups.md). Canonical PC application snapshots and complete rehearsal-state snapshots are protected locally and copied/verified on the NAS. Owner login, same-PC protected keys, ten real readers, all 5,797 photo records, originals/thumbnails and curated artwork recover in isolation. The actual unattended task and queued NAS-copy retry finish with result 0; 03:00 local time is correct across DST. The live cutover expanded the daily plan to actual production Companion owner/state/keys/configuration; its first unattended run and verified NAS copy passed.

Use an explicit inclusion manifest rather than copying entire data directories recursively.

| Include | Exclude |
|---|---|
| Canonical tracker definitions, progress, notes, history, source permissions/settings, verified Century state and first-unlock evidence. | Photo/video originals, original sidecars and the capture-library tree, covered by existing drive backups. |
| Authored guides/content, achievement groups, links, pins, custom maps, custom artwork and other non-rebuildable app records. | Generated thumbnails/previews, copied NAS originals, provider artwork caches, conversion staging and temporary exports. |
| Game/photo indexes and authored grouping/identity/removal metadata; retain small saved provider snapshots where needed for recovery. | Rebuildable caches, ordinary logs, binaries and nested previous backup archives. |
| Companion owner/authentication state, protected keys, web preferences, account scopes and necessary machine configuration/secret-store recovery material, access-controlled appropriately. | Bulk NAS photo archives. Existing photo copies remain untouched until a separate cleanup decision. |

- Proposed schedule: daily app snapshots, retaining 14 daily and 8 weekly snapshots, plus a named snapshot before migration, upgrades and schema changes. Retain migration baselines through acceptance. Treat these as initial plan defaults.
- Use SQLite's backup API for live databases. Briefly coordinate writes across related stores to obtain a coherent recovery point; record schema versions, root mappings, backup boundaries and checksums in a manifest. Do not copy live SQLite main files alone or omit committed WAL data.
- Store local snapshots in the protected existing backup area and copy only those app snapshots to the NAS for separate-device protection. Queue/retry failed copies; expose the last successful backup and any failure. The NAS is optional for runtime access.
- Verify archive integrity and rehearse restore into an isolated root before cutover. Confirm that state, history, settings, owner access and photo-group mappings restore correctly while originals remain on their existing drives. Rebuild derivatives from those originals.

**Gate:** An isolated restore succeeds, and archive inspection proves that backups contain app data without photo/video payloads or derivative caches.

## Phase 6 — Rehearse, reconcile and cut over

**Deployed:** [frozen reconciliation and live acceptance](PV-PLN-WEB-003-phase-6-cutover.md). Companion 0.39.0 and Native 0.114.000 are active, NAS hosting/connector startup are retired, and actual phone owner login/library/tracker/photo loading pass. Performance and normal-use follow-ups remain active.

1. Build and test against isolated copies of app data and read-only access to representative existing photos. Keep the production NAS as the sole live Companion writer during rehearsal. Do not activate real automatic workers in the rehearsal instance.
2. Exercise all supported areas on desktop and phone: login, library/profile views, trackers/notes/history/undo, achievement groups/guides, Century state, galleries/timeline, JXR/HDR previews, originals/XMP, preferences and manual/automatic save refresh.
3. At cutover, stop both sides' application writers and relevant background jobs for a short maintenance window. Capture coherent **app-only** recovery snapshots, including final pending queues and receipts. Keep the NAS service stopped before the PC becomes writable.
4. Run the final reconciliation and compare durable records, account scope, histories and media identity mappings against the report. Preserve or reconcile pending requests deliberately; never replay the old return-sync against the shared database.
5. Activate the PC direct-local configuration and corrected hosting marker. Disable the old connector replication loops and its recovery startup job so they cannot restart and write to the retired NAS workspace. Start exactly one local source-monitoring owner.
6. Start the PC server, verify the stable LAN URL from the actual phone, update saved destinations and test real edits/downloads. Watch errors, source-refresh health, cache work and app-backup status through a normal use cycle.
7. Update setup, hosting, connector, backup and recovery documentation after the successful move. Mark the NAS runtime architecture superseded and mirror the implemented status in the normal planning/release records. Keep historical deployment evidence intact.

**Gate:** With NAS connectivity blocked for the test, normal phone/Native use, new captures, edits, readers and local app backups continue. Failed separate-device backup copies are reported and retried.

## Validation and performance evidence

- Reconciliation dry runs, conflicts, replay, all-account preservation and history/unlock deduplication.
- Concurrent Native/web edits, draft preservation, lost-response retry, undo, source protections, process restart and schema compatibility.
- Authenticated local media resolution, forbidden paths, moved/missing/in-progress files, unavailable roots, cache regeneration, pagination stability, JPEG/PNG/JXR and exact original/XMP downloads.
- Save/provider refresh with Native closed, pause/resume, duplicate-worker exclusion, reader failure and service-account behavior across restart.
- App-only archive contents, backup interruption, coherent restore and NAS copy failure/retry.
- Compare equivalent warm/cold library, gallery and original-download requests before/after; record first-content latency, source-to-visible delay, transfer bytes, disk usage and background CPU/I/O. Provisional targets: committed local edits visible within 2 seconds in an idle active view, and a settled indexed photo listed within 5 seconds; preview generation is separately measured. Eliminate the upload wait without assuming every page becomes faster.

## Rollback and retained NAS data

Retain the stopped NAS installation and existing media unchanged during acceptance. Do not create a new photo backup or delete old NAS copies as part of this move.

Before any new PC-authored writes, rollback can restore the pre-cutover application configuration and resume the NAS with its matching connector setup. After PC writes, the NAS is stale: first capture the PC app state and reconcile post-cutover changes and history back into the rollback destination, then validate it before reopening NAS writes. Never start both authorities or restore an old snapshot over new progress. Keep a checkpoint and change journal sufficient to identify that post-cutover work.

## Definition of done

- Companion runs on the PC and uses canonical PC data, not an independently writable copied workspace.
- Photos/originals are served in place; only rebuildable browsing derivatives occupy Companion cache space.
- Web and Native edits share durable records and preserve conflicts, history, source rules and approved accounts.
- Local background readers work without Native open; no NAS publication or old connector recovery job remains active.
- Application backups run and have passed a restore rehearsal, with no additional photo backups.
- The actual phone works at the PC address, and loss of NAS connectivity does not interrupt use.
- Documentation reflects the deployed architecture; NAS data cleanup remains a separate task.

## Implementation references

- `src/PixelVault.Companion/TrackerWorkspace.cs`, `CompanionOptions.cs`, `Program.cs`: workspace mode, roots and server composition.
- `src/PixelVault.Tracking/TrackerRepository*.cs` and `src/PixelVault.Native/Services/Achievements/AchievementTrackingStore.cs`: shared tracker storage and Native wrapper.
- `src/PixelVault.Companion/AchievementStore.cs`, `GameLibrary.cs`, `ScreenshotLibrary*.cs`: current copied catalogue/media authority.
- `scripts/PixelVault.Companion.Export/*SnapshotExporter*.cs`: existing PC inventory and metadata readers to extract/reuse.
- `src/PixelVault.Companion.Connector/AutomaticConnection.cs`, `AutomaticPhotoSync.cs`, `TrackerWebSync.cs`: replication retirement and local worker replacement.
- `src/PixelVault.Native/Services/Achievements/`, `Services/Library/CaptureGroupStore.cs`, and indexing/storage services: canonical domain operations and source readers.
- [SQLite network guidance](https://www.sqlite.org/useovernet.html): keep database files and database access local; remote clients use an application API.
