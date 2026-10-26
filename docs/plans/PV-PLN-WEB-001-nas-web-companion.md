# PV-PLN-WEB-001 — PixelVault Companion: NAS web app

Created: October 24, 2026. Status: **in progress — Companion is deployed on the NAS with the migrated owner/workspace; full native tracker cutover and PC connector remain pending.**

Working product name: **PixelVault Companion**. First milestone: **Trackers on NAS**.

Naming rule confirmed by the user: Companion is the overall app, with tracking as its first feature area. Keep the application/package/launcher identity broad as maps, achievements, captures and the rest of PixelVault are added; tracker-specific names belong to that feature module.

## Outcome

Run a private, phone-friendly PixelVault web app on the user's NAS. Start with the existing trackers, then expand the same service and navigation to maps, achievements/guides, games, screenshots, clips and additional management features. The first usable release must support real saved progress and notes, shared with PixelVault Desktop, while the Windows PC is off.

The user confirmed **QNAP with Container Station** and supplied **TS-464 / QTS** identification. QNAP's [hardware specifications](https://www.qnap.com/en-us/product/ts-464/specs/hardware) establish **`linux/amd64`**. Installed software versions, RAM, storage paths and runtime checks remain installation details. The current Companion edits a verified separate tracker copy, with its own authentication store. Live Desktop progress/settings and existing sessions are unchanged by this work.

## Current checkpoint — library, pins and PC refresh 0.5.0

- Console editions consolidate into281 cards; screenshot sorting defaults newest, with oldest/title/last-played/playtime options and saved pins. Profiles retain platform-specific stats. Tracker header checkboxes save check/uncheck directly, including grouped columns, with prior source/field-version protections.
- Paired on-demand PC refresh is deployed and online for the five configured Death Stranding sources. A scoped, revocable outbound Windows connector starts at user sign-in and reuses the existing isolated read-only readers. Authenticated owner requests create durable jobs; credentials bind exact workspace/accounts/source revisions. Observations are validated on NAS; manual-conflict/older/undone/concurrent changes require review. Manual progress and notes are preserved. See [PC connection](../COMPANION_CONNECTOR.md).
- All48 Companion and3 JS tests pass. Browser QA390/1080px verified sorting/pins, console selection, collapsed header saves and real PC reads against isolated data (143→148). Live NAS/PC pairing is healthy/online. Backed-up deployment preserved539 existing digests except the intended catalogue update. The original native UI/store were not changed or routed to NAS.

- Cache follow-up0.4.1: the user's phone retained the0.3 shell while reading the expanded library. Public shell/assets now explicitly prohibit caching and use new asset URLs. All42 tests pass, including HTTP cache regression coverage. Deployed0.4.1 to the same NAS with a fresh full backup; use `http://192.168.1.99:8787/?v=0.4.1` once to replace an already-cached page. No tracker/schema migration or browser-data reset is required.

- Deployed the cover-grid home page and compact banner game profiles to the existing NAS address. Search/platform filters distinguish platform editions. Profiles prioritize available playtime, achievement counts and live tracker goals; games without trackers can still be opened. Trackers expand below the stats. Achievements and Screenshots remain Coming soon.
- One owner-wide section order is saved for all games/devices using a versioned, additive preference table. Two-device stale saves require explicit reload; tracker state/history are unaffected.
- A private, workspace-bound library snapshot contains 316 covers, 274 banners, 91 achievement summaries and 97 playtime summaries. Artwork is resized/metadata-stripped and authenticated. The Windows exporter reads approved desktop data and optionally fetches Steam playtime locally; no provider keys, source paths or screenshots are sent to the NAS. Automatic metadata/source refresh remains Phase E work.
- All 41 Companion tests pass. Browser QA at 390px/1080px confirms fit, artwork, navigation, ordering persistence/concurrent-save review, retained tracker drafts, saves and undo. The NAS upgrade preserved all fourteen pre-upgrade table/file digests and retained a fresh private PC/NAS backup. The production container is healthy on 0.4.0; real-user authentication was not automated. New-layout handset/Safari/Home Screen checks remain unperformed.

