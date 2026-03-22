## 0.726
- Hardened deferred library image loading so a follow-up refresh no longer blanks folder art, banner previews, or capture thumbnails while another async request is still resolving.

## 0.725
- Fixed the deferred library and gallery image loader so folder art, banner previews, and capture thumbnails actually render again instead of staying blank after the performance refactor.

## 0.724
- Reduced repeated intake folder scans by building shared source inventories for preview, process, and manual-intake flows instead of re-enumerating the same files several times.
- Switched metadata writes to bounded parallel ExifTool execution so larger imports can lean on more CPU without freezing the UI on one file at a time.
- Added capped in-memory image caching, broader on-disk thumbnail reuse, and deferred library/gallery image loading so large folders stay more responsive while browsing.
- Added optional FFmpeg-based video poster generation with hardware-accel preference and fallback poster rendering when FFmpeg is unavailable.

## 0.714
- Fixed library self-healing so files whose tags were corrected away from `Multiple Tags` can remap onto the proper single-platform game record when the library reloads.
- Added cleanup for stale zero-file `Multiple Tags` game-index rows so the old master record disappears once its files have been reassigned to the corrected platform record.

## 0.713
- Recentered the folder-preview capture-size slider around `320` so the preview sizes feel more balanced between small and large.
- Removed the border chrome from the capture previews and tightened their spacing for a cleaner folder-detail strip.
- Added a right-click menu to folder preview captures with quick actions including open, open folder, copy path, and single-photo metadata editing.

## 0.712
- Tightened the library folder-size slider range so smaller tiles are easier to reach, with `240` now sitting at the midpoint instead of near the low end.
- Added persistence for the library folder-size slider so PixelVault reopens the library using the last folder-tile size you chose.

## 0.711
- Fixed single-folder `Fetch Cover Art` so it no longer rewrites the cached library folder list with only the selected folder after a scoped cover refresh.
- Scoped cover refresh now merges updated Steam AppID state back into the full cached library instead of replacing the entire folder cache.

## 0.710
- Made the library the startup view so PixelVault now opens directly into the main browser instead of the old workflow home screen.
- Moved the old home screen into a separate Settings utility window, removed the library link from that view, and reorganized its actions around paths, import tools, maintenance, and utilities.
- Added library search plus a dedicated folder-size slider for the left-hand folder browser, and increased the default library window size substantially while keeping it bounded to the current screen.

## 0.704
- Added a right-click `Fetch Cover Art` action on library folders so a single selected game folder can refresh Steam cover art without running the full library cover pass.
- Reused the existing cover-refresh status window, cancellation flow, and completion logging for folder-scoped cover refreshes.

## 0.703
- Switched library tag writes from clear-and-append to full-list assignment, which makes the file metadata itself match the tags shown in the metadata form much more reliably when tags are added or removed.

## 0.702
- Changed the library metadata apply flow so the photo index is updated from the same normalized tag set submitted by the metadata form, instead of rereading tags back from disk for that immediate post-edit update.

## 0.701
- Fixed library tag writes so the full tag set is replaced across all tag fields PixelVault reads back from, which means deleting a tag in `Additional tags` now removes it from the file instead of leaving it behind in older XMP fields.

## 0.700
- Fixed library `Edit Metadata` so tag-field, console, custom-platform, and photography-tag changes explicitly force a metadata tag write back into the file instead of relying only on the old change-detection path.

## 0.699
- Updated the Photo Index editor to support multi-row selection.
- Changed the Photo Index `Reload` action so it rereads tags from the selected file(s), writes those values back into the matching index rows, saves the index, and refreshes the grid.
- Expanded the Photo Index `Pull From File` and delete actions to work across multiple selected rows.

## 0.698
- Fixed game-index remapping so scans no longer keep a stale `GameId` when the current file name and current console tags point to a different title/platform identity.
- Stopped applying saved game-index rows by folder path alone, which prevents bad rows from overwriting a folder with the wrong game name or console.

