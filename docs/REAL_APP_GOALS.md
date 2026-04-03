# PixelVault "Real App" Goals

This document captures the broader product and engineering qualities that make PixelVault feel like a mature, reliable app.

These goals are intentionally higher-level than the MainWindow extraction roadmap or service split plan. They describe the app experience we are working toward while the architecture continues to improve underneath it.

## Core idea

What makes an app feel "real" usually is not one big feature.

It is the combination of:

- speed
- trust
- polish

## Responsiveness

- Make every expensive action window-first, work-second. Open the UI immediately, then stream in data.
- Treat "time to first useful paint" as a core metric for Library, Settings, editors, and intake dialogs.
- Push more work to background lanes with cancellation, especially cover fetch, metadata reads, folder refresh, and image/video prep.
- Prefer stale-while-revalidate everywhere possible. Show cached library state fast, then quietly swap in fresh results.
- Add priority tiers for background work: visible selection first, nearby tiles second, offscreen work last.
- Coalesce repeated refreshes. If multiple things ask for a library reload, do one.

## UX Polish

- Make empty, loading, error, and canceled states feel intentional instead of generic.
- Preserve user context aggressively: scroll position, selection, search text, expanded sections, active folder, unsaved editor filters.
- Add keyboard flow for the most common actions: search, next/previous item, open metadata editor, import, refresh, cover actions.
- Reduce modal friction where possible. Real apps let people keep moving.
- Add better inline feedback: "saved", "refreshing 12 folders", "waiting on Steam", "cover already cached".
- Keep heavy maintenance actions out of the main browsing path.

## Trust And Quality

- Add undo and recovery everywhere users might feel nervous: deletes, cover changes, metadata edits, index edits.
- Make long workflows resumable or at least failure-tolerant. If cover refresh fails halfway through, the app should still feel stable.
- Harden startup and shutdown. Real apps reopen cleanly, restore state, and do not make people babysit them.
- Add lightweight diagnostics: timing logs for hot paths, and possibly a hidden perf panel or debug overlay in dev builds.
- Make tests track the new seams, especially async UI-adjacent workflows, because those are the bugs users notice first.

## Next High-Leverage Wave

If choosing the next highest-leverage set of improvements, prioritize:

1. Library startup and selection feel instant.
2. Cover and metadata workflows become fully backgrounded and cancellable.
3. State restoration gets much stronger.
4. Keyboard shortcuts and inline status polish land.
5. Undo and recovery get broadened.

## Important Note

PixelVault probably does not need to wait until the monolith is fully separated before this work begins.

The extraction work creates safer places to implement polish, but a lot of the "real app" feeling can start landing now.

## How To Use This Doc

Use this file as:

- a north-star doc for UX and responsiveness decisions
- a filter for deciding whether a refactor meaningfully improves the user experience
- a companion to the extraction and performance roadmaps

This document should stay stable and strategic. Detailed implementation steps belong in the active roadmap and performance docs.
