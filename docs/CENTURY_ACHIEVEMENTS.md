# Century Achievements


November 2 — Native 0.117.006 / Companion 0.44.1 lower The Weight of a Nation to **50,000 kg**. Kilogram progress stays visible on the tracker and achievement; shared `AggregateAsSingleItem` metadata counts it as **one** item in PC and web combined totals, complete only at the goal. Existing stable tracker/achievement IDs, raw saved observations, first unlock dates and save-only award rules are preserved. The new reader is required because the old worker validates the former 60,000 kg definition. Evidence: `artifacts/cargo-goal-50000`. Historical launch figures below refer to the previous goal.
## Item requirements (November 2)

Native 0.117.000 and Companion 0.44.0 expose universal per-item Missable and Required for completion settings. Optional items remain visible but do not contribute to tracker or attached Centurian completion totals. These independent settings are stored in `tracker_item_rule`, shared across accounts, and apply to a whole milestone record. Concurrent changes require a reload and at least one item must remain required. Changing settings does not update achievement progress or award points. Only a subsequent approved source snapshot is evaluated, using its incoming observations with no manual values; full catalogue validation, account/source binding, stale checks, unknown required items and first-earned dates remain enforced. The four Mountaineer timing emails default optional; the email source still validates all 240 identities while the default completion target is 236. Existing saved Centurian totals update on the next verified refresh. This supersedes the fixed target described in older entries below.

## Tracker sections (November 1)

Native 0.116.010 and Companion browser revision `0.43.0-century-trackers1` give linked Century trackers their own counted **Century Achievements** section after **Game Trackers**. Group by the actual achievement link, never the checklist title or completion amount. Official 100-read goals and unlinked catalogues remain game trackers. Each section has unfinished trackers first, stable title ordering and its own counted Completed subgroup; empty sections are omitted. Existing data, links, source scope, navigation, artwork and profile expansion are preserved. Twenty Native and nineteen web checks pass; live served browser hashes and health verify the deployed revision.

## The Weight of a Nation — fifth achievement (November 1)

Native 0.116.000 / Companion 0.43.0 add the final planned Death Stranding goal: deliver **60,000 kg** for **100 PV points**. The linked `death-stranding-dc.the-weight-of-a-nation` tracker reads whole kilograms from validated cumulative statistic 49. The packaged-reader rehearsal reports **16,503/60,000**, matching the user's 16.50 t screenshot and the separate official Steam counter. All five shelf spots are now defined. Source observations alone supply Century credit, with existing account, manual-exclusion, first-unlock and older/failure guards; the tracker retains above-target totals while Century display caps at the goal. See [cargo research](DEATH_STRANDING_CARGO_RESEARCH.md).

Nothing Left to Deliver artwork is replaced with a detailed courier carrying stacked cargo beneath three stars. The new weight goal uses a matching loaded-truck badge. Both retain the weathered monochrome appearance and cyan Century trim; unlocked Legends URI v2 and browser image revision 4 refresh cached art. Tests: 1,409 Native and 160 Companion pass. Packaged-reader and 768/390-pixel WPF shelf rehearsals pass. Live activation completed at 21:07:35 UTC: **16,503/60,000**, locked, zero points, SaveChanges enabled/unpaused and a fresh automatic check with no failures/review. Existing tracker rows and automatic policy are preserved; loopback/LAN health, service path/identity and served artwork revision pass. Receipts are under `artifacts/century-cargo`.

## Nothing Left to Deliver — fourth achievement (November1)

Native0.115.000 / Companion0.42.0 add `century.death-stranding-dc.all-lll-orders`: **Nothing Left to Deliver**, worth100 PV points. Earn a **Legend of Legends of Legends (LLL)** evaluation on all540 standard orders. The dedicated built-in `death-stranding-dc.nothing-left-to-deliver` tracker contains the original540 LLL item identities/names/categories, with binary targets, no milestone columns and no category caps. It links directly to this Century goal. The official019 Growth of a Legend catalogue remains1080 independent LL/LLL records with20-per-category targets; it is neither relinked nor edited.

