# PV-PLN-WEB-002 — Companion Screenshot Library

Status: active; Steps 1–5 implemented and verified; Step 6 (full timeline) is next. Created October 26, 2026.

Notion: [Companion Screenshot Library](https://app.notion.com/p/40573adc59b68150a18ae048e3a8987b).

Parent: [PixelVault Companion](PV-PLN-WEB-001-nas-web-companion.md).

## Outcome and boundaries

Browse screenshots from the NAS while the PC is off, first within games and later through the home-page strip and full timeline. Keep existing tracker/achievement behavior stable. Original downloads were explicitly requested and implemented in0.10.0. Clips, editing and two-way group authoring remain later decisions.

## Agreed direction

- Game galleries: newest capture date first, then existing named photo groups when grouping is enabled. Grouping defaults off; groups collapse independently within each date. Console filters reuse the existing icons and names.
- Layout: small/large square thumbnails, Compact/Comfortable density. Crop thumbnails only; show the full photo in the viewer. Prototype adaptive final rows so arbitrary counts do not leave interior holes while preserving date/group boundaries. Final sizing rules require phone review.
- Viewer: no visible X; swipe down, Escape or browser Back dismiss. Information initially hidden; tapping toggles a compact overlay. Swipe down dismisses and restores gallery position. Horizontal next/previous and zoom gesture interaction are validated with the viewer increment.
- Home: horizontally swipeable strip of square recent thumbnails above game covers. Photo-library entry opens the full timeline; thumbnails open their photos directly.
- Shared catalogue, paging, renderer and viewer across surfaces. Initial performance targets (provisional): 30 home thumbnails and 60 gallery rows per batch, lazy image loading and bounded decoded images.
- PC generates metadata-free thumbnails and bounded viewing copies; NAS stores and serves them with existing authentication. Original captures remain unchanged on PC. Capture dates come from the desktop index, never NAS upload time. Unknown dates remain unknown.

## Increments and acceptance gates

1. **Single-game media foundation.** Death Stranding Director's Cut pilot. Read saved inventory/index/groups without writes; export sanitized stable photo identities, exact indexed timestamps, console/game identity and named group assignments. Generate small thumbnails and full-frame viewing copies; stage, verify hashes and atomically publish on NAS. Retry unchanged data without duplicate media; failed/incomplete uploads preserve prior catalogue. Authenticated catalogue/image endpoints remain usable without the exporting PC. Validate formats, storage/bytes, source hashes and existing-workspace preservation before broadening scope. This increment does not enable the gallery or alter Coming soon.
2. **Basic game gallery and viewer.** Uniform squares, date headings, console filters, info toggle and swipe dismissal. Verify actual phone behavior and scroll restoration.
3. **Variable-size layout.** Deterministic small/large patterns, two density options and adaptive final rows. Verify narrow/wide/rotated screens, tiny groups, large counts and no overlaps/holes.
4. **Existing groups.** Default-off grouping, per-date named/ungrouped sections, collapsible groups and retained view state. Preserve desktop group membership, including across console editions; no group-edit writes in this increment.
5. **Home strip.** Bounded recent thumbnails and the shared viewer; no increase in home startup work from loading the entire photo library.
6. **Full timeline.** Photo-library entry, date browsing across games, console filters and incremental loading using the same components. Keep equally named groups from different games independent.

## Step 1 operating scope

Start with a bounded one-game proof. A one-shot export/sync command is sufficient for this gate; recurring media monitoring and all-game rollout must be explicit later work. Existing pairing can authenticate the transfer without publishing private filesystem paths, provider secrets or settings. The pilot allowlist is explicit on the NAS; existing automatic/library pause controls still apply.

Use versioned content-addressed derivatives and a manifest published only after all referenced files are present. Bound file sizes, entry counts, staging and asset bytes. Do not delete originals or mirror deletions during the pilot. Keep source fingerprints private on PC. Verify platform identity from the indexed game rows, not folder names. Reuse authored storage-group scope and legacy console-specific memberships read-only.

Unsupported/corrupt/changed sources produce explicit diagnostic counts and preserve the current NAS manifest. HDR/JXR conversion, if encountered, needs a measured supported conversion path; never silently omit or claim complete coverage. First proof may use a declared recent subset, showing selected/eligible totals.

## Decisions deferred

Final density/packing patterns, clip playback, date-navigation controls, recurring media cadence and cache eviction beyond the pilot. Original downloads are now implemented for the pilot; separate thumbnails/viewing copies remain in use for browsing.

## Evidence

### Step 5 — recent-photo home strip (0.13.0)

Home renders its game grid independently, then requests `/api/v1/screenshots/recent?limit=30`. The owner-authenticated endpoint returns at most 30 recent photo records, sorted by indexed capture UTC ticks descending and stable ID for ties. Unknown dates sort last. Scope/title accompany each photo, with no source path, workspace or export fields. Empty/disabled media returns an empty feed; paused/revoked/offline PC connections do not block saved-photo reads. The single-game pilot remains explicit. The response contract can carry separate games, but this increment does not broaden extraction or media sync.

The native horizontal strip contains 30 lazy square thumbnails plus a photo-library icon linking to the currently enabled gallery. It scrolls by touch, keyboard, or 44px arrow controls. Page-back gestures ignore the whole native-scroll area, including gaps. Width stays inside the home page at all tested sizes. Reduced-motion preferences disable animated arrow scrolling. The icon will open the full timeline in Step 6; it is a working gallery link in this increment.

A workspace-scoped, coalesced in-memory request/cache (60-second refresh interval) avoids reloading the feed on each viewer round trip. Home polling can refresh expired metadata; identical manifests retain thumbnail DOM and horizontal scroll. Sign-out/workspace changes clear the cache and reject late responses. First-load failures offer retry while games remain usable; refresh failures retain the saved strip. No photo metadata or originals are stored in browser localStorage.

Recent thumbnails use `#recent-photo/provider/game/id`. The shared viewer opens directly from these bounded rows without fetching a gallery page. Next/previous follow recent order, with replacement history; closing returns directly home. Original download/info/gesture behavior is reused. A fresh bookmark outside the recent cap resolves just that photo's metadata and returns home, with neighbors disabled. No visible X.

Validation: **70 server +32 JS tests pass**. New API coverage checks auth, empty/disabled feeds, cap/invalid limits, newest-first ordering, safe metadata, pause/revocation/restart and thumbnail access without the PC. JS coverage checks bounded rows, retained original/scope data, safe route encoding, strip gesture exclusion and direct-bookmark home fallback. Existing screenshot/group/packing/swipe/navigation/achievement/tracker/cache tests remain green. Browser QA verifies 30 thumbnails/281 game cards, 92px phone and 110px wide squares, no page overflow at 390/844/1265px, scroll arrows, shared info/download actions, next-photo info reset, direct fresh bookmarks outside the cap with zero gallery tiles, gallery entry and no runtime errors. Exact photo round trip retained page y=126.66666412353516 and strip x=297.3333435058594, including focus. Physical iPhone touch testing is not claimed. Proof: `C:/Codex/tmp/companion-home-photo-strip-0.13.0.png`. QA tabs 26/27 and server closed; viewport reset.

NAS image `sha256:4dd8a6fe1b28f59c91794be8acf34ce2171f96d77b9a213b0e371dbdd35807d1`. Fresh backup `pre-0.13.0-20261026T205538Z.tar` and protected PC copy match SHA256 `65B0C1BC4C7604DE7272399E1E0ED89EFB3B09DAD86931FF732B6ADFA852C43F`; all 1376 stable entries match before the normal connector restarts. Image archive and deployment record retained. Installed connector stays 0.8.0; one-shot media tool stays 0.10.0. No photo/group/progress/source changes. Next: **Step 6, full timeline**.

### Step 4 — existing photo groups and clean viewer (0.12.0)

Group photos defaults **off** for a new browser; only its boolean preference is stored in localStorage. When enabled, existing named groups and explicit Ungrouped sections are displayed inside each date. Identical names across dates remain separate; authored names are escaped, and a literal group named Ungrouped remains distinct from unassigned photos. Group order follows first occurrence in the capture order; photos stay chronological within each group. No membership edits or extraction changes. All 18 existing memberships (16 Steam, 2 Xbox) are displayed across seven separate date/group sections when all pages are loaded.

Native details/summary controls support keyboard collapse/expand and independent open state. Collapse keys include date and exact group identity, held per game in memory. They survive console filtering, grouping toggles, appended pages and viewer navigation; full reload resets collapse/filter/page state, while grouping/density preferences persist. Group counts explicitly describe loaded photos, rather than claiming complete totals before older pages arrive. Reused thumbnail nodes avoid rebuilding earlier images; collapsed grids are explicitly hidden and excluded from layout/scroll-anchor calculations.

The photo X markup, styles and listener are removed; the photo stage receives initial focus. Information still starts hidden. Swipe-down gesture logic, Escape and browser Back remain; original download and next/previous behavior are unchanged. Direct tap → Escape retained exact scroll 192.6666717529297 → 192.6666717529297 and the collapsed group.

Validation: 30 JS cases pass, including new date/group isolation, cross-console membership, explicit Ungrouped, Unicode/literal-markup names, append merging, stable collapse keys and existing 8,000 packing combinations/swipe guards. Shell/cache test passes. Isolated browser QA verifies default-off, preference after reload, independent collapse, retained collapse through grouping/filter/paging, all 233 unique photos and 18 group memberships, Xbox pagination to 102, PS5 10, both densities at 390/844/1265px, no overlaps/out-of-bounds/overflow/runtime errors, no X, initial photo-stage focus, hidden info and exact gallery return. Physical iPhone touch testing is not claimed. Proof: `C:/Codex/tmp/companion-photo-groups-mobile-0.12.0.png`. QA process/tab 25 closed, viewport reset and QA preferences returned to defaults.

NAS image `sha256:32dfbd5a87c1dd08343716a2b99d424bff04a92809ead86869ce125bf4eeccea`. Backup `pre-0.12.0-20261026T204330Z.tar` and protected PC copy match SHA256 `1CCD17C3ADE564FD4137F10D8DB93A1761C1089D273088A24F9CBCB172BCF530`; all 1376 stable file/table digests match exactly before the normal connector restarts. Image archive and deployment record retained. Installed connector remains 0.8.0, media sync tool 0.10.0. Next: **Step 5, bounded recent-photo home strip**.

### Step 3 — variable square layout (0.11.0)

Implemented mixed small/large square bands with **Comfortable** (default) and **Compact** density. Choice persists per browser in `PixelVault.Companion.screenshot-density`, storing only the enum string. Date boundaries and photo order stay intact. DOM/tab order matches visual top-left order; no dense backfilling or shuffled records. Tiny sets use simpler rows; final rows balance their item count and adjust square size to fill the width. A one-photo date uses a full-width square. Normal6px gutters are intentional; no empty cells inside the packed bands.

The pure packing function returns deterministic rectangles. The gallery repositions existing thumbnail anchors rather than rebuilding/reloading earlier images on pagination. Width changes use a ResizeObserver with a height-loop guard; stable scrollbar space avoids width/height feedback. Density/append reflow retains a visible photo anchor, and viewer navigation keeps existing cached pages/filter/focus/scroll. Original-download behavior and media bytes are unchanged. No data extraction or PC connector upgrade needed.

28JS cases pass, including exhaustive coverage of **8,000** combinations (counts1–500, eight widths, two densities). Checks enforce complete cell coverage, no overlaps/out-of-bounds rectangles, source order, deterministic layout and balanced tails. Existing shell/cache test passes. Isolated desktop/phone/landscape QA verifies square/no-overlap/contained rectangles and matching visual/DOM order, saved density after reload, no horizontal overflow/errors, Xbox pagination to102 unique rows, small PS5 groups, and exact direct-tap viewer return3982→3982. Proof:`C:/Codex/tmp/companion-mixed-square-compact-0.11.0.png`. QA server stopped, tab24 closed, viewport reset and QA density returned to Comfortable. Physical iPhone review remains user-facing validation; no native touch test claimed.

NAS0.11.0 image`sha256:41d72c2a682a1b4cccd070418947417dfbfd67765b5848135928ef95627b1692`. Backup`pre-0.11.0-20261026T200813Z.tar`, matching NAS/protected-PC SHA256`14D586054045FB400E2AFA1DFF4465A9A083D4EFDD3A51E6E4946EBBB892EC23`; all1376 prior stable data entries match exactly. Original files, metadata, groups and progress retained. Normal connector0.8.0 restarted; screenshot sync tool remains0.10.0. Next is Step4: default-off existing named groups inside dates, with collapsible groups.

### Original downloads — 0.10.0

User-requested extension to Step2: tap a photo to show information, then Download original. All233 originals are stored on the NAS and served unchanged with their original filename/media type, preserving embedded metadata and full resolution. Raw bytes/ranges/authentication, embedded PNG text, Unicode filenames and PC-independent reads verified;69server+9connector+26JS cases pass. Actual browser PNG/JPEG downloads and every NAS original hash match the PC files. Added1,607,576,615 original bytes, no source edits. Only the existing catalogue gains Original descriptors;1142 other prior entries unchanged and233 files added. Installed automatic connector stays0.8.0, one-shot pilot tool is0.10.0; Step3 remains next. [Behavior, metadata boundaries and evidence](../COMPANION_ORIGINAL_DOWNLOADS.md).

### Step 2 polish — 0.9.2

Fixed the Screenshots card stacking its icon, copy and chevron on phones by sharing linked-section flex layout/padding/wrapping with other sections. Photo close is now20px and borderless/translucent, preserving the44px target and keyboard focus. Phone preview and existing shell/cache test pass. Proofs:`C:/Codex/tmp/companion-profile-card-fix-0.9.2.png`, `C:/Codex/tmp/companion-photo-subtle-close-0.9.2.png`. No screenshot data, ordering or viewer gestures changed. Step3 remains next.

### Step 2 — game gallery and viewer (NAS 0.9.1)

The synced Director's Cut profile now opens **Screenshots** instead of Coming soon. Other games remain Coming soon until their media is explicitly enabled. The gallery shows uniform square thumbnails, desktop calendar-day headings and All/PS5/Steam/Xbox filters with resized copies of the existing PC app icons. Grouping is still off; variable tiles/group controls/home strip/timeline remain later increments.

The NAS filters the entire catalogue before paging (60 per batch), returning console totals and a manifest version. Version changes reject stale continuation pages. A direct photo-metadata endpoint supports a bookmarked photo outside the first gallery batch. Gallery continuation appends into existing dates without recreating prior thumbnails. Manual Refresh photos reloads NAS metadata; it does not claim to sync new PC media. Filter and loaded-page state survive in-app guide/profile/photo navigation; they are not persisted across a full browser reload.

The full-screen dialog shows the uncropped viewing copy with information hidden. Tap toggles game/console/capture date/group/dimensions and previous/next controls. Swipe down dismisses; horizontal swipes/arrow keys change among loaded filtered photos. Close/Escape/browser Back also work. Replacement history for next/previous returns directly to the gallery; direct bookmarks fall back there. The viewer releases the full-image source on close. Multi-touch, zoomed, diagonal, cancelled and Safari-edge gestures cannot accidentally dismiss/navigate. The actual swipe thresholds are unit-tested; phone-sized browser QA does not claim physical iPhone touch testing.

QA passed at desktop and390px mobile viewport:233 total; Xbox pagination reaches102 distinct rows; PS5 shows10; dates reflect original2024/2026 captures; images load, info toggles, next photo resets info, and direct-tap dismissal restores exact scroll285.3333→285.3333. Locator-induced scrolling was distinguished from actual tap behavior. Returning while Show more is pending now resumes the interrupted page rather than leaving controls disabled. No horizontal overflow or runtime errors. Screenshot:`C:/Codex/tmp/companion-screenshot-gallery-mobile-0.9.1.png`. Isolated QA process stopped, tab21 closed and viewport reset.

Validation:68 full server tests and26 JS cases pass; final focused screenshot/cache cases also pass. New script/style assets use0.9.1 cache URLs and are covered by no-cache verification. Installed PC connector stays0.8.0; no Native changes or new media extraction. Fresh NAS/protected-PC backup`pre-0.9.1-20261026T183520Z.tar`, SHA256`C2C3CF82AF86C1648D8DDBD751EB8BAC021756D29AC0C3FEACDD5746992F8CBD`. Image`sha256:b50293b7a6ad92ad7a1d5cdcd03f46e97b5f562b10fdabce67dd9675f1ae966b`. Complete before/after integrity comparison and deployment record retained with backup.

### Step 1 — media foundation

Step 1 shipped on NAS Companion **0.9.0**. The explicit allowlist is `Companion__ScreenshotPilot=steam:1850570`; it is empty/disabled by default in code. No gallery UI is enabled yet and Screenshots still says Coming soon.

- Exported every eligible screenshot in the Director's Cut storage group: **233** (Steam121, Xbox102, PS5 10), retaining exact indexed capture UTC ticks, Central Standard Time calendar days, source edition identity and **18** authored group memberships. No original-game/sequel captures included. **7 videos excluded explicitly**. Formats:223PNG/10JPEG; no unsupported/HDR formats were encountered.
- Private content-addressed NAS store contains408 distinct derivatives plus one catalogue,94,969,378 image bytes (~90.6MiB). Thumbnail long edge480px; viewing-copy long edge2560px; JPEG metadata is rebuilt rather than copied. The thumbnails retain aspect ratio for later square cropping in UI.
- Every original selected image was SHA256-checked before/after export. Independent index audit matched all233 photo IDs, console labels and exact capture timestamps. Private source fingerprints/audit stay under the protected connector data folder; paths/settings/tokens are absent from the media catalogue/source image package.
- One-shot connector0.9.0 pilot command transferred and downloaded every distinct published image, verifying all408 hashes plus every metadata row. Re-preparing the same catalogue returned Current=true/MissingAssets=0. Interrupted/incomplete manifests retain the old visible set; restarted service/owner-cookie access, auth/scope/pause/revocation and source corruption behavior covered in tests.
- Actual NAS verifier read all233 rows and408 images in a container with **network disabled**, while the normal PC connector remained stopped. Public catalogue request returns401. Restored the unchanged installed connector0.8.0 afterward; one-shot pilot0.9.0 is not installed as an automatic startup process.
- **68 server +9 connector tests pass** (`screenshot-foundation.trx` in each test project). Source-only Windows/Linux packages built. One full-frame viewing derivative inspected visually. No claims of new phone-gallery/gesture QA before Step2 exists.
- Pre-upgrade backup`pre-0.9.0-20261026T180053Z.tar` on NAS and protected PC `C:/Codex/Companion Backups/pre-0.9.0-20261026T180053Z/`; matching SHA256`755C47C01FD3ECE60F6482FE7F14FD68E858DB8B382A7515D2F81DB6FB4BB937`. All734 prior stable entries matched before sync **and after media publication**. Exactly409 media entries added; existing tracker/achievement/layout/owner/source data retained.

Operator command (uses the existing private pairing file, never place its token in docs): `dotnet "C:/Codex/Companion Preview/connector-0.9.0/PixelVault.Companion.Connector.dll" --sync-screenshot-pilot "C:/Codex/PixelVaultCompanionConnectorData/connector.json" steam 1850570 500`.

Read APIs: `/api/v1/screenshots/steam/1850570?offset=0&limit=60` and `/api/v1/screenshots/steam/1850570/{photoId}/{thumbnail|preview}`. Owner authentication required; staged connector uploads also require the existing bearer pairing and enabled library/automatic controls. Stored images are readable without the PC process or active pairing.

Pilot limits:500 selected images,6MiB per derivative,512MiB total NAS derivative store; no automatic deletion/eviction/mirroring. Current IDs are stable for the same workspace/edition/relative file path; a later rename is a new photo identity until a durable desktop asset ID contract is added. The initial source set is completely covered, but media is **not watched automatically**. Recurring capture/change/group sync and expansion beyond this pilot require explicit follow-up work.
