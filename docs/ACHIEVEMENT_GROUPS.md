# Achievement groups

PixelVault organizes achievements at two levels. **Base Game** appears first, followed by named **DLC** sections. Each section has its own categories, such as **Story**, **Actions**, **Difficulty**, **Collectibles**, or a custom category. Story appears first and Actions second by default. **Settings → Achievements** lets you select any subcategory, use **Move up** or **Move down**, then **Save order**. The saved order applies across all games/providers, Base Game and DLC, both collection views, the guide sidebar and category menus. Moves remain a draft until saved. Newly created categories are appended after a saved order. Blank categories appear as **Uncategorized**. Headers have independent earned counts and completion percentages; collapsing Story in Base Game does not collapse Story in a DLC. Both Grid and List use this hierarchy.

Group assignments are provider-neutral. They are stored by the stable tuple:

`provider + providerGameId + providerAchievementId`

This allows the same grouping system to work for Steam, RetroAchievements, and future achievement providers.

## Editing one achievement

1. Right-click an achievement in Grid, List, or the Hub collection.
2. Choose **Assign category** for Story, Difficulty, Collectibles, or a custom category.
3. Independently choose **Assign DLC** → an existing DLC, **Create New DLC…**, or **Base Game**.

The individual achievement detail and guide-reading screens also include a **Change category…** button beside the category. It opens the same choices and saves immediately. In the guide editor it is disabled while notes have unsaved edits; the existing category field remains available while editing.

Category choices are shared across all games/providers in this installation. Defaults and existing/custom categories are registered in the durable `achievement_category` table; a category remains reusable even after its last assignment moves elsewhere. Menus read the registry on opening, so other open games see newly created categories. This shares names only: each achievement keeps its own category assignment, and unused categories do not add empty headers.

Changing DLC keeps the category; changing category keeps DLC membership. DLC names remain scoped to the provider/game and reusable after their last achievement moves away. Guide notes, pins, points, player progress and trackers are independent. The Guide editor's **Achievement category** field edits the assignment; shared subcategory ordering is managed in Settings. Order is stored separately in `achievement_category_order` and does not alter assignments or notes.

Existing `DLC: Name` and generic `DLC` assignments migrate to the independent DLC field. A one-time, provider/game-specific metadata mapping also carries over the older imported expansion groups (including Hearts of Stone, Blood and Wine, The Frozen Wilds and the saved Mordor/Mirage expansions). Other category names stay under Base Game until explicitly assigned a DLC; arbitrary category names are not guessed to be expansions. This metadata remains in the durable guides database outside the rebuildable photo index.

## Importing a researched mapping

Use **Import JSON** or **Paste JSON** in the Achievement Guide window. A grouping-only bundle does not require guide text and does not overwrite existing guides.

```json
{
  "schemaVersion": 1,
  "provider": "steam",
  "providerGameId": "123456",
  "achievements": [
    {
      "providerAchievementId": "ACH_FINISH_STORY",
      "dlcName": "",
      "groupName": "Story",
      "groupSortOrder": 0
    },
    {
      "providerAchievementId": "ACH_DLC_ONE_START",
      "dlcName": "Expansion One",
      "groupName": "Story",
      "groupSortOrder": 10
    }
  ]
}
```

The achievement IDs must exactly match those returned by the provider. Unmatched IDs are reported during import and are not saved.

`dlcName` is optional: omitting it retains the saved assignment; an empty string returns the achievement to Base Game. `groupName` remains the category. Legacy `groupName: "DLC: Name"` imports are accepted as DLC moves and retain an existing category. Organization-only imports do not overwrite guides.

## Percentage behavior

PixelVault calculates each group as `earned / total × 100`, rounded to the nearest whole percent. If player progress is unavailable, the group displays `progress unavailable` instead of incorrectly treating every achievement as locked.

## Finding what remains

