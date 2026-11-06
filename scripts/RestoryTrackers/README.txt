Restory trackers — installed November 3, 2026 (America/Chicago)

Scope
20 achievement-linked custom trackers for Steam game 3812600, using existing
PixelVault achievement identities. How did I do this? and All bills paid! are
excluded. The later unverified suggestions and story achievements are excluded.
November 4: removed the five single-action goals Hacking 101, Instant Cool,
Lootbox, Official Partner and Promise of Akiba at the user's request.
No achievement unlock file or provider progress is imported or modified.

Runtime
Private configuration, generated observations and installed reader:
C:\Codex\PixelVaultData\achievement-tracking\restory

The Python standard-library reader checks local saves every ten seconds while
this Windows user is signed in. A per-user Startup shortcut starts pythonw;
an OS lock prevents duplicate watchers. The current watcher starts immediately.
PixelVault's existing Companion service consumes each generated JSON file using
its supported pixelvault.json-file adapter, SaveChanges mode, unpaused.
Its serial refresh queue can take a minute or two to finish all 20 sources.
Native and Companion share the same canonical tracker database.

No PixelVault application binaries, services or global automation settings were
changed. To pause a tracker, use its normal automatic refresh settings. To stop
all Restory file generation, end the pythonw process identified in status.json
and remove the PixelVault Restory Save Reader shortcut from the user's Startup
folder. This leaves all tracker progress and notes intact. Do not stop unrelated
Python processes. To start it again, open that shortcut.

Sources and rules
Game data is read from the installed Unity data bundle. Runtime only reads the
Restory SaveData folder. The Steam autocloud account must match the configured
PixelVault Steam account, and the selected local profile must remain profile 1.
Newest written Story checkpoint is selected. Two seconds of stability, gzip
integrity, supported version/type and all JSON references are checked. Failed,
partial, unknown-version or mismatched-account reads preserve prior snapshots.
Missing fields are errors, not zero progress. Files are published atomically.
Snapshots use save modification time, allowing PixelVault's existing stale-data
and manual-override protections. Raw saves and game files are never modified.

Supported game version: 1.0.033r. Revalidate after a game update.
Platinum Partner: the actual shop contains 23 licenses; this excludes seven
license assets not included in the shop catalogue. Golden Partner counts active
licenses. Official Partner tracks received license objects or active licenses,
matching the delivery registration event rather than only a shop purchase.
Legend of Akiba: 31 entries from DeviceInfoDatabase, as used by CompetitionsApp.
Business Shark: the game's unlock code uses >=100000 yen, despite the localized
description saying 'more than'. Its current-day counter resets the next day.
Displayed counters are capped at each achievement target; source saves retain
the uncapped values. Tracker progress never awards or revokes Steam achievements.
Devices missing English localization use readable asset names as a fallback.

Maintenance
build_catalogue.py is a one-time UnityPy extraction using existing provider
metadata; it does not run at login. restory_reader.py needs only standard Python.
scripts/PixelVault.Restory.Setup uses the shared TrackerRepository APIs to validate,
add definitions, import observations, bind sources and enable SaveChanges. It
refuses duplicate tracker identities and saves an SQLite backup before writes.
It is an installation tool, not a recurring database writer.

Verification
Seven reader tests cover all five real saves, references, known counters,
account/profile boundaries, corrupt/unsupported data, unchanged exports and
daily reset behavior. An isolated copy rehearsed all 25 tracker installations.
The installed Companion headless worker read the 23-license snapshot successfully.
Live installation receipts and source-rule evidence:
C:\Codex\artifacts\restory-trackers

Source/reader status is in restory/status.json. A current checkedUtc with
'unchanged' means the reader is running and the save content has not changed.
After a version/account/read failure, status is 'error' and its message explains
why. PixelVault's progress remains the last successful save observation.
