# Can 'Em All save trackers

November 16, 2026. Steam app 5133900. Standalone reader 1.0.0 supplies four custom achievement-linked trackers through PixelVault's existing JSON-source refresh pipeline.

| Tracker | Saved progress | Target |
| --- | --- | --- |
| Feeding the Future | Lifetime cans sold | 100 |
| Ending World Hunger | Lifetime cans sold | 10,000 |
| World's Greatest Humanitarian | Lifetime cans sold | 100,000 |
| The Perfect Employee | Individually purchased permanent upgrades | 21 |

The three counters aggregate as one dashboard item each. The permanent-upgrade checklist has one named item per purchase. One-action achievements are omitted; this setup does not claim partial progress for explosive multikills or hidden event achievements.

## Mapping evidence

The installed full game, build 26596503 / Unity 6000.2.15f1, saves plain JSON to `%USERPROFILE%\AppData\LocalLow\LuisDev\Can 'Em All\save.sav`. GameAssembly SHA-256: `68554EC79696FDE66961F0360A800ABFDFE1AE2DCB76D052B344AE235965A470`. Metadata SHA-256: `A6CA56FAC5D9879C333E8FB8FB37857B83597640D285A4FA8C4656F8DCE214AB`.

Static inspection of `RewardMachine.RewardCan` (RVA 0x410050, progress call at 0x4103AF) establishes that selling a can increments both run `totalCans` and `lifetimeStats.totalCans`, then passes the lifetime value to `SteamManager.UnlockProgress` (0x43F380). Its thresholds are 100, 10,000 and 100,000, corresponding to Steam achievement IDs FeedingTheFuture, EndingWorldHunger and WorldsGreatestHumanitarian. The reader uses the lifetime field, never current-run totals or wallet money.

`SaveData.unlockedPermanentUpgrades` is a list of **SkillId**, not PermanentUpgradeId. The correct saved identities are **232–252**, not 0–20. The installed level0 PermanentUpgradeManager has 21 nodes; their Skill references map to Starting Cash I–III, Brute Force I–IV, Profit Boost I–IV, Grinder Speed, Never Skip Legs, Starting Shockwave, Luxury Cans, Lucky Hits, Shiny Shipment, Damaged Delivery, First Blood, Death Touch and Cold Storage. `HasAllPermanentUpgrades` (0x405220) checks every node's purchased Skill flag. Catalogue names and point prices come from these nodes. Personal saves and game binaries/extracted assets remain outside source history and the reader package.

## Reader behavior

`scripts/PixelVault.CanEmAll` contains the bounded JSON parser, embedded catalogue, export/setup bridge and CLI. `scripts/PixelVault.CanEmAll.Tests` contains synthetic tests. Runtime reads only save.sav and steam_autocloud.vdf; it does not load game code or depend on the research tools.

The reader requires an explicit Windows SID and Steam64 account, verifies the current Windows user and AutoCloud accountid, and checks the save's structural signature. The JSON save has no embedded account identity, so account attribution relies on that checked local profile and AutoCloud marker. It uses bounded private memory copies, denies concurrent write sharing while reading, and rereads both inputs after 100 ms to verify bytes and timestamps. Files are never opened for writing. Invalid/truncated JSON, duplicate fields, invalid counters, unknown/duplicate permanent IDs and identity mismatches fail without new observations. Missing lifetime or upgrade fields remain unknown; an explicitly empty upgrade array means zero purchases. Saved 64-bit counters are retained by the parser and saturated at the tracker's int32 storage maximum when projected, without overflowing. All current targets are far below that limit.

The bridge validates every incoming and previous observation before exporting any changes, atomically replaces changed files, and leaves unchanged timestamps alone. A known decrease retains an older/equal timestamp so standard reconciliation requests rollback review. Setup preflights definitions/bindings, creates a SQLite backup, and applies initial progress through the shared source lease, preview, apply and history APIs. Manual counts, notes, unrelated trackers and other accounts are preserved. SaveChanges is enabled only for newly configured sources; repeat setup preserves compatible source identities and existing automation preferences. Receipt files support interrupted setup recovery.

## Runtime

- Package: `C:\Codex\Game Readers\Can Em All\1.0.0`.
- Configuration and exports: `C:\Codex\PixelVaultData\achievement-tracking\can-em-all`.
- Scheduled task: `PixelVault Can 'Em All Progress`, hidden, every minute and at user logon under the configured interactive Windows user.
- Existing Companion service reconciles the exported JSON through its ordinary automatic checks.
- Pausing the task stops new exports; pausing a tracker's automatic setting stops that tracker applying changes.
- Native/Companion application versions, shortcuts and service configuration are unchanged.

```powershell
dotnet test scripts/PixelVault.CanEmAll.Tests -c Release
PixelVault.CanEmAll.exe export <absolute-config.json>
PixelVault.CanEmAll.exe setup <absolute-config.json> <absolute-receipt.json>
```

## Validation

32 tests cover lifetime/run separation, exact upgrade identities and thresholds, malformed/duplicate JSON, unknown fields, integer limits, account/user mismatch, read-only behavior, stable exports, rollback review, setup preservation, idempotency and conflicting source refusal. The packaged reader was tested on the real save and a private backup of the actual tracker database. A malformed private save preserved all four prior export files and their timestamps. The rehearsal preserved all 22 unrelated database tables. Initial real save: 973 lifetime cans sold, zero of 21 permanent upgrades. Controlled before/after gameplay validation remains pending; mappings were verified against installed code and assets.

Private evidence, receipts and preservation checks: `C:\Codex\artifacts\can-em-all-setup-20261116`.

Final live verification: all **4/4** sources completed automatic checks, with **0 failures** and **0 pending reviews**. All 24 saved fields are known. The scheduled task returned success and repeated exports were unchanged. Fourteen unrelated data/settings comparisons passed; the source save hash and timestamp are unchanged. See `verification.json` in the private evidence folder.