Select **Remaining Only** beside Grid/List, or click the **remaining** count on Game Profile. This displays only achievements with known locked progress. Full-game and group totals still include all achievements; unavailable progress is explicitly labeled and does not count as locked or complete. A group with no matching rows keeps its full summary and explains whether it is complete or has unavailable progress.

The refresh symbol and **F5** retrieve current status through the same action. The window retains its view, filter, and collapsed groups through refresh and guide round trips. The **Updated** label records the last successful retrieval in local time; its tooltip includes seconds and UTC offset. A failed refresh keeps the previous result and timestamp.

Guides open for reading with completion instructions, tag chips, and source attribution. Editing is explicit; existing JSON import and source-opening actions remain available. Unsaved edits remain visible while searching, and navigating away or closing still offers Save/Discard/Cancel.

## Next Up

Right-click a known unfinished achievement in Grid or List and choose **Pin to Next Up**; pinned cards offer **Unpin from Next Up**. Focused cards also expose the menu through Shift+F10 or the Menu key. Collection cards have no pin symbol. The Guide title and Next Up strip retain their compact pin controls. Up to three targets are saved per provider/game. The strip opens each matching guide and has an unpin symbol. At the limit, unpin a target before adding another. New pins appear after existing targets. Next Up shows one compact planning tag; its tooltip lists all tags, and the guide shows the full set.

Pins survive restarts, definition refreshes, and guide imports. A successful progress result removes confirmed earned targets and frees their slots. Missing or unknown progress preserves targets; a temporarily absent achievement opens its saved guide without reactivating its definition. You can still edit the saved notes or unpin it, but provider imports are unavailable in that fallback view.

Pins live in the guide database's separate `achievement_pin` table, with the existing rolling backup lifecycle. The current local user shares the provider/game targets across configured accounts; a newly opened collection evaluates progress against the configured account.

## Progress details and sorting

Hover over a Grid or List achievement to see the percentage of players who achieved it globally, alongside its description and status. Missing or invalid percentages display **Global unlock percentage unavailable**; a supplied zero remains 0%.

Expand **Progress details** in the collection to see the latest valid dated unlock, rarest earned achievement where global percentages are supplied, and cumulative dated unlocks by UTC day. **History data** provides selectable dates/counts. Coverage labels explain missing dates, missing rarity, or incomplete player progress; first observation never substitutes for an unlock date.

The **Sort** selector offers provider/default, global unlock percentage in either direction, and unlock date in either direction. Sorting is within each group, with unavailable values last and default order retained for ties. Custom sorting combines status sections to keep that ordering coherent. Group totals, statistics, and Next Up stay independent of Remaining Only and sorting. Steam supplies global percentages; other providers currently show rarity as unavailable.

If Steam player-progress retrieval fails after a successful load, refresh retains that result and timestamp and displays the error. On initial load it shows definitions with unknown progress. Merely having a SteamID configured is not evidence that an achievement is locked.

## Collection feedback

Group headers pair numeric progress with a thin bar calculated from the full group. Groups containing unknown progress show an unavailable label without an empty bar. Earned tiles and List rows use a restrained green accent; achievement cards also open their guides with Enter or Space when focused.

After refresh, confirmed locked-to-earned transitions receive a static six-second border highlight and a short count above the collection. The count remains visible when Remaining Only hides those rows. Initial loads, new identities, missing/unknown progress, conflicting duplicate statuses, account changes, and failed results cannot generate false unlock feedback. A failed fetch preserves the last successful comparison snapshot. Reopening the window starts a fresh account-specific comparison. Feedback has no animation, never moves focus, and clears on the next fetch or window closure. High-contrast rendering uses system colors.

## Companion web editing

Companion0.7.0 preserves the Base Game/DLC/category hierarchy and saved category order. Open an individual achievement, then **Change groups** to edit category and DLC independently. Assignments synchronize both ways with the desktop, with explicit conflict resolution and guide-database backups; guides and progress remain independent. See [Companion achievements](COMPANION_ACHIEVEMENTS.md).