## Historical checkpoint — NAS deployment 0.3.0

- Deployed on QNAP TS-464/HomeServer, QTS6.1.0/build20261007, ContainerStation3.1.3.1854, Docker27.1.2-qnap9 and Compose2.29.1-qnap2. User selected home-network-only HTTP for this installation: **http://192.168.1.99:8787/**. HTTPS/domain/external access remains separate work. See [NAS operation](../COMPANION_NAS.md).
- Built the real Linux image on the NAS and fixed published-file read/traverse permissions for non-root startup. Persistent storage is private and NAS-local. Runtime uses UID1000:GID100, read-only root/temporary tmpfs, dropped capabilities, CPU/memory limits, health checking, log rotation and unless-stopped restart.
- Paused the PC Companion service, captured the complete workspace/owner/keys through SQLite backup, matched every database-table/file digest before starting NAS, and retained verified backups on PC/NAS. The original PC data remains frozen behind a hosting marker; launchers defer to the NAS instead of opening a second writer. Existing original native tracker windows/readers remain local.
- Disposable Linux runtime validation covered owner login, reads, progress/note saves, retry receipts, undo and session persistence through restart. The real migrated container is healthy, retained its owner after restart, requires login and rejects unknown hosts. It mounts only NAS-local data and works with the PC Companion service stopped. Physical NAS reboot and phone confirmation at the new address remain untested.
- Remaining: final native field/source/authoring contracts and reconciliation, trusted PC pairing/observations, HTTPS/external access if later requested, login recovery and additional feature modules. This migration establishes NAS authority for Companion, not synchronization of the separate original native tracker database.

## Historical checkpoint — desktop connection preview 0.3.0

- New native **PixelVault Companion** window has explicit server/sign-in, approved account/workspace identity, tracker selection, count/check-off/note edits, draft preservation, conflict choices, history/undo and unconfirmed-save retry. It uses the service directly and has no local repository fallback. The ordinary local dashboard labels its storage and opens Companion separately.
- `PixelVault.exe --companion` opens only that window, before local service composition, readers or automation. `Build-PixelVaultCompanion.ps1 -IncludeDesktop` publishes an isolated review build/launcher under Companion Preview. Only the destination/HTTP preference persists; passwords, cookies and drafts stay in memory. Network HTTP is explicit, redirects are disabled and certificates remain validated.
- Each view retains its own field baselines, independent saves preserve unrelated drafts, refresh cannot silently rebase a conflict, and an unconfirmed/5xx save remains immutable until the same request settles. Authenticated workspace identity pins requests across reconnects. The prepared-copy account scope is displayed, not inferred from the active local profile.
- Verification: 34 service/domain/client cases and 1,200 Native cases pass. These cover independent windows, explicit conflict choices, offline editing, acknowledgement loss/5xx retry, history, private workspace identity, address-only settings persistence and native control bindings. Initial native layouts were inspected at normal/minimum sizes. Windows/Linux/server and native preview builds are produced. Native published version/shortcut and the private prepared data are unchanged.
- This completes the explicit connection preview, not the app-wide storage switch. Local full overrides/authoring/source reviews, paired PC observations, final reconciliation/cutover, NAS runtime and actual iPhone checks remain open.

## Historical checkpoint — editable workspace 0.2.0

