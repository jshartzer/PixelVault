# Companion PC connection

The NAS UI is now **0.13.0**; the installed PC connector remains **0.8.0**. This UI update requires no connector upgrade or extraction changes.


Updated with Companion/connector **0.8.0** on October26,2026. The NAS remains the authoritative Companion workspace. A small Windows process monitors approved save sources, refreshes the indexed library, publishes newly created desktop tracker definitions and synchronizes desktop tracker names. It reads desktop definitions/bindings through a read-only SQLite connection; achievement export is read-only, and the explicit achievement-group outbox writes only Native guide category/DLC metadata after backup. The original Native tracker database and game saves remain read-only.

## Using it

See [achievement support and group synchronization](COMPANION_ACHIEVEMENTS.md). Category/DLC edits now synchronize both ways, with offline queuing, conflict review and consistent guide-database backups. Provider progress follows the desktop's saved refresh result; guide/group metadata syncs automatically.


Automatic updates start with Windows sign-in. The PC must be awake and signed into Windows; locking it does not stop the connection. The left menu shows PC online/offline, save/library check times, pending reviews and **Pause/Resume**. Manual **Refresh from PC** remains available on supported game profiles, including while automatic updates are paused.

Save signatures are checked every five seconds and must remain stable for at least three seconds before a read is queued. Trackers have independent durable receipts, so retries/restarts do not repeat a completed observation and a review for one tracker does not block another. The foreground browser checks for changes every fifteen seconds and on returning to the app; it preserves active edits and uses the existing dismissible update popup.

The indexed desktop library is checked every thirty seconds. New games, screenshot dates, resized covers/banners and approved cached achievement summaries sync when their inputs change; Steam playtime refreshes approximately every fifteen minutes. The Native library must first index new captures. Uploads stage their catalogue and missing artwork before atomic activation; an interrupted upload leaves the current library available. No raw screenshots, provider keys or PC paths are sent in the library catalogue.

New custom desktop trackers for approved provider accounts publish automatically within approximately thirty seconds. Publication is add-only: existing NAS definitions, check-offs, notes, progress and history are not replaced, and removing a desktop definition does not remove its NAS copy. Initial desktop progress/history and later definition edits are not a bidirectional migration. A supported new Death Stranding source can inherit only the exact same previously approved account, game, save folder, specific-save selection and game directory. Both PC and NAS validate this restriction; no new filesystem permission is granted. Unsupported new trackers still appear with manual web controls.

Tracker **names** synchronize separately every thirty seconds for both bundled and custom trackers. The foreground web app refreshes every fifteen seconds; unsaved edits defer background refresh, and **Reload saved progress** retains drafts while updating the heading. The first name update requires a matching prior publication digest, paired original name, canonical bundled name or an already matching name. Later updates require the last shared name to still match the NAS. Independently changed NAS names stay intact and the menu reports a name conflict. Names can converge again when both sides deliberately match. Only display-name metadata changes: checklist items/targets, IDs, source bindings/revisions, account scope, progress, notes, history and automation settings remain independent. Title-neutral source comparison still requires every other definition field and existing save permission to match. See [rename synchronization evidence](TRACKER_NAME_SYNC.md).

The paired Death Stranding sources include the existing gameplay trackers and validated interview-count tracker. They reuse the existing validated-copy reader and isolated game-DLL worker. Each refresh checks the newest supported checkpoint, sends bounded observations to the NAS and updates the profile/trackers. Source-confirmed completion remains protected by the existing tracker rules. The detailed interview catalogue has no source binding; its individual checkboxes remain manual.

Version0.8.0 preserves optional **Content** when publishing a new custom definition. Expand an item and choose Content in the browser to read its privately stored prose and metadata, including while the PC is offline after publication. Viewing is read-only and does not mark it complete. Both server and connector must understand the field; older writers can omit it. Existing-definition publication stays add-only, so later prose edits require deliberate reconciliation. Null Content stays omitted to preserve historical definition/source fingerprints. The149-entry Death Stranding catalogue matches the desktop's full text exactly while retaining the separate67/100 automatic counter. See [content operation and validation](TRACKER_CONTENT.md).

If the PC is offline, the request reports that immediately. A PC that disconnects during a check cannot cause a partial tracker transaction; completed tracker results remain durable, and pending work can resume. Each tracker result is idempotent. Older saves, manual conflicts, an undone refresh or edits during review require explicit review. **Apply save changes** preserves manual progress and existing notes; older observations are excluded unless the owner explicitly selects **Allow older save observations**. History/undo remains available.

## Installed files

