# Library folder “smart views” (filters)

This document is the source of truth for **folder list filter modes** in the library browser (**Slice F**, `PV-PLN-V1POL-001`). Behavior is implemented in `MainWindow.LibraryBrowserRender.FolderList.cs` (search is applied first, then filter).

Canonical mode strings are normalized by `SettingsService.NormalizeLibraryFolderFilterMode` and persisted as `library_folder_filter_mode` in settings.

| Canonical `filter_mode` | User-facing label | Definition |
|-------------------------|-------------------|------------|
| `all` | All Games | No filter; every folder view row passes. |
| `completed` | 100% Achievements | `folder.IsCompleted100Percent` is true. |
| `crossplatform` | Cross-Platform | More than one distinct normalized platform label **or** `folder.IsMergedAcrossPlatforms`. |
| `large` | 25+ Captures | `folder.FileCount >= 25`. |
| `missingid` | Missing ID | **Any of:** `GameId` is blank (no game-index id), **or** `SteamGridDbId` (STID) is blank — required for every row — **or** the folder is **Steam-tagged** (see note) **and** `SteamAppId` is blank, **or** the folder is **Emulation-tagged** (see note) **and** `RetroAchievementsGameId` is blank. |
| `nocover` | No cover path | `PreviewImagePath` is null or whitespace (no on-disk existence check). |

**Notes**

- **Steam-tagged folder:** normalized `PrimaryPlatformLabel` is Steam **or** any entry in `PlatformLabels` normalizes to Steam (same rules as `LibraryBrowserFolderViewIsSteamTagged` / the Filter menu).
- **Emulation-tagged folder:** same pattern for the Emulation console — `LibraryBrowseFolderSummary.IsEmulationTagged` / `LibraryBrowserFolderViewIsEmulationTagged`. RetroAchievements game ID is only required when this applies.
- **Legacy persisted values** `needssteam`, `needssteamgrid`, and `missinggameid` (and their text aliases) normalize to **`missingid`**.
- **Timeline** grouping builds a synthetic view; filters still apply to the underlying folder rows that feed the timeline.
- **Search** (search box) further narrows rows using `SearchBlob` (lowercased name, paths, ids, platforms).

**Sort** and **grouping** modes are separate settings; see `NormalizeLibraryFolderSortMode` and `NormalizeLibraryGroupingMode` in `SettingsService.cs`.

---

## Browse row projection (`GameSummary`-shaped)

Library folder rows in the browser are materialized as **`MainWindow.LibraryBrowserFolderView`** (see `MainWindow.LibraryBrowserViewModel.cs`) after merging **`LibraryFolderInfo`** from the folder cache. For **API- and mobile-friendly** snapshots without WPF, use **`LibraryBrowseFolderSummary`** (`UI/Library/LibraryBrowseFolderSummary.cs`), built via **`LibraryBrowseFolderSummary.FromFolderView(...)`**.

| Concept (see `docs/ios_foundation_guide.md`) | `LibraryBrowserFolderView` / summary field | Notes |
|---------------------------------------------|--------------------------------------------|--------|
| Stable row identity | `ViewKey` | Session restore, grouping variants may encode path segments. |
| Game / list id | `GameId` | Saved game-index id; may be empty. |
| Display title | `Name` | |
| Primary disk location | `PrimaryFolderPath` | Cross-platform merge still has one “primary” path for chrome. |
| Console context | `PrimaryPlatformLabel`, `PlatformLabels`, `PlatformSummaryText` | Normalized labels drive filters, badges, grouping. |
| Library stats | `FileCount`, `PreviewImagePath` | `nocover` filter uses `PreviewImagePath` only (no existence check). |
| Recency | `NewestCaptureUtcTicks`, `NewestRecentSortUtcTicks` | Sort modes (`captured`, `added`, `photos`). |
| External ids | `SteamAppId`, `SteamGridDbId`, `RetroAchievementsGameId` | `missingid`: STID always; App ID when Steam-tagged; RA id when Emulation-tagged. |
| Collection | `IsCompleted100Percent`, `CompletedUtcTicks` | `completed` filter. |
| Merge | `IsMergedAcrossPlatforms` | `crossplatform` filter. |
| Synthetic row | `IsTimelineProjection` | Timeline synthetic views; not a disk folder. |
| Search (desktop) | `SearchBlob` | **Not** on `LibraryBrowseFolderSummary` — keep search client-side or rebuild from fields on a service. |
| Rich refs | `SourceFolders`, `PrimaryFolder`, `FilePaths` | **Not** on summary — use **`LibraryFolderInfo`** / detail APIs when full paths are required (`GameDetail`-shaped work). |

**Plan:** `docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md` Step 4.

---

## Manual verification checklist (Slice F)

Use this when changing filter normalization, `LibraryBrowseFolderSummary` predicates, or folder list rendering.

1. **Search then filter:** Type a partial game name; set filter **Missing ID**; confirm rows match `missingid` in the table above (including Steam/Emulation tag rules in the Notes).
2. **Filter only:** Reset search; toggle **All games** → **No cover path** → **100% Achievements**; confirm counts and rows change without errors.
3. **Grouping:** With filter **25+ Captures**, switch grouping **By console** and **Timeline**; confirm no crash and filters still apply to underlying folder rows.
4. **Persistence:** Change filter, close library, reopen; mode matches **Settings** / `library_folder_filter_mode` (see `SettingsService.NormalizeLibraryFolderFilterMode`).
5. **Legacy settings:** If you still have old `needssteam`-era values in settings JSON, confirm they normalize to **`missingid`** (see Notes above).
