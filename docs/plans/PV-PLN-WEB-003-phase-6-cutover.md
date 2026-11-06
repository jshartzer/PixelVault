# PV-PLN-WEB-003 — Final reconciliation and cutover

October 31, 2026. **PC cutover is live: Companion 0.39.0 and Native 0.114.000. The owner confirmed existing phone login and library/tracker/photo loading. Initial app loading is slow; the measured cover-transfer fix is prepared as 0.39.1, awaiting administrator installation and phone retest.** The NAS Companion is stopped with restart disabled; the connector and its startup paths are retired. Full production app/state/key/configuration backups run daily at 03:00 local time. Retained NAS data has not been deleted.

## Fresh reconciliation

The fresh PC/NAS capture has 3,416 equal manual progress/note fields, no pending web/group/source reviews and no unfinished refresh jobs. Old failed jobs are retained as history rather than replayed. Two photos arrived since Phase 4; all 5,799 visible originals are present on the PC, across 282 collections, with 18 sidecars and retained removal/identity metadata. Curated artwork and library editions match.

The complete PC application archive was restored to a new short isolated root. The actual canonical apply function then ran against that restored root, not live `PixelVaultData`. It initialized only guarded existing schemas, reconciled 28 approved reported values, mapped five binding IDs, retained 3,150 NAS origin rows, mapped 73 history rows and one catalogue-history row, and preserved 21 request receipts. Authored manual fields, catalogue definitions and source permissions remained identical. The full application folder was retained; no selected snapshot replaced it.

The real owner/password hash, all web preferences and the original NAS key ring were copied into the isolated production-state candidate and compared exactly. The key ring successfully protected/unprotected data on this PC without changing its files. No owner password was requested or reset. The owner subsequently signed in at the PC phone origin using the existing credentials and confirmed library, tracker and photo loading. The earlier isolated hash comparison remains distinct from that actual login.

The reconciled root passed all ten actual save-reader tasks in 14,103 ms, all 5,799 photo metadata rows, an 8.7 MB original stream, regenerated thumbnail and 489 curated artwork hashes. Manual progress/notes remained unchanged. No listener, Native UI or NAS runtime call was used. Existing per-tracker settings remain canonical; the final capture's global pause setting is carried forward rather than unconditionally enabling work.

The full Companion suite passes 118 tests. Native passes all 1,387 tests serially; the initial parallel run had nine failures and is not used as acceptance evidence. Twenty-five reconciliation/backup tests plus four pause/readiness/root gates pass. NAS/PC-service launcher modes and rejection of unknown hosting markers pass. The explicit direct-local automatic-settings command was tested only against the isolated root and reset to paused; no automatic host was started there.

## Executed command and guarded sequence

Historical administrator entry point, already executed successfully. Do not rerun it against the existing cutover journal:

```powershell
& 'C:\Codex\Companion Production\Complete-Cutover.ps1'
```

The sealed plan is under the protected `Companion Backups/pc-cutover-20261031T201033Z` evidence directory. It pins 1,125 package/runtime/script/configuration files, the original Native destination, NAS image identity, connector Startup shortcut, exact PC address/interface and explicit production roots. The source and the actual NIC are checked again before stopping any writer. The user believes the router already reserves `192.168.1.69`; that reservation was not independently inspected.

The administrator command:

