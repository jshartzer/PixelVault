# Desktop tracker names

Validation: all38 focused cases and1,226 full Native cases pass;62 Companion/shared-code compatibility cases pass. Rename dialog rendered at380/540px; existing high-contrast control path retained. Reports: tracker-rename-focused.trx, tracker-rename-full.trx, tracker-rename-shared-compatibility.trx. Review build C:\Codex\App Testing\Achievement Trackers\PixelVault.exe, native0.077.000 unchanged; running apps and live tracker names retained.

October25,2026 — implemented for Native App Testing; native version0.077.000 unchanged.

Open a tracker and choose **Manage tracker… → Rename tracker…**. The dialog starts with the current name selected; Enter saves and Escape/Cancel keeps it. Names are trimmed, single-line and1–200 characters. Both bundled and custom trackers can be renamed without copying their catalogue or changing tracker/item identity.

The repository updates only display-name metadata in one SQLite transaction. `tracker_title(tracker_id,title)` supplies a durable override for bundled and custom definitions. Custom definitions also store their updated portable title in `tracker_definition`; bundled embedded definitions stay canonical/read-only. The override is applied when definitions are read, so new dashboard/game/achievement views and exports use the chosen name. Renames are game-wide, independent of which account is viewing progress.

Tracker/item IDs, item metadata/targets, achievement link overrides, reported/manual progress, notes, history, sources and automatic settings are retained. An expected-title check rejects a stale rename from another window. The active tracker updates only its selector rather than reloading progress, preserving selection and unsaved note/count drafts. Cancel and no-op changes write no tracker data.

The initial desktop action was local-only. Companion/connector0.7.1 now synchronizes display names separately from add-only catalogue publication, including bundled overrides. Names update automatically through the running PC connector; independent web-name changes are retained as conflicts, and progress/notes/source permissions are preserved. The user's Road Building rename is confirmed live. See [Name synchronization](TRACKER_NAME_SYNC.md).

Validation includes bundled/custom persistence, canonical catalogue preservation, raw state/source/automatic/link/history equality, invalid/missing/stale names, menu access, save/cancel, retained selection and unsaved drafts. Final test counts, render QA and review build are recorded in the handoff/changelog.
