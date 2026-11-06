# Companion migration inventory tools

[PV-PLN-WEB-003](../../docs/plans/PV-PLN-WEB-003-pc-hosted-companion.md) capture, reconciliation, guarded cutover and app-only backup tools. **Production cutover has executed; do not rerun the existing cutover journal or restart NAS/connector writers.** The PC is authority. The historical staging tools remain isolated by default; `cutover.py apply-frozen` is the explicit guarded live boundary. [Current deployment/acceptance](../../docs/plans/PV-PLN-WEB-003-phase-6-cutover.md).

`Snapshot.csproj` is a standalone NAS snapshot helper. It uses read-only/query-only SQLite connections and the backup API, without invoking application constructors or schema initialization. SQLite can require writable WAL sidecars, so the live data mount permits those coordination files. All output is outside the source root, in a new directory. The helper copies selected JSON catalogues/removals and inventories key filenames/sizes. It never copies photo, preview, video, XMP or key payloads.

`capture.py` uses the existing installation's SSH connection and a pre-uploaded helper, transfers independent NAS and PC database snapshots, and collects account scope, approved-source configuration with the bearer token removed, indexes and catalogue metadata. It is deliberately scoped to the verified current roots, NAS host/container and library index. Review those paths and the image version before a later run. It never stops either application. The output must be a new empty, access-controlled directory under the ignored `Companion Backups` area. NAS work goes on the main volume, never the NAS's small `/tmp` filesystem.

Build and package the helper:

```powershell
dotnet publish scripts/CompanionMigrationAudit/Snapshot.csproj -c Release -r linux-x64 --self-contained false -o tmp/companion-migration-snapshot-helper
tar -cf tmp/companion-migration-snapshot-helper.tar -C tmp/companion-migration-snapshot-helper .
```

After creating a protected empty evidence directory, upload the helper archive as `<evidence-directory-name>-helper.tar` under the installation's NAS `build` directory. Then run:

```powershell
python scripts/CompanionMigrationAudit/capture.py '<protected-evidence-directory>'
python scripts/CompanionMigrationAudit/compare.py '<protected-evidence-directory>' --output '<protected-evidence-directory>/comparison.json'
python -m unittest discover -s scripts/CompanionMigrationAudit -p test_compare.py -v
```

`compare.py` opens captured databases read-only and stats indexed local photo/sidecar paths without reading media bytes. It checks the common workspace identity and original shared history before using a three-way baseline; otherwise different authored fields require review. Its report includes per-table coverage, manual/source differences, identity mappings, history preservation proposals, pending jobs, guide/group/provider comparisons and local photo availability. Legacy edition and storage scopes are included when resolving photo groups.

Proposal IDs derive from scope, source identity, disposition and before/incoming hashes. Re-running against unchanged evidence and unchanged media stamps produces the same report and proposal IDs. **No apply command exists.** Exact common history rows can be mapped directly; other NAS history proposals mean preserve origin evidence, not replay those observations or append them blindly as current history. The implementation phase must map undo links, request receipts, mirrored web edits and source provenance first.

Reports and snapshots contain private notes, paths, account identifiers and authentication database state. Keep them in the protected ignored evidence directory. The public phase report contains aggregate findings only. Snapshot timestamps are per database/file, not a globally coordinated recovery point. A fresh final capture is required at cutover.

## Phase 2 isolated reconciliation

`stage.py` accepts only a new `phase2-*` child of the protected evidence directory. It validates the reviewed comparison and scopes, prepares selected app snapshots, calls the explicit C# schema owner, and reconciles tracking in one transaction. Completed-run receipts support replay after interruption. Source IDs require full permission equality; all changed observations require accepted reader/history evidence and cannot replace newer observations. Histories retain origin keys/payloads; common/mirrored events and catalogue history map to existing IDs. Request receipts retain mapped history pointers. No old refresh job is executed.

The output separates `application`, `state` and `cache`; `direct-local.json` describes those roots. The state DB retains the owner/preferences, without a second achievement/library authority. This selected rehearsal is **not a replacement for the full live application folder**. Live key transfer and final refreshed-input application belong to cutover. The CLI has no live-apply option.

