# Saved game maps

October 24, 2026 — implemented for App Testing, version 0.077.000 unchanged.

Game profiles now offer **Maps** and **Trackers**. Achievement detail and guide screens, and the full tracker form, offer context-prefilled map links. Linked views can switch to **Show all game maps** to select an existing image and edit its links. Adding a map from an achievement or tracker preselects that link. A map can serve multiple achievements and trackers, or neither.

The shared map window supports PNG/JPEG import, region/floor labels, attribution, zoom, drag-to-pan, numbered/named pins, marker search and **Remaining only**. Selecting a pin shows its description. Pins can optionally reference an individual tracker item; completed colours come from that item's saved state for the current game/provider/account. Opening a map never reads a save, changes tracker progress, or enables automatic refresh. **Reload progress** reads saved tracker state; returning to the window also reloads it.

Use **Add pin**, click the image, and enter a name/description. The optional tracker/item selectors use the same generic definitions as other forms. **Edit pin** can change a description/link or remove a marker. **Map details & links** manages map metadata and many-to-many links. Original images are copied into durable storage, so moving the source file does not break a map. Maps are game-wide media; personal completion stays in the existing account-scoped tracker store.

## Storage and implementation

- `PixelVaultData/game-maps/maps.sqlite` stores map definitions, links, normalized marker locations, source/calibration notes and optimistic revisions. `images/<SHA256>.png|jpg` stores immutable copied images. This private library is ignored by Git and separate from rebuildable caches.
- `GameMapStore` owns bounded image validation/copying, game/provider queries and optimistic concurrency checks. Concurrent edits must reopen a changed map instead of overwriting newer pins/links. PNG/JPEG imports are bounded to 160 MB / 100 million decoded pixels; temporary copies are cleaned up on failure.
- `GameMapsWindow`, `GameMapDetailsWindow`, `GameMapPinWindow` and `GameMapLinks` are shared across all games. Non-provider library games can save maps using their library identity. Achievement/tracker surfaces use their exact provider game identity.
- Positions are normalized image X/Y; width, height, DPI, zoom and aspect ratio do not change the stored pin location. Coordinate systems from other games can be converted before import; the forms contain no Death Stranding rules. Pins associated with another game's tracker never read that tracker's progress.
- Definition metadata for the first game is embedded as `PixelVault.Maps.DeathStranding.json`. It contains no proprietary raster assets or personal progress. This user's local maps were installed through the same store import path. No runtime download, game asset redistribution, save modification or source automation is introduced.

## Death Stranding memory chips

