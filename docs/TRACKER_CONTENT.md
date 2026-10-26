# Tracker reference content

October26,2026 — optional plain-text content is shared by desktop and Companion tracker records.

`AchievementTrackerItem.Content` stores reference text with a private custom definition. It defaults to null and is omitted when null, preserving serialization and publication/source fingerprints of existing catalogues. The shared validator limits content to100,000 characters per item and2,000,000 per tracker; existing portable-definition and connector payload limits still apply. JSON definition import/export preserves it, as does milestone expansion and catalogue reconciliation. No game-specific UI or arbitrary file-path access is required.

On desktop, each populated item has a compact **Content** button in the full tracker and the achievement checklist once expanded. It opens a separate, resizable plain-text reader with selectable/wrapped text and scrolling. **Save copy…** exports a UTF-8 text file with title and metadata. Text is never executed as HTML/XAML. Viewing or exporting content does not write progress, mark an interview read, or affect provider achievements. Manual/confirmed checkbox behavior is independent.

Private extracted lore stays in the persistent tracker database and its exports/backups, outside source-controlled catalogues. Do not embed full extracted game text in bundled JSON, test fixtures, logs, repository docs or source archives. Published custom definitions containing content require both a server and connector that understand the optional field; a downlevel connector could silently omit it.

## Death Stranding reference catalogue

The user-supplied `A:/Downloads/death_stranding_interview_data_spec.txt` is a research lead, not a validated save schema. The installed archives use `DSInterviewResource2` UUIDs and localized-text references. Full English prose resolves through **DescriptionText**, rather than the illustrative `body`/`string_id` fields. `BodyText` is empty. Color markup is removed; paragraphs remain plain text.

Of158 resources, **149 have both titles and full English content**; nine have no populated title/body and priority zero and are excluded. Stable ID: `death-stranding-dc.interview-catalogue`, linked to Steam1850570 achievement039. The catalogue groups records into twelve subjects. Each item retains its UUID, title, author/date/location and content. Total stored prose:175,776 characters. This includes story spoilers; opening the library does not imply an in-game unlock.

The existing `death-stranding-dc.the-past-guides-the-present` automatic counter remains separate at **67/100**. Individual catalogue checkboxes are manual because per-title save flags remain unvalidated. The sample `ui_notifications`/`unlocked_interviews` structures in the supplied spec have not been established in the binary saves. No first67 entries are guessed as read; the reference catalogue has no automatic source binding.

`scripts/Prepare-DeathStrandingInterviews.py` consumes the private read-only `InterviewContentCatalogue.java` export, validates UUID uniqueness, removes known color/line-break markup, excludes empty resources and emits a private definition plus a content-free evidence summary. It neither writes game files nor infers progress.

Private evidence: `C:/GameDataExtractor/Backup Data/Death Stranding Director's Cut/Reference/Interviews-20261026`. `native-before-content.sqlite` is a consistent backup. `native-content-install.json` records matching fingerprints of every existing table row before/after addition, preserved67 count, and zero invented observations. Raw/prepared extracted text stays private under that root.

## Validation

- 31 focused Native content/interview/inline cases, two synthetic Python transform tests, and nine rename cases pass.
- Full Native suite: **1,230 passed**. An initial unrelated scoring test failed; it passed targeted, then the full serial rerun passed without assertion changes.
- Native list inspected at850/1100px and reader at380/760px: `App Testing/Achievement Trackers QA/Interviews`.
- Review executable: `C:/Codex/App Testing/Achievement Trackers/PixelVault.exe`; Native version remains0.077.000. Existing running sessions are retained.

Companion **0.8.0 is deployed** with connector0.8.0. Open [Interview catalogue](http://192.168.1.99:8787/?v=0.8.0#tracker/death-stranding-dc.interview-catalogue). Expand a record and choose **Content** to read its stored text and metadata. Close/Escape returns to the same record without changing progress or losing a note draft. Rendering uses `textContent`; markup remains literal, and prose is not saved to browser localStorage. Existing published definitions remain authoritative; later content edits are not an automatic definition overwrite.

The normal connector publication delivered149 populated entries/175,776 characters to the NAS. Desktop and NAS match SHA256`65900E268DEA52F016CD421753C6DDBE4E009019D3C07DD71140DD8F304681AB`, computed from compact .NET JSON of sorted `{Id,Content}` records. The catalogue has zero source bindings and zero invented progress rows; the separate automatic counter remains67/100. Every existing NAS tracker state/history/source/automation/link/import/catalogue-history table still matches the pre-upgrade backup. The two existing custom definitions retain their exact raw-row digest; all19 manual-value/note rows, paired-source configuration and all7,413 achievement/676 guide metadata rows are unchanged.

Validation:65 Companion service cases,8 connector cases and20 JS cases pass. Isolated packaged UI verified real interview prose at390px, literal script/image text without markup nodes, preserved unsaved notes, Escape/focus restoration and no horizontal overflow. Opening the reader left the QA catalogue at zero progress rows. Screenshot:`C:/Codex/tmp/companion-interview-content-mobile-0.8.0.png`. Standalone QA connector launch was blocked by automatic review, so UI QA used an isolated local definition copy; transport was verified through integration tests and the approved installed connector's actual NAS publication.

All609 prior file/table entries matched before enabling the new connector; no tables were added. Fresh NAS backup:`backups/pre-0.8.0-20261026T120257Z.tar`, protected PC:`C:/Codex/Companion Backups/pre-0.8.0-20261026T120257Z/`; SHA256`8CFB0866EBD3C823D070D0272BE1E7038CD171AC7E701385FFA64CDB58EFEA94`. Image`sha256:f31ecd8b5ca539c9291940dd00a98f6ec79536db8abe562ccbd42dd36cbaad17`; private config/start backup:`PixelVaultCompanionConnectorData/backups/pre-0.8.0-20261026T120424Z`. Preserve the populated definitions before a downgrade: older writers can omit the unknown Content field. PhaseE/F remain In Progress; per-title save identification and other outstanding reader/QA work remain open.
