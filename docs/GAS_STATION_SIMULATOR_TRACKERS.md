# Gas Station Simulator automatic trackers

November 16, 2026. Steam app **1149620**. Installed as a separate read-only helper, without replacing Native or Companion releases.

## Installed catalogue

48 custom achievement-linked trackers, 50 fields:

- 31 cumulative or level/streak counters: shop customers, tips, fuel customers, decorations, lockpicking, painting, trash, floor stains, repairs, popularity, washed cars, boat refueling, volcanic-eruption-free days and accident-free days.
- 16 building-level goals: station, workshop, warehouse and car wash, levels 2 through 5.
- Nothing But Net: three separate coconut spots, each requiring a successful hit. Repeated hits at one spot do not complete the other spots.

First-time actions, single-shot fuel accuracy, single trash throws, individual race runs and other one-off events are excluded. One-row cumulative counters remain included. Counter trackers aggregate as one dashboard item; the coconut checklist has three distinct items. Exact Steam API IDs provide achievement links. Tracker observations never change Steam achievement state or grant points.

The installed Achievement_DT and Steam schema were inspected directly. Looking Smooth uses an actual game threshold of **20 painted objects**, although Steam's description says 25. The catalogue states this discrepancy. `scripts/GasStationSimulator/MappingEvidence.json` records the mappings and input hashes. Raw extracted game assets and personal saves remain outside source history.

## Reader and authority

`UnrealSave` is a bounded UE4 tagged-property parser. It accepts GVAS format 2, UE4 package 522 / engine 4.27, custom-version format 3, exact researched save classes, and gameplay save version 17. It checks property sizes, string encodings, duplicate keys, nesting/collection limits, scalar types and finite nonnegative values. Unknown payload structures remain opaque. No game code is loaded or executed.

`GasStationSimulatorProgressReader` requires an explicit Windows SID and Steam account. It verifies `steam_autocloud.vdf` and the statistics save's `PlayerId`, including the current Steam/Epic composite suffix. There must be exactly one matching statistics file, and its complete filename identity must match its contents. Profile-zero and ambiguous files are rejected. Achievement confirmation comes from the correspondingly named AchievementProfile file.

Building progress comes from the latest modified gameplay save explicitly assigned to that same composite identity by SaveSlots. A filename prefix alone is insufficient. Slot names must be safe leaf names. Foreign-account slots are excluded. Switching to an older/lower-progress slot can require normal rollback review. A missing account-owned slot leaves building progress unknown.

All files are opened read-only, parsed from bounded private memory copies, and reread to verify byte/timestamp stability before emitting any observations. Missing/invalid files, identity mismatch, unsupported layouts or interrupted writes preserve the previous output. Missing statistic/building fields remain **unknown**, never guessed as zero. Fractional counts are floored and raw cumulative amounts can exceed their target. A positively saved achievement unlock supplies at least its goal, so a completed streak does not appear unfinished after it resets. This is game-saved confirmation, not inferred Steam credit.

## Automatic installation

Maintained source: `scripts/PixelVault.GasStationSimulator`, tests in `scripts/PixelVault.GasStationSimulator.Tests`; runner/install scripts in `scripts/GasStationSimulator`.

Package: `C:\Codex\Game Readers\Gas Station Simulator\1.0.0`.
Private configuration and output: `C:\Codex\PixelVaultData\achievement-tracking\gas-station-simulator`.
Scheduled task: **PixelVault Gas Station Simulator Progress**, every minute and at user logon, with a hidden launcher under the configured user's interactive identity. Its bounded `reader-status.json` records success or failure. The existing Companion service consumes the JSON observations using its normal SaveChanges reconciliation.

The helper atomically replaces only changed per-tracker JSON files. Known decreases retain an older/equal observation timestamp, requiring normal rollback review. Setup preflights definitions and sources, backs up the SQLite store, uses the standard source lease / preview / apply / history APIs, preserves notes/manual overrides, and enables SaveChanges only for the new bindings. Repeated setup preserves compatible existing definitions, source IDs and automation settings. Receipts support recovery after interruption. Pausing the scheduled helper or an individual tracker stops that part of the update pipeline.

Commands:

```powershell
dotnet test scripts/PixelVault.GasStationSimulator.Tests -c Release
# Packaged executable:
PixelVault.GasStationSimulator.exe export <absolute-config.json>
PixelVault.GasStationSimulator.exe setup <absolute-config.json> <absolute-receipt.json>
PixelVault.GasStationSimulator.exe inspect <absolute-copy.sav>
```

`inspect` is diagnostic and can contain personal save information. Do not publish its output or personal configuration.

## Validation and limitations

26 tests cover typed parsing, standard/composite accounts, filename/content mismatch, malformed/truncated/versioned saves, duplicate/invalid counters, distinct coconut spots, saved streak completion, slot ownership/path bounds, unknown building fields, catalogue links, stable exports, rollback review and setup preservation/idempotency. The packaged reader was also tested on the user's real fresh save and installed first into a private SQLite rehearsal.

Initial real save: **38 known fields and 12 unknown building fields**. The unknown workshop/warehouse/car-wash fields were omitted from the fresh gameplay save. They will be read when explicitly saved or confirmed complete by the corresponding saved achievement. These are not errors or zero-progress estimates.

Employee-skill goals (including Jack of all Trades), caravan counts, ocean-landmark/all-upgrade collections and the cinema-level goal are **not installed yet**: their partial-progress mappings have not been validated. They are multi-step candidates, not excluded as one-action goals. The task does not claim complete coverage of every multi-step achievement. Further game-save samples with employees and those DLC systems are needed to validate them. Broad before/after gameplay verification remains pending; parsing the fresh save is verified.

Installation backup, rehearsal receipts, preservation comparisons and automatic-check results: `C:\Codex\artifacts\gas-station-setup-20261116`.

Final installation verification: all **48/48** bindings completed their first Companion automatic check; **0 failures**, **0 pending reviews**. The scheduled helper completed repeated successful exports with unchanged values. Unrelated definitions, bindings, automation settings, authored notes and manual progress matched their pre-installation backup. See the verification receipt above.