## 0.697
- Stopped auto-adding the `PC` tag whenever `Steam` is selected, so Steam and PC now behave as separate platform tags in both intake/review metadata writes and library metadata edits.

## 0.696
- Fixed the batched ExifTool photo-index scan parser so `Refresh`, `Rebuild`, and `Scan Folder` keep the real tag set from disk instead of dropping rows when ExifTool returns placeholder path fields.
- Added safer fallback matching for batch tag reads so files with special characters in their paths still resolve back to the correct photo-index row.
- Filtered ExifTool placeholder `-` values out of the parsed tag list so they cannot end up as fake tags in the photo index.

## 0.695
- Fixed the library `Edit Metadata` apply path so it only submits the currently selected images into the file-write, organize, and photo-index update workflow.
- Tightened the metadata form's new-game flow so verified entries are created at the `Add New Game` confirmation step instead of being silently added later during apply.
- Changed folder metadata saves to refresh the photo-level index from the tags actually written to each file, which keeps the index aligned with disk and promotes console-name tags like `Xbox` or `PS5` into the photo index console field.

## 0.690
- Added delete actions to both the Game Index and Photo Index editors so selected rows can be removed before saving.
- Added a `Pull From File` action in the Photo Index editor to reread embedded tags from the selected file and refresh that row in the index.
- Fixed the library metadata editor so changing a file's `Game ID` no longer triggers a file-tag rewrite by itself, which prevents tag clearing and stops bad master-record rows from being created during reassignment.

## 0.680
- Added a top-level `Photo Index` button on the main screen that opens the per-file index in its own in-app editor window.
- Added a searchable photo-index table with editable `Game ID`, `Console`, and `Tags` fields, plus open-file/open-folder shortcuts for the selected row.
- Saving the photo index now rewrites the photo-level cache and rebuilds the grouped library immediately so Game ID changes take effect right away.

## 0.675
- Switched master game IDs to shorter sequential values in the `G00001` format and added migration so older saved IDs are rewritten through the game index and related caches.
- Added an `Add Game` action to the Game Index editor so new master records can be created directly in-app before they have assigned files.
- Updated the library metadata title dropdown to show platform-first labels like `Xbox | Diablo IV`, while still saving just the canonical game title behind the scenes.

## 0.670
- Added a stable `GameId` field to the photo-level metadata index and shifted library grouping to follow that ID instead of raw game-name matching.
- Changed rebuild and folder-scan refreshes to rescan embedded file tags into the photo index and prune stale entries from the scan scope so the cache stays aligned with the files on disk.
- Updated the Game Index and metadata-edit flows to preserve `GameId`-based master records, keep AppID lookups keyed to those records, and expose the `GameId` directly in the editor.

## 0.660
- Turned the game index into a stricter master record by merging duplicate rows per game and platform while keeping same-title entries on different platforms separate.
- Changed game-index save and AppID resolution to key off the merged game-plus-platform identity instead of raw folder-path duplicates.
- Updated the Edit Metadata title field to use an alphabetized dropdown from the master game index and prompt before adding a brand new game title.

## 0.651
- Fixed folder-level `Edit Metadata` so saving a folder merges the edited files back into the full photo-level metadata index instead of risking a partial index rewrite.
- Changed folder-level metadata saves to record the tag set directly from the applied edit state, which keeps the photo-level index aligned with what the editor just wrote to each file.
- Preserved the existing saved Steam App ID when a folder-level metadata edit reorganizes or renames a game folder, so the game index does not get blanked during that refresh.

## 0.650
- Added a dedicated saved game index cache so manual game-index edits and resolved Steam App IDs persist independently from the transient library folder cache.
- Added App ID resolution into the Game Index flow so missing Steam App IDs can be searched, written back into the game index, and synced into the folder cache.
- Updated cover fetching to consult the saved game index for Steam App IDs before falling back to filename parsing or live Steam title lookups.

