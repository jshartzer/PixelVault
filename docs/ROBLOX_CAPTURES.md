# Roblox captures

October 24 group identity fix: photo groups and the grouped-view preference now use the game's stable shared storage identity. Older per-platform records remain readable across all member GameIds, so changes to the platform selected as the cover/hero primary cannot hide existing groups. This also applies to manually created groups in other games. Canonical assignments and explicit removals override older platform records, without rewriting those records during recovery reads.

October 18 photo grouping: The individual-game photo view defaults to named subgame sections for Roblox. Both named conventions below are supported, including underscores/emoji in the title and collision suffixes. Existing captures and future imports use the same projection; no rename, metadata rewrite, or API call is needed for grouping. Unrecognized or unresolved names stay in Ungrouped. The Group photos checkbox switches to the combined chronological grid and saves the preference for that game/library. Right-click selected captures to add/create a group or remove membership; manual choices take priority over automatic title grouping. Group state is stored in the durable `PixelVaultData/capture-groups.sqlite` database rather than in screenshot metadata or the rebuildable photo index.

Current Roblox desktop screenshots are recognized as `<account-id>_<place-id>_<13-digit-capture-token>.png`. The obsolete `RobloxScreenShot20260525_200505425.png` convention is excluded.

During intake analysis, PixelVault resolves the place through Roblox's public place-to-universe endpoint, then fetches the experience name for that universe. Only the place/universe IDs are sent; no account lookup, credentials, or image upload is needed. The filename parser itself performs no network requests. Successful lookups are cached in memory for one day; failures are cached for 30 seconds. Requests are cancellable and have a ten-second overall timeout per lookup.

The import rename step puts the game name first and separates the experience with an underscore, for example:

`1869456976_79268393072444_1788648631989.png`

becomes:

`Roblox_Sell Lemons 🍋_1788648631989.png`

The experience title is sanitized for Windows filenames. The parser and intake preparation identify the game as **Roblox** (PC), so different experiences import under the same game; the experience name remains in the filename. The previous `Experience_Roblox_token.png` format is still recognized and also resolves to Roblox when parsed. This update does not rename existing library files or rewrite saved game assignments. The Roblox marker supports recognition on subsequent scans; the capture token remains verbatim for uniqueness. Collisions use the existing unique-name handling, and metadata sidecars follow the rename. Re-importing a named capture does not repeat the API lookup or rename.

Both raw and named captures use the existing file creation time. The numeric token is not parsed into a date, and file creation/modified times are preserved. Captures are tagged as PC. If lookup is unavailable, the title stays empty and the capture remains in manual intake; an unfinished automatic rename also prevents moving the raw ID filename into the library.

Built-in convention IDs are `roblox_capture_ids` and `roblox_capture_named`. These rules participate in the existing enable/disable and background-intake capability policy. Standard and manual intake preparation runs off the UI thread so API requests do not freeze the window.

Verification on September 7, 2026: 703 tests pass, including 12 Roblox checks covering parser separation from Steam, title round trips, API identity validation/cache/failure handling, cancellation, creation-time selection, collisions, sidecars, and unresolved-file retention. A live call through the application service resolved place `79268393072444` to `Sell Lemons 🍋`. The user's original screenshot files were not renamed during development.

September 7 naming follow-up: 77 targeted importer/parser/intake tests pass, including cached raw lookup, both naming formats, filename-title extraction, unresolved-file retention, and collision-safe renames. Updated in App Testing without a release-version bump.
