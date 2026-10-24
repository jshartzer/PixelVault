# Death Stranding tracker expansion research

Read-only investigation, October 21, 2026. Source: the installed PC Director's Cut resources and the previously copied, decoded checkpoint. No game files, live saves or tracker progress were changed by this research.

Follow-up: [World trackers and controlled-save evidence](DEATH_STRANDING_WORLD_TRACKERS.md). Manual 13-type construction, 10-type maximum-upgrade and 56-chip restoration catalogues are now in App Testing. User-controlled repeat zip-line and first Chiral Bridge construction save pairs have been preserved and compared. The Chiral Bridge trophy fact changes from absent to a record followed by `1`; this is a positive candidate transition, while full table/absence semantics and upgrade/chip progress remain unverified. Phase E remains in progress, with native refresh disabled for these three trackers.

## Confirmed catalogue findings

- `ds/mission/system/mchips/mchips_resource_area01.core` contains exactly 56 `DSCollectorsItemResource` records and 56 `DSCollectorsItemLocator` records. Display numbers cover 1–56 without duplicates; every chip has one matching locator reference. The file name does not mean these 56 records are all physically in one player-visible region.
- Each chip has a stable resource UUID, `DisplayNo`, `HashId`, `NameHash`, localized name/detail references, an unlock fact and a locator reference. English names resolve through `localized/sentences/ds_ui/ds_datalibrary/simpletext.core`. All 56 names resolve. Examples include #32 Honda NSR250R and #45 dawn/SILENT POETS.
- `ds/mission/system/ldf_trophy.core` contains 26 persistent Boolean facts: 15 `ldfp_builded_*` and 11 `ldfp_lvmax_*`. Both groups include `sample` records. They are a research inventory, not a finalized achievement checklist.
- The maximum-level facts cover post, charger, bridge, watch tower, rain shelter, safe house, zip line, jump ramp, catapult and chiral bridge, plus the sample fact. Build facts additionally include road, field rope, signboard and ladder.
- The trophy resource also includes `ldfn_enum_build_allmax_check` with entries `NoChecked`, `NotAllMax` and `AllMax`. This looks relevant to upgrade checking but does not establish where or how its current value is serialized.
- Construction resources expose category, construction-name references, material requirements for each upgrade step and `MaxLevel`. For a postbox the raw config has MaxLevel 3, 200 metal for the first upgrade and 600 metal for the second. Other configurations expose raw MaxLevel 4. Determine how construction-stage encoding maps to visible levels before setting targets.
- Existing fabrication remains a separate validated 108-entry Homo Faber tracker. The fabrication table does not establish chip or structure progress.

## Save investigation

The existing decoded checkpoint payload was scanned for known identifiers:

- All 56 chip hashes occur in the payload (56 total occurrences). No chip resource UUIDs occur.
- Sixteen structure-fact UUID occurrences were found: nine build facts and seven maximum-level facts. The cached Steam Master Builder counter independently reports 9/13, matching the number of build UUIDs found. This is encouraging corroboration, but presence alone does not establish the serialization semantics. Fact names do not appear as readable strings.
- These matches establish candidate locations only. The byte fields, record boundaries, bit packing and completion semantics have **not** been decoded or validated. No completion list was generated from these matches.

Use the existing checksum-validated decompression pipeline when analyzing the candidate regions. An identifier existing in a save can reflect a definition, spawned object, unlock or history; presence must not be equated with completion.

## Recommended trackers

1. **Structure construction:** historical credit for building each required type. Verify the Director's Cut Master Builder predicates before deciding which build facts are required; do not include the sample entry.
2. **Structure upgrades:** historical maximum-upgrade credit by required structure type for Trail-Blazer. Keep this separate from the current level of any one world structure. World objects can be removed, deteriorate or belong to another player.
3. **Memory chips:** 56 stable numbered entries for Fount of Knowledge. Distinguish revealed/spawned, picked up and restored/submitted where the save supports them; only the verified achievement-relevant state should complete the linked checklist.
4. **Facilities/connection levels:** a later candidate using facility identities and connection milestones; not decoded in this pass.
5. **Orders, delivery ranks and roads:** later candidates with separate count/instance/history semantics; not decoded in this pass.

Order-rank follow-up requested during gameplay: [standard-order research](DEATH_STRANDING_ORDER_RESEARCH.md). All 540 stable order identities and a candidate fixed-record region are located. The user-confirmed order 331 LLL result changes its rank field at relative offset 72 from 0 to 0x100, with other standard-order rank fields unchanged. The final manual save has 40 orders with that bit, exported as a private research list. An intermediate autosave lacks the final rank, so post-results saves matter. LL's bit-7 mapping lacks a positive control, and full table/grade semantics remain unvalidated. No production source or personal tracker progress has been changed.

The connected account's cached Steam definitions confirm these identities for game 1850570: Master Builder `023`, Trail-Blazer `057`, Fount of Knowledge `035`, Homo Faber `062`. Master Builder's description explicitly includes signs, ladders and climbing anchors, and its cached target is 13. This is consistent with excluding both the sample and road facts from the 15 build facts, but verify the game's achievement predicate before shipping that inference. Manual templates can use the catalogue, with unknown initial progress; automated refresh must wait for validated save mappings.

## Validation needed

Capture stable checkpoints immediately before and after: one construction, one final upgrade, one chip pickup and the same chip's terminal restoration. Compare candidate records against an unrelated action/save, and use in-game per-category totals plus known positive/negative examples. Check historical completion after removing a structure and progress after loading an earlier save. Test another account and a different save/profile.

For every parser publish: record source game/edition/build, stable item IDs, save format version, parser version, checksum outcome, account identity and field-level confidence. Preserve unknown values on unsupported saves.

## Local evidence

Private research folder: `C:\GameDataExtractor\Backup Data\Death Stranding Director's Cut\Reference\TrackerResearch-2026-10-21`.

- `resource-paths.txt` and `additional-resource-paths.txt`: resource inventories.
- `Raw/` and `Parsed/`: read-only extracted resources, kept outside source control.
- `memory-chip-catalogue.json`: resolved names, numbers, UUIDs, hash IDs and locator validation.
- `structure-trophy-facts.json`: all 26 facts with IDs and defaults. Defaults are definition values, not this player's progress.
- `structure-configs.json`: selected raw configuration fields and upgrade materials.
- `save-probe.json`: identifier occurrence counts, explicitly marked `InterpretedProgress: false`.
- `additional-extraction.log` and `errors.json`: extraction diagnostics; the 17 selected resource reads completed without errors across two runs. The summary file describes the second run only.

Extraction used the already installed Decima Workshop v0.1.27 model library and a focused `TrackerResearch.java` helper under `C:\GameDataExtractor\Tools\CatalogueExtractor`. A future adapter must not depend on these machine-specific research paths.

## External source boundaries

The [save resigner's maintained repository](https://github.com/mi5hmash/DeathStrandingSaveDataResigner) documents decryption, encryption and re-signing for PC saves, including Director's Cut. It provides container access, not validated tracker fields. This research did not find an authoritative public field map for chip restoration or structure-upgrade credit; those mappings still need local validation.

The implementation sequence, reconciliation rules and release gates are in [PV-PLN-ACHG-004](plans/PV-PLN-ACHG-004-game-progress-trackers.md).