The shared decoder parses the full1140-record mission table once and derives the additional LLL projection from those validated ranks. Analysis schema v5 has11 independent outcomes. Source setup validates the exact540 identities and targets. Century persistence counts incoming known LLL states only within the approved bound refresh transaction; all540 are necessary. Manual checks, LL, provider status, category counts and unknown states cannot supply Century credit. Existing account, first-unlock, idempotency, source-change, failure and older-save guards apply.

`scripts/PixelVault.Legends.Setup` reuses the existing approved standard-order source paths and schedule for the new tracker only, takes a consistent SQLite backup and verifies hashes of all other tracker rows. It does not inherit manual-replacement authority. Rehearsal and the completed live installation report 138/540, locked, zero awarded points, SaveChanges enabled/unpaused and all other tracker rows unchanged. Companion 0.42.0 passed a fresh automatic check after restart, with zero failures and no pending review. Evidence: `artifacts/century-legends/rehearsal-verification.json`, `live-connection.json` and `update-result.json`.

Century Achievements is the achievement group for PV-authored goals alongside Base Game and DLC. The initial plan is five achievements per game. Only defined goals appear; unfilled slots are not achievements and do not supply completion credit.

The shared catalogue is `src/PixelVault.Tracking/CenturyAchievementCatalog.cs`. Native and Companion read the same definitions, independently of provider achievement records, progress, scoring, guide assignments and tracker check-offs.

## Death Stranding Director's Cut — 2 of 5 defined

- **All Roads Lead Home** — rebuild every road section; 100 PV points; linked to Road Building.
- **The Whole Story** — collect every interview; 100 PV points; linked to the full itemized Interview catalogue. The separate automatic 100-read tracker is retained while the user consolidates them.

### All Roads Lead Home

| Field | Definition |
| --- | --- |
| ID | `century.death-stranding-dc.all-roads` |
| Group | Century Achievements |
| Title | All Roads Lead Home |
| Description | Rebuild every road section in Death Stranding Director's Cut. |
| Category | Infrastructure |
| Requirement | Every road section is built in the player's save, including Director's Cut mountain routes. Online contributions count. |
| Progress | Unknown — awaiting save connection |
| Intended update | Automatic progress and unlock from validated game data |

This definition targets Steam Director's Cut (`1850570`). Other editions require their own validated catalogue/reader. The exact section catalogue/target must come from installed game data; no unverified road count is displayed.

## Connection follow-up

The user deferred source linking until later. Capture account-matched saves immediately before and after finishing a known auto-paver and record its number. Verify the full per-section catalogue, serialized table boundaries and the built-state transition, including online contribution and deterioration behavior. The existing `ldfp_builded_road` fact proves a single construction milestone and cannot complete this goal.

Until that validation is complete, browsing only displays authored definitions with **Awaiting save connection**. There are no earned/locked observations, manual completion controls, source bindings or automatic schedules. Definitions do not change official totals or award the diamond completion tier. Future connection work must preserve account isolation, source provenance and last verified progress on read failure, and must require the complete intended Century set before awarding true 100% completion.

Install verification: Native 0.101.000 is published at `C:/Codex/dist/PixelVault-0.101.000/PixelVault.exe`; the root shortcut/current junction target it. Companion 0.31.0 is deployed on the existing NAS URL. All 37 pre-existing audited tables are present; 35 match exactly, with only routine connector heartbeat/activity tables changing. All screenshot catalogue hashes match. Pre-upgrade state/catalogue archives are retained on NAS and in the protected PC folder `Companion Backups/pre-0.31.0-20261028T171907Z`. State SHA-256 `4F383DC697E73542F844EC01FC2E5F183EE503E210D9AFC1F026345993A4BDE9`; catalogue SHA-256 `99665A0DE281C4D96ECB06050B4B1E55E6721D14CEDE24857788206FBC6C9CD8`. Isolated browser rendering at 390/1280px verifies the actual Century section renderer, search/status filters, no horizontal overflow and no console/CSP errors. Live owner session was not available in the verification browser; authenticated definition delivery and account isolation are covered by API tests. Existing connector installation remains 0.20.0; source linking stays deferred.

