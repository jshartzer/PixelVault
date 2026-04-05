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
