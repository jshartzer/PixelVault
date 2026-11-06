# PV-PLN-WEB-003 — Phase 5 app-only backup and restore

October 31, 2026. **App-only snapshot, archive verification, NAS replication and isolated restore pass. Production now runs on the PC.** The daily 03:00 task now includes real production Companion owner/state/keys/configuration as well as canonical PC data. Its first post-activation run returned 0: 704 files, 260,951,621 bytes, verified NAS copy and no pending copies. Photo originals and generated derivatives remain excluded. Earlier rehearsal-only coverage below is historical; [current deployment](PV-PLN-WEB-003-phase-6-cutover.md).

Related: [migration plan](PV-PLN-WEB-003-pc-hosted-companion.md), [Windows service/provider checks](PV-PLN-WEB-003-phase-4-runtime.md).

## Snapshot boundaries

`scripts/CompanionMigrationAudit/app_backup.py` uses a reviewed inclusion plan. It never walks the capture drive or recursively copies a data directory. The live-PC plan includes the canonical tracker, guide, map, photo-index and capture-group databases; settings/provider keys; small saved achievement records; pins/preferences; curated artwork; saved/custom covers and map images. Generated covers, thumbnails, original captures/sidecars, reader/decoder scratch data, ordinary logs, binaries, old database exports/backups, OS locks and On Open signals are excluded.

Two protected plans and snapshots are distinct:

| Scope | Verified content |
|---|---|
| Real PC application data, `C:\Codex\PixelVaultData` | 693 files, five SQLite databases, 680 authored artwork/cover/map assets; initial archive 252,904,830 bytes. This is the daily task's current source. |
| Isolated migration application plus Companion state/configuration | 703 files, seven SQLite databases, test owner/password hash, protected cookie keys, queue/reviews, preferences, account scope, photo identities/removals and explicit service configuration. This is the complete pre-cutover restore fixture. |

The selected earlier application fixture had metadata references but omitted the corresponding authored images. Its 680 authored assets were materialized from the matching live PC files before backup; curated images were checked against their manifest fingerprints. **No capture original was copied.** The live PC source was not replaced with the selected fixture.

All included SQLite writers are reserved together with `BEGIN IMMEDIATE`; independent read-only connections use SQLite's backup API so committed WAL records are captured. Provider JSON writers share Native's exclusive file leases. Metadata bytes and the selected file set are checked before/after the capture interval. Changes, busy writers or a bounded SQLite-copy timeout abort/retry instead of publishing a mixed snapshot. Large artwork reads and ZIP compression occur outside the SQLite write barrier. The task raises its process priority to normal for the short database reservation, avoiding Task Scheduler's default I/O throttling.

Each archive records source-root mappings, boundaries/timestamps, schema versions, table counts, lengths and SHA-256 checksums. Verification rejects CRC/checksum mismatches, unmanifested or duplicate files and unsafe paths. Restore requires a new destination outside all source roots; it cannot overwrite a live store.

## Storage, retention and scheduling

Local archives are under `C:\Codex\Companion Backups\AppData`, split into `pc-application` and `rehearsal`. Local permissions grant the owning Windows user, SYSTEM and administrators. Snapshots contain credentials and authentication state and must stay in this protected area, outside Git/source bundles. NAS copies use dedicated `pixelvault-app-backups/pc-application` and `/rehearsal` directories under the existing Container share, with directory mode 700 and file mode 600. NAS production data/container files are not backup destinations.

Replication uses strict host-key checking, explicit SSH key/known-host paths, a temporary remote filename, SHA-256 comparison and an atomic rename. Failed copies remain queued in `replication-status.json`; a valid local snapshot is retained independently. Retries reuse a recent successful local snapshot instead of creating repeated copies within an hour. Normal local snapshots and remote copies retain 14 daily and eight weekly points; named migration snapshots and uncopied points are pinned. Retention removes only verified, engine-named archives directly within their reviewed backup directories. It never prunes old migration evidence, unrelated backups or capture files.

