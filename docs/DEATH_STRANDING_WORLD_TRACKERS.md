# Death Stranding structures, upgrades and memory chips

October 24 native chip addition: the complete 190-entry collector catalogue matches installed chip/customization resources in all 58 copied saves. The bounded parser distinguishes pickup, intermediate terminal state and final restoration. Current controlled save gives 22/56 restored; the native source and SaveChanges automation reuse the existing generic implementation. See [Chip reader evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md). Earlier dated validation counts and unsupported-reader statements below are historical.

ACHG-004 Phase E is partly implemented in App Testing. Construction, maximum upgrades and memory-chip restoration support native save refresh. All are available from the game, Hub and the corresponding achievement's **Track progress…** entry, using the shared account-scoped notes, optional achievement links and transactional undo.

October 24 chip control: the before-pickup, after-pickup and after-terminal saves isolate chip #52, with candidate flags 8→9→27 (intermediate autosave 25). Pickup bit 0 and final restoration bit 1 are distinct; other 55 candidates are unchanged. One confirmed #52 restoration observation is imported after backup, with duplicate suppression and unrelated/authored state retained. The user control is obtained; full catalogue/header/flag/layout validation remains before native refresh. No application code/build changed. [Chip evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md). Earlier dated figures below are historical.

| Tracker | Steam achievement | Items | Check off when |
| --- | --- | --- | --- |
| Structure construction | Master Builder `023` | 13 | You complete/place that type in the world |
| Structure upgrades | Trail-Blazer `057` | 10 | You complete a maximum-level upgrade for that type |
| Memory chips | Fount of Knowledge `035` | 56 | You restore the chip at a terminal after picking it up |

Definitions contain no personal progress. Unobserved entries start unknown in storage (displayed Not Complete). The latest October 22 account-matched save reconciliation supplies 44/108 fabrication, 10/13 construction, 1/10 maximum upgrades and 41 LLL order observations; LL and chip restoration remain unverified. Manual overrides and notes were preserved after consistent SQLite backups. Construction and upgrades are connected to the same native save source as fabrication, with automatic modes unchanged/default off. Chips and premium-order native refresh remain unavailable. The newer manualsave20→21 pair adds LLL order #480 but no structure/chip candidate transition; private evidence is under `Analysis/Validation/plan-continuation-20261023T0352419141750Z`. Earlier validation runs below remain dated baselines.

## Native construction and upgrade reader

`DeathStrandingWorldReader` validates a complete persistent boolean map: a uint32 record count followed by unique GUID/boolean pairs (17 bytes each), ending at two empty typed-map count markers. It requires a unique known anchor, bounded record/count limits, boolean values, unique nonempty IDs and at least 15 of the 26 bundled trophy-fact identities. The current supported variant rejects nonempty following maps, ambiguous/truncated tables and insufficient known coverage. It uses no absolute save offset. All 24 earlier controlled and current checkpoints match complete boundaries, with 2,184–2,210 records. Chiral Bridge absence→true and Postbox false→true transitions remain distinct; current 10/13 and 1/10 totals agree with cached Steam counters.

The bundled `DeathStrandingFacts.json` contains only extracted reference UUIDs/names and persistent/default metadata. All 26 defaults are false. Missing targets are interpreted only after the entire recognized map is validated; sample/debug and road facts never award bundled targets. These are historical personal milestone flags, not current object level, ownership or condition.

The existing isolated worker accepts a tracker ID (defaulting to fabrication for backward-compatible requests). It routes construction and maximum-upgrade definitions to the new reader and rejects chips/orders. Source validation requires the exact bundled item identity set; returned coverage must match the selected definition. Stable copying, account/MD5 verification, DLL hashing, timeouts/cancellation, reconciliation, rollback barriers, history and opt-in automatic scheduling use the existing implementation. The adapter retains its legacy `death-stranding-dc.fabrication` ID/version 1 to preserve existing bindings; the UI calls it the Director's Cut save source.

