# Memory-chip pickup and restoration control

Verification: 51 focused cases, the actual packaged-worker automatic turn-in control (1/1), and all 1,180 full-suite cases pass. The broad suite was rerun with collection parallelism disabled after two SQLite cleanup locks and a browsing timeout; all 50 affected-class cases also pass targeted rerun, with no relaxed assertions. Reports: chip-pre-final-focused-final.trx, chip-pre-final-packaged-auto.trx, chip-pre-final-existing-rerun.trx, chip-pre-final-full-serial.trx.

## October 24: chip #8 pre-final state and reader correction

The user's latest confirmed turn-in supplied a second control. All four copied saves pass account, decryption and decompressed MD5 validation. Their prefix, all 190 resource keys, sentinels and sequential indices still match. Chip #8 / HashId 181681264 is the only collector-table record changing across `quicksave13 → autosave22 → manualsave11`: **25 (0x19) → 11 (0x0B) → 27 (0x1B)**. The user reported that a chip may have been carried in the earlier save, then confirmed terminal submission and a new save. This establishes 0x0B as a pre-final observation for this pair; it does not prove that it always means carrying rather than another unfinished terminal stage.

The former reader rejected 0x0B as an unknown state. Simply adding it to the allowed values while retaining the old 0x02 completion mask would have awarded premature restoration credit. Reader v2 accepts the corroborated states **0, 8, 9, 11, 25, 27** and completes chips only at the validated final state **0x1B**. Earlier interpretation of 0x02 as independently sufficient is superseded by this control. Unknown states still fail without observations; structurally intact tables now identify the unvalidated value/record in the error, and unsupported flags cannot hide an ambiguous second table.

The before/pre-final snapshots must remain **23/56** restored; the confirmed after snapshot is **24/56**. Only chip #8 changes completion. Source adapter ID/version, binding, automatic SaveChanges opt-in, account scoping, notes/manual overrides and provider achievement state are retained. A game running during a stable copy is not the cause of this failure.

Private read-only evidence: `C:/GameDataExtractor/Backup Data/Death Stranding Director's Cut/Reference/ChipReaderFailure-20261025-001848561` (`diagnostic.json`, encrypted/private decoded copies, `turn-in-table-differences.json`, `pending-review.json`). The directory name uses UTC; this investigation occurred October 24 locally. Final packaged-worker, automatic-refresh and suite results are recorded in the handoff.


## October 24: coordinate descriptions

All 56 chip records now begin their existing `Detail` with `Map coordinates (X, Y): … - … Region.` Coordinates come from the installed Director's Cut `DSCollectorsItemLocator.Orientation.Position` X/Y values, joined through `CollectorsItem.uuid` to the existing resource UUID and display number. Values are rounded to one decimal place; Z is elevation and is not substituted for map Y. Region mapping is Area01/Eastern (9), Area02/Central (44), Area04/Western (3).

