# Companion progress notifications

Installed November 7, 2026 as Companion **0.49.0** at `C:/Codex/Companion Production/app-0.49.0`. Local and LAN health, authenticated endpoint protection, served assets and package hashes verified. Existing settings, data roots, worker paths and shared tracker/map assemblies are unchanged; `app-0.48.1` is retained for rollback. All 48 targeted checks pass. Reload the web app to establish the new achievement baseline.

Companion 0.49.0 uses the Detailed progress popup: tracker changes and completions appear first, followed by newly unlocked achievements with their saved artwork, title, game and platform. Clicking the popup or its backdrop, the close button, or Escape dismisses and acknowledges the displayed snapshot.

The authenticated `GET /api/v1/achievement-updates` endpoint returns only confirmed locked/unlocked IDs and an opaque workspace/provider/game/account scope. It reads canonical achievement observations and verified Centurian progress. It does not run provider sync, read game saves, award progress or change stored achievements. Full achievement details are requested only for games with new unlocks; existing library/tracker revisions avoid repeated snapshot requests while idle.

Browser history is separate from tracker history in `PixelVault.Companion.seen-achievements.<workspace>`. A first visit, a newly connected game/account, or malformed legacy state establishes a baseline without announcing historical unlocks. Only a previously confirmed locked achievement becoming confirmed unlocked is announced. Unknown progress is not a confirmed lock. Earned identities remain acknowledged through temporary disappearance, unknown observations and provider resets. Browser storage failure retains the baseline for the current session.

Acknowledgement advances on dismissal, or silently when there are no new updates. Failed snapshot/detail requests preserve the previous baseline and retry; manual refresh reports incomplete achievement checking rather than claiming everything is current. Workspace changes close an old popup and establish the appropriate scope.

Implementation source: `C:/Users/justc/.codex/worktrees/pixelvault-test-source/Codex` (the maintained branch matching the installed 0.48.1 web app). The `C:/Codex` main source checkout is an older 0.16.0 snapshot and must not be published over the current app.

Validation: `tests/companion-updates.test.cjs`, `tests/companion-updates.browser.cjs`, `tests/companion-performance.browser.cjs`, and the Companion achievement/direct-local/API tests. Rendered evidence and installation result: `C:/Codex/artifacts/achievement-notification-implementation/`.
