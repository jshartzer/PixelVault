# Death Stranding mail tracker

## October 29 evening: per-email mapping verified and deployed

Read-only static inspection of the installed game’s mail serializer locates a versioned 512-record mail table through directory marker IDs `0x16151FB1` and `0x192D3A52`. The observed block signature is `34 B0 EB A9 01`, followed by a little-endian record count of512, 512 records of20 bytes and an eight-byte queue-time field. Each record carries its installed mail-resource index at+8 (uint16), received state at+15 (low two bits), and meaningful flags at+16 (low four bits). The opened/read flag is bit`0x04` at+16, independent of the notification flag. Higher bits and trailing record bytes contain padding and are not interpreted.

The installed `MailResources` order has242 slots:239 populated unique emails and three aliases of the same empty resource at indices120/160/170. Runtime index assignment follows the last alias, so all three empty records carry index170. Unused records242–511 carry index`0xFFFF`. The catalogue’s extra populated title **The Cold, Hard Truth**, UUID`1da30b96-f880-bc4b-9560-5c52bc64730e`, has no runtime slot and must remain unknown. It must not be silently marked locked, read or deleted.

All **160 copied payloads** satisfy the complete slot/index/static-flag/empty/unused/state checks, and counting opened flags exactly matches statistic38 in every copy. The new Roboticist control changes only slot98, UUID`c6e266d8-38ed-f74a-8152-87612dcda21b`, from unread to opened (low flags`0x09 → 0x0C`), with total139→140 and interviews72 unchanged. The earlier Photographer control changes only slot108, **Weird Stuff in Dad’s Old Photos**, UUID`767aa420-d1b4-d44f-97cc-c64d481e7411`, from`0x0B → 0x0E`, with total138→139. Its separate interview transition does not change the mail mapping.

The latest table predicts **How Prepping Got Started** from The First Prepper remains received/unread, even though its notification flag is clear. The user independently confirmed there is no exclamation point but the email has not been selected and read. Therefore notification badges cannot establish completion.

Reproducible read-only audit: `C:/Codex/tmp/mail-mapping/audit_mail_states.py`. Private report: `Reference/MailReadControl-20261030-002255192/mail-state-mapping-audit.json`. Read-only executable inspection/disassembly and intermediate candidates are under `tmp/mail-mapping`. The profile was copied stably and decoded as a separate settings format; it does not supply a validated mail-state mapping. Initial persistent-fact additions were all false and were correctly excluded as mail evidence. Research used the installed game and private copies; no game save was modified. Native **0.109.000** and installed connector **0.24.0** are deployed. Companion remains **0.36.6**. The tenth shared-save projection uses cache schema v4.

### Live state, authority and preservation

Desktop and NAS both report **140 read among239 known email states** in the original240-item catalogue. **How Prepping Got Started** remains received/unread; **The Cold, Hard Truth** remains unknown. The user explicitly requested “Override anything I’ve manually put in.” Within this email task, only the email catalogue binding opts into `ReplaceManualProgress=true`; known fresh game states replace manual checkmarks. Notes, content, links, titles, icons, catalogue history, unknown states and other trackers’ manual values/permissions remain protected. Nine native manual progress values were cleared, including one disagreement with the save. Direct final NAS audit confirms game authority, zero known manual overrides, paired source and140 read.

SaveChanges is enabled/unpaused. After restart, the Native monitor successfully checked the source with zero failures/no pending review. The upgraded connector delivered observations normally and has an empty error log. Credentials, workspace/account map, startup settings and all prior source objects match the pre-append configuration; the existing recovery task remains installed. Root shortcut/current junction and App Testing target0.109.000.

Validation: **1,361 Native tests**, **29 connector tests**, and **17 actual packaged-worker mail checks for each executable** pass, including the automatic scheduler and preservation control. Private setup and live audit evidence is under `tmp/mail-mapping`; no game save was modified.

Recovery: consistent Native backup `Reference/MailReadControl-20261030-002255192/AutoInstall/native-before-mail-states.sqlite`; consistent NAS tracking/companion backups at `data/mail-state-source-backups/20261030T012034994Z`, copied to protected `C:/Codex/Companion Backups/mail-states-20261030T012034994Z` with matching SHA256. Tracking hash `1E68B60F84374881612E64E89856B49D30FC8A134E98DDAE29F63765A2498A3A`; companion hash `29AFF8F143C8C172E3B1490457AE9076F834656F6FE26BA6FF0AE9BEE2A0D27C`. Connector rollback: `PixelVaultCompanionConnectorData/backups/pre-0.24.0-20261030T012112Z` and `connector.json.before-mail-states-20261030T012111380Z`.