1. Disables the connector recovery task and retires its separate Startup shortcut. Stops the rehearsal service, closes Native gracefully and stops only the reviewed connector executable. Temporarily suspends the app-backup task during the write boundary.
2. Stops only NAS container `pixelvault-companion` and disables its automatic restart. Other NAS applications and all stored media remain untouched. Captures final PC/NAS app-only recovery data, real owner/key files, receipts and pending-state metadata while writers are stopped. A bounded SQLite busy retry handles transient live-capture contention; final capture is performed after the NAS writer stops.
3. Repeats the comparison and isolated candidate preparation using the final capture. Conflicts, pending work, missing originals or changes in reviewed source permissions stop the command. Guarded schema preparation verifies frozen live-file hashes before any schema write. Reconciliation changes only approved canonical records; it never replaces the entire application folder.
4. Creates new production state at `C:\Codex\PixelVaultCompanionLocalData`, separate disposable cache at `C:\Codex\PixelVaultCompanionLocalCache`, and dedicated service `NT SERVICE\PixelVaultCompanion`. The server uses canonical `C:\Codex\PixelVaultData`, in-place `E:\Game Captures`, loopback/LAN port 8787, delayed startup and 5/15/60-second crash recovery. The firewall permits only the reviewed interface/address/home subnet. Outbound access to the retired NAS Companion endpoint is blocked for the new server, reader and Native binaries; unrelated NAS services/SSH backup copies are unaffected.
5. Expands the tested daily backup plan to the actual production owner/state/key/configuration roots and saves a named app-only snapshot before PC activation. Original and generated photo payloads remain excluded. The existing real owner is preserved rather than using dormant PC state or a rehearsal owner.
6. Updates the Native/Companion shortcuts, current-build junction, saved desktop destination and historical hosting marker to the PC service. Restores the final captured global automatic-update policy while retaining per-source permissions/settings. Starts the sole production service, requires both HTTP health and a fresh service-worker heartbeat/access check, disables the old rehearsal service/firewall, then resumes the full daily backup task.

The command requires a brief maintenance window: avoid editing in Native or Companion while it runs. It intentionally does not recreate the owner, delete old NAS copies, restart unrelated apps, change the router or replace the library drive's backups.

## Failure and rollback boundaries

`cutover-journal.json` records the checkpoint. A rerun refuses an existing journal until its state has been reviewed. Before canonical modification, a failure restores the previous NAS restart policy/host and connector startup paths. Once canonical modification begins, the command leaves authorities stopped for review rather than guessing at rollback. Once the PC becomes authority, it never automatically resumes NAS writes: the PC may have accepted new observations or user edits.

Retained baseline archives, original startup/hosting/desktop settings and stopped NAS data support recovery. After PC writes, capture/reconcile those new records before any rollback; never reactivate two independent writers or overwrite new progress with the old NAS snapshot. A shallow new restore/cache location is required for the existing Windows decoder's working-directory limit.

## Deployment and remaining acceptance

The real production service, dedicated-account canonical access, actual phone owner login, seven automatic source jobs, Native shortcuts/current-build junction and final daily full-owner backup have passed. The post-cutover backup contains 704 files (260,951,621 bytes); the scheduled task returned 0 and the separate NAS copy was verified. The PC address is `http://192.168.1.69:8787/`. Replace the old phone Home Screen destination with that address.

The owner reports slow initial app loading. Local HTTP shell/health requests take roughly 2–23 ms; isolated representative library reads measured 33–579 ms warm and 1,446 ms initially, while tracker detail took 48–99 ms. These are PC-side measurements, not phone end-to-end timings. Full-size canonical cover images total 30,650,525 bytes across 44 available covers, with the first 12 totaling 9,022,319 bytes. Prepared 0.39.1 serves display copies from a separate disposable 128 MiB artwork cache. All 44 covers are prewarmed: 1,605,903 bytes total (94.8% less), with originals verified unchanged. All 120 Companion tests pass, including original preservation, cache hits, changed-source invalidation and path boundaries. The sealed administrator update saves an app backup and can restore the previous PC service binary/configuration if readiness fails. It never resumes NAS hosting.

Production phone performance after that update, deliberate edit/undo, new-capture observation and a normal-use cycle remain follow-ups. Full NAS device-offline and a second actual production reboot have not been physically repeated; runtime binaries are blocked from the retired NAS HTTP endpoint and rehearsal boot/crash/lock checks passed. Keep this distinction in acceptance records. The migration remains active while performance and remaining normal-use checks are open.
