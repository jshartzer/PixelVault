# PV-PLN-WEB-004 — Companion map viewer

Date: November 1, 2026
Status: **Steps 1–4 verified; Step 5 deployed as Companion 0.41.1 with live service checks passed. The 0.41.0 Maps experience was confirmed working by the user; 0.41.1 fullscreen and landscape refinements are live.** Latest installation completed November 1 at 06:59:23 UTC; Native remains 0.114.005.

## Outcome

Browse the existing saved game maps from a phone or desktop browser: select a region, pan and zoom, search/select pins, and filter by saved tracker completion. Start with the three Death Stranding regions and 56 calibrated memory-chip pins, while keeping the implementation game-independent.

Use the canonical PC `ApplicationDataRoot/game-maps/maps.sqlite` and private images in place. Preserve map IDs, normalized top-left-origin X/Y coordinates, links, revisions and desktop editing. Map browsing reads saved progress only; it does not run save readers or change progress or automation.

Related: [existing map library](../GAME_MAPS.md), [PC-hosted Companion](PV-PLN-WEB-003-pc-hosted-companion.md).

## Design decisions

- Share plain models and SQLite reads in a platform-neutral `PixelVault.Maps` project. Retain Native's Windows image importer and guarded writes. Reads never initialize or migrate the map database.
- Add owner-authenticated, game-scoped API projections. Resolve games through the existing Companion library. Do not send image filenames, local paths or internal calibration/source records to browsers.
- Direct-local hosting supplies maps. Isolated/demo workspaces report the feature unavailable rather than reading PC data implicitly. A game without maps has an empty list; an existing broken/unavailable store returns a distinct service-unavailable response.
- Use locally bundled Leaflet with `CRS.Simple`; no external map provider is required. Convert normalized X/Y to the viewer's coordinate convention at one boundary and test corners and non-square images. Reference: https://leafletjs.com/examples/crs-simple/crs-simple.html
- Generate multi-resolution image tiles under the separate rebuildable cache. Existing originals include two 8,192-square images and one 4,096-square image; the largest PNG is about 58 MB. Originals remain private durable data, never bundled in source or copied into web static assets.
- Treat saved completion as a separate, account-scoped projection. Unknown, unavailable and unlinked pins must not be presented as completed. Keep viewport and loaded tiles stable through progress updates.

## Ordered implementation steps

### 1. Shared map reads and API foundation

**Complete in source, November 1.** Shared models, validation and read-only queries are in `src/PixelVault.Maps`; Native delegates reads to that project while retaining its image importer and optimistic writes. Companion registers `MapLibrary` and authenticated map endpoints. Source-bundle scripts, Docker build inputs and solution/project references include the shared project.

Extract models/read logic; have Native use the shared reader. Add list and detail routes under `/api/v1/maps/{provider}/{game}` with optional achievement/tracker link filters on the list. Return metadata, dimensions, revisions and normalized pins through explicit DTOs. Keep image delivery and personal completion out of this first increment.

Acceptance: authenticated reads see current desktop definitions without export/restart; provider/game identities and filters are exact; API responses omit internal storage/provenance fields; missing and unavailable data differ; no read creates/migrates a map store; original Native import/edit/concurrency behavior still passes. Verify in isolated fixtures, including malformed data and no HTTP write routes.

### 2. Bounded tile generation and authenticated delivery

**Implemented and verified in source, November 1.** `MapTileCache` serves authenticated manifests and versioned JPEG tiles; `PixelVault.Companion.Media` has a `map-tiles` operation. The server streams an opened/validated original to the worker and does not decode it. One map worker runs at a time, with a 120-second deadline, 160 MB encoded / 100-million-pixel limits and cancellation that kills the worker and cleans staging. Complete flat pyramids plus their digest index publish by directory rename. A private 512 MiB LRU cache is configurable with `MapCacheBytes`.

The manifest gives original width/height, tile size 512, max zoom, tile URL template and an overview URL. At max zoom, one image pixel equals one map unit; lower zoom levels divide dimensions by powers of two, rounding up. X/Y tile indices start at the top-left. Every tile is 512 square, with black padding beyond rectangular image edges. Zoom zero's sole tile doubles as the overview. Version identity includes generator version, original content-addressed filename, opened-file version and dimensions. Original content hash is verified on generation. SQL metadata revisions are independent: a pin-only edit reuses the image pyramid.