Online research found [Kamigouryaku's coordinate list](https://www.kamikouryaku.com/death_stranding/?%E3%83%A1%E3%83%A2%E3%83%AA%E3%83%BC%E3%83%81%E3%83%83%E3%83%97) in indexed search excerpts and the [GameSpot location/region guide](https://www.gamespot.com/articles/death-stranding-memory-chip-location-guide-how-to/1100-6479594/). Public examples corroborate direct X/Y map coordinates and regions but differ for some entries: the indexed list gives chip 4 X=119, chip 46 X=126 and chip 47 (2010,1076), while this installation's locators give (19.3,-235.9), (25.0,-968.7) and (2617.5,1675.7), respectively. The tracker uses the installed locator positions consistently rather than mixing in the differing guide coordinates. No online walkthrough prose is copied. Full source locators and the number/UUID/description comparison are private at `Reference\ChipCoordinates-20261024` under the game's backup directory.

Only the 56 `Detail` strings change. All existing descriptions remain after the coordinate prefix; every ID, hash, category, milestone, target and other catalogue is unchanged. Built-in definitions load from the updated embedded catalogue, with no personal database migration or progress/source/automation writes. Coordinates describe pickup locations and never imply restoration credit.

All 29 existing world-catalogue/chip-reader cases pass (`memory-chip-coordinates.trx`). The three achievement-entry/render cases also pass (`memory-chip-coordinate-layout.trx`), and the selected chip's coordinate paragraph was visually inspected in the actual tracker. Updated review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`; published version remains 0.077.000. Phase E/F scope is unchanged.

## Final setup and verification

The user's tracker is now connected to the native save source with **After save files change** enabled/unpaused. A consistent backup (`before-native-chip-refresh-20261024-151554776.sqlite`) preceded the 56-observation reconciliation: **22/56 restored**. All authored notes/manual values, other tracker progress and source/automatic configuration were verified unchanged; all 69 original checkpoint hashes are unchanged. Private-copy setup also passed. A subsequent targeted automatic-mode production read with the updated worker succeeds unchanged (0 observations applied), verifies all 56 states, clears the older app's unsupported-reader failure status and retains the exact source/automatic settings. Its consistent backup is before-chip-automatic-check-20261024-152252340.sqlite. No scheduler is run across unrelated configured trackers. The older running apps retry the new connection using their older reader: close those windows and open the updated review executable for ongoing automatic monitoring.

Verification: 57 focused cases and the 18-case private packaged-worker run pass. Full native suite: 1,146 passed plus one unrelated AchievementPinTests temporary SQLite cleanup lock; the affected case passes on targeted rerun (1/1). Reports: chip-reader-focused.trx, chip-reader-private-controls.trx, chip-reader-full.trx, chip-reader-cleanup-rerun.trx. A final packaged-worker control check also passes (1/1, chip-reader-final-worker.trx).

New code is confined to the chip reader/reference and registration in the existing native source, worker and source picker. No executable release/version bump, game-save write, running-app shutdown or unrelated tracker opt-in occurred. Phase E remains In Progress for facilities and broader compatibility/OS QA; Phase F scope is unchanged. Detailed implementation and earlier control evidence follow.

## October 24: native restoration reader implemented

The supported Steam DC collector table is now validated against **all 190 static installed resource HashIds**: 56 `DSCollectorsItemResource` objects in `ds/mission/system/mchips/mchips_resource_area01.core` plus 134 in `ds/catalogue/customize/customize_collection.core`. These are reference identities only, with no account or progress data in the embedded catalogue. They exactly match ascending unsigned-key order in all 58 private decoded copies.

Supported serialization: fixed prefix `81 BE 00 81 60 09 01`, then 190 records of 12 bytes: uint32 HashId, uint32 `FFFFFFFF` sentinel, uint16 sequential index 0–189, uint16 flags. The compact entry count is 190; the next field's meaning remains unknown and is treated only as part of the supported prefix, not a claimed byte length. Every identity, sentinel, index and bounds check must pass; more than one valid table, unsupported flag combinations, reordered/missing/duplicate/unknown keys, changed prefix and truncation fail without observations. Only currently corroborated flag values 0, 8, 9, 11, 25 and 27 are accepted. Future variants need explicit validation.

Current reader v2 completes restoration only at validated state `0x1B`. State `0x0B` and other pickup/pre-final/default states never create restoration credit; the earlier bit-`0x02` interpretation is superseded by the second control above. Availability stays Unknown. Exact hash-to-existing-item UUID mapping supplies all 56 binary observations, including explicit zero only after full table validation. Duplicate raw hash occurrences outside that table are ignored. Latest controlled manualsave5 gives **22/56 restored**; before/pickup/intermediate stages give 21/56.

`DeathStrandingChipReader` is routed through the existing isolated worker and legacy source ID/version, generic source form, capability indicators, reconciliation/history and automatic scheduler. The tracker retains its stable IDs, single Restored column and game-independent form. SaveChanges setup is user-authorized; defaults for other trackers/accounts remain unchanged. Manual counts, notes, rollback review, account isolation, stable private copies and read-only game-save access remain enforced.

Verification: 57 focused reader/source/dashboard/catalogue cases pass, including 18 chip cases. A separate actual packaged-worker run passes all 18 chip cases with the private review manifest: all 58 copied payloads, before/pickup/intermediate/restored automatic save-change transitions, 21→22 restored, no-change history suppression, authored notes, manual-conflict review and other-account isolation. A consistent private copy of the actual tracking database also connects, reconciles all 56 observations (22 restored) and saves the opt-in with other tracker configuration and authored data preserved. Full-suite and live setup results are recorded in the final setup section above.

Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`; published version remains 0.077.000. Reopen this updated build for ongoing monitoring. The running older application is retained. Automation runs while PixelVault is open. Facility level/cap/catalogue and broader compatibility/OS QA remain Phase E work. Earlier sections below describe the research control before implementation.

October 24, 2026. The user made manual saves before and after picking up the fifth chip from the end, between Mountain Knot City and the Roboticist, then restored it at a terminal and made another manual save. Resource/hash matching identifies **#52 — Nicolas Winding Refn The Wicked Die Young**, resource UUID `af6d917f-2d09-064b-a4a4-2809694ea0d6`, HashId `1202779670`.

## Controlled observations

| Save | User action / stage | Raw flag candidate |
| --- | --- | --- |
| manualsave3 | Before pickup | 8 (`0x08`) |
| manualsave4 | After pickup | 9 (`0x09`) |
| autosave1 | Intermediate terminal autosave | 25 (`0x19`) |
| manualsave5 | After confirmed restoration and final save | 27 (`0x1B`) |

In this original #52 pair, the isolated pickup adds bit 0 (`0x01`) and the final restoration adds bit 1 (`0x02`). The later #8 control above demonstrates that bit 1 alone is not a sufficient completion test. Another bit (`0x10`) changes in the intermediate autosave, before the restoration bit appears; its exact semantics remain unverified. It must not be mistaken for completed restoration. A save after the full results/restoration sequence matters. The other 55 chip candidate flag values are unchanged throughout these transitions.

The research probe requires one local 12-byte-aligned cluster covering each of the 56 extracted chip hashes once, with sentinel `FFFFFFFF` after each hash and packed index/flag candidates. Other occurrences of the same hash can represent world objects: chip 52 has an extra qualifying occurrence before pickup that disappears afterward. A first/unique raw-hash assertion is therefore invalid. The cluster probe is research, not a complete supported-format parser.

The 56 chip candidates span a wider apparent indexed catalogue with unrelated entries. Observed indices reach 188, with a following nonchip record at 189; a nearby compact value is 190. These bounds/header meanings are still candidates. The supplied list's 0–55 values are display-number-minus-one, not verified catalogue indices or save offsets. Chip 52's candidate catalogue index is 55. Validate complete counts, entry ownership, all surrounding records, unknown flag bits, identity coverage and supported layout variants before enabling native refresh or reporting all chips from this field.

## Capture and persistence

Private evidence: `C:\GameDataExtractor\Backup Data\Death Stranding Director's Cut\Analysis\Validation\chip-restoration-20261024T1444584408247Z\restoration-calibration.json`, with the earlier `chip-pickup-20261024T1439409573038Z` capture. All eight turn-in-capture accounts, decompressed checksums and copied-save hashes pass; the manualsave3/4 encrypted copies match the earlier pickup capture byte-for-byte. Twelve earlier copied slots also remain preserved. Source game saves are never written.

One user-confirmed positive restoration observation for #52 was imported through the production tracking store after consistent SQLite backup (`before-single-observation-20261024-144902702.sqlite`). It applies once; a duplicate import applies zero. The reported completion is 1, with no manual override. All other item-state rows, authored notes/manual values and source/automatic settings remain unchanged. The import retains normal history/undo. Other chip candidates were not bulk imported.

Native chip refresh/automation remains disabled pending a complete parser and compatibility checks. The first independent pickup/restoration control is now obtained; it is no longer an outstanding user example. Existing construction, upgrade and premium-order native readers remain separate and unchanged. No application code/build/version changed; implementation baseline remains the earlier 1,129-pass native suite. This data/research pass checked captures, identity/hash/checksum, isolated field changes, import preservation and duplicate suppression instead of rerunning unrelated application tests.

Private helpers: `decode_research_checkpoints.py` separates capture decoding from semantic probes; `probe_chip_records.py` finds the aligned identity cluster without treating duplicate raw matches as failure or progress. Both remain outside the application and source releases.