Online research found the [official 505 Games interactive map](https://deathstrandingpc.505games.com/en/interactive-map/) with a 4,096 × 4,096 Central Region image, plus [PowerPyx's regional overview maps](https://www.powerpyx.com/death-stranding-full-world-map-revealed/). The installed Director's Cut assets provide sharper, matching maps for all regions, so the local library uses those instead of estimating a screenshot crop.

The read-only Decima helper extracted `UITexture.SmallTexture` BC1 tiles from `interface/textures/ds/map_base/area01|area02|area04/*_high`. DDS decoding and row-major tile assembly produce:

| Region | Tiles | Image | Chip pins | World bounds X/Y |
| --- | --- | --- | --- | --- |
| Eastern | 8 × 8 | 8,192 × 8,192 | 9 | −3,584 to +3,584 |
| Central | 8 × 8 | 8,192 × 8,192 | 44 | −3,584 to +3,584 |
| Western | 4 × 4 | 4,096 × 4,096 | 3 | −1,792 to +1,792 |

`interface/menu/ds/device/map/ui_device_map_menu_area_resource.core` supplies region extents and tile configuration. Area04's tile filenames start at 03,03 and are rebased to its 4 × 4 image. Assembled images were compared to each region's default overview texture; mean absolute channel differences after downsampling were 0.969 / 2.652 / 0.559 on a 0–255 scale. This checks tile order, orientation and coverage independently of marker placement.

All 56 authoritative locators from the earlier coordinate investigation retain their resource UUID / tracker item identity. For image coordinates `u=(X + extent/2)/extent`, `v=(extent/2 − Y)/extent`; elevation Z is not projected. The bundled metadata retains world X/Y and calibration provenance; pin descriptions retain the existing chip instructions. The installed library's current tracker progress is read in place, with no new progress writes.

Private extraction/provenance evidence is at `C:/GameDataExtractor/Backup Data/Death Stranding Director's Cut/Reference/GameMaps-20261024`, including the three assembled images, `map-definitions.json` and `assembly-audit.json`. The isolated helper is `C:/GameDataExtractor/Tools/CatalogueExtractor/MapTextureExport.java`; original archives were only read. Intermediate raw/parsed configuration and tile data are under `C:/Codex/tmp/map-research`. Local game-map rasters belong to their original rights holders and are kept outside the source repository.

## Verification and remaining scope

### Companion web extension — November 1, 2026

[PV-PLN-WEB-004](plans/PV-PLN-WEB-004-companion-maps.md) defines the staged web viewer. Steps 1–4 add shared map reads, authenticated metadata and tiles, a phone-friendly browser viewer, and pins with saved-progress filters. Companion 0.41.1 is installed and service-verified on the PC. The user confirmed the Maps experience works; the latest fullscreen and landscape-list refinements are now live.

- `GET /api/v1/maps/{provider}/{game}` returns `{ enabled, maps }`; optional `achievement` and `tracker` query parameters filter exact stored links (both must match when both are supplied).
- `GET /api/v1/maps/{provider}/{game}/{id}` returns `{ map, pins }` with normalized top-left-origin X/Y, descriptions and optional tracker/item links. SQL revisions remain authoritative.
- Only known Companion games can be queried. Both routes require the owner session and retain private/no-store API headers. Image filenames, source/calibration records, world coordinates and account state are omitted.
- Direct-local mode reads the canonical application root. A never-created map directory gives an empty list; an existing broken/missing database or a store disappearing after observation gives 503, with sanitized errors. Unknown games/maps give 404. Isolated/demo mode returns `enabled: false` and no maps.
- Native keeps its importer and optimistic writes while sharing the same models, validation and reader. Browsing does not change progress or run save readers.
- Step 2 adds `GET /api/v1/maps/{provider}/{game}/{id}/manifest` and `/tiles/{z}/{x}/{y}?v={version}`. Manifests describe 512-square padded JPEG tiles, original bounds, maximum zoom and local URLs; zoom zero is the overview. Tile responses require authentication, validate current originals, and support private one-hour caching and ETags.
- Tile pyramids are rebuildable under `CacheRoot/maps`, with configurable `MapCacheBytes` (default 512 MiB). A single isolated worker converts maps within 120 seconds; complete pyramids publish atomically. Cached data can be regenerated after eviction, corruption or cancellation. Private image originals stay in the canonical library.
- Step 2 validation: all 148 Companion tests pass, including 8 tile cases; three real regions and a rectangular fixture reconstruct correctly. Actual owner-phone acceptance awaits rollout.

- Step 3 adds a Maps action to game profiles. Select a region, drag/pinch or use keyboard/zoom buttons, reset view or fill the screen. Region positions are remembered within the current signed-in workspace. Loading/empty/error states and Retry are provided; map gestures do not trigger global swipe-back.
- Step 4 adds numbered pins, name/description search, an accessible pin list, descriptions and links to existing trackers/achievement guides. Choose All, Remaining or Completed; unknown/unavailable/unlinked pins remain visible under Remaining. A selected pin keeps its detail panel when a filter hides the marker. Show on map clears the filters and centers it. Tracker and achievement screens offer View linked maps, with a way to return to all maps for that game.
- `GET /api/v1/maps/{provider}/{game}/{id}/progress` returns `{ workspaceId, mapRevision, version, pins }`. Each pin reports its status, optional effective saved count/target and valid links. Only active, existing same-game/provider tracker items under the manifest-approved account can supply progress. Missing/archived/foreign links are unavailable; a linked item with no known saved value is unknown. URL account parameters and additional connected provider accounts cannot select personal progress.
- Progress reads use a read-only SQLite snapshot and do not invoke save readers, update notes/history or alter source/automation settings. Polling and Reload saved progress update pin presentation in place, preserving loaded tiles, viewport, search focus and selected details. Note-only changes do not repaint progress. A failed read clears stale completed styling, keeps the map visible and supports retry; workspace/auth changes clear remembered selection. Image changes reload the viewer; pin-only revisions keep its tiles.
- Step 4 tests exercise all 56 seed coordinates and item identities across the three regions, account isolation, saved changes/reversal, read-only state preservation, WAL visibility and recovery. Production-CSP browser tests cover phone/desktop pin alignment, filters, links, keyboard activation, stable view/tiles/focus and workspace/navigation changes. Actual phone acceptance remains part of rollout.
- Step 5's final published-package rehearsal uses private copies of all three originals and current saved progress with local workers disabled. All 56 pins and adjacent tracker/guide/photo workflows pass. Corrupt tiles and an offline original recover correctly; original images and map/progress databases stay unchanged. Three current pyramids plus one retained recovery version use about 51 MiB, below the 512 MiB quota. Local phone-viewport cold opens took 1.99–7.50 seconds; warm returns took 339–386 ms. These are PC browser measurements, not actual phone acceptance.
- The approved 0.41.0 installation passed live LAN health, worker/photo readiness, asset verification and actual service-identity map/cache access. The service writes its own fresh startup access receipt; the installer does not inspect or impersonate its process token. App-only backup and NAS copy are verified, automatic policy is preserved, and the backup task is Ready.
- Companion 0.41.1 adds true browser fullscreen with a layout fallback, fills the pin-list height in landscape whether it sits below or beside the map, and gives the game-profile Maps link a small monochrome folded-map icon. The installed service and live assets are verified; the 0.41.0 Maps experience was confirmed by the user.

### Desktop verification

Map-specific tests cover durable image ownership, scoped/multiple/optional links, update concurrency, duplicate imports, corrupt-image cleanup, image traversal rejection, marker validation, world projection, tracker-state preservation and all 56 catalogue identities. All 1,174 full-suite cases pass, with 46 focused map/achievement/order/profile cases passing after final presentation/editor changes. An earlier broad-run STA timeout passed both focused and full reruns. Render checks cover real regional images, marker colours, filtering and the narrow layout; profile shortcut rows were also rendered at 850/1,130 px without overlap. Final counts and review build are recorded in the handoff/changelog.

This delivers the requested map-library extension. Existing tracker Phase E/F validation and broader Excel/OS QA remain active. Optional later work includes portable map packages and additional game-specific location adapters; no automatic map discovery is enabled.
