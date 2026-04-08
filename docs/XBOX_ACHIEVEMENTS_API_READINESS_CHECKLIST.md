# Xbox Achievements API Readiness Checklist

This document is a parking spot for the work PixelVault needs to complete before we pursue a real Microsoft Xbox achievements integration.

It is intentionally a readiness checklist, not an implementation spec.

Use it when Xbox achievements moves from "interesting future idea" to "active project."

## Goal

The future goal is:

- let a PixelVault user connect an Xbox account
- resolve local game rows to real Xbox title identity
- pull the user's real achievement progress for supported Xbox titles
- show that progress in the existing achievements surface alongside the current Steam and RetroAchievements behavior

## Current reality

PixelVault already has:

- an achievements button and modal viewer
- a provider-style fetch path for Steam and RetroAchievements
- game-level storage for Steam App ID, Non-Steam ID, SteamGridDB ID, and RetroAchievements game ID

PixelVault does **not** yet have:

- Xbox account linking
- Xbox token handling
- Xbox title identity fields in the game index
- a confirmed supported Microsoft API route for this companion-app scenario

## Readiness checklist

Do not start implementation until the boxes in Sections 1-4 are answered well enough to remove major ambiguity.

### 1. Product decision lock

- [ ] Confirm the exact v1 scope:
  - signed-in user only
  - no public gamertag scan fallback in v1 unless explicitly approved
- [ ] Confirm whether the first target is:
  - Xbox console titles only
  - Xbox PC titles too
  - Game Pass / cross-save titles where title identity can diverge
- [ ] Confirm whether v1 is:
  - read-only achievements display
  - or a broader Xbox profile sync feature
- [ ] Confirm whether Xbox achievements should appear:
  - only in the existing game-level achievements modal
  - or also as library badges / filter state / completion signals
- [ ] Confirm the rule for unsupported or ambiguous titles:
  - hide Xbox achievements entry point
  - show "needs Xbox title mapping"
  - or show a disabled state with guidance

### 2. Feasibility and policy proof

- [ ] Identify the supported Microsoft path for a non-game companion app to access a signed-in user's Xbox achievements.
- [ ] Confirm whether the web-app / Xbox services token flow is allowed for PixelVault's scenario.
- [ ] Confirm whether per-title achievement access works for arbitrary Xbox titles the user owns, not only the calling title.
- [ ] Confirm which title identifiers are required by the service:
  - `TitleId`
  - `ProductId`
  - `SCID`
  - anything else the final endpoint needs
- [ ] Confirm privacy limitations:
  - signed-in self access
  - public-profile access
  - friends-only / blocked cases
- [ ] Confirm whether a pure local desktop integration is acceptable, or whether a small backend/service is required for secure token exchange.
- [ ] Confirm rate limits, token lifetimes, refresh behavior, and any publisher registration requirements.
- [ ] Record the go / no-go answer in writing before coding starts.

### 3. Auth and platform architecture

- [ ] Decide whether auth will be:
  - local-only in the desktop app
  - service-backed with server-side secret handling
  - hybrid
- [ ] Decide where refresh tokens and Xbox identity state will live.
- [ ] Define the minimum secure storage requirements for any user tokens.
- [ ] Define the sign-in UX:
  - connect Xbox account
  - disconnect Xbox account
  - token expired / re-auth needed
- [ ] Define failure UX:
  - no internet
  - Microsoft sign-in canceled
  - insufficient privacy permissions
  - title not mapped
  - service unavailable
- [ ] Decide whether Xbox sync is:
  - on-demand only
  - background refresh
  - manual refresh plus cache

### 4. Data model readiness

- [ ] Add the Xbox identifiers PixelVault will need to its design backlog.
- [ ] Choose the minimum game-level fields, likely including:
  - `XboxTitleId`
  - `XboxProductId`
  - `XboxScid` if the API path requires it
- [ ] Decide whether these fields belong only in the Game Index, or also need denormalized mirrors in the Photo Index / folder cache.
- [ ] Decide how Xbox identity behaves in combined `All` library rows when multiple console rows disagree.
- [ ] Define how manual edit flows should expose Xbox IDs.
- [ ] Define how auto-lookup or assisted lookup for Xbox title identity would work, if at all.

### 5. Title matching and catalog strategy

- [ ] Decide how PixelVault will map a local game row to an Xbox title.
- [ ] Define the authoritative match inputs:
  - local title
  - platform label
  - cover/source hints
  - external IDs from other ecosystems
- [ ] Define what counts as a confident automatic match.
- [ ] Define what forces manual review.
- [ ] Plan for known hard cases:
  - same game with different Xbox editions
  - remasters / bundles / definitive editions
  - Xbox console vs Xbox PC variants
  - regional naming differences
  - Game Pass container titles vs store titles
