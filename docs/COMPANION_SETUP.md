# PixelVault Companion — setup and tracker workspace

**Recent photos (0.13.0):** The home page shows up to 30 recent saved photos from the enabled gallery. Swipe sideways, use the arrows, or focus the strip and use arrow keys. A thumbnail opens the existing full-screen viewer; dismissing it returns to the same home and strip position. Tap for photo info and the original download. Browse photos opens the enabled game gallery for now; the full timeline is the next increment. This does not add automatic screenshot syncing or broaden the one-game pilot.

**Photo groups (0.12.0):** Turn on Group photos to use your saved desktop groups within each capture date. Tap a group heading to collapse/expand it; Ungrouped photos remain available. The grouping preference is remembered by this browser; collapsed sections, filters and loaded pages are retained during app navigation, and reset on a full reload. Counts say loaded because older pages may add more photos. The viewer has no X: swipe down, press Escape or use browser Back. Tap the photo for info/original download.

**Screenshot layout (0.11.0):** Tile size offers Comfortable and Compact, remembered by this browser. Small and large squares fill date sections, with adjusted final rows. Chronological order, console filters, full-screen viewer and original downloads remain available. Existing photo-group controls are now available in 0.12.0.

**Original photo downloads (0.10.0):** Open a photo, tap to show information, then Download original. The original file/filename/embedded metadata remain intact; no resize/re-encoding. Available for all233 pilot photos with PC off. iPhone Safari downloads appear in Files → Downloads. OS filesystem dates and PixelVault-only database groups are distinct from embedded image metadata. [Details](COMPANION_ORIGINAL_DOWNLOADS.md).

**Screenshot pilot (0.9.1):** Open Death Stranding Director's Cut → Screenshots. Browse capture dates, filter All/PS5/Steam/Xbox and load additional photos. Tap a square for the uncropped photo; tap again for information. Swipe down, Close, Escape or Back returns to the same gallery position. Horizontal swipes/arrow keys navigate loaded photos. Other games remain Coming soon. Existing233-photo NAS pilot is available with PC off; recurring new-photo sync is not implemented yet.

**PixelVault Companion** is the overall web application. Tracking is its first feature area; maps, achievements, the media library and other PixelVault features will use the same application. Version **0.13.0** starts with a searchable cover grid and compact game profiles with banners, available playtime/achievement counts, live tracker totals and reorderable sections. Trackers support saved progress, notes, conflict review, history/undo and a Content reader for privately stored reference text; Achievements now has searchable grouped cards or lists, individual guides and two-way category/DLC editing; Screenshots opens the enabled Director's Cut pilot gallery; other games say **Coming soon**. If a phone still displays 0.3, open [the fresh home page](http://192.168.1.99:8787/?v=0.13.0) once; the old HTML was cached. The current shell and scripts prohibit caching across updates.

