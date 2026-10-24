# Automatic tracker refresh

October 24 chip-reader addition: Fount of Knowledge now uses the existing native source and shared automatic modes. The requested setup enables **After save files change** for this tracker/account. A full validated collector table returns 56 restoration observations; pickup alone never completes a chip. Actual copied-save automation tests verify 21→22 restored, no-change suppression, manual-conflict review and note retention. Close the older running PixelVault windows and open `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe` for the new reader; monitoring runs while the app is open. Other tracker settings are retained. See [Chip reader evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md).

ACHG-004 Phase D is implemented in the unreleased native App Testing build. It uses the existing adapter, validation, reconciliation and history services. The scheduler belongs to the running PixelVault process; it does not create Windows tasks, services or app-closed monitoring.

October 23 order-reader addition: Growth of a Legend now uses the same native save source and automatic modes as fabrication/construction/upgrades. The user's requested setup selects **After save files change** for the connected Steam account. All 540 order identities and independent LL/LLL ranks are checked by the bounded worker. The updated executable is `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`; an older running build must be reopened to load the new reader. Facility connections remain unsupported for direct refresh. Chip restoration was added separately on October 24; see the update above. Earlier verification and default-off statements below describe the original automation implementation; enabling this one tracker is explicitly user-authorized.

## Use

Open a tracker from its game, achievement or the Hub. Configure **Source…**, then choose **Automatic refresh…**. Settings are separate for each tracker, connected provider account and source binding. Existing sources default to **Manual only (off)**.

- **When this tracker opens** requests a read when the tracker is selected/opened. It does not become a recurring poll of progress.
- **After save files change** checks source signatures every five seconds. Two stable checks at least three seconds apart are required before reading.
- **Periodically while PixelVault runs** checks at the selected interval (1–1440 minutes), after the source settles. A delayed/sleeping app performs one due check on resume, without replaying missed intervals.
- **Pause automatic / Resume automatic** persist the chosen mode while stopping/resuming work. **Refresh** always provides a manual read and preview. **Cancel refresh** stops the active read; background cancellation holds automatic retries for 30 seconds. Pause stops them persistently.

Source reconfiguration or disconnection turns automation off for that binding until explicitly enabled again. Account changes cancel work for the old account. Shutdown cancels work and immediately initiates termination of the owned native save worker. No unrelated processes are stopped.

## Applying and reviewing observations

Routine validated updates apply automatically and appear in **History / undo**. Manual values and notes remain separate. No-change reads update source diagnostics without rewriting item timestamps or creating undo batches.

Any manual conflict, older/equal conflicting observation, source-time rollback or undone refresh requires **Refresh** review before background changes can apply. This review requirement survives retries, restarts and cancelling a manual review. Completing a manual review without skipped older observations clears it. Choosing to retain a conflicting manual value does not permit a future background read to replace it.

The settings dialog shows the last result and ten recent automatic checks. Up to 100 results are retained per tracker/account, including unchanged reads and failures. Successful progress changes retain normal transactional undo history. A notice is emitted only for changed progress or an actionable failure/review requirement; identical notices are deduplicated across restarts. The live Library displays a nonmodal toast with a tracker link, respecting fullscreen suppression. With no Library surface, the persisted result and application log remain available; there is no modal fallback.

Unsaved note/count drafts defer background UI reloads, including drafts entered while status is loading. Failed or cancelled reads retain existing progress and last-success metadata. A cancelled review cannot relabel an already completed batch as cancelled.

## Implementation

`ITrackerSourceChangeDetector` supplies adapter-specific file signatures; the scheduler has no game-specific path assumptions. JSON and Death Stranding adapters combine candidate metadata with SHA-256 of the current/newest input, detecting same-size/same-time cloud replacements. Adapters still perform their full bounded stable-copy, hash and identity validation when observing. Polls are hints, not progress evidence. No recursive filesystem watcher is installed.

The application has one active scheduler tick/read at a time. The existing per-source gate also coordinates manual reads; background work does not supersede an open manual preview. Settings revision, source binding, refresh attempt and tracker-state revision are checked before applying. Automation settings are checked again inside the atomic progress transaction. Errors retry after 30 seconds, doubling to a maximum of 30 minutes; the failure count/time survive restarts. Source replacement/paused settings invalidate in-flight work.

`tracker_automation` stores binding-specific settings and last-result/review/notice state. `tracker_automation_result` stores bounded result history. Tables are additive and do not rewrite existing item state. Startup leaves all existing trackers manual-only unless previously opted in. Source adapters and shared reconciliation retain ownership of validation and progress writes.

## Verification

23 added automation/UI cases cover default-off/account isolation, restart persistence, burst writes, same-time cloud replacement, on-open/periodic scheduling, sleep catch-up, pause, source replacement/disconnection, manual conflicts, rollbacks, persistent undo review, manual-preview ownership, error backoff/deduplication, cancellation/shutdown, atomic settings guards, bounded result history and unsaved drafts. The real copied Death Stranding checkpoint also runs through automatic refresh against a temporary store: 35/108, no duplicate history and unchanged source hashes. Automatic settings and the minimum-width tracker were rendered and inspected.

All 106 focused tracker tests pass. Full native suite: 1,003 passed and the same two known unrelated TradingCardFoil shader failures (1,005 total). Results: `tests/PixelVault.Native.Tests/TestResults/tracking-automation-full.trx`. A temporary Windows SQLite handle cleanup race found under the full suite was fixed with bounded test-cleanup retries.

Live in-game fabrication followed by automatic refresh, real OS sleep/resume, high-contrast/scaling and notification presentation remain manual QA. Version remains 0.077.000; automation has not been enabled for the user's live tracker as part of implementation.