- Shared library now owns catalogue resources/validation, SQLite persistence, manual/source separation, reconciliation, history/undo, provenance and field-version/request-ID commands. Native retains its map/achievement/authoring/Windows reader and scheduler integration through a derived store. Existing serialized data and rules are retained.
- Owner-authenticated API reads approved provider accounts from a private prepared-workspace manifest. Clients cannot select another account by query/body. Guarded writes, source clearing and undo commit state/history/request receipts together; native confirmation is classified from saved typed provenance, not source labels. Connector observations are not exposed until pairing is implemented.
- Mobile/browser editing retains unsaved drafts, offers both explicit conflict-resolution choices and preserves full totals under filters. Prepared copies have a visible separation banner; sample mode remains opt-in and read-only.
- A net8.0 `PixelVault.Companion.Client` signs in and uses the same read/write/undo API as the browser. It is tested, but native windows and settings have not switched to it. This is not live two-way Desktop synchronization.
- Backup preparation opens the source read-only and uses SQLite backup plus integrity/application-table digest checks. Actual rehearsal: 5 trackers, 1,267 state rows, 52 history entries. All four browser QA changes were undone in the temporary copy. A fresh owner-unclaimed review copy is under `C:\Codex\PixelVaultCompanionData`; review builds are under `C:\Codex\Companion Preview`. Persistent data must never be placed in a build-output tree such as `App Testing`.
- Verification: 20 service/domain/client cases, 215 focused Native tracker cases and 1,185 full Native cases pass. Browser verifies check-offs/notes, persistence, retained drafts, both conflict choices and undo. Build script supplies Windows/Linux publishes, a launcher and source-only ZIP; no Docker image is claimed.
- Remaining: native UI/server connection and migration cutover; source/connector pairing/uploads; web authoring; actual container/NAS/iPhone/restore checks. Source ZIP and private data are deliberately separate.

## Historical checkpoint — foundation preview 0.1.0

- Added `PixelVault.Tracking` (`net8.0`) with the existing tracker models/milestone expansion and a shared completion calculation consumed by Desktop and Companion. Existing `PixelVaultNative` type namespaces are retained for source compatibility. Validation, state persistence and reconciliation extraction are still pending.
- Added `PixelVault.Companion` (`net10.0`): persistent single-owner SQLite authentication, local setup token, protected cookies/CSRF, login attempt limits, explicit HTTPS/proxy settings, health/version and versioned read-only sample game/tracker endpoints. Sample mode defaults off. No personal-data API or connector pairing exists yet.
- Added a responsive sample tracker UI with game/tracker navigation, category grouping, search/filters, notes/details, unknown/count/column examples, bounded rendering and Home Screen metadata. Uses the existing PixelVault PNG brand asset. Sample state cannot be edited.
- Added a Linux x64 Dockerfile, QNAP Compose example, local launch script and [setup guide](../COMPANION_SETUP.md). Desktop source bundles now include the new shared project.
- Verified nine integration/domain cases, including authenticated sessions across a complete service restart. Desktop tracker-focused cases: 215 passed. Full native suite: 1,180 passed. Linux x64 publish and JavaScript syntax checks pass. Browser checks at 390px and 1080px confirm navigation, sample details/milestones, search, filter-invariant totals and no horizontal overflow/console warnings.
- Docker is unavailable on this PC. Container build/run, NAS installation/reboot/restore and real iPhone Safari/Home Screen tests remain unverified. A Linux publish is not a container runtime test.

At that earlier checkpoint, only sample projections were available. The 0.2.0 checkpoint above supersedes those implementation limits.

## Hosting decision

Selected deployment direction: **QNAP TS-464 / Container Station application / `linux/amd64`**:

- One Linux container serving both the web interface and an ASP.NET Core API, with a persistent NAS-local data volume.
- SQLite remains an appropriate starting store for this personal installation. Only the service opens its database, on the NAS's local filesystem. Neither the phone nor the Windows app opens that database across SMB/NFS.
- Both Safari and the Windows tracker screens call the service. There is one authoritative tracker store once this installation enters connected mode.
- A Windows connector reads game saves and other PC-local sources, then submits validated observations to the NAS service. Existing save readers that require Windows/game DLLs stay on Windows.
- Start on the home network. Use the NAS reverse proxy and HTTPS if available; add private access from outside the home as a later deployment step.