Tile routes check game/map scope, tile bounds, current original handle/version and tile digest before delivery, including conditional requests. Responses are private, cache for one hour and supply content ETags; the manifest retains normal API no-store headers. Missing originals/corrupt images yield a sanitized 503. Invalid coordinates/stale versions yield 404. Missing cache files regenerate automatically; a corrupt tile yields 503, invalidates that pyramid and rebuilds on retry. Filesystem redirects and path escapes are rejected through existing validated-handle helpers. No original-image HTTP route was added.


Add an isolated image-worker operation producing a tile pyramid and a small overview. Define tile size, zoom/bounds and manifest format; version derivatives by image content identity plus generator version. Publish atomically, limit concurrent generation, bound cache size and support cancellation/retry. Validate dimensions, file containment and reparse points; authorize every manifest/tile request. Cache private image responses with versioned URLs and ETags. Metadata reads must not decode large images or generate tiles.

Acceptance: all three existing regions generate correctly; corners/seams match originals; invalid tile coordinates and paths fail; concurrent requests do not duplicate expensive work; missing/corrupt images have recoverable errors; cache removal loses no authored data. Measure first/warm load and memory at phone-sized viewports.

### 3. Map navigation and touch viewer

**Complete in source, November 1.** Game profiles have a Maps action without changing the saved section-order contract. `#maps/{provider}/{game}` lists regions; `#map/{provider}/{game}/{id}` opens the selected region. The viewer bundles Leaflet 1.9.4 locally, including BSD license and required CSS images. Its JS/CSS SHA-256 values match the official distribution hashes.

`maps.js` owns viewer lifecycle, request-generation guards, per-workspace/version/region viewport memory (bounded to 40 views), ResizeObserver cleanup and tile loading/error/retry states. It uses CRS.Simple with top-left image conversion at one boundary. Tile zoom maps directly to pyramid zoom, with negative zoom using the overview and higher zoom enlarging native tiles. Region selection navigates with history. Reset, Leaflet zoom/keyboard controls, R to reset, CSS full-screen layout and Escape to exit work at phone/desktop sizes. The map canvas uses the existing `data-native-scroll` navigation exclusion, so drags/pinches cannot become global swipe-back gestures. Authentication resets remove viewers and remembered views.

Validation: one pure geometry/URL/manifest unit test; one multi-scenario browser test at 390 and 1280 pixels under the full production CSP; 25 existing/new library/navigation/control tests; three targeted server shell/section-order checks, including new static asset no-cache coverage. The browser test checks actual pinch zoom, map drags, zoom controls, reset, full-screen exit, resizing, per-region viewport restoration, failed tiles and retry, delayed manifest suppression, deep-link reload/back fallback, empty/disabled/error states, and zero page/CSP errors. Phone/desktop fixture screenshots were inspected under `tmp/map-viewer-step3`. Actual owner-phone acceptance remains in Step 5. No production installation or version bump.


Add a Maps section/action to game profiles and hash routes for map lists/details. Implement region switching, pinch/scroll zoom, drag pan, fit/reset, fullscreen layout, loading/empty/error states and keyboard controls. Bundle Leaflet and its license locally. Exclude map gestures from global swipe-back handling while retaining explicit Back navigation. Preserve viewport per map and release old viewers/listeners on navigation.

Acceptance: phone/desktop layouts, actual production CSP, navigation/back history, rapid region switching, orientation changes and touch gestures work. Other profile sections remain responsive; no image/pin alignment drift on rectangular maps.

### 4. Pins and saved-progress integration

**Implemented in source, November 1.** The viewer has 44-pixel numbered markers, a searchable keyboard-accessible list, selected pin descriptions, saved counts and tracker/achievement links. Search matches names and descriptions. All / Remaining / Completed filters keep unknown, unavailable and unlinked pins in Remaining. Completed markers have a check badge; unavailable/unknown markers use dashed outlines and explicit text. Selection remains visible in the detail panel even when a filter hides its marker. The list shows 100 matches at a time with Show more; regional view memory now includes selection/search/filter, bounded within the signed-in workspace.

