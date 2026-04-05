# Library Timeline Layout Reference

Last updated for source review on `2026-04-05`.

This note captures the specific layout behavior worth borrowing from Immich for PixelVault's timeline.

Reference repo:

- [immich-app/immich](https://github.com/immich-app/immich)

Most relevant files reviewed:

- `C:\Temp\immich-review\web\src\lib\managers\timeline-manager\internal\layout-support.svelte.ts`
- `C:\Temp\immich-review\web\src\lib\managers\timeline-manager\timeline-day.svelte.ts`
- `C:\Temp\immich-review\web\src\lib\components\timeline\Month.svelte`
- `C:\Temp\immich-review\web\src\lib\utils\layout-utils.ts`

## Core takeaway

Immich does not render each date as a full-width band.

Instead, it:

- builds a justified mini-grid for each day
- treats each day as a measured block with its own header
- packs multiple day blocks left-to-right inside a larger month row
- starts a new row only when the next day block would overflow the viewport

That is why a one-photo day can appear directly beside another one-photo day.

## What PixelVault should borrow

### Borrow directly

- packed day cards instead of full-width date sections
- smaller timeline-specific tile sizing than the standard detail pane
- lightweight day labels inside each card
- a greedy row-packing pass based on measured or estimated card width

### Borrow later

- justified photo layout inside each day card
- month-level scrubber navigation
- deeper viewport-aware layout deferral

## PixelVault first-slice adaptation

For the first implementation slice in `C:\Codex`:

- keep the existing timeline data model grouped by capture date
- keep the existing tile footer with game, platform, time, and comment
- replace the full-width day header + photo rows with day cards
- estimate each day-card width from capture count and a timeline-specific tile size
- greedily pack multiple day cards into the same virtualized row when they fit

This gets the visible "adjacent sparse days" effect without forcing a rewrite of the metadata, selection, or tile action layers.