## 0.640
- Added a top-row `Game Index` button on the main screen so the cached game index is easier to reach.
- Replaced the raw text-file handoff with an in-app table editor for the cached game index, including search, editable game/platform/AppID fields, and a save action that writes changes back into the cache.

## 0.635
- Fixed a library/manual metadata regression where saving a loaded folder could clear keyword tags across every file in that batch.
- Changed library and manual metadata writes so PixelVault only rewrites tag fields when the tag-related values actually changed, leaving untouched files and untouched tag sets alone.
- Added original-value tracking for metadata editor items so bulk edits can tell the difference between a real change and a mixed or blank UI state.
## 0.634
- Fixed Fetch Covers so it refreshes cached Steam art instead of reusing older downloaded covers, which lets portrait-style art replace earlier wide header images.
- Kept custom cover overrides untouched while clearing only the built-in cached Steam cover before a refresh.

## 0.633
- Changed Steam cover downloads to prefer the portrait-style library capsule art instead of the wide store header image.
- Added a small fallback chain for Steam portrait art URLs so PixelVault can try the tall library image before falling back to the old header image.

## 0.632
- Added a non-blocking cover refresh monitor so Fetch Covers now shows live progress, descriptive status lines, and remaining work while it resolves AppIDs and pulls art.
- Added cancel support for the cover refresh flow so long Steam lookup/download runs can be stopped cleanly without freezing the library window.
- Expanded the cover refresh summary to report both AppIDs resolved and titles with dedicated cover art ready.

## 0.631
- Expanded the game-level library index so every game entry can store a best Steam AppID for cover-art lookups, not just Steam-tagged folders.
- Updated Fetch Covers to backfill missing Steam AppIDs into the shared library index before downloading art, so later cover refreshes can reuse cached IDs.
- Reused the stored Steam AppID during cover downloads so cover art can be fetched by ID instead of repeating title guesses each time.

## 0.630
- Added a persistent game-level library index so PixelVault can reopen the library from cached virtual game entries instead of rebuilding every game bucket from scratch.
- Stored per-game file lists in that index so the library detail view can load each virtual folder without regrouping the physical folder on every open.
- Added per-game Steam AppID tracking in the library index so Steam cover fetches can persist a resolved AppID for later cover refreshes.

## 0.620
- Batched ExifTool metadata reads during library scans so PixelVault no longer launches ExifTool once per file while rebuilding the library index.
- Batched tag reads for the photography gallery query path so tag-driven media lookups reuse a single ExifTool pass across many files.
- Kept the write path unchanged in this pass so the first performance upgrade stays focused on the highest-impact read bottleneck.
## 0.610
- Updated the library metadata editor so the library edit flow applies to every loaded item in the form instead of only the currently selected subset.
- Simplified the filename guess panel to the lighter secondary style and shortened it to the new Best Guess | ... format.
- Changed library folder covers to a portrait game-cover ratio in both the folder tiles and the selected-folder preview area.
## 0.600
- Added MP4 and other video captures to the import, review, library, and photography gallery flows so they show up alongside screenshots instead of disappearing behind empty folders.
- Switched video metadata writes to XMP sidecars and wrote Immich-friendly tag fields including digiKam TagsList and Lightroom HierarchicalSubject.
- Made video sidecars move, sort, and undo together with their media files so tags stay attached through the full PixelVault workflow.
- Added generated video poster thumbnails so videos render as browseable items in the library and gallery views.
## 0.591
- Fixed a cross-thread error in the library metadata progress flow by making metadata keyword reads safe for background processing.