- [ ] Decide whether PixelVault needs its own Xbox title catalog snapshot/cache.

### 6. UI and workflow prep

- [ ] Extend the Path Settings / account area with an Xbox connection surface if the feasibility spike says "go."
- [ ] Extend the ID editor with the Xbox identity fields required for achievement lookups.
- [ ] Decide how the achievements button behaves for Xbox-tagged games before setup is complete.
- [ ] Design the user-facing states:
  - connected and synced
  - connected but title not mapped
  - account needed
  - title identity needed
  - private / unavailable
- [ ] Decide whether the achievements modal should show:
  - unlocked vs locked state
  - unlock timestamps
  - gamerscore
  - secret achievement handling
  - achievement art/icons

### 7. Backend / sync considerations

- [ ] Decide whether PixelVault needs a lightweight backend to safely exchange Microsoft tokens.
- [ ] If yes, define the minimum backend responsibilities:
  - OAuth callback handling
  - token exchange
  - token refresh
  - optional sync jobs
- [ ] Decide whether achievement pulls are live-per-click or cache-backed.
- [ ] Define cache invalidation and refresh rules.
- [ ] Define structured logging for:
  - auth failures
  - title mapping failures
  - sync failures
  - service throttling

### 8. Compliance and user trust

- [ ] Review Microsoft terms/policy requirements for storing and using Xbox account data in this scenario.
- [ ] Decide exactly what Xbox-derived user data PixelVault is allowed to persist.
- [ ] Define the minimum privacy copy shown to users before they connect an Xbox account.
- [ ] Define deletion behavior for disconnect:
  - remove tokens
  - keep cached achievement snapshots
  - or clear all Xbox-derived user data
- [ ] Decide whether Xbox data ever leaves the local machine.

### 9. Prototype gates

- [ ] Build a tiny proof-of-concept outside the main achievements UI first.
- [ ] Prove that a signed-in PixelVault-controlled flow can retrieve real achievement data for one known Xbox title.
- [ ] Prove that PixelVault can map one local game row to that same title identity cleanly.
- [ ] Prove the returned data is rich enough for the existing achievements modal:
  - title
  - description
  - unlocked / locked
  - unlock date if available
  - icon/art if available
- [ ] Only after those proofs pass, create the real implementation plan.

### 10. Implementation-entry checklist

These are the final "green lights" before engineering work begins:

- [ ] Microsoft access path is confirmed and documented.
- [ ] Auth architecture is chosen.
- [ ] Required Xbox identifiers are known.
- [ ] Game Index schema impact is understood.
- [ ] UI states are defined.
- [ ] At least one successful proof-of-concept call exists.
- [ ] A small implementation plan has been written.

## Recommended first project when this resumes

When Xbox achievements becomes active work again, the first task should be:

1. run a short feasibility spike on the real Microsoft auth + Xbox services route
2. answer the policy / title-scope questions in Section 2
3. build one tiny proof-of-concept for a signed-in user's single title
4. only then decide whether the feature belongs in PixelVault proper

## Non-goals for the first pass

- Do not start by wiring the full UI.
- Do not add Xbox fields to the persistent schema until the feasibility path is confirmed.
- Do not assume the Steam / RetroAchievements provider model can be reused unchanged.
- Do not assume a public gamertag scan path is available or allowed just because third-party sites appear to do it.

## Research references

These links are the starting point for the future spike:

- Microsoft Learn: Xbox services sign-in for title websites
  - <https://learn.microsoft.com/en-us/gaming/gdk/docs/services/fundamentals/s2s-auth-calls/service-authentication/live-website-authentication?view=gdk-2510>
- Microsoft Learn: Achievements service REST reference
  - <https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/live/rest/uri/achievements/uri-usersxuidachievementsscidachievementidget?view=gdk-2510>
- Microsoft Learn: GDK/XSAPI achievements function reference
  - <https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/live/xsapi-c/achievements_c/functions/xblachievementsgetachievementsfortitleidasync?view=gdk-2510>
- DeepWiki mirror of `microsoft/xbox-live-api` achievements services
  - <https://deepwiki.com/microsoft/xbox-live-api/4.4-achievements-services>
- TrueAchievements getting started
  - <https://www.trueachievements.com/gettingstarted.aspx>
- TrueAchievements privacy settings
  - <https://www.trueachievements.com/privacy-settings>

## Related PixelVault files

When work resumes, start by reviewing:

- `C:\Codex\src\PixelVault.Native\Services\Achievements\GameAchievementsFetchService.cs`
- `C:\Codex\src\PixelVault.Native\UI\AchievementsInfoWindow.cs`
- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserAchievements.cs`
- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryFolderIdEditor.cs`
- `C:\Codex\src\PixelVault.Native\UI\Settings\SettingsShellHost.cs`