**Current installation:** Companion now runs on the QNAP at **[http://192.168.1.99:8787](http://192.168.1.99:8787)** with the existing Companion login and data. The user selected home-network-only HTTP access for now. The PC preview is stopped and its current launchers defer to the NAS. See [NAS installation, backup and recovery](COMPANION_NAS.md). The PC can be off for browsing and manual tracking; automatic save/library/tracker updates require it awake and signed in.

The **desktop connection preview** signs into the same Companion workspace as the browser, now hosted on the NAS. Existing local tracker windows, game readers and automation still use Desktop's original database. **The final native storage switch remains pending. The paired PC connection supports automatic save refresh, indexed-library sync and add-only desktop tracker publication; see [PC connection](COMPANION_CONNECTOR.md).** See [PV-PLN-WEB-001](plans/PV-PLN-WEB-001-nas-web-companion.md).

## Optional development review on this PC

For the migrated workspace, these launchers print the NAS URL instead of starting a second writable copy. Use a separate explicitly prepared development data root for a new local rehearsal.

Build the Windows/Linux previews and source package:

```powershell
pwsh -File C:\Codex\scripts\Build-PixelVaultCompanion.ps1 -IncludeDesktop
```

Build output is under `C:\Codex\Companion Preview`. Persistent review data is separately stored in `C:\Codex\PixelVaultCompanionData`, outside every build-output folder. Rebuilding does not replace that data. The Windows launcher needs the .NET 10 runtime and binds only to this PC:

```powershell
pwsh -File "C:\Codex\Companion Preview\Start-Companion.ps1"
```

Open `http://127.0.0.1:8787`. On first start, read `C:\Codex\PixelVaultCompanionData\setup-token.txt`, enter it in the setup screen, and choose an owner name and a password of 12–128 characters. The token is not logged or served by HTTP. There is no default password. The review workspace is prepared without a QA owner; test credentials belong only to temporary QA directories.

## Try it from a phone on home Wi-Fi

The ordinary launcher accepts connections only from this PC. On a phone, `127.0.0.1` refers to the phone itself. To opt into a home-network HTTP preview, stop the existing Companion server with **Ctrl+C**, then run:

```powershell
pwsh -File "C:\Codex\Companion Preview\Start-Companion-Phone.ps1"
```

Approve the one-time Windows administrator prompt. The launcher detects this PC's active home-network IPv4 address, permits that exact host, and listens on it alongside PC loopback. It prints a **Phone on home Wi-Fi** URL; open that URL in Safari while the phone is on the same home Wi-Fi. On the network checked October 25, this PC is `http://192.168.1.69:8787` (the NAS remains a separate device).

The firewall allowance covers only this address/port, the .NET host application, the selected network adapter, and clients on the attached local subnet. It works even when Windows labels the home adapter Public; it does not reclassify that network or disable the firewall. The administrator prompt is skipped on subsequent launches when that exact allowance already exists. To choose among multiple active adapters, add `-ListenAddress <this-PC-home-IP>`.

The normal launcher stays PC-only. From source, the equivalent is `scripts/Start-PixelVaultCompanionPhone.ps1`. These switches are for an explicit home-network HTTP preview. The current NAS installation also uses home-network HTTP at the user's request; the HTTPS configuration below is for a future proxy/domain deployment. Your existing owner account and tracker workspace are reused.

## Connect from Desktop

Keep the Companion service running, then open the desktop connection preview:

```powershell
pwsh -File "C:\Codex\Companion Preview\Start-CompanionDesktop.ps1"
```

Use `http://127.0.0.1:8787` on this PC and the owner name/password created in the browser. The launcher opens only the Companion window; it does not open the local tracker store or start game-save readers/automation. A newly built normal PixelVault app also offers **Trackers → Open Companion…**. The published native version and shortcut are unchanged by this preview.

Choose a tracker and item, edit its completion/count or note, then save. The browser and this window use the same API/store. **Reload** retains unsaved edits and their original versions. Conflicts show both values with **Use saved value** or **Save my edit**. **History** supports guarded undo. Game-confirmed completions retain the ordinary check-off protection; full local manual-override/source-review/authoring workflows remain local and are not silently redirected.

If a request loses its confirmation, controls stop accepting edits. Reconnect or sign in again if needed, then choose **Retry unconfirmed save**. It resends the same request ID and cannot duplicate its history entry. A disconnected view labels cached values and keeps drafts visible; there is no offline queue or local database fallback. Drafts/cookies are memory-only, so closing with unsaved/unconfirmed work prompts before discarding it.

Only the successful server destination and explicit HTTP option are remembered in `%LOCALAPPDATA%\PixelVault\Companion\companion-connection.json`; no passwords or cookies are stored. Use an HTTPS root address for NAS access. LAN HTTP requires explicitly selecting **Allow HTTP on my home network**; certificates are validated and redirects are not followed. The connection displays the server-approved accounts, independently of the desktop's selected provider profile.

This is a prepared-copy rehearsal. Changes to local Desktop trackers and to Companion are separate until the final reviewed cutover. Do not merge them by copying either database over the other.

## Prepare a separate data copy

With the destination Companion stopped, run this **once for a new destination**:

```powershell
pwsh -File C:\Codex\scripts\Prepare-PixelVaultCompanion.ps1 `
  -DesktopDataRoot C:\Codex\PixelVaultData `
  -DataRoot C:\Codex\PixelVaultCompanionData
```

Preparation opens Desktop's tracker database read-only and uses SQLite's backup API, including committed WAL data. It checks integrity and hashes every application table inside the source snapshot against the copy before accepting it. Definitions, links, all accounts' state, history, source bindings, automation settings and import markers are preserved. No game saves are read. The destination must be outside the source data folder and must not already contain a tracker database or workspace manifest; preparation refuses to replace existing progress.

Private `workspace.json` approves one account per provider for this single-owner installation. When multiple accounts exist for a provider, supply `-AccountsFile <private-json-file>` containing an explicit provider-to-account map. Preparation checks those accounts against the copy. Other accounts remain preserved, but browser parameters cannot select them. Keep this manifest and database out of Git/source packages.

To run from source with the .NET 10 SDK:

```powershell
pwsh -File C:\Codex\scripts\Start-PixelVaultCompanion.ps1 `
  -DataRoot C:\Codex\PixelVaultCompanionData
```

`-Demo` explicitly selects fictional, read-only examples. Without a prepared workspace or `-Demo`, the authenticated view is empty.

## Editing behavior

- Expand an item. Binary items offer a completion action; count targets have a count editor. Named columns retain independent progress and notes. Game-confirmed completions are protected using typed saved history/provenance; a source label alone cannot establish that protection.
- **Save note** changes only that note. **Use source progress** removes a manual override without replacing observations. When no source value is known, **Clear manual progress** returns the item to unknown.
- Edits carry field versions and unique request IDs. Stale saves cannot silently replace another window's edits; retrying an acknowledged-but-lost request does not duplicate history. Unrelated count/note edits can save independently.
- Conflict review shows the saved value beside the draft. Choose **Use saved value**, or explicitly **Save my edit** after reviewing. Drafts stay in browser memory across ordinary updates/filters, not localStorage. Leaving with an unsaved draft prompts the browser's normal warning.
- **History / undo** rechecks affected fields transactionally, preserving unrelated newer edits. A changed field may require undoing its newer change first.
- Search/filtering preserves full totals. Completion columns count each qualifying record once for capped category goals. Rendering is bounded for large catalogues.

The banner identifies the separate workspace. Browser navigation never starts a save reader, provider unlock call, desktop synchronization or source automation.

## QNAP TS-464 / Container Station

Target: `linux/amd64`. Build output includes `PixelVaultCompanion-0.5.2-source.zip` and a Linux publish folder. The source archive contains no tracker database, account map, passwords or keys. It is a build package, **not a Docker image archive**.

On a Docker-capable host, extract it and run from its root:

```text
docker build --platform linux/amd64 -f deploy/companion/Dockerfile -t pixelvault-companion:0.5.2 .
docker save -o pixelvault-companion-0.5.2.tar pixelvault-companion:0.5.2
```

The NAS Docker CLI was used to build and run this image. Docker is not installed on the development PC. A source ZIP remains build input; the installed image has a separate NAS release archive. QNAP documents image import and Compose applications in [Container Station 3](https://www.qnap.com/en-us/how-to/tutorial/article/how-to-use-container-station-3).

The current explicitly selected home-LAN installation uses [compose.home-lan.yml](../deploy/companion/compose.home-lan.yml). For a later HTTPS/proxy installation, use [compose.qnap.yml](../deploy/companion/compose.qnap.yml) and configure the actual proxy/hostname:

1. Replace the example host folder with a dedicated NAS-local directory, mounting the prepared data folder at `/data`. Do not open a live SQLite database across SMB/NFS.
2. Grant the non-root container user (`app`, UID 1654 in the official image) read/write/traverse access, keeping the folder private. Use a deliberate matching QNAP UID/GID override if needed; do not make the app privileged to fix permissions.
3. Choose an available host port. `AllowedHosts` must contain the actual NAS IP/hostname; the example saved IP is unverified.
4. The original QNAP example enables demo mode/HTTP. For HTTPS, configure the proxy, set `Companion__AllowHttp: "false"`, set `Companion__DemoEnabled: "false"`, and set `Companion__TrustedProxies__0` to the actual reverse-proxy source IP. Include the HTTPS hostname in `AllowedHosts`. The current home-only deployment explicitly enables HTTP and real data at the user's request; it has no Internet access setup.
5. Retrieve the first-owner token privately in File Station and use your own account. Do not upload the temporary QA directory or its credentials. No library/game-installation mount is required yet.

`/health` exposes readiness/version; tracker data requires login. Cookie sessions use HttpOnly/SameSite protection, writes require antiforgery tokens, and authentication attempts are rate limited. Keys persist under `/data/keys`, protected by NAS volume/access settings.

## Backup, update and recovery

For a consistent preview backup, stop the service, copy the **entire persistent data folder** to a separate protected location, then restart. Do not copy only a running SQLite main file. Include the tracker DB, manifest, owner DB and keys; retaining keys preserves sessions.

Back up before updates and use separately tagged images with the same data mount. Unsupported future owner-database schemas are rejected; tracker changes remain additive and compatible with the current Desktop schema. An incompatible future migration requires its matching earlier image and backup for rollback.

After Companion accepts edits, reverting to an older Desktop snapshot would discard them. Finish native cutover, reconciliation of subsequent edits and connector pairing before treating this copy as the installation's central source of truth.

## Verification limits

Tests cover authentication, persistent sessions, account isolation, verified source copying, progress/notes/source clearing, stale writes, retry/undo guards, native provenance and a .NET 8 client alongside the browser. The actual-data rehearsal copied five trackers, 1,267 state rows and 52 history entries. Browser QA verified saves, reload persistence, retained drafts, both conflict choices and undo of all four QA changes in the temporary copy.

Docker/NAS runtime, full backup restoration/comparison and container restart are now verified. The user confirmed iPhone access to the PC-hosted preview; the NAS URL still needs their handset confirmation, and Home Screen behavior/full NAS reboot were not tested. The explicit native Companion connection is implemented; full native routing, periodic PC observations, login recovery and web catalogue management remain planned. The published native version and original native tracker store are unchanged.
