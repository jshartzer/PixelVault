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

Map-specific tests cover durable image ownership, scoped/multiple/optional links, update concurrency, duplicate imports, corrupt-image cleanup, image traversal rejection, marker validation, world projection, tracker-state preservation and all 56 catalogue identities. All 1,174 full-suite cases pass, with 46 focused map/achievement/order/profile cases passing after final presentation/editor changes. An earlier broad-run STA timeout passed both focused and full reruns. Render checks cover real regional images, marker colours, filtering and the narrow layout; profile shortcut rows were also rendered at 850/1,130 px without overlap. Final counts and review build are recorded in the handoff/changelog.

This delivers the requested map-library extension. Existing tracker Phase E/F validation and broader Excel/OS QA remain active. Optional later work includes portable map packages and additional game-specific location adapters; no automatic map discovery is enabled.