## 0.590
- Added a library metadata progress window so long-running metadata edits now show a live step count, remaining work, and detailed per-file status.
- Added a display-only filename-based console guess in the metadata editor to help with manual tagging without changing any metadata automatically.
## 0.580
- Split the library-wide scan actions into Refresh for incremental metadata updates and Rebuild for a full forced rescan.
- Reworked the library header so Photography stays visually separate on the left while Refresh, Rebuild, and Fetch Covers stay grouped on the right.
- Increased library cover art sizing, added persistent custom cover overrides with a tile right-click menu, and kept those overrides in shared data so they survive app updates.
- Updated the right-hand capture browser to preserve original image aspect ratios and increased the default thumbnail size for easier browsing.
## 0.571
- Added a shared on-disk thumbnail cache under PixelVaultData so library thumbnails persist across app updates instead of regenerating every version.
- Library tile art and preview-sized images now reuse cached thumbnail files when available, which should make the library feel faster after the first load.
## 0.570
- Changed the full-library Scan Library action to force a true metadata rescan instead of trusting older cached entries as unchanged.
- This keeps whole-library scans aligned with Scan Folder so tag-based regrouping updates correctly across the full library.
## 0.569
- Fixed a background-thread logging bug that could make a completed library scan throw a cross-thread error at the end of the run.
- Library scan monitor logging now routes safely back to the UI thread so long scans can finish cleanly.
## 0.568
- Added a non-blocking library scan monitor so library and folder scans now run in the background instead of freezing the app.
- Added cancel support for library scans, with a dedicated Cancel Scan button that lets the current file finish cleanly before stopping.
- Added detailed scan progress reporting with processed counts, remaining counts, and per-file activity messages so it is clear what PixelVault is doing.
# PixelVault Changelog

## 0.567
- Switched virtual library grouping to embedded platform tags only, removing the old filename fallback from the library route.
- Normalized legacy custom platform tags like Platform:PC and Platform:Steam so they no longer create false Multiple Tags groupings.
- Updated the library metadata editor to read current embedded tags from the file, so reopening the editor reflects the actual saved console tag state.
- Fixed the selected-items apply path in the metadata editor so only the files you confirm are carried forward.

## 0.566
- Narrowed library tag reads to the explicit XMP/IPTC fields PixelVault writes, which avoids phantom platform combinations coming back from aggregate keyword fields.
- Normalized platform-family parsing so Steam stays a single logical platform instead of reappearing as both Steam and PC in the library editor.
- Fixed library regrouping and editor reloads to use the same normalized platform-family rules as the metadata index.


## 0.565
- Fixed library metadata edits so changing a console tag now replaces the old platform tags instead of appending a new one on top.
- Library regrouping should now stay aligned after reopening the editor because the file metadata and index agree on the final console tag set.

## 0.564
- Fixed library metadata editing so files in a Multiple Tags view now rebuild their console checkboxes from stored file tags instead of showing blank.
- Updated virtual library grouping to prefer the indexed tag list over stale aggregate folder labels, which helps mis-grouped single-platform files correct themselves.

## 0.563
- Moved settings, logs, and cache into a shared PixelVaultData folder so library scans persist across version upgrades.
- Added one-time migration of existing cache and settings from older version folders into the shared data location.
- Library metadata indexing now stays consistent when you open a newer build of the app.

## 0.562
- Added support for Xbox direct-export screenshot names like Game-2026_03_21-03_53_32.png during import detection.
- Updated Xbox timestamp parsing so direct-export files pull their capture time from the filename correctly.
- Direct Xbox exports now flow through import and destination sorting the same way as the auto-uploaded Xbox screenshots.

## 0.561
- Stabilized the library metadata index so folder classifications no longer fall back to Other just from reopening the app.
- Folder and file groupings now keep the last scanned platform label until you explicitly rescan the library or a folder.

## 0.560
- Added a dedicated PC platform checkbox to the manual and library metadata editor.
- Added an Other platform option with a required custom platform name field for manual platform tagging.
- Updated platform grouping and metadata indexing so custom platform tags and direct PC tags are recognized across the app.