Current private evidence: `Analysis/Validation/all-trackers-refresh-20261022T0348391227349Z`, including stable newest captures and `fact-table-validation.json`. The latest manualsave19 is October 21 10:28:20 PM America/Chicago. Consistent pre-sync backup: `before-current-trackers-20261022-035252122.sqlite`; pre-native-source backup: `before-native-world-sources-20261022-035951403.sqlite`. Normal store imports applied the verified observations; packaged-worker refresh then returned unchanged for all three native trackers, recording successful source reads without duplicate progress. No game-owned save was written.

Verification: 12 new reader cases cover separate placement/upgrade/defaults, malformed counts/values/keys/markers, missing/duplicate anchors, insufficient coverage, excluded debug/road facts and real copied-save worker/reconciliation/manual-preservation checks. All 134 focused tracker cases pass. Full native suite: 1,031 passed, the two known shader failures (1,033 total), `tracking-current-world-full.trx`. Actual checks use all 24 copied checkpoints. Review executable remains `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`, published version 0.077.000 unchanged.

## Catalogue evidence

`Services/Achievements/TrackingData/DeathStrandingWorldTrackers.json` is embedded as `PixelVault.Tracking.DeathStrandingWorld.json`. Structure item IDs are the installed game's persistent trophy-fact UUIDs, with distinct construction and maximum-upgrade identities. Chip IDs are resource UUIDs with number, hash and restoration milestone metadata. No account, save path or personal completion is embedded. Names do not determine saved identity.

Construction covers the ten PCC types (postbox, watchtower, generator, bridge, timefall shelter, safe house, zip-line, jump ramp, cargo catapult and chiral bridge) plus sign, ladder and climbing anchor. This manual set matches the cached Director's Cut provider target of 13 and extracted trophy facts after excluding the sample and road facts. The exact compiled achievement predicate is not yet independently decoded; the road fact remains research evidence, not an automatic completion field. Older/base-game guides sometimes include roads and must not be treated as Director's Cut parser specifications.