`GET /api/v1/maps/{provider}/{game}/{id}/progress` returns `{ workspaceId, mapRevision, version, pins }`; each pin has `{ id, status, current, target, trackerUrl, achievementUrl }`. Status is `completed`, `remaining`, `unknown`, `unavailable` or `unlinked`. `TrackerRepository.ReadGameTrackers` opens one read-only SQLite snapshot, reads definitions once and reads state once per referenced same-provider/game tracker. Approved tracker accounts come only from the workspace manifest; connected achievement-only accounts and URL parameters cannot broaden that scope. Archived/missing/foreign items expose no progress or links. Saved manual overrides use the same effective value as trackers. Notes, source evidence and timestamps are omitted; semantic versions ignore note-only changes and include workspace/map revision.

Connection polling refreshes only the map's progress while the viewer is active, including while its search input has focus. Unchanged responses retain marker/list nodes; changed state updates overlays and details without replacing tiles, moving the viewport or clearing selection. Pin-only metadata revisions update in place; an image-version change reopens the viewer. Failed progress reads remove stale completion presentation and offer retry while retaining the map. Request-generation and workspace checks discard late responses. Cleanup detaches view-memory events before Leaflet removal, preserving the selected pin on return. Tracker and achievement screens now link to scoped map lists, with an explicit Show all game maps escape from link filters.

Render numbered/named markers, searchable accessible pin list and pin details. Add all/remaining/completed filters and achievement/tracker deep links. Resolve only existing same-provider/game tracker items under approved account scope; show unknown/unlinked states explicitly. Batch tracker reads per map, and update only changed pin presentation while preserving viewport/tiles/selection.

Acceptance: the 56 calibrated pins align in all regions; changes to existing saved tracker state appear without save-reader activation; cross-game/provider/account links cannot leak progress; unknown stays visible under remaining; source/progress/history remain unchanged by map requests.

### 5. Integration, packaging and rollout

**Original 0.41.0 feature package and deployment record.** `Companion Production/app-0.41.0` contains the reviewed Windows app, map/media worker, shared map library and local Leaflet assets/license. Its headless readers are byte-identical copies of the working 0.40.0 reader package; installed Native stays 0.114.005. Windows/Linux builds and the source archive pass package checks. Source packaging now includes Leaflet's LICENSE and README.txt, and excludes private databases/images. Browser assets use `0.41.0-maps1` cache keys.

A fresh protected copy of current metadata/progress and all three private originals ran on loopback with a fresh rehearsal owner and local/provider workers disabled. No production credentials were copied. The final published app passed authenticated map browsing, all 56 pins, scoped progress, search/filtering, selection/view preservation, links through trackers and achievement guides, and photo viewing. Phone-sized and desktop renders were inspected. Corrupt derivative recovery and one-region offline-original recovery passed. Source map/image hashes and rehearsal map/tracker database hashes stayed unchanged. Three current pyramids plus one retained recovery version occupy 53,137,427 bytes under the 512 MiB quota.

Local Chromium phone-viewport measurements (not actual phone/Wi-Fi timings):

| Region | Cold visible | Warm return | Initial tile transfer |
|---|---:|---:|---:|
| Central | 7,504 ms | 380 ms | 66,743 bytes |
| Eastern | 7,007 ms | 386 ms | 25,471 bytes |
| Western | 1,988 ms | 339 ms | 23,144 bytes |

Each initial viewport requested one overview tile; warm returns reused browser-cached tiles. The full app remains responsive across map/tracker/guide/photo navigation. The isolated listener was stopped after rehearsal.

Final validation: 158 Companion tests, 18 Native map tests, 54 selected JavaScript/browser checks, the real-data release browser test, recovery checks and six updater simulations pass. The three startup access tests pass again after the final shared-file access change; they cover concurrent SQLite access, original/database preservation and missing stores without initialization. Six receipt cases accept a valid result and reject missing, stale, wrong-identity, incomplete-file and unwritable-cache results. Simulations cover successful installation, a disabled backup task, changed package rejection before writes, and rollback after health/access/policy failures. A separate isolated startup confirms that LocalRuntimeHost writes the receipt. Actual virtual-service-account access is checked during installation, not inferred from the isolated run.

