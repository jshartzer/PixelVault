# Death Stranding road-state research

## Live desktop and web verification

Companion **0.35.0** and installed PC connector **0.21.0** are live. A direct health request verified the NAS version; the running connector executable is `C:/Codex/Companion Connector/app-0.21.0/PixelVault.Companion.Connector.exe`. A direct NAS audit after normal connector delivery reports51active,51known,28restored, zero active manual overrides and the save-authoritative source linked. The prior19manual overrides were cleared only for known active road observations, as explicitly authorized. Archived manual state remains unchanged.

Direct comparison with the pre-connection NAS audit confirms matching hashes for notes, protected manual values (other trackers/accounts and archived row), other definitions, achievement links, titles, icons and pairing credentials. Original tracker/item IDs are retained; only41-34was archived. Direct evidence: `tmp/road-live-direct-before.json` and `tmp/road-live-direct-audit.json`. Server regression suite now passes95cases, including the Windows/Linux revision-serialization compatibility guard; connector suite29cases pass. Canonical revision hashing normalizes actual formatting line endings without changing stored Linux source revisions or file paths.

Road auto-refresh is enabled on desktop and web. Open the current Native shortcut to load0.106.000; the independent PC connector updates NAS progress while the Native UI is closed. A separate generic failure from another configured save check was observed after the successful road delivery; it does not negate the audited road observations and was being diagnosed separately. Per-game read outcomes remain independent. No claim is made that every unrelated reader is now supported by this road update.

## Automatic refresh shipped — October28,2026

Native **0.106.000** adds `DeathStrandingRoadReader` and51 metadata-only embedded references (`DeathStrandingRoads.json`). It uses the guarded persistent-fact parser shared with construction/upgrades and the explicit geometry-linked highway facts, counting restored roads regardless of builder. The shared analysis worker now has seven isolated projection outcomes; a failing road format cannot prevent another supported reader from returning its own validated result. The source maps canonical fact observations back to existing custom item IDs by exact route names, allowing parenthetical descriptions, and requires all51 unique active binary route records. Archived unmatched rows remain unknown.

The user explicitly authorized save data to replace incorrect manual checkmarks. Generic per-binding `ReplaceManualProgress` defaults false and is omitted when false, preserving existing serialization/source contracts. Only this road binding is opted in. Known non-stale incoming values clear manual overrides atomically with reported observations/history; notes, unknown values, revision/attempt guards, older observations and undo review remain protected. The source label/review describes the policy.

Live Native Road Building retains tracker/item IDs, title, achievement link and authored history. UC Route41-34 is reversibly archived via catalogue history because the installed data contains routes41-1through41-33, not41-34. Existing notes and all unrelated tracker states/settings/history match pre-change fingerprints. The binding inherits the existing approved Death Stranding account/save/game paths and has SaveChanges enabled/unpaused. Current validated count: **28/51**, including41-20, with zero active manual overrides. The later `manualsave6` also reports28, supporting persistence after the original control. No game saves were edited.

Evidence: `Reference/RoadResearch-20261028/native-auto-setup.json` and consistent `native-before-road-auto-20261029-014935951.sqlite` backup. Tests:90 focused road/world/cache/reconciliation/automation cases, one actual packaged-worker/scheduler control (27→28, wrong manual check cleared, notes/archived unknown preserved), and full Native suite **1,327/1,327 passed**. Renders850/1100px under `App Testing/Achievement Trackers QA/Roads` show Game files connected, Automatic on and Save replaces manual. Test logs: `tmp/road-policy-focused.log`, `tmp/road-packaged-control.log`, `tmp/road-full.log`.

Published executable: `C:/Codex/dist/PixelVault-0.106.000/PixelVault.exe`; root shortcut/current junction point to it. The published executable checksum matches the packaged worker tested in review. Running old app sessions were not stopped; close/reopen the desktop app to load this reader. Companion rollout evidence follows separately. PhaseE remains In Progress for other outstanding readers/QA; this feature does not award Century points or infer personal construction credit.

