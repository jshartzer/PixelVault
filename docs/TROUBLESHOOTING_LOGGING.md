# Troubleshooting Logging

PixelVault now has an opt-in troubleshooting log for cases where the normal run-history log is too noisy or too broad.

## Where logs live

- Normal app log: `data\logs\PixelVault-native.log`
- Troubleshooting log: `data\logs\PixelVault-troubleshooting.log`

You can open the containing logs folder from `Settings > View Logs`.

## How to enable it

1. Open `Settings`
2. Find the `Diagnostics` card
3. Turn on `Enable troubleshooting logging`

The setting is persisted in the app settings file, so it stays on until you turn it off again.

## What it captures right now

The troubleshooting log is focused on Library behavior first, especially the kinds of async issues that are hard to reason about from the UI alone.

Current Library entries include:

- folder refresh start, completion, stale refresh discard, and failure
- cached folder snapshot prefill
- selection changes
- detail render start
- loading-state resets on real selection changes
- snapshot apply events
- stale render skips when a newer selection/render wins
- embedded metadata repair start, completion, diff outcome, and failure
- banner art resolve failures
- first detail snapshot dispatch wall time (`LibraryDetailQuickSnapshotDispatchComplete` → `dispatcherWallMs`)
- virtualized folder/detail scroll refreshes (`VirtualizedRowHostScroll`) showing whether the refresh was render-coalesced or an immediate page-sized jump
- virtualized visible row rebuilds (`VirtualizedRowHostRowsRebuilt`) showing host name, row range, recycler state, scroll offset, and whether measured row height changed

When a folder’s first detail paint is slow enough, the main app log also emits **`PERF | LibraryDetailRender`** with a breakdown of the **background quick snapshot** (prep vs media-dimension map vs timeline/groups tail), whether the media map was **reused** from an earlier snapshot pass, and **`uiApplyMs`** (time inside the first UI-thread apply that builds virtual rows).

Each line includes:

- timestamp
- thread id
- event area
- compact context like view key, game name, file count, source folder count, grouping mode, and render version

## When to use it

Turn this on when you are trying to reproduce or explain behavior such as:

- the right-hand pane looking frozen or out of sync
- a folder switch showing the wrong screenshots
- library refreshes racing each other
- merged game rows behaving unexpectedly
- cover/detail updates not appearing when expected

## Recommended workflow

1. Turn troubleshooting logging on
2. Reproduce the issue
3. Open the logs folder from Settings
4. Check `PixelVault-troubleshooting.log`
5. Turn troubleshooting logging back off when you are done

Keeping it opt-in helps the log stay focused and easier to read during a real bug chase.

## Large-folder scroll check

For `PV-PLN-PERF-001` scroll/render polish, use a large folder such as `Diablo IV`, Timeline, or any folder with hundreds of captures.

1. Turn troubleshooting logging on.
2. Open the Library and select the large folder.
3. Wait for the first usable detail pane.
4. Fast-scroll the right-hand detail pane down and back up for 10-15 seconds.
5. Switch to another folder while thumbnails or metadata are still settling.
6. Review `PixelVault-troubleshooting.log` for `VirtualizedRowHostScroll`, `VirtualizedRowHostRowsRebuilt`, `LibraryDetailRenderSkipped`, and `LibraryDetailRenderApplied`.

Pass if `DetailRows` emits render-coalesced scroll refreshes, page-sized jumps are rare/immediate, row rebuild ranges follow the current scroll offset, and stale detail renders are skipped instead of repainting over the new selection.