The sealed installer is `Companion Production/Update-Maps.ps1`, calling `scripts/Update-PixelVaultMaps.ps1`. Revision 3 verifies 310 reviewed files and current settings/service/firewall paths, creates a named app-only backup, preserves the current automatic-update policy and backup schedule, and switches only the PC app/worker/firewall paths. Readiness requires 0.41.0 HTTP health, fresh worker and healthy photo-background checks, unchanged private map images, and a fresh access receipt written by the service itself. LocalMapReadiness checks the existing map database/images and creates, reads and removes a temporary cache probe under the service identity. It does not decode images, initialize stores or change progress. Failure restores the old PC configuration and verifies 0.40.0 health. The retired NAS service remains retired. Evidence and plan: `Companion Backups/maps-release-0.41.0`.

The initial approved attempt restored healthy 0.40.0 after its access check failed. The second attempt stopped before switching because Windows denied external inspection of the service token. Revision 3 replaces token inspection with the service's own startup check. Earlier plans, packages and failure evidence are retained under `attempt-1` and `attempt-2`. The browser rehearsal now checks public session setup status before choosing setup versus login, so a retained setup-token file cannot overwrite a prior rehearsal owner's credentials.

The original 0.41.0 feature rollout completed at 06:46:23 UTC. Its service-readiness and deployment receipts are retained under `Companion Backups/maps-release-0.41.0`.

Companion 0.41.1 UI refinements went live at 06:59:23 UTC. Fullscreen uses the browser Fullscreen API with an expanded-layout fallback; the pin list grows to the remaining landscape height below or beside the map; and the profile Maps link has a small monochrome folded-map icon. The live service and served fullscreen/landscape/icon assets match the 163-file sealed release plan. Fresh worker/photo/map-access receipts pass; enabled automatic updates, Ready backup schedule, 0.41.1 service health, original map hashes and anonymous-map authentication pass. The app-only backup `app-20261101T065820Z-c3fa2d27.zip` contains 704 files (263,177,504 bytes); the NAS copy hash is verified. The user confirmed the original Maps experience worked. The UI refinements are deployed; their latest phone layout can be checked in use. Evidence is under `Companion Backups/maps-ui-0.41.1`.

Run API/security, Native map regression and browser/touch tests. Rehearse with private copies of the real maps and verify actual service-identity access, bounded cache behavior, warm reopen and error recovery. Prepare a versioned Companion package and normal app-only backup/rollback update. Verify deployed version/health after installation; record actual phone acceptance separately from automated results.

Acceptance: map browsing works on the user's phone with Native closed, alongside tracker/photo/achievement workflows. Record cold/warm timings and any unresolved limitations. Update changelog/handoff and the map documentation.

## Deferred scope

Browser uploads, map/pin editing, automatic map discovery, new game coordinate adapters, location tracking, routing and offline downloading are later work. Existing desktop editors remain available.

## Execution record

- November 1: inspected current desktop maps, Companion direct-local architecture, navigation gestures and packaging; completed Step 1 without touching production services or private map data. Production remains Companion 0.40.0 / Native 0.114.005.
- Validation: Companion build passes with zero warnings/errors; all **140 Companion tests** pass (including **15 new map cases**); all **18 Native GameMapTests** pass. Native test compilation reports existing warnings in unrelated tests.
- New coverage: authentication/private response fields; exact game/provider/link scope; ordinary library games; no HTTP map writes; live metadata/revision changes; read-only empty/unavailable/corrupt store handling and recovery; no implicit isolated-workspace store; both accounts' progress/history/source/automation preservation; SQLite WAL committed/uncommitted visibility. Existing Native tests cover imports, duplicate/concurrent edits, image ownership, pin filtering and all 56 seed identities.
- Packaging-script syntax and targeted diff whitespace checks pass. A repository-wide whitespace check also reported an existing blank line at EOF in `docs/CURRENT_BUILD.txt`, which this increment does not change.
- Next: Step 2 tile worker/cache/manifest and authenticated delivery. No browser viewer, image endpoint, completion projection, release version bump or installation has been added yet.

