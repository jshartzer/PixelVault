# Library folder “smart views” (filters)

This document is the source of truth for **folder list filter modes** in the library browser (**Slice F**, `PV-PLN-V1POL-001`). Behavior is implemented in `MainWindow.LibraryBrowserRender.FolderList.cs` (search is applied first, then filter).

Canonical mode strings are normalized by `SettingsService.NormalizeLibraryFolderFilterMode` and persisted as `library_folder_filter_mode` in settings.

| Canonical `filter_mode` | User-facing label | Definition |
|-------------------------|-------------------|------------|
| `all` | All Games | No filter; every folder view row passes. |
| `completed` | 100% Achievements | `folder.IsCompleted100Percent` is true. |
| `crossplatform` | Cross-Platform | More than one distinct normalized platform label **or** `folder.IsMergedAcrossPlatforms`. |
| `large` | 25+ Captures | `folder.FileCount >= 25`. |
| `needssteam` | Steam · missing App ID | `SteamAppId` is blank **and** the folder is tagged as Steam: normalized `PrimaryPlatformLabel` is Steam **or** any entry in `PlatformLabels` normalizes to Steam. |
| `nocover` | No cover path | `PreviewImagePath` is null or whitespace (no on-disk existence check). |

**Notes**

- **Timeline** grouping builds a synthetic view; filters still apply to the underlying folder rows that feed the timeline.
- **Search** (search box) further narrows rows using `SearchBlob` (lowercased name, paths, ids, platforms).

**Sort** and **grouping** modes are separate settings; see `NormalizeLibraryFolderSortMode` and `NormalizeLibraryGroupingMode` in `SettingsService.cs`.