## 0.550
- The Game Library now creates virtual per-console entries, so the same game can appear separately as Steam, PS5, Xbox, Multiple Tags, or Other without changing the physical NAS folder layout.
- Library previews and library metadata edits now operate on the selected virtual console view instead of every file in the physical folder.
- Scan Folder and selection refresh now preserve the current console-specific view more reliably.
## 0.540
- Added a persistent library metadata index so console grouping can be driven by embedded tags without rescanning the whole library on every open.
- Added Scan Library in the library header and Scan Folder in the preview banner so you can index everything or just the selected game folder on demand.
- Import sorting, library metadata edits, and undo now keep the library index in sync so new changes show up faster in the browser.
## 0.532
- Opening the Game Library no longer tries to fetch cover art on the UI thread before the window appears.
- Library folder grouping now uses the cached folder platform label first instead of rescanning every folder immediately on open.
- The explicit Fetch Covers button still handles Steam cover downloads when you want them.
## 0.531
- Library metadata editing now applies only to the files you actually select instead of silently targeting the whole folder.
- The library metadata editor now opens with a single visible selection and a clearer selected-state highlight in the left file list.
- Library console badges and library-browser grouping now refresh correctly after console-tag edits, so Steam/PS5/Xbox changes stop lingering under Other.
## 0.530
- Replaced the broken library separator glyph with a plain pipe so folder details read cleanly.
- Grouped the Game Library folders into collapsible Steam, PS5, Xbox, Multiple Tags, and Other sections.
- Increased the library folder art size a bit and tightened the caption text underneath for a cleaner browse view.

## 0.520
- Added an Edit Metadata action to the Game Library banner so existing folders can be updated without going back through intake.
- Reused the batch metadata editor for library files, including comments, tags, console tags, custom capture time, and game-title renaming.
- Library edits now reorganize renamed captures into the proper game folder automatically after metadata changes.

## 0.510
- Reorganized the main page buttons into clearer import, library, and utility groups.
- Promoted View Logs into the header alongside Settings and Changelog for faster access.
- Tightened the button labels so actions read more clearly at a glance.

## 0.501
- Wired destination sorting into both import paths so files are grouped into game folders automatically after import.
- Kept the Sort Destination button for re-running the organizer manually later.

## 0.500
- Switched the default intake and library paths to the NAS-based Game Capture Uploads and Game Captures locations.
- Added built-in destination sorting based on the existing PowerShell folder rules, using the current Destination setting.
- Added Undo Last Import so the most recent moved files can be sent back to their source folders.

## 0.420
- Expanded the main window so the workflow buttons stay on one line more reliably and the preview area has more vertical room.
- Switched the main workflow canvas to a cleaner white treatment while keeping cards and panels visually separated.
- Refined button styling with stronger shadows, a green Process with Comments action, and light-gray Open Sources/Open Destination buttons.

## 0.410
- Added support for multiple source folders in Settings so intake can scan across more than one location.
- Updated preview, process, manual intake, and move workflows to read from every configured source folder.
- Added an Open Sources action to open each configured intake folder from the main screen.

## 0.396
- Hardened manual intake date handling so the custom date picker no longer silently falls back to today's date when nothing is selected.
- Manual intake now opens with all unmatched files selected by default, making bulk date and tag edits apply more predictably.
- The finish action now re-applies the current custom date/time to the selected manual items before processing.

## 0.395
- Moved the Changelog button next to Settings and added an in-app changelog reader window instead of opening the markdown file externally.
- Simplified the manual multi-select preview art so it shows only the selected count.
- Fixed the manual title field so typing spaces or other edits no longer jumps the cursor back to the beginning.

## 0.390
- Added changelog tracking and a main-window button to open it.
- Updated Manual Intake so the badge reflects the selected console tag instead of always showing Manual.
- Manual Intake console tags are now mutually exclusive and separated visually from the Game Photography tag.
- Manual Intake now supports multi-select editing so shared game names, tags, dates, and comments can be applied to multiple unmatched files at once.
- Multi-select preview now switches to a stacked multiple-image placeholder with the selected item count.

## 0.370
- Added Steam, PS5, and Xbox console-tag checkboxes next to the Game Photography option in the review popup.
- Refined the main workflow layout with a lighter Preview button, consistent button sizing, stronger button shadows, and white content cards.






