## Step 2 measurements and evidence

Isolated worker rehearsal of all three private regional originals (November 1; generation timing, **not phone loading timing**):

| Region | Pixels | Tiles | JPEG pyramid bytes | Worker time | Peak worker working set | Full-resolution decoded mean absolute error (0–255) |
|---|---|---|---|---|---|---|
| Eastern | 8192 × 8192 | 341 | 8,128,793 | 2.844 s | 715.3 MiB | 0.6376 |
| Central | 8192 × 8192 | 341 | 21,678,895 | 3.266 s | 785.4 MiB | 1.7965 |
| Western | 4096 × 4096 | 85 | 1,562,536 | 0.691 s | 199.3 MiB | 0.4344 |

All full-resolution tiles were reassembled and compared against decoded originals; original SHA-256 hashes stayed unchanged. Overview contact sheet inspected. Private outputs/script/audit are under `tmp/map-tiles-step2` and are not source assets. Large-map decoded memory stays in a bounded-lifetime worker; only one map conversion runs at a time. Browser viewport/network and warm phone load timings remain Step 3/5 acceptance work.

API contract:

- `GET /api/v1/maps/{provider}/{game}/{id}/manifest` → `{ version, width, height, tileSize, maxZoom, tileUrl, overviewUrl }`.
- `GET /api/v1/maps/{provider}/{game}/{id}/tiles/{z}/{x}/{y}?v={version}` → private JPEG, with ETag/conditional 304 support.
- Warm requests use the published pyramid without starting a worker. Metadata list/detail requests do not generate tiles.

Step 2 validation: all **148 Companion tests** pass, including **8 new tile cases**. The 8 tile cases were rerun successfully after the final rectangular-edge padding fix. Build reports zero warnings/errors. Coverage includes authenticated/conditional delivery, private headers, coordinate/version/game rejection, concurrent publication, warm reuse without a worker, corrupt/missing derivatives and retry, changed/offline originals, quota eviction/failure, corrupt/mismatched images, invalid source paths and cancellation before/during worker execution. A 1025 × 513 quadrant image reconstructed with decoded MAE 0.4296; all edge tiles remain 512 square with correct padding. A test build briefly encountered a DLL lock while the full suite still ran; the sequential rerun succeeded.

Step 3 is next: locally bundled Leaflet, game-profile Maps entry, routes, pan/zoom, touch/back-gesture handling and viewer states. No live deployment or version bump was performed in Step 2.

Step 3 complete; Step 4 implementation is recorded above.

## Step 4 validation

Seven new server tests cover approved account/provider/game boundaries; effective counts, zero/unknown/archived/missing links; progress changes/reversal and note-insensitive versions; changed/extra connected accounts; missing tracker databases and recovery; committed SQLite WAL state; and all 56 seed pin identities, coordinates and account-scoped completion across three regions. The existing preservation test now calls the progress endpoint and confirms both accounts' state/history/source/automation and database hashes remain unchanged. Authentication/no-store/no-write checks also cover progress.

52 selected JavaScript/browser tests pass, including two pure map tests and two map browser scenarios at 390 and 1280 pixels under production CSP. Coverage includes non-square pixel alignment, safe text rendering, keyboard activation, search/filter semantics, in-place progress and metadata updates, tile/node/focus retention, selected details after filtering, unavailable/retry, regional return, delayed response suppression, workspace changes, image replacement and inbound/outbound links. The navigation browser test also checks links from actual tracker and achievement screens. The three affected browser scenarios pass again after final lifecycle fixes. Phone/desktop fixture renders were inspected under `tmp/map-pins-step4`.

All **155 Companion tests** and all **18 Native GameMapTests** pass. Companion compilation has zero warnings/errors; Native test compilation reports four existing warnings in unrelated tests. JavaScript syntax and targeted whitespace checks pass. Next: Step 5 integration rehearsal, packaging and rollout. Actual owner-phone acceptance and production service-identity checks remain Step 5 work. Live versions remain Companion 0.40.0 / Native 0.114.005.