Release record: [Native0.109.000 / connector0.24.0](https://app.notion.com/p/40973adc59b681119017c74ece44df35).

Earlier dated entries below describe the evidence available at those times.

## October 29 evening: isolated Roboticist reading control

The user identified **We Aren't Really Here** from **The Roboticist** as unread. Before the user read it, a stable, account/decompression/MD5-validated copy of **manualsave16** was captured (source UTC `2026-10-30T00:20:38.5725883Z`). After the user read that email, left Mail and saved, **manualsave17** was captured (source UTC `2026-10-30T00:22:30.7964037Z`). The complete typed achievement-statistic table shows mail statistic 38 **139 → 140**, while interview statistic 39 remains **72 → 72**. This provides a named mail control independent of an interview-reading transition.

Both published Native **0.108.000** and installed connector **0.23.0** workers separately reproduce 139/140 from the private copied pair, validating account, decompression and checksum. Ordinary automatic refresh delivered 140 to both the desktop and NAS. Both original checkpoint hashes were rechecked unchanged after inspection. No tracker definitions, manual checkboxes, notes or source settings were edited for this validation.

Installed identity: `c6e266d8-38ed-f74a-8152-87612dcda21b`; archive MailResources index 98. Its UUID is absent from both payloads in the checked raw byte orders, and the initial `0x400` payload bytes—including the known interview table—are identical. This control validates the saved total but does **not** establish a per-email read-state location or identity mapping. The 240-item catalogue remains manual; do not infer its checkboxes from the total or this archive index.

Private evidence: `Reference/InterviewSaves-20261030-002151699/manualsave16`, `Reference/InterviewSaves-20261030-002255192/manualsave17`, and `Reference/MailReadControl-20261030-002255192/named-mail-control.json` under the extraction root below. The last folder also holds four published-worker requests/results. Encrypted SHA256: before `1B0DFBE0AEE34BF9EC22C63CBB19647B9AEA6F77D6EDCB28CF53BB447A1A67A1`; after `CEB8356731A8802B5C989DBE3DDA0236A4EE8E2536B639004236A1DFC1F94869`. No application release was needed.

## Earlier implementation and controls

October 29, 2026: Native **0.108.000** and PC connector **0.23.0** ship an automatic mail total. Companion **0.36.0** supports publication and refresh without a server upgrade. Two private custom trackers are installed on desktop and NAS:

- `death-stranding-dc.email-catalogue`: 240 populated emails from 45 senders, with full plain-text content. Individual checkboxes are manual; no itemized saved read-state mapping is validated.
- `death-stranding-dc.the-post-guides-the-present`: saved mail-reading total **139/100**, linked to Steam1850570 achievement038, **The Post Guides the Present** (Read 100 mails), confirmed against cached official metadata. SaveChanges is enabled/unpaused on desktop; the background connector delivers observations independently.

## Catalogue evidence

Read-only installed `ds/archives/mail_resource.core` extraction contains244 DSMailResource objects,240 with populated English titles and prose. Four empty resources are excluded. Sender/location resolve through the sender's BridgesIdLine1/BridgesIdLine0. Plain-text content totals255,888 characters. `scripts/Prepare-DeathStrandingEmails.py` prepares the private definition; game prose is never bundled in source or test fixtures.

The installed archive list's MailResources contains242 references but240 unique identities: one empty identity appears three times. It covers239 populated emails. The extra populated resource **The Cold, Hard Truth** is retained in the reference catalogue, rather than silently discarded for not appearing in that array. This discrepancy is another reason not to infer per-email state from array positions.

Desktop/NAS sorted identity+content SHA256 both equal `7796392C577E2E26699803E42DFC26812CB261A8B827B6D6A3217162F3232C64`. The complete private extraction/preparation/setup evidence is under `C:/GameDataExtractor/Backup Data/Death Stranding Director's Cut/Reference/Emails-20261029`. `Install/native-before-email-catalogue.sqlite` and `AutoInstall/native-before-mail-counter.sqlite` are consistent backups. Setup receipts verify existing rows/authored values/notes/other tracker settings and metadata were preserved. Ordinary add-only connector publication delivered both new definitions and inherited only the already approved save/account/game path scope; no manual replacement authority was added.

## Controlled saves

The initial named email was **How Prepping Got Started** (First Prepper). Manualsave9 and10 have identical early state data and statistic38=138, so they do not establish its named reading transition. After leaving Mail and saving, manualsave11 still reports138. Never reinterpret this as proof the user's reading action did not occur.

After the user explicitly read **Weird Stuff in Dad's Old Photos** (Photographer), left Mail and saved, manualsave12 changes statistic38 **138 to139**. Before/after copied saves are in `Reference/InterviewSaves-20261029-170942990` and pass account/container/decompression/MD5 validation. Manualsave11 timestamp:2026-10-29T17:02:49.6147984Z; manualsave12:2026-10-29T17:09:22.3884853Z. The latter encrypted SHA256 is `8502B3C28620605D2B7EB519C0C50AEA86E9C022D969E2032ED59523D5C70114`.

The same pair also changes interview Report#4 at0x2E6 from0x0B to0x0D, and interview statistic39 from69 to70. This separate observed change prevents treating the entire binary diff as exclusively mail-related. No title-specific mail flag has been established; whole UUID searches and initial state-array positions did not establish one. Further per-email refresh requires an independently validated identity/state mapping, not assignment of the first139 catalogue entries as read.

## Reader and verification

The mail reader selects statistic38 from the existing complete47–50-record typed achievement-statistic table, bounded at240 populated resources. Required identities, optional7/8/41, types, booleans, duplicates, truncation and ambiguity checks remain. Interview statistic39 stays independent. Mail is the ninth independently validated shared-save projection; analysis cache schema advances to v3. Existing stable-copy, isolated-worker, game DLL, account and checksum checks remain authoritative.

Full Native suite:1,344 passing; connector suite:29 passing. Both actual published workers separately pass the controlled138→139 automatic-scheduler test, preserve its note and clear failure/review status. Tests reject out-of-range mail counts, ambiguous tables and wrong statistic types; synthetic catalogue checks cover empty resources, markup conversion and duplicate identities. An initial malformed-type test changed an already-boolean unrelated field; corrected it to mutate statistic38's type before the successful full rerun.

Native live automatic state has zero failures/no pending review. Direct NAS audit confirms Current139, Target100, AchievementId038, linked source and a successful observation matching manualsave12. Evidence: `tmp/email-research/live-nas-counter.json`, packaged-worker and full-suite logs in the same directory. Root shortcut/current junction and App Testing target0.108.000. Connector sign-in startup and private pairing are retained; upgrade backup `PixelVaultCompanionConnectorData/backups/pre-0.23.0-20261029T171354Z`.

## Achievement presentation, October30

October30 — Native0.110.010 / Companion0.37.2 apply the clean player-facing wording to the email achievement and shared official achievement views. Keep Read100mails, ordinary guides, points, progress and earned dates; remove technical score/counter explanations, save timestamps and routine sync chatter. Existing automatic mail progress remains144/100 with zero manual overrides.78 focused Native checks,3 Companion API/cache checks,10 achievement JS checks,2 existing browser regressions and phone/desktop email rendering pass. Live0.37.2 health/shell/achievements.js match source. Recovery: `pre-0.37.2-20261030T212725Z`. A new all-email Century goal is awaiting the user’s scope choice; none has been created.


## Century email catalogue connection (October30)

October30 — **Native0.111.000 / Companion0.38.0 are live.** The user clarified that the Century email goal is the whole catalogue, separate from official Read100mails. **No Mail Left Unread** is a100-point Century achievement linked to the existing240-entry Email catalogue on both installations. Independent Native and installed connector reads confirm **144/240**, locked, with zero earned Century points; official038 remains earned at144/100. Catalogue/item IDs, full private content, notes, source bindings/authority, manual overrides and history are preserved. Cards and details show only player-facing requirements and progress.

The complete240 target includes **The Cold, Hard Truth**, whose state remains unknown. The239 mapped runtime entries supply verified read values, received-but-unread mail remains incomplete, and the unmapped entry never receives manual/inferred Century credit. The goal cannot award completion from239 known reads or the100-mail counter. Shared persistence retains unknown count and account/source provenance; verified complete240/240 would award100 points once and retain the first-verification date. Road observation compatibility and completion-tier rules remain intact.

Validation: **1,377 Native**, **97 server**, **29 connector** tests pass; actual packaged-reader automatic139→140 control verifies139/240→140/240. Ten achievement JS checks and actual390/1280px Century rendering pass. Live0.38.0 health/shell/achievements.js match source. Existing PC connector0.24.0 and its configuration bytes are unchanged; no new read scope or reinstall. Native link backup: `PixelVaultData/century-link-backups/before-email-century-20261030T214447658.sqlite`; read backup: `before-email-century-read-20261030T214453248.sqlite`. NAS link helper reports all protected tracker tables and permissions unchanged. All139 prior NAS audit entries remain;133 match exactly, with only Century progress, tracker link/source status, connector config/activity and requested refresh jobs differing.26 added audit entries belong solely to consistent link-recovery databases. Screenshot catalogue hashes match. Protected NAS/PC recovery: `pre-0.38.0-20261030T214823Z`, state SHA256 `06627321385D09EF01A163471668A66E68FA70DE92936FB6B7FD44924DD91703`, catalogue SHA256 `9ED62D9E20A03229CFD809E41DA80888C1EE8C34A649EDDC2F8A10E2D2740439`. Evidence: `tmp/century-email-native-audit.json`, `tmp/century-email-nas-data-audit.json`, `tmp/century-email-live-verification.json`, and `artifacts/century-email-connection`.

Root shortcut/current junction target `C:/Codex/dist/PixelVault-0.111.000/PixelVault.exe`. Reopen Native and reload Companion. [Native release](https://app.notion.com/p/40973adc59b68199921ed2208a920e67), [Companion release](https://app.notion.com/p/40973adc59b681c4abf5e78f67b770de). This resolves the earlier pending email-goal scope choice.