## Card presentation — October 28

Native 0.102.000 / Companion 0.32.0 use the normal achievement card/list presentation with bundled state artwork and a fixed 100 PV point value per Century achievement. Points remain unearned until verified completion is linked. The locked image is a white padlock on gray; the unlocked road image uses asphalt, pavement markings and black hanging chiral tendrils. See the artwork candidates, prompt record and actual Native two-state mockup in artifacts/century-achievements. Artwork access requires the same approved game/account scope; no provider or personal-progress records are added.


Native is published and the root shortcut/current junction target 0.102.000; NAS health/shell confirm 0.32.0. All 37 audited tables remain present: 35 match exactly, with only normal connector heartbeat/activity changes; every screenshot catalogue hash matches. Protected NAS/PC recovery: `pre-0.32.0-20261028T173613Z`. State SHA-256 `7A2B2775984F5AC4B19B9318C7A8B309ADD1DC5512F7A2697EDAF57CFF817193`; catalogue SHA-256 `99665A0DE281C4D96ECB06050B4B1E55E6721D14CEDE24857788206FBC6C9CD8`. PC connector remains 0.20.0; no save source was linked.


## Shared artwork, navigation and Road Building link — October 28

Native 0.103.000 / Companion 0.33.0 resolve Century art through the shared achievement-art loader and ordinary authenticated achievement icon/unlocked-icon endpoints. Trackers always request unlocked art unless an explicit custom tracker icon takes priority. Century card details show the fixed 100 PV point value, requirements and linked checklists. Century definitions appear in the native tracker achievement picker, with a stable game-scoped logical identity that is excluded from provider progress/scoring.

All Roads Lead Home is explicitly linked to the existing 52-section **Road Building** tracker on both installations. The tracker keeps its existing ID and catalogue. Its icon selection uses automatic achievement artwork. Consistent pre-link SQLite backups are retained at Native `PixelVaultData/century-link-backups/before-road-link-20261028T1749122805835.sqlite` and NAS `data/century-link-backups/before-road-link-20261028T1749456594453.sqlite`. Definition, item state (all accounts), history, source, automatic settings, imports and titles were verified byte-for-byte unchanged at the table-value level. Only link/icon preference metadata was changed. The existing add-only connector is not a general link-edit synchronizer, so this requested link was applied explicitly on both installations.

The native timeline selector and renderer accept valid game-scoped Century rows only when their progress is known, earned and dated. Tests verify unlocked art in timeline tiles and exclusion of unknown/locked/undated rows. There is no real Century unlock yet: the validated road save-reader and durable verified-unlock recording remain deferred. Connecting that source must supply account-scoped Century observations to the timeline without inserting them into provider caches or treating manual tracker checks as an unlock. Companion's current photo timeline does not display official achievement events either; this change preserves that existing scope.

Validation: 94 Native, 6 Companion API/artwork/cache/completion and 12 JavaScript checks pass. A browser fixture using the actual achievement UI verifies card → details, 100 PV pts, the linked Road Building checklist and its unlocked artwork at 390px, with no overflow. The fixture's checklist state is synthetic; live metadata was verified separately by the maintenance helper. No manual or synthetic Century completion was written.

Native 0.103.000 is published and the root shortcut/current junction target it. NAS health/shell confirm 0.33.0. The post-link deployment audit has 50 tables: 48 unchanged; only ordinary connector heartbeat/activity tables differ. All screenshot catalogue hashes match. Protected NAS/PC recovery: `pre-0.33.0-20261028T180106Z`; pre-link NAS SQLite backup is also copied into that protected PC folder. State SHA-256 `C7D3A67F4CF03362288A38FC1F9D1FC1D1C5635E739E03FFB8DC0C3BF9D3F665`; catalogue SHA-256 `99665A0DE281C4D96ECB06050B4B1E55E6721D14CEDE24857788206FBC6C9CD8`. PC connector remains 0.20.0.