- Application: `C:\Codex\Companion Connector\app-0.8.0\PixelVault.Companion.Connector.exe`.
- Start script: `C:\Codex\Companion Connector\Start-Connector.ps1`.
- Private configuration, snapshots, PID and logs: `C:\Codex\PixelVaultCompanionConnectorData\`.
- Automatic start: current user's Startup folder, `PixelVault Companion Connector.lnk`.
- Endpoint: `http://192.168.1.99:8787/`, using the existing explicitly chosen home-LAN HTTP deployment.

The private folder permits only its Windows owner, SYSTEM and Administrators. `connector.json` contains the device credential, approved workspace identity and exact allowed bindings/definitions. Do not publish it, print it, copy it into source archives or put it under a build-output folder. Back it up privately if retaining this pairing through recovery. Intermediate pairing exchange copies were removed after installation.

Restart using the start script. It checks the recorded PID/executable and leaves an already-running connector alone. To stop it, read `connector.pid`, verify that process's executable is the installed connector, then stop only that process. The original PixelVault desktop process and NAS service are separate.

## Build, pair and revoke

Build Windows/Linux server and the separate PC connection with:

```powershell
pwsh -File C:\Codex\scripts\Build-PixelVaultCompanion.ps1 -IncludeConnector
```

Pairing is an administrator operation on the existing private NAS workspace, not a public web endpoint. In a restricted one-off container with the data mounted at `/data`, invoke:

```text
dotnet PixelVault.Companion.dll --pair-connector /data /private-exchange/new-pairing.json http://192.168.1.99:8787/
```

Copy that private file to the PC via the existing SSH key and use `scripts/Install-PixelVaultCompanionConnector.ps1 -PairingFile <private-file>`. The installer uses no administrator elevation, protects the configuration, installs a hidden background process and a user Startup shortcut. For this existing installation, `scripts/Upgrade-PixelVaultCompanionConnector.ps1` backs up the private configuration/start script, verifies the installed PID/executable, retains the credential and startup shortcut, and validates the original approved workspace manifest before enabling local metadata reads. Source ZIPs for the NAS contain only server/shared source; PC connection source is in `src/PixelVault.Companion.Connector` and links the existing Native reader files at build time.

The NAS operator grants automatic source/library/definition updates with `dotnet PixelVault.Companion.dll --enable-automatic /data`. The browser can pause/resume this grant through authenticated CSRF-protected requests. Pairing/revocation and the original Native storage cutover remain separate operations.

To revoke immediately on the NAS:

```text
dotnet PixelVault.Companion.dll --revoke-connector /data
```

Re-pairing also rotates the token and clears pending work. The NAS stores a token hash, approved workspace and exact binding revisions. Changing account scope or source configuration invalidates the prior permission. Device routes accept only the scoped bearer credential, not browser cookies; it cannot authenticate to owner APIs. Web refresh/review requests still require owner authentication and CSRF protection. The PC receives task IDs/revisions, never arbitrary remote paths or commands.

## Verification and limits

Version0.7.1 passes64 server tests,7 connector tests and17 JS cases. Packaged QA verified custom/bundled renames, persisted baselines, independent NAS conflict retention, title-only source acceptance, rejected non-title structural changes and an unsaved note retained during heading refresh. Live **Road Building** has the same52 items and19/52 progress as the fresh pre-upgrade backup; every existing tracker state/history/source/automation/link/import table digest is unchanged. All583 prior file/table entries matched before connector sync; credential/workspace/config/source permission digest and the19 manual-value/note rows matched afterward. All7,413 achievement metadata rows/676 guides remain unchanged. Both NAS and installed connector are0.7.1; pairing and sign-in startup are retained.

Version0.6.0 passes53 server tests,4 connector tests and13 JS cases, including explicit grants, add-only/idempotent publication, read-only desktop discovery, exact read-path inheritance, reduced snapshot retention, stable-save receipts, Pause before apply and staged library activation. A separate packaged QA workspace exercised real save monitoring, manual review, library sync and interview publication at67/100. Live QNAP/browser checks confirmed the retained owner/session/pairing, healthy automatic save/library/publication status and interview67/100. All541 prior stable table/file digests matched before enabling automatic updates; every existing manual value and note still matched after live observations. Fresh complete NAS/PC backups and the prior application image are retained.

The initial0.5.0 validation passed48 Companion tests and three JS grouping/sort cases. Its first five-source read was applied only to an isolated QA copy (143→148); installation at that earlier release did not refresh production progress. Version0.6.0 now intentionally enables live automatic observations under the user's authorization. Physical iPhone touch/reboot behavior has not been exercised by the agent; the user previously confirmed the phone web app works.

Automatic source observations, add-only tracker publication and indexed library/artwork/provider-stat synchronization are implemented. Full native tracker routing/reconciliation remains separate work. Screenshot sorting uses image capture dates from the native index with file-time fallback; videos do not determine that sort. With the PC off, saved data and manual tracking remain available on the NAS; new PC observations resume after reconnecting.