The `PixelVault App Data Backup` Windows task uses the current user's S4U batch logon, limited privilege, hidden PowerShell, daily 03:00 local time, missed-run catch-up, no overlapping instances and three 20-minute retries. Its trigger uses a floating local boundary, so the November 1 clock change does not shift it to 02:00. The runner uses a checksum-verified, PSF-signed standalone [Python 3.14.10 embeddable runtime](https://www.python.org/downloads/release/python-31410/); the Microsoft Store executable was not usable from the task. The runtime is separate from installed Python and excluded from app snapshots.

The existing backup task and drive/photo backup arrangements are untouched. Current daily snapshots protect actual PC application data. **They do not yet include production Companion owner state on the NAS.** The complete seven-database restore proof uses isolated test owner state. During cutover, add the final PC Companion state/configuration roots to the same reviewed daily plan; verify that expanded task before retiring NAS hosting. Do not substitute the dormant historical PC Companion state or a selected snapshot for live production state.

Actual scheduled verification detected and corrected the Store-runtime access failure, default task I/O throttling, a busy-source timeout and the DST offset. With the standalone runtime/normal priority, the five-database reservation took 654 ms. A queued earlier copy was then successfully retried from the unattended task; all local archives have verified remote counterparts. Managed retention also removed one superseded unlabelled test snapshot locally/remotely after checking its identity/hash, preserving named migration points and unrelated evidence. The next scheduled run is November 1 at 03:00 CST, confirming the clock-change correction. No second reboot was required for these backup checks.

`latest-status.json`, `replication-status.json`, `capture-progress.json`, `retention-status.json` and Task Scheduler's last result expose current status. `app_backup.py status <plan>` reads them. Diagnostics and historical failed attempts stay private beside the backups.

## Restore proof and limits

The full archive was restored into the new protected `C:\Codex\pv-restore-20261031` root. Verification checked every file and all seven database integrity/table inventories before application code ran. `RestoreProof` then authenticated the restored test owner, used the restored machine-protected key ring without replacing it, checked provider-key file access, verified all 489 curated artwork hashes, executed all ten real save readers and checked all 5,797 photo metadata rows, an 8.7 MB original stream and a regenerated thumbnail. Authored manual progress/notes were preserved; no Native UI, listener or NAS runtime request was used. Generated derivatives and save scratch files were rebuilt only in the restore cache. The real scheduled PC application archive was separately restored to `C:\Codex\pv-restore-pc-20261031`, verifying all 693 files and five database inventories. All 311 original captured evidence hashes remain unchanged.

An initial deep restore path exceeded the existing Windows save decoder's working-directory limit. Its failure evidence remains private; the successful repeat used the shorter root above. Use a short restore/cache location for decoder acceptance. This does not weaken archive/path validation or change source saves.

Protected cookie-key recovery was tested on this PC. On a different Windows installation, machine-protected cookie keys require an explicit key-ring replacement/re-sign-in procedure while preserving the restored owner/password hash; copying these files is not a cross-machine DPAPI recovery proof. Production NAS owner/key migration remains Phase 6 work.

Ten backup tests pass under the standalone runtime, covering committed WAL data, simultaneous database reservations, changing metadata, bounded busy-source failure, owner/settings recovery, checksum/path/extra-file rejection, restore isolation, failed-copy retry and retention policy. The original capture manifests, service seals, credential/restore proofs and final task result are retained in the protected migration evidence area.

## Operation

```powershell
# Scheduled runner; explicit plan also permits an on-demand app-only snapshot.
& 'C:\Codex\scripts\Run-PixelVaultAppBackup.ps1' -PlanPath '<protected-plan.json>'

# Verify/restore using the standalone runtime; destination must not exist.
& 'C:\Codex\Companion Backup Runtime\python-3.14.10\python.exe' 'C:\Codex\scripts\CompanionMigrationAudit\app_backup.py' verify '<archive.zip>'
& 'C:\Codex\Companion Backup Runtime\python-3.14.10\python.exe' 'C:\Codex\scripts\CompanionMigrationAudit\app_backup.py' restore '<archive.zip>' 'C:\Codex\pv-restore-new'
```

Restore tests never activate a server or rewrite production roots. Final reconciliation, updated Native coordination deployment, actual owner/key transfer, final backup-plan expansion, phone-app acceptance and the hosting/connector switch remain Phase 6.