Companion application policy and source-permission regression verification: **94/94 server tests** and **29/29 connector tests** pass, including six focused cases. Bound source authority corrects wrong checks while keeping unknown rows, authored notes and other accounts untouched; older or undone observations still require review. Publication distinguishes identical read paths from permission to replace manual values: true replacement authority requires prior explicit true approval for that exact tracker ID and cannot be inherited onto another tracker sharing the save folder. False remains omitted from JSON for old source contracts. Logs: `tmp/road-server-focused.log`, `tmp/road-server-full.log`, `tmp/road-connector-full.log`. NAS/connector rollout remains separately coordinated until its live proof is appended.

## Original control investigation

October28,2026 (America/Chicago) — user-confirmed restoration of **UC Route41-20** provides a named positive control. This is research evidence; no road adapter or tracker migration was shipped in this step.

## Read-only capture

The account-bound save reader copied16 checkpoints, decrypted/decompressed them with the installed game library and verified checksums. Controlled pair: `manualsave4` (2026-10-29T01:25:11Z) before, `manualsave5` (2026-10-29T01:27:16Z) after, both October28 evening locally.

Private copies/manifest: `C:/GameDataExtractor/Backup Data/Death Stranding Director's Cut/Reference/InterviewSaves-20261029-012748609`. The reused helper accounts for the historical folder prefix. Source saves were only read.

## Identity and observed transition

Archive `ds/location/area02/2001/2001_road_rebuilder.core` has52 locators:51 named UC Route sections and the Racetrack. Localized delivery-point descriptions connect visible names to locators. `ds/default_assets/world/dsroadasset.core` has57 geometry entries including circuit geometry and explicit state-fact references. `ds/location/system/ldf_road.core` has115 BooleanFacts.

UC Route41-20 resolves to locator2068, `rebuilder_area2_d_08`, UUID `fbec8a50-fa9d-de43-908c-b444ceb8339b`, geometry ID158193846. Two facts change0→1:

- `ldfp_road_area2_d_08`: `27cf8e4c-4e91-b744-9bc3-22611c5eef7a`.
- `ldfp_ai_highway_d_08`: `0069bbb7-6d96-9542-9a27-4992f78b4c67`.

The latter is explicitly referenced by geometry `StateLinkBooleanFact`. Both resources are persistent with false defaults. The previously validated persistent-fact-table format yields one unambiguous map in each save, with2,563 then2,562 records. Achievement-statistic values do not change.

All51 UC Route sections join through delivery-point/locator/geometry/fact references. Explicit highway flags count **27→28**, with41-20the only changed route. Six preceding recent autosaves consistently show27 with41-20false. This supports road-network state; personal construction credit is not independently established.

## Interpretation limits

Do not substitute similarly named `ldfp_road_*` facts across the catalogue: only48 of51 routes have that simple name counterpart, and22 states disagree with geometry-linked highway state in the after-save. Counterpart totals also vary across preceding autosaves. Names alone do not establish restored-road progress.

The Racetrack is separate from the51 highway flags. A source must define whether it reports restored/available roads, personal contributions or personal construction. This test does not distinguish online contributions or prove ownership/repair semantics. A later save after leaving/reloading and a second named road control would strengthen lifecycle/generalization checks.

Private reference/proof root: `Reference/RoadResearch-20261028`, including `road-catalogue.json`, `road-reference-map.json`, `road-control-proof.json`, parsed/raw resources and path inventories. Existing tracker definitions, manual check-offs, notes, source bindings and automation are unchanged.

PhaseE remains In Progress. A future source should validate the road references and reconcile observations with manual data, without assigning personal build credit from world-state flags.

## Century save connection, October30

Native0.110.000 and Companion0.37.0 now persist All Roads Lead Home from the existing approved full51-route save source. Independent fresh reads report36/51; it remains locked with zero awarded points. No new save-reader format or ownership inference was added. Manual tracker edits remain independent of Century observations. Full details, evidence and recovery paths are in [Century Achievements](CENTURY_ACHIEVEMENTS.md).