## The Whole Story — second achievement

Native 0.104.000 / Companion 0.34.0 add `century.death-stranding-dc.all-interviews`, **The Whole Story**: collect every interview in Death Stranding Director's Cut, worth **100 PV points**. Century Achievements now contains **2 of 5** planned definitions.

At the user's explicit request, the linked tracker is the full itemized **Interview catalogue** (`death-stranding-dc.interview-catalogue`, currently 149 entries). The separate automatic **Interviews — The Past Guides the Present** tracker retains its official `039` link and source settings while the user consolidates them. No trackers were merged, deleted, renamed or re-authored. The catalogue's effective achievement link is overridden through normal link metadata; all progress, notes and private reference content remain intact.

The new unlocked archive artwork uses the shared normal achievement/tracker/detail/timeline art resolver. Locked artwork reuses the established white padlock on gray. [ImageGen artwork and prompt](../artifacts/century-achievements/INTERVIEWS_ARTWORK.md).

This goal is collecting the complete archive; the validated statistic39 100-read counter does not prove per-title acquisition or full catalogue completion. Individual interview game-state mapping still needs validation, so this achievement displays **Awaiting save connection** and earns no points/events from manual check-offs.

Consistent pre-link backups: Native `PixelVaultData/century-link-backups/before-interview-link-20261028T2147125748175.sqlite`; NAS `data/century-link-backups/before-interview-link-20261028T2147589921149.sqlite`. The helper verified seven non-cosmetic tracker tables unchanged, plus the separate automatic counter definition unchanged, after linking. Both installations now use the unlocked archive art automatically for the itemized tracker.

Validation: 36 focused Native, 5 Companion API/artwork/cache/completion and 12 JavaScript checks pass. Both Century definitions have distinct unlocked artwork and share the generic locked image; game/provider scope and provider-progress separation remain enforced.

Native 0.104.000 is published; the root shortcut/current junction target it. NAS health/shell confirm 0.34.0. The post-link deployment audit contains 63 tables: 61 unchanged; only normal connector heartbeat/activity tables differ. Every screenshot catalogue hash matches. Protected NAS/PC recovery: `pre-0.34.0-20261028T215323Z`, including a protected PC copy of the NAS pre-interview-link SQLite backup. State SHA-256 `C3E01AE3B01E1ECF4EB8008AC8970E38E3B25F348AC68A0D8D31488E3058C25E`; catalogue SHA-256 `99665A0DE281C4D96ECB06050B4B1E55E6721D14CEDE24857788206FBC6C9CD8`. PC connector remains 0.20.0.


## Simplified official-style artwork and Century border