Upgrade targets are binary historical milestones, separate from current world-structure levels, deterioration, removal or ownership. Raw resource `MaxLevel` values are not used as tracker counts; some include construction-stage encoding. The displayed player milestone is level 3. Material details come from extracted upgrade requirements. They align with the [Director's Cut upgrade walkthrough](https://www.playstationtrophies.org/forum/topic/329763-death-stranding-ps4-platinum-to-ps5-death-stranding-directors-cut-quick-platinum-full-walkthrough/), whose three new structures use two upgrades ending at level 3. This is guidance, not save-flag evidence.

The 56 chip entries retain extracted display numbers/names, numbered search labels and ten-chip category groups. The single replacement character in chip 48's extracted album title is normalized to an ellipsis for display; its resource UUID remains unchanged. Pickup details can be noted, but pickup alone must not complete the restoration checklist. Native refresh now reports terminal restoration only; separate discovery/pickup columns remain unimplemented.

## Controlled save evidence — October 21, 2026

The user stayed in-game. Stable copies were captured with before/after length/time and SHA-256 checks; only copied inputs were decrypted/decompressed. Every checkpoint matched the connected Steam account and its decompressed MD5. No game-owned file or tracker item state was written.

- **Repeat zip-line construction:** manual saves at 9:16:40 PM and 9:17:57 PM America/Chicago. The candidate `ldfp_builded_zip_line` record exists with following byte `1` in both. Its maximum-upgrade candidate remains `0`. None of the 26 structure-fact candidate values or 56 chip candidate record tails changes. This is a useful negative/control case, not a first-time construction transition.
- **First reported Chiral Bridge construction:** `manualsave12` at 9:24:01 PM versus `manualsave13` at 9:24:44 PM America/Chicago. The named trophy fact `ldfp_builded_crl_bridge` (`138099b2-ea49-fc4f-b590-e6abd1a26775`) is absent before and appears once with following byte `1` after. `ldfp_lvmax_crl_bridge` remains absent. Other structure trophy candidates and all chip candidate record tails remain unchanged. Earlier `manualsave11` is also negative. This provides a controlled positive construction example consistent with the fact definition's default false.

These findings validate a candidate transition, not a complete persistent-fact parser. The surrounding serialization, record bounds/counts, absence/default semantics and upgrade/chip transitions still need validation. An exploratory older directory-marker parser did not recognize this layout and is explicitly marked unrecognized in its private output. Raw UUID/hash searches are not shipped as a production decoder.

### Postbox maximum-upgrade positive control

The user confirmed upgrading a postbox to level 3 and saving. Eight stable checkpoints were captured under `Analysis/Validation/postbox-level3-20261022T0312291493248Z`. Every original/copied SHA-256 pair, account and decompressed checksum was verified. In `manualsave17` at **10:00:17 PM October 21 America/Chicago**, `ldfp_lvmax_post` (UUID `a3f2c2f0-63c9-2544-958a-f719e59b78f3`) occurs once with following byte **0**. In `manualsave18` at **10:00:57 PM**, the same named fact has following byte **1**. The two preceding captured saves also retain 0; autosave6 and both subsequent quicksaves retain 1. Only this value changes among the 26 structure trophy-fact candidates in the controlled pair; all 56 chip candidate tails are unchanged.

This corroborates a first maximum-upgrade milestone separately from construction. The resource defines this fact as persistent with default false. Full serialized fact-table bounds/default and ownership/history behavior still need validation; native world refresh remains disabled. Machine-readable and readable evidence are `postbox-upgrade-calibration.json` and `.md` in that private capture folder.

The user-confirmed milestone plus controlled pair supplied a one-item private account-matched observation dated from manualsave18. Postbox is now completed in the Trail-Blazer tracker; other world entries remain unknown unless manually recorded. The normal importer created reversible history and digest suppression after a consistent SQLite backup (`before-postbox-level3-20261022-031434186.sqlite`). Existing unrelated tracker item-state rows were verified unchanged and no manual override was present. No native source or automation was enabled. This was data/research work; the existing review executable is unchanged and no additional app tests/build were needed.

Private evidence is organized under `C:\GameDataExtractor\Backup Data\Death Stranding Director's Cut\Analysis\Validation\zipline-20261022T021921Z` and `chiral-bridge-20261022T022547Z`. UTC run names cross midnight; the gameplay occurred October 21 Central time. Each holds source manifests, unchanged encrypted copies, checksum-validated private payloads and candidate probes/comparisons. `C:\GameDataExtractor\Tools\CatalogueExtractor\compare_world_saves.py` is a private research helper and is not an application dependency or release asset.

## Remaining Phase E work

1. Persistent boolean-table parsing and native construction/maximum-upgrade refresh are implemented. Continue compatibility checks for other map variants and broader gameplay removal/ownership/rollback cases.
2. Native chip restoration is implemented after the #52 control and full 190-key catalogue validation across 58 copied saves. Continue compatibility checks; unsupported serialization/flag combinations stop without resetting progress. Shared unlock facts/raw hash presence never imply restoration. Pickup remains distinct.
3. Retain the shared source/reconciliation safeguards; only enable additional fields after independent validation. Native LL/LLL order reading is implemented separately (see DEATH_STRANDING_ORDER_RESEARCH.md).
4. Continue facilities/connections and roads as separate investigations; facility level/cap/catalogue serialization remains research.

Phase E remains in progress; Phase F spreadsheet/import work progresses independently.

## App implementation verification baseline

11 added catalogue/persistence/source-isolation/UI cases pass. All 117 focused tracker tests pass. The full native suite reports 1,014 passed and only the same two known unrelated TradingCardFoil shader failures (1,016 total): `tests/PixelVault.Native.Tests/TestResults/tracking-world-full.trx`. All three tracker layouts were rendered and inspected.

Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`; published version remains 0.077.000. The original catalogue rollout seeded no progress. Later research imports supplied 40 premium-order LLL observations and one Postbox maximum-upgrade observation; no new automation was enabled. The latest implementation suite remains 122 focused passes and 1,019 full-suite passes plus two known shader failures; this Postbox-only research/data update did not change app code.
