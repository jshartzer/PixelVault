# Achievement Guide JSON import

PixelVault achievement-guide bundles use a versioned JSON contract. Open a game's **Achievement Guide** window and choose **Import JSON...** for a file or **Paste JSON** for clipboard text. PixelVault previews matched, changed, unchanged, and unmatched entries before writing anything.

## Version 1 contract

```json
{
  "schemaVersion": 1,
  "provider": "steam",
  "providerGameId": "1245620",
  "sourceUrl": "https://example.com/game-guide",
  "sourceTitle": "Example achievement guide",
  "achievements": [
    {
      "providerAchievementId": "ACH_FIND_ALL_ITEMS",
      "guideText": "Collect all items before entering the final area.",
      "tags": ["collectible", "missable"],
      "isMissable": true
    }
  ]
}
```

Required bundle fields:

- `schemaVersion`: currently `1`.
- `provider`: `steam` or `retroachievements`.
- `providerGameId`: Steam App ID or RetroAchievements Game ID for the open Guide window.
- `achievements`: one or more guide entries.

Required achievement fields:

- `providerAchievementId`: Steam API achievement name or numeric RetroAchievements achievement ID.
- `guideText`: concise, original completion instructions.
- `tags`: JSON array; use an empty array when there are no tags.
- `isMissable`: `true` or `false`.

Optional attribution:

- Bundle-level `sourceUrl` and `sourceTitle` act as defaults for every entry.
- An achievement may supply its own `sourceUrl` and `sourceTitle` to override those defaults.
- Source URLs must be complete `http://` or `https://` addresses.

## Matching and safety

PixelVault matches only the tuple `(provider, providerGameId, providerAchievementId)`. Display titles are not used because titles can be renamed, duplicated, or localized.

Before import, PixelVault reports:

- entries matched to the open game's local achievement catalog;
- entries that would change stored guide data;
- entries already up to date;
- unmatched provider achievement IDs;
- validation errors such as the wrong game, unsupported schema, duplicate IDs, missing guide text, or invalid source URLs.

Unmatched entries are skipped. Matched changes are committed in one SQLite transaction, followed by a rolling guide-database backup. Provider metadata and player unlock progress are not modified.

## Preparing sourced bundles

Use short original summaries rather than copying complete walkthroughs. Keep the source URL attached, and use tags such as:

- `missable`
- `collectible`
- `multiplayer`
- `difficulty`
- `story`
- `grind`
- `point of no return`

When a guide page covers several platform editions, confirm that its achievement IDs belong to the provider/game version shown in PixelVault.

Do not import full copyrighted walkthroughs unless you wrote them or have permission to store them. A good bundle paraphrases only the steps needed for each achievement and retains the page URL so the full source can be opened separately.

## Storage and recovery

Guides are authored data, not cache data. PixelVault stores them in `PixelVaultData/guides/pixelvault-guides.sqlite`; clearing the provider/library cache does not remove them. Successful guide saves and imports create rolling snapshots under `PixelVaultData/guides/backups/`, retaining the newest 12 backups.

Before manually restoring a backup, close PixelVault and preserve the current database. The backup files are complete SQLite snapshots; provider progress is not stored in them and will be fetched again normally.
