# Library Timeline Mode Verification

Use this checklist when the Timeline mode implementation begins landing in source.

This file is intentionally short and practical.

## Core browse checks

- Open the Library and switch between `All`, `By Console`, and `Timeline`.
- Confirm `Timeline` expands into a single full-width capture stream.
- Confirm the split left/right folder-detail layout returns cleanly when leaving `Timeline`.
- Confirm the timeline renders image captures newest-first.
- Confirm captures are grouped by date in descending order.

## Context checks

- Confirm each tile shows subtle game context without overpowering the image.
- Confirm each tile shows subtle console/platform context.
- Confirm mixed-game and mixed-platform recent captures remain readable at a glance.

## Search and scope checks

- Apply Library search text before entering `Timeline`; confirm the timeline reflects the filtered scope.
- Clear search and confirm the timeline expands back to the expected full set.
- Reopen the Library and confirm persisted search + mode restore behave as intended.

## Selection and actions

- Single-select a capture in timeline mode.
- Multi-select with `Ctrl`.
- Range-select with `Shift` if supported.
- Open single-file metadata from a timeline tile.
- Delete selected captures from timeline mode and confirm the feed refreshes correctly.
- Open containing folder from a timeline capture and confirm it resolves to the real source folder.

## Performance checks

- Open `Timeline` on a library with a meaningful number of captures.
- Scroll quickly through several viewport heights and watch for hitching or blank gaps.
- Confirm no obvious UI freeze occurs while timeline rows populate.
- If troubleshooting logging is on, review timing for timeline render paths.

## Edge cases

- Confirm timeline mode handles libraries with no captures gracefully.
- Confirm libraries with only a few captures do not leave awkward empty chrome.
- Confirm stale or missing metadata-index dates do not permanently break ordering after repair.
- Confirm timeline mode does not expose folder-only actions that now have ambiguous scope.
