# Achievements in the capture timeline

Calendar Timeline mixes saved achievement unlocks with screenshots and clips, newest first. Achievement art occupies one square cell of the photo quilt, follows the existing photo size control and window resizing, and never becomes a rectangular or hero tile. Sparse achievement-only views keep the normal cell size instead of stretching an icon across the viewport.

The Timeline date toolbar includes **Show achievements**, enabled initially. It retains its state while browsing within the open Library window. Turning it off removes achievement tiles while preserving capture selection and the active screenshot/clip filter. Media filters apply to captures; the achievement toggle controls unlocks independently. Achievement tiles are not added to Sessions.

## Dates and scope

- Use provider-reported unlock timestamps, not fetch/import/award-creation timestamps. Convert UTC unlocks to local time for calendar range filtering and day labels.
- Include known unlocked achievements with valid, nonfuture dates from saved progress for the currently configured Steam/RetroAchievements accounts and the games in the filtered Timeline projection's source folders.
- Deduplicate provider/game/achievement identity while preserving case-sensitive provider achievement IDs. Missing dates and unknown or locked progress are excluded.
- Include days with achievements even if no screenshot was taken that day. A date stamp may anchor to an achievement when it is the newest item for its day.
- Recompute mixed order when embedded capture metadata corrects a photo's time, even if the day and relative photo order stay unchanged.

Timeline reads existing Hub progress snapshots and hydrates stored PV points through the durable scoring service. No additional achievement API fetch is required just to open Timeline. A short cache avoids reloading account data on every size adjustment; successful Hub snapshot updates invalidate it. Background achievement observations trigger a coalesced refresh of an active Timeline while preserving scroll position.

## Artwork and interaction

Use the provider's achievement icon, scaled uniformly inside the square tile, with a subtle gold border and star marker. Keep the art clear of PV-point labels. Hover shows the name, game, unlock date, PV points, and rarity; click or keyboard activation opens the achievement detail window. The detail window's Open game achievements action opens the provider/game collection. Artwork decoding follows the displayed size and DPI, with bounded decoding and the existing shared icon cache/download limit.

Achievement layout identities exist only in a separate mixed-layout projection. Capture snapshots, selected-file sets, metadata work queues, export/delete targets, and capture-viewer navigation contain actual capture paths only.

## Clips-only correction

Timeline and Sessions previously discarded videos while flattening folder views, before the Clips only filter ran. Both projections now retain supported images and videos, and their existing media filter determines which captures to render. Timeline summaries count photos, clips, and achievement unlocks separately. Read-only inspection confirmed video records in the live indexes; regression tests exercise videos through the actual view-model projection into Clips only.

## Verification

`LibraryTimelineAchievementTests` covers local date boundaries, invalid/unknown/locked progress, scope and identity deduplication, mixed chronological order, achievement-only days, within-day metadata corrections, compact single-cell sizing across photo sizes, capture-only virtual-row file lists, independent toggle behavior, and accessible detail activation. `LibraryBrowserViewModelTests` covers videos in Timeline and Sessions projections. Mixed quilt layouts were visually checked with cached capture thumbnails and actual downloaded achievement artwork at compact and larger photo sizes.