```powershell
dotnet build src/PixelVault.Companion/PixelVault.Companion.csproj -c Release
python scripts/CompanionMigrationAudit/stage.py '<protected-evidence-directory>' '<protected-evidence-directory>/phase2-example' --companion-dll 'src/PixelVault.Companion/bin/Release/net10.0/PixelVault.Companion.dll'
dotnet run --project scripts/CompanionMigrationAudit/Proof/Proof.csproj -c Release -- '<protected-evidence-directory>/phase2-example/direct-local.json'
python -m unittest discover -s scripts/CompanionMigrationAudit -p 'test_*.py' -v
```

Re-run the identical `stage.py` command to verify zero-write reconciliation replay. A different source report or different tool code requires a new stage. The proof tool launches a hidden separate-process domain writer, verifies stale-edit/retry/undo behavior, restores its test note, and checks source-completion protection. Its history/receipt records remain in the rehearsal only. It does not call the NAS, serve a TCP port or open Native UI. Its metadata timings are domain timings, not browser timings.

`--prepare-direct-local-schema <application-root>` on the Companion binary requires the explicit rehearsal marker and is never invoked by a web request. Native/the preparation tool owns shared schema migration; direct API consumers only validate/open existing stores.

## Phase 3 isolated direct photos

Make a separate options JSON beside the staged `direct-local.json`. Retain its roots/mode and add `ScreenshotLibraryEnabled: true`, an absolute `PhotoWorkerPath` for the built Windows worker DLL, and optionally `PhotoDerivativeReuseRoot` for the existing PC connector's `screenshot-library` cache. `PhotoCacheBytes` defaults to 1 GiB. Do not repoint the installed service or launcher during rehearsal.

```powershell
dotnet build src/PixelVault.Companion.Media/PixelVault.Companion.Media.csproj
dotnet build src/PixelVault.Companion/PixelVault.Companion.csproj
dotnet src/PixelVault.Companion/bin/Debug/net10.0/PixelVault.Companion.dll --prepare-direct-local-photos '<stage>/phase3-direct-local.json' '<protected-evidence-directory>'
dotnet run --project scripts/CompanionMigrationAudit/PhotoProof/PhotoProof.csproj -- '<stage>/phase3-direct-local.json'
```

Preparation requires the rehearsal marker, seeds the old workspace epoch and captured removal metadata, and stats indexed originals without reading image contents. It atomically publishes `application/local-photos/identities.sqlite`; replay retains the existing map. This SQLite file is durable app metadata, **not a cache**, and must be in the later app-backup inclusion manifest. Keep it when clearing `CacheRoot/photos`.

`PhotoProof` compares all visible photos' IDs/scopes, groups, dates, dimensions and original/sidecar descriptors to the captured NAS metadata. It hashes/downloads only representative requested originals/XMP, reuses/generates requested derivatives, deletes each selected cached thumbnail and forces regeneration with connector reuse disabled. Its private inspection manifest can be used for decoded-pixel comparison; JPEG container bytes can differ. Results are aggregate JSON in the stage; source paths remain in the protected private inspection manifest. It does not call the NAS, modify originals or start Native UI. Unit/HTTP tests cover new indexed photos, moves, file changes, unavailable sources, deletion evidence, range/version/authentication checks and cache behavior.

The direct media worker receives checked original bytes through a pipe and writes only generated JPEGs under the separate cache. Windows packages include it under `media-worker`; the Linux package retains NAS mode. Worker/service installation, scheduled app backups and live cutover remain later phases.

## Phase 4 runtime rehearsal

Build with `scripts/Build-PixelVaultCompanion.ps1 -OutputRoot '<separate-rehearsal-package>' -IncludeLocalReaders`. The Windows package includes a headless Native reader under `readers`; its command branch executes before UI/bootstrap initialization. It uses explicit roots and the existing validated read-only adapters. No new Original-photo store is created.

```powershell
& '<package>/PixelVault.Companion.exe' --prepare-local-service '<staged-phase3-config>' '<package>' '<new-protected-service-plan-directory>'
dotnet run --project scripts/CompanionMigrationAudit/RuntimeProof/RuntimeProof.csproj -- '<plan-directory>/plan.json' --domain
```

The preparation command accepts only a marked rehearsal application root, creates new test-only owner/state/cache locations, pins the PC interface/address and port 8788, and seals package/configuration hashes. Automatic/provider jobs remain off. The domain proof opens **no listener**: it executes the ten real approved readers, checks authored-field preservation, and exercises local photo streaming/conversion. Validated game-save scratch copies are disposable cache, not photo backups. Results and any private diagnostics stay beside the protected plan.