The current tracker database explicitly uses WAL. SQLite documents that WAL requires all processes opening the database to be on the same host. A NAS share opened by both PC and NAS applications is therefore excluded. A bind mount of NAS-local storage into its container is different from mounting a remote share. See [SQLite WAL](https://www.sqlite.org/wal.html) and [network filesystem guidance](https://www.sqlite.org/useovernet.html).

The model/architecture question is resolved. Verify the selected image starts under the installed Container Station version during deployment; installed versions and actual NAS access are not prerequisites for platform-neutral development.

### QNAP installation package

QNAP documents application creation using Docker Compose YAML and host-folder mounts in [Container Station 3](https://www.qnap.com/en-us/how-to/tutorial/article/how-to-use-container-station-3) and [Container Station 2](https://www.qnap.com/en-us/how-to/tutorial/article/how-to-use-container-station). Tailor the final instructions to the installed version.

- Application name: `pixelvault-companion`. Provide a versioned image and a Compose YAML suitable for the Container Station application editor, plus a CLI alternative. Verify the editor's configuration/secret support rather than assuming an adjacent `.env` file will be loaded.
- Use one user-selected QNAP-local shared folder for persistent data/configuration/backups. Bind its verified absolute host path to `/data` in the container; paths such as `/share/Container/pixelvault-companion` are examples only. Check permissions for the container user before first launch. Backups must also have a copy outside the working data folder.
- Publish one configurable host port to the application port; document the actual browser address after checking port availability. Reuse the NAS reverse proxy for an HTTPS hostname if supported/configured. Container Station administration and the PixelVault user login remain separate.
- Set the container restart policy and verify application persistence after NAS reboot. Updating/recreating the container must retain the mounted data and authentication keys. Supply backup-first update and recovery instructions rather than relying on a disposable container's filesystem.
- Package **`linux/amd64`** for this TS-464. Additional ARM64 builds are optional future portability work, not required for this user's installation. The supplied screenshot is saved configuration context, not proof of current network reachability or installed software versions.
- NAS access was supplied October 25 through the existing SSH key and `admin1` account. Installation/container restart and backup restoration are verified. A complete NAS reboot was not performed; no NAS password was needed or stored.

## First release scope

The initial experience is Games → Trackers → Tracker detail, with a same-game tracker switcher and direct links to individual trackers.

Include:

- All existing bundled and custom tracker definitions, provider/game identities, achievement links and the selected account's progress.
- Search, category grouping, remaining/completed/unknown/availability filters, and full-list totals independent of filters.
- Binary check-offs, count targets, independent named completion columns, record grouping and capped category goals. Preserve the desktop's unique-record counting rules, including LL/LLL semantics.
- Item details, notes, manual-versus-reported values, source status, last observation time and visible conflicts.
- Manual edits, clearing a manual override back to source progress, recent history and guarded undo through the shared service rules.
- Responsive touch controls, accessible names/focus, a usable on-screen keyboard flow, and Safari Home Screen launch.
- Saved state available when the PC is off; clear source-staleness and connector-offline messages.

Use the current desktop contract of up to 16 named completion columns and 10,000 boxes, rather than older historical documentation mentioning four columns. Large trackers need bounded rendering; a phone should not render every possible control at once.

Later tracker milestones add web-based tracker creation, definition/catalogue management, spreadsheet/JSON import/export, source setup and automation settings. Existing definitions and progress must be migrated before the first release; web authoring does not block basic use. Tracker browsing/polling never starts a save read or enables automation.

Maps and the rest of PixelVault remain on this product's expansion path, but are outside the first tracker release. The long-term goal includes all workflows; operations dependent on Windows, game installations or local tools may execute through the connector even when initiated from the browser.

## Existing code and extraction boundaries

The desktop targets `net8.0-windows` and WPF/WinForms. A Linux service cannot reference that executable as its application layer.

Current source boundaries (the original Native paths were extracted):

- `src/PixelVault.Tracking/TrackerModels.cs`, `AchievementTrackingCatalog.cs`, `TrackingData/`: models, built-ins, stable identities and validation.
- `TrackerRepository.cs`, `.History.cs`, `.Inline.cs`, `.Commands.cs`, `.Automation.cs`: persistence, source reconciliation, history, provenance and guarded writes. `TrackerSourceModels.cs` owns neutral configuration/preview models.
- Native `Services/Achievements/AchievementTrackingStore.cs` retains map and achievement integrations; `TrackerRefresh.cs`, `TrackerAutomation.cs` and readers retain Windows execution/scheduling. Native authoring extensions remain on the derived store.
- `TrackerDashboard.cs`: game/tracker projection, currently coupled to achievement hub sessions and source registrations.
- `docs/ACHIEVEMENT_TRACKING.md`, `TRACKER_COMPLETION_COLUMNS.md`, `TRACKER_DASHBOARD.md`, `TRACKER_AUTOMATIC_REFRESH.md` and `TRACKER_ACHIEVEMENT_INLINE.md`: behavior contracts.

Proposed project boundaries, finalized during extraction:

| Project | Responsibility |
| --- | --- |
| `PixelVault.Tracking` | Platform-neutral models, totals, validation, manual/source reconciliation, history and persistence contracts. |
| `PixelVault.Companion` | API, server-owned persistence/authentication, web UI and NAS configuration. |
| `PixelVault.Companion.Client` | net8.0 HTTP/session client for native integration and future clients. |
| Desktop integration | Remote tracker repository and PC connector; WPF stays in the existing Windows project. |

Extract incrementally with existing tests. Remove hub-cache/account-discovery and map-store construction dependencies through explicit interfaces or separate services. Do not copy a second set of business rules into browser code. Preserve local-only desktop mode for installations that have not enabled Companion.

Use a currently supported .NET LTS for the new server, provisionally .NET 10. Choose a compatible shared-library target or multi-targeting after inspecting dependencies; the desktop must continue to build without an incidental full runtime migration. Microsoft lists .NET 10 as active LTS: [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy). Confirm image/runtime support for the actual NAS architecture before selecting the deployment image.

## Data ownership and connection contract

1. **Server authority:** in connected mode, definitions, links, personal progress, notes, history and conflict decisions live on the NAS. Desktop writes for that installation use the same API as the web app. Local-only and connected modes are explicit; no automatic switching between writable databases.
2. **Identity:** server login is separate from a Steam/RetroAchievements account. Pair a server user with approved provider accounts. Bind account scope on the server; a client-supplied account ID alone does not authorize access. Preserve existing provider-specific identity normalization and stable tracker/item IDs.
3. **Writes:** use revisions/expected versions for the affected fields, atomic history, and idempotency keys. An outdated edit returns a conflict and the current value; it must not silently overwrite a newer note, checkbox or observation. A timeout followed by retry must not duplicate a count update/history entry.
4. **Observation handling:** retain imported counts separately from manual overrides and notes. Preserve unknown versus zero, partial snapshot semantics, archive rules and protected native-confirmed check-offs. Raw browser requests cannot claim trusted game-reader provenance. Reconciliation and undo revalidate current state inside the transaction.
5. **Connection loss:** the first release is online for editing. Show a disconnected state and stop accepting saves when the NAS is unreachable. Do not report an edit as saved until acknowledged; keep an unsaved note visible. Optional cached viewing must identify stale data. Offline edit queues and multi-master synchronization are later work.
6. **Source ownership:** a paired PC owns Windows paths, game dependencies and scheduling for its sources. The NAS stores safe status/identity metadata and observations. Preserve existing source configuration in the desktop backup and migrate connector bindings explicitly; never reinterpret a Windows path as a NAS path. Only one active executor owns a source schedule.
7. **PC off:** existing progress and manual phone edits still work. Native-save observations remain at the last successful read. Report this clearly and resume source reads only under the user's existing opt-in mode after pairing.

## Delivery phases and exit checks

### A — Confirm the NAS and establish a runnable foundation

Status: local service, owner authentication and actual NAS/container installation implemented. Container restart and owner/data persistence are verified; complete NAS reboot was not performed. Connector pairing remains pending; no observation-upload endpoint exists.

- QNAP TS-464, QTS, Container Station and `linux/amd64` are established. Record installed OS/Container Station versions, available RAM, local volume path and reverse-proxy facilities before deployment.
- Select an image/runtime supported on that host and specify the LAN address/port, persistent data mount and configuration/secrets locations.
- Create the service/web shell, health/version endpoint, container build and Compose deployment example. Keep paths configurable; do not embed `C:\\Codex`.
- Establish first-user setup/session authentication before personal browser endpoints. Establish separately revocable connector pairing before exposing trusted observation endpoints; connector credentials are independent of the browser session.

Exit: the service starts on a supported development container and the NAS, survives a restart with its configuration intact, and unauthenticated requests cannot read tracker data. If NAS access is unavailable, document exactly which deployment checks remain unverified.

### B — Extract the tracker application layer and add the API

Status: shared validation/store/reconciliation and approved-account reads/writes/history/undo are implemented. Field conflicts, idempotency, unknowns, archives and provenance have meaningful tests. Connector observation/configuration and broader source-management endpoints remain pending.

- Move reusable rules behind shared contracts while retaining desktop behavior and current on-disk compatibility.
- Implement versioned endpoints for games/trackers, details/state, notes, manual progress, clearing overrides, history/undo and connector observations/reconciliation.
- Add schema migration/version checks, bounded inputs, authorization, concurrency controls and repeat-request handling.
- Build representative fixtures for custom trackers, counts, unknowns, multiple columns, capped goals, archived rows and source conflicts.

Exit: desktop behavior tests remain green; API tests prove account isolation, totals parity, transactional history and safe handling of concurrent changes.

### C — Connect Desktop and rehearse migration

Status: net8.0 API client, explicit native Companion window/destination settings and consistent backup rehearsal are implemented. The actual Companion owner/workspace moved from PC to NAS with full comparison and backups; the old PC service is stopped/guarded against accidental restart. The explicit connection edits the NAS store with retained drafts and no offline local fallback. App-wide original native routing/reconciliation and subsequent-edit rollback remain pending; no silent switch of the original native database is enabled.

The current API implements ordinary/compact completion protection. Native full-view manual overrides and source reconciliation require their own explicitly reviewed server operations before routing those workflows remotely. Do not weaken the ordinary web check-off guard or silently map every legacy write to it. Preserve a baseline or derive field baselines from recorded client requests/history when reconciling edits accepted in both the isolated copy and Desktop after preparation.

- Add explicit server address, login/pairing and connected tracker repository support to Desktop, retaining local-only mode.
- Rehearse on a consistent backup made through SQLite's backup mechanism, not by copying an actively changing `.sqlite` file alone.
- Transfer bundled/custom definitions and overrides, links, notes, reported/manual values, provenance, import digests and history. Transfer relevant game titles and supported display metadata without requiring the entire media library.
- Compare stable IDs, account scopes, counts, notes, history and completion totals. Inventory source/automation configuration separately for connector pairing.
- For real cutover, suspend tracker writes and automation in every old running desktop instance, take a final consistent backup, migrate and verify, then enable connected mode. Never leave an older desktop process writing a competing local store.
- Keep the original backup and record a rollback procedure. Once the NAS has accepted edits, rollback requires exporting/reconciling those edits; blindly restoring the earlier local database would discard them.

Exit: desktop and API show matching data and edits propagate both ways; interruption/retry cannot produce partial migration or duplicate history. Live cutover occurs only as an explicit setup step, after the rehearsal and usable client are ready.

### D — Build the mobile tracker experience

Status: real-copy editing and conflict/history flows implemented and browser-tested. Actual iPhone/Safari/Home Screen checks remain pending.

- Implement game list, tracker list/detail, filters, grouping, count/column controls and note details using server projections.
- Add visible pending/saved/error states, conflict handling, history/undo and low-cost refresh while the page is visible; refresh after returning to the app.
- Add app identity/icons/manifest and Home Screen behavior; keep ordinary Safari browsing fully usable.
- Verify narrow iPhone widths, landscape, long names/notes, large column sets, keyboard, touch targets and navigation back to the same filtered position.

Exit: from a phone, browse a migrated custom/bundled tracker, edit progress and notes, refresh/reopen and see the saved state on both clients. Two browsers editing the same field receive a resolvable conflict. With the PC off, saved/manual tracking still works.

### E — Connect PC observations and automatic refresh

Status: scoped/revocable pairing and on-demand observations shipped in0.5.0. Version0.6.0 adds stable-save monitoring, independent tracker receipts, online/check status, Pause/Resume, staged indexed-library/artwork/provider-stat sync and add-only custom tracker publication. Supported new sources inherit only the exact existing approved game/account/save/game-directory permission after validation on both ends. Existing NAS definitions/manual values/notes/history stay authoritative; desktop initial progress and subsequent definition changes are not bidirectional reconciliation. The PC connection starts at Windows sign-in independently of Native. Additional adapters and full original native routing remain pending.

- Reuse the existing validated Windows save readers and opt-in scheduler; submit observations through the server application layer.
- Bind connector/device identity, adapter version, provider account and source revision; reject wrong-account, stale or duplicate submissions.
- Preserve manual-conflict/rollback review barriers and cancellation behavior. Show last connector contact, last observation and review-needed state.
- Use the independently running Windows connector so automatic updates continue while the Native UI is closed, provided Windows is signed in and the PC is awake.

Exit: a known copied-save control changes only expected server observations; phone notes/manual overrides survive; retries add no duplicate history; PC shutdown only makes the source stale. Game saves remain read-only.

### F — Package, protect and verify the NAS installation

Status: installed with private NAS-local data, non-root/read-only runtime, bounded resources/logs, health checks, retained owner/keys, verified backups and container restart. The user selected home-LAN HTTP for now; HTTPS/external access remains deferred. Full NAS reboot, failed-upgrade rehearsal and handset confirmation at the new URL remain untested.

- Provide a concise install/update/rollback guide, example configuration and troubleshooting for service status, volume permissions and connection failures.
- Use persistent local storage outside the image, a non-root container where supported, bounded logs, restart policy, compatible CPU images and explicit schema versions.
- Configure HTTPS/session protection, CSRF defenses for cookie writes and restricted proxy/origin settings. Persist authentication keys across restart; keep credentials and provider tokens out of the web bundle, logs and Git. Back up configuration/secrets securely with appropriate access.
- Back up the service database consistently and prove restore into a separate test volume. Back up before schema upgrades; do not run an older binary against an unsupported upgraded schema.
- Verify NAS/container restart, failed upgrade recovery, expired sessions, NAS unavailable, desktop/phone concurrent edits and real iPhone Safari/Home Screen launch.

Exit: the user can install, restart, update and restore the tracker service following the guide, with personal progress intact. Report any checks that could not be run on the actual NAS/phone.

## Expansion after the first tracker release

1. Full web tracker authoring, catalogue reconciliation, spreadsheet/JSON workflows and source management.
2. Saved maps/pins linked to the same tracker state; transfer private map assets explicitly.
3. Achievement guides, achievement hub and game profile information.
4. Screenshot library, search, thumbnails and clip playback; plan storage mounts and Safari-compatible media delivery at that milestone.
5. Remaining management workflows, with background job/status APIs and the PC connector for Windows-specific tools.

Keep one app, service identity and navigation system. Introduce separate data modules/jobs when needed, rather than rebuilding the tracker app for each new feature.

## Next concrete work

Use the cover grid and game-profile sections as the foundation for subsequent features. Achievement/screenshot modules remain to be built; automatic indexed-library metadata and PC observation delivery are implemented. Continue Phase C through explicit native tracker contracts and reconciliation of original Desktop changes with NAS edits before a broader cutover. The NAS deployment is authoritative while the former PC Companion copy is retained as a guarded backup. Keep original native storage/automation and the connected Companion distinguished until their final migration is implemented.

## Achievement module — October25,2026

Companion0.7.0 now provides Steam/RetroAchievements lists, saved guides, preserved DLC/category assignments/order and two-way group edits with conflict checks/backups. Metadata matches115 games/7,413 achievements/all676 saved guides. See [operation, validation and limits](../COMPANION_ACHIEVEMENTS.md). Provider progress follows desktop cached refresh results; direct provider refresh from the web, guide authoring, Next Up management and screenshot modules remain separate expansion work. Original Native tracker cutover remains pending.
