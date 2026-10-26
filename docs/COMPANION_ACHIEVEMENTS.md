# Companion achievements

Companion **0.8.2** defaults to compact cards; choose **View → List** for rows. The preference persists in this browser. Within every DLC/category, Not obtained achievements appear first and Obtained achievements follow, preserving full totals and group order. Desktop rarity boundaries/palettes are shared: below 0.01% metallic red, up to 1% gold, up to 5% silver, up to 10% bronze, otherwise common. Earned cards use the brighter metallic finish. Guides and group editing remain available from every card.


Companion **0.7.0** adds achievements to game profiles. Open **Achievements** to browse the configured Steam or RetroAchievements edition. Progress is not combined between consoles.

## Browsing and guides

Search titles, descriptions, tags or groups; filter All, Remaining, Earned or Progress unavailable. Remaining excludes unknown progress. Sort by saved order, title, rarity or recently earned. Base Game and each DLC retain their assigned categories and the desktop's saved category order. Filtered lists keep full-game/group totals. Collapsed expansions and filters survive guide round trips.

Each achievement opens its saved guide, tags/missability, source attribution, available counter and linked trackers. Missing guides are explicitly marked; nothing is invented. All676 existing guides transferred, including all63 Death Stranding guides. Text is escaped and supports basic headings, lists and emphasis; source links allow only HTTP/HTTPS.

**Progress checked** records the desktop's saved provider result. The connector imports newer cached results automatically, but this release does not query providers when the web page opens. Use the desktop's existing achievement refresh to retrieve newer provider status. An older/missing cache cannot erase a newer NAS progress result; guide/group metadata can update independently.

## Two-way group editing

Choose **Change groups** on an individual achievement. Category and DLC are separate fields; choose an existing name or enter a new one. Blank DLC means Base Game. Names remain reusable after their last assignment moves. Guide text, sources, earned status and trackers are independent.

Web edits save immediately on the NAS, including while the PC is offline. **Waiting to sync with PC** changes to **Synced with desktop** after application. The PC must be awake, signed in and have automatic updates enabled; the connector checks about every30seconds. Desktop changes also import automatically. Existing Native windows may need refreshing/reopening to display an external assignment change.

Version checks retain drafts when another browser edits the same assignment. If the desktop changed after a queued web edit, the detail shows **Resolve groups**, offering **Use desktop groups** or **Keep web groups**. Neither side silently wins. A late acknowledgement for an older web request advances the newer request's baseline without discarding it.

PC writes are limited to category, DLC and existing sort value for stable provider/game/achievement IDs. Remote paths/commands are not accepted. Each batch first creates a consistent SQLite backup in the private connector's `backups/achievement-groups/`, retaining the newest12 generated backups. It does not rewrite guide content, player caches, pins or the original tracker database.

## Scope and storage

The tracker manifest/pairing is unchanged. Separate approved achievement accounts live in private NAS`data/achievement-accounts.json` and PC`AchievementAccounts`. Both currently connected providers were included. The operator command `--configure-achievement-accounts /data /private/accounts.json` can add a provider but refuses replacement/removal of an existing account; later account changes require deliberate migration.

Five additive `companion_achievement*` tables in `companion.sqlite` hold snapshots, registries and edit receipts. Owner APIs require authentication and CSRF for writes. PC synchronization requires the existing bearer credential, approved workspace/accounts and enabled automatic/library grant. Per-game snapshots are bounded and atomic; a rejected game does not block other games. Export reads Native data read-only; only the explicit group outbox writes back. Provider keys stay on the PC.

Badges use matching game paths on fixed official provider hosts, redirects disabled, bounded downloads, four concurrent fetches and a private128MiB cache. Both current and legacy Steam badge paths are supported. No screenshots are uploaded for this module.

## Deployment and checks

Live: [Companion0.7.0](http://192.168.1.99:8787/?v=0.7.0). Source metadata matches **7,413 achievements across115 games**, all676 guides, titles/descriptions, sources/tags/missability and category/DLC/sort assignments. Canonical PC/NAS digest:

`D87AE22F2F260E3BDAC337BFE9FC7B3EA2C461CDFBC9310B213C7467600EAA61`

All544 prior file/table checks matched before sync; tracker manual values/notes still matched afterward. Full NAS backup:`backups/pre-0.7.0-20261026T001823Z.tar`; protected PC copy:`C:\Codex\Companion Backups\pre-0.7.0-20261026T001823Z\`. SHA256:`2F5D2E2796A6588649AB10E5B09719E4E215BAB48D7F1DC9E5EE9EEA2C2C086A`. Native guide baseline:`C:\Codex\PixelVaultCompanionConnectorData\backups\pre-achievements-20261026T000214Z\native-guides.sqlite`; SHA256:`61222CF61D29C67B0BA360A250F6D2824EB875C7E28B14AD1B8547EEBCE005FA`. Artifact stamps are UTC; deployment was October25 in the user's time zone.

Checks passed:61 full server cases plus an added badge case and focused long-title/stale-cache/concurrency regressions,6 connector tests and17 JS cases. Real web→desktop→web editing was tested only in an isolated copy, retaining guide/progress. Browser390/1080px checks covered filters, Witcher DLC hierarchy, badges, guides, linked trackers and the retained real NAS login. The live HTTP group dialog opened and was cancelled; no test assignment reached real Native data. Physical phone testing remains user-side.

Preserve newer NAS/desktop edits during rollback; do not blindly restore an older database. See [NAS recovery](COMPANION_NAS.md) and [PC operation](COMPANION_CONNECTOR.md).