`Install-PixelVaultLocalService.ps1 -PlanPath '<plan-directory>/plan.json'` requires Administrator PowerShell. It installs only the unique `PixelVaultCompanionPhase4` rehearsal service, virtual account, staged permissions, delayed boot/recovery and narrowly scoped Ethernet firewall rule. The package/configuration must still match the sealed hashes. This is not live cutover. Once an administrator has installed it, run `RuntimeProof <plan>`, without `--domain`, for authenticated HTTP save/photo checks, then separately verify actual service identity, SCM crash recovery, lock-screen/boot behavior and LAN/phone access.

Current development status: the administrator installed the rehearsal service, authenticated HTTP save/photo proof passes under its actual virtual account, and the user confirmed phone access to its health endpoint over home Wi-Fi. Cache validation required non-inherited `ReadAttributes` on the two protected evidence containers; no backup-listing/content permission was granted. `Test-PixelVaultLocalServiceRecovery.ps1 -PlanPath '<plan-directory>/plan.json'` is the guarded administrator crash-recovery check; actual crash recovery passed in 8.1 seconds with the owner session, paused automation and readers preserved; phone health also passes while Windows is locked and before sign-in after a real reboot. Windows boot evidence and the authenticated post-reboot save/photo repeat pass; the first connection-health request timed out before successful follow-up/repeat, with cause unconfirmed. Both provider credentials now pass actual packaged-reader calls under the service account, and Phase 5 app-only backup/restore is verified; final live acceptance remains pending. Earlier automatic approval review rejected an agent-launched rehearsal listener with “blocked by policy”; the administrator subsequently installed the service directly. The NAS, connector/recovery and production authentication/keys are unchanged. See [Phase 4 status](../../docs/plans/PV-PLN-WEB-003-phase-4-runtime.md).

## Phase 5 app-only backups

`app_backup.py snapshot <private-plan> [--label <name>]` snapshots only explicit inclusion rules. `verify <archive>` rejects corruption/extra content; `restore <archive> <new-isolated-root>` refuses existing/source-overlapping destinations. `retry <plan>`, `prune <plan>` and `status <plan>` handle verified NAS replication, 14 daily/eight weekly points, protected named/pending points and visible status. SQLite backups include committed WAL data under a bounded cross-database write barrier; provider JSON leases and a stable metadata interval preserve the capture boundary. Capture originals, derivatives, reader scratch, logs, binaries, old backups and OS locks are excluded. Curated artwork, custom covers and map images remain app data.

`CredentialProof` runs the existing packaged provider worker once per approved provider. `Test-PixelVaultServiceCredentials.ps1` temporarily uses the existing rehearsal service identity and restores its original command in `finally`. Both provider checks pass; no recurring work is enabled. Correction: the selected settings already held matching PC provider keys; earlier excluded-key notes were inaccurate.

`RestoreProof <new-restored-root> <service-settings> <private-test-owner-auth>` validates the restored owner, same-PC protected keys, key-file access, curated artwork and the actual offline save/photo domain path. Use a short restore/cache root because the existing decoder working directory has Windows path-length limits. It opens no listener and writes only in the new restore root.

The daily canonical-PC app-data task uses `Run-PixelVaultAppBackup.ps1`, an explicit standalone Python runtime and S4U batch logon at 03:00 local time. It catches up missed runs, prevents overlap and retries queued NAS copies; recent local snapshots are reused during transfer retries. The actual task/replication retry passed with result 0 and next run at 03:00 CST after the DST change. `Register-PixelVaultAppBackups.ps1` requires Administrator PowerShell and can repair only a matching task's trigger. [Phase 5 scope, results and limitations](../../docs/plans/PV-PLN-WEB-003-phase-5-app-backups.md).

Current daily coverage includes real canonical PC data and production Companion owner/state/queue/preferences/keys/service configuration. The first live run returned 0 and its 704-file archive was verified on the NAS. Fresh reconciliation and real owner/key migration have executed; actual phone login and browsing pass. Photo-drive backups remain unchanged; NAS Companion hosting is stopped. Performance and normal-use acceptance remain open.