Native 0.104.005 / Companion 0.34.1 replace the earlier detailed illustrations with grayscale ink/splatter badges inspired by the official Death Stranding artwork. All Roads Lead Home uses a marked road with tar tendrils; The Whole Story uses a stack of interview pages. A slim pale ice-blue (#A8D9E5) notched frame distinguishes Century artwork. The shared white-padlock-on-gray locked image has the same Century color/frame treatment.

The border is baked into the PNGs, so the existing shared art resolver carries it into cards, details, trackers and eligible native timeline tiles. Asset identities, definitions, point values, links and source behavior are unchanged. Earlier candidates are retained. [Selected images, actual card preview and ImageGen prompt set](../artifacts/century-achievements/official-style-v1/README.md).

Validation: five Native Century/card/tracker/timeline-art integration checks and two Companion artwork/cache checks pass. The actual Native card renderer was inspected at normal size for both unlocked badges and the shared locked image. This is an artwork-only change.

Native 0.104.005 is published and the root shortcut/current junction target it. NAS health/shell confirm 0.34.1. Audit: all 63 tables retained, 61 unchanged; only routine connector heartbeat/activity tables differ. All screenshot catalogue hashes match. Protected NAS/PC recovery `pre-0.34.1-20261028T221930Z`; state SHA-256 `922E57F87D745A931A75E918178B39EF57596DBBEEDAA2520FA49EB86D70145E`, catalogue SHA-256 `99665A0DE281C4D96ECB06050B4B1E55E6721D14CEDE24857788206FBC6C9CD8`. PC connector stays 0.20.0.


## Verified road save connection (October 30, 2026)

October 30 — **Native 0.110.000 / Companion 0.37.0 are live.** All Roads Lead Home now follows verified save data through the existing approved Road Building source. Independent desktop and PC-connector reads confirm **36/51 restored roads**, locked with zero earned Century points. Complete 51/51 observations award the fixed 100 points once and retain a first-verification date and account-scoped evidence. Manual checklist changes/imports cannot unlock it; failed, unknown and older reads preserve verified progress. Earned status persists after later rollbacks or disconnection. Verified Century unlocks join the Native timeline without entering provider caches. Interview Century progress remains unconnected; the full intended Century set is still required for diamond completion.

Validation: **1,368 Native tests pass serially**, actual packaged-reader automatic 27→28 control passes, 95 full-suite Companion tests plus the corrected version/cache regression and both Century tests pass, 29 connector tests and 33 affected JS checks pass. Actual web renderer at390/1280px verifies saved36/51 cards → detail, remaining filter and no overflow; Native saved-data rendering is in `artifacts/century-road-connection`. Live NAS health/shell/achievements.js hashes match. A list timestamp reference error found during QA was corrected in the final image; no save data was affected.

All138 pre-existing NAS audit entries remain,134 unchanged; only tracker source status, connector heartbeat/activity and the requested refresh job differ. The new `century_progress` table contains one verified observation; screenshot catalogue hashes match. Protected consistent NAS/PC recovery: `pre-0.37.0-20261030T153338Z`, state SHA256 `D7BB207873A160053C92D6C06E6FA7F57E5DFFE0E1AE5749BDBEBE4904C62684`, catalogue SHA256 `9ED62D9E20A03229CFD809E41DA80888C1EE8C34A649EDDC2F8A10E2D2740439`. Native backup: `PixelVaultData/century-link-backups/before-road-save-connection-20261030T152945947.sqlite`; notes and unrelated tracker data match. Current shortcut/junction target `C:/Codex/dist/PixelVault-0.110.000/PixelVault.exe`. Existing app sessions were not replaced; reopen Native and reload Companion. Installed connector remains0.24.0. [Native release](https://app.notion.com/p/40973adc59b681c78614e84f8756714b), [Companion release](https://app.notion.com/p/40973adc59b681d88c3dd27aacba797a).

The shared repository records a Century observation atomically only after a current bound `death-stranding-dc.fabrication` v1 observation is applied or acknowledged by the automatic scheduler. It requires all51 unique active UC Route identities with binary known incoming values and the exact game/account/binding/version/fingerprint. Counts use the incoming observation, independently of effective/manual tracker state. Archived unmatched routes are excluded. Unbound imports and JSON-file sources never write Century progress.

`century_progress` uses provider/game/definition/account keys and retains current/target, source observation time, first verification/unlock time, tracker and binding identities, SHA256 fingerprint, source and evidence. Older observations and changed fingerprints at the same timestamp cannot replace it. Same-content refreshes are idempotent. Unknown/error paths leave it alone. First verified completion is permanent; its date is not a claimed historical game unlock time. Each achievement has its own connection status; The Whole Story remains awaiting connection. Official provider totals and completion tiers are unchanged.

## Player-facing achievement wording (October30)

October30 — Native0.110.005 / Companion0.37.1 simplify Century achievement cards and details to player-facing requirements, points, progress and earned status. Save-connection messages, verification explanations, technical timestamps and planning text are removed. Road requirement: rebuild all51 UC Route sections, including the Director's Cut mountain routes; online contributions count. Interview requirement remains reading every interview. Existing progress and automatic behavior are unchanged. Validation:22 focused Native checks,3 Companion API/cache checks,10 achievement JS checks and actual phone/desktop browser rendering pass.


## Century email catalogue connection (October30)

October30 — **Native0.111.000 / Companion0.38.0 are live.** The user clarified that the Century email goal is the whole catalogue, separate from official Read100mails. **No Mail Left Unread** is a100-point Century achievement linked to the existing240-entry Email catalogue on both installations. Independent Native and installed connector reads confirm **144/240**, locked, with zero earned Century points; official038 remains earned at144/100. Catalogue/item IDs, full private content, notes, source bindings/authority, manual overrides and history are preserved. Cards and details show only player-facing requirements and progress.

The complete240 target includes **The Cold, Hard Truth**, whose state remains unknown. The239 mapped runtime entries supply verified read values, received-but-unread mail remains incomplete, and the unmapped entry never receives manual/inferred Century credit. The goal cannot award completion from239 known reads or the100-mail counter. Shared persistence retains unknown count and account/source provenance; verified complete240/240 would award100 points once and retain the first-verification date. Road observation compatibility and completion-tier rules remain intact.

Validation: **1,377 Native**, **97 server**, **29 connector** tests pass; actual packaged-reader automatic139→140 control verifies139/240→140/240. Ten achievement JS checks and actual390/1280px Century rendering pass. Live0.38.0 health/shell/achievements.js match source. Existing PC connector0.24.0 and its configuration bytes are unchanged; no new read scope or reinstall. Native link backup: `PixelVaultData/century-link-backups/before-email-century-20261030T214447658.sqlite`; read backup: `before-email-century-read-20261030T214453248.sqlite`. NAS link helper reports all protected tracker tables and permissions unchanged. All139 prior NAS audit entries remain;133 match exactly, with only Century progress, tracker link/source status, connector config/activity and requested refresh jobs differing.26 added audit entries belong solely to consistent link-recovery databases. Screenshot catalogue hashes match. Protected NAS/PC recovery: `pre-0.38.0-20261030T214823Z`, state SHA256 `06627321385D09EF01A163471668A66E68FA70DE92936FB6B7FD44924DD91703`, catalogue SHA256 `9ED62D9E20A03229CFD809E41DA80888C1EE8C34A649EDDC2F8A10E2D2740439`. Evidence: `tmp/century-email-native-audit.json`, `tmp/century-email-nas-data-audit.json`, `tmp/century-email-live-verification.json`, and `artifacts/century-email-connection`.

Root shortcut/current junction target `C:/Codex/dist/PixelVault-0.111.000/PixelVault.exe`. Reopen Native and reload Companion. [Native release](https://app.notion.com/p/40973adc59b68199921ed2208a920e67), [Companion release](https://app.notion.com/p/40973adc59b681c4abf5e78f67b770de). This resolves the earlier pending email-goal scope choice.


## Game-profile shelf, approved dedicated layout

Native0.112.005 adds the approved prismatic foil finish to earned Century badges in this shelf and shared cards/details. The gold frame and pastel reflection are presentation only. The pointer changes the reflection; clicks play a single1.35-second sweep while preserving navigation. Windows animation preferences disable motion and high contrast retains normal presentation. Locked/unknown goals and placeholders are unaffected. No data/schema or Companion/connector changes. Native software rendering and actual shelf/card layouts are documented in `artifacts/century-prismatic-finish`; all1,379 Native tests pass sequentially.

October30 — Native0.112.000 implements the approved dedicated Century achievement shelf in the game profile. It appears between the official achievement summary/progress bar and provider badge grid, with five slots per game. Defined achievements show their bundled artwork, locked/earned marker, title and saved progress; a single click opens existing details. Undefined slots are noninteractive question-mark placeholders labelled To be announced, excluded from achievement definitions, totals, points and completion awards. Games without Century definitions show five placeholders. The shelf is independent of provider retrieval success and reads saved observations only; it refreshes while visible and on activation, rechecks the account before opening details and preserves same-account display on transient read failure.

