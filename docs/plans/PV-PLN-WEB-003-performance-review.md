# PC Companion startup performance review

October 31, 2026. Review only; no production code, configuration or service changes. Owner confirmed persistent achievement artwork now works on 0.39.3, but general app opening feels slower than NAS hosting.

## Evidence and limits

Representative domain operations ran against the verified isolated full application restore (316 games, 7,507 achievements, 5,799 photos), using current code and the existing photo drive. The live owner credentials were not used or bypassed. No diagnostic listener or NAS authority was started. Source app roots and live configuration remained unchanged. Measurements are individual PC-side operations, not a phone end-to-end benchmark; do not add sequential probe numbers to estimate parallel HTTP startup. No comparable historical NAS timing is available, so a numeric NAS-versus-PC speed ratio is unsupported.

The live LAN-address HTTP shell/static files responded locally in roughly 4–7 ms. First isolated library read was 3,166 ms; subsequent library reads were 720–809 ms. Tracker-list reads were 213–221 ms; connection status 365–1,339 ms; achievement page 252–603 ms. In a component probe, the first lazy global projection took 1,270 ms and a subsequent unchanged projection after expiry took 433 ms; 30 unchanged metadata reads totaled 350 ms. The first photo-catalogue read in that probe took 2,519 ms, including initial validation; this is not a measurement of every warm photo request.

The PC was under heavy game load: approximately 87% overall CPU on the initial sample; the game used about 13.4 logical CPU equivalents on a 20-thread PC. Disk queues were zero in the sampled windows, C: disk time about 1%, E: about 8%, with approximately 9 GiB RAM available. Contention can inflate timings, but repeated work is present independently in the code. These samples do not measure phone Wi-Fi latency.

Private raw evidence: `Companion Backups/pc-cutover-20261031T201033Z/startup-review/timings.json` and `component-timings.json`.

## Findings, in priority order

1. **Unchanged global data is rebuilt after one second.** `LocalApplicationData.Refresh` computes a filesystem stamp before every cache check, including hundreds of cover files. Once its one-second expiry passes, it rebuilds all achievement, guide, game and photo-summary projections even when that stamp is unchanged. Refresh holds a shared lock; concurrent library/artwork requests can wait behind the rebuild. The former NAS mode loaded a prepared catalogue instead. Prioritize a derived read model invalidated by actual source changes, with a bounded reconciliation check for missed changes. Cache rendered/read-only projections; writes, conflict checks and account-scope authorization must still validate current authoritative state.

2. **Health polling repeats expensive library work.** `LocalRefreshService.Connection` loads recent jobs, reads source bindings and calls `library.Games()`. Direct-local clients poll every two seconds, versus fifteen seconds in the NAS flow. Startup also fetches `/games` alongside `/refresh/connection`, duplicating library construction. Make status a small heartbeat/queue projection, compute supported source groups independently of the full library, and avoid rebuilding game/photo data on idle status checks.

3. **The game list synchronously refreshes photo inventory.** `GameLibrary.Games` calls `ScreenshotLibrary.AvailableAll`, whose direct-local branch calls the full local catalogue. `LocalPhotoLibrary.Refresh` reads the index, loads thousands of identity rows and starts a transaction on its one-second cadence; file sweeps add initial/periodic work. Keep a lightweight photo-count projection for startup and update the derived inventory on changes/background work. Preserve original-file handle/path checks on actual image requests and retain offline/removal safeguards.

4. **Achievement pages perform per-row database access.** `LocalGame` invokes `LocalView` for every row; each opens the guide database and queries ID/groups/revision separately. Batch one game's groups/revisions in one read transaction and reuse the resulting projection. Individual icon lookups also repeatedly enter global projection refresh, so shared projection work should be fixed first.

5. **Rendering waits for all startup requests.** `app.js` waits for session, then antiforgery, then all six main requests before rendering. Consider parallelizing independent bootstrap calls and rendering useful content before optional connection details finish. Preserve authentication, saved layout, drafts and workspace checks. Backend duplicated work is the first priority.

## Recommended next work

Implement findings 1 and 2 first in an isolated package and benchmark the same dataset before/after, including unchanged warm requests and concurrent startup requests. Then separate photo summary work and batch achievement reads. Repeat with the game running and idle, and verify real phone cold/reopen timings. Keep current direct-local authority, photo storage and app-only backups. No return to NAS hosting or new independent writable dataset is needed.


## Implementation — Companion 0.40.0 deployed

The prepared update reuses unchanged application projections, retains immediate file/account validation and checks SQLite data_version on persistent read-only connections for committed index/guide changes even when WAL size/timestamps are unchanged. A 60-second forced reconciliation covers ancillary missed changes. Read connections are disposed with their workspace; the staging photo-preparation helper now disposes its workspace as well.

Connection status reads source definitions/bindings in one scoped read transaction, derives supported group identities from metadata, and avoids full game/photo construction. Source changes and removals remain immediate and other accounts are excluded. Achievement pages read group assignments/revisions in one query rather than opening the guide database for each row; detail/edit/ABA guards retain the authoritative commands.

Game cards use lightweight durable photo counts, which remain available while background photo validation runs. The sole local service maintains the existing photo projection in the background; unchanged photo sources skip full scans using committed database versions and source metadata. File changes, settling imports, explicit cache invalidation and the periodic safety sweep still refresh it. Actual image/original requests retain handle, path, identity and offline safeguards. This introduces no new durable store or schema, and copies no photo originals. Application-data backups retain their existing scope.

All 125 Companion tests pass, including five startup regressions: unchanged projections persist across polling; committed WAL writes remain visible with reused file size/timestamps; title/source/account changes take effect; connection status does not require photo identities; saved photo counts do not rewrite identities and refresh to include indexed captures; batched group versions match detail reads. An additional final source-scope/removal test passes after its assertions were strengthened. An intermediate run exposed a staging workspace disposal leak from the new read connections; disposal was fixed before the passing final run.

The same isolated dataset was exercised sequentially with the published 0.39.3 binaries and prepared 0.40.0 code. CPU load varied, so these are component comparisons, not promised phone timings or a NAS comparison:

| Operation | 0.39.3 median | 0.40.0 median |
|---|---:|---:|
| Warm library | 1299.8 ms | 99.6 ms |
| Connection status | 491.6 ms | 80.2 ms |
| Achievement page | 378.2 ms | 46.9 ms |

Initial library read: 2367 ms before, 246 ms after. Raw comparison and test receipts are retained in the protected startup-review evidence directory. The administrator update succeeded: live health and installation/readiness receipts confirm 0.40.0, fresh worker and healthy photo-background work, and the current automatic setting preserved. Actual phone startup/reopen acceptance remains pending. `Companion Production/Update-StartupPerformance.ps1` saves a named app-only backup, updates the service/package/firewall paths, preserves the current automatic policy and requires fresh source and healthy photo-background checks. Failures restore the prior PC service, never NAS authority. Real phone startup/reopen acceptance remains pending.

SQLite change detection follows [the connection-local data_version contract](https://www.sqlite.org/pragma.html#pragma_data_version); the WAL regression keeps the writer open and preserves file size/timestamps to test this case.


Owner acceptance: startup is confirmed much quicker on the actual phone. Subsequent tracker artwork flicker was traced to repeated profile DOM replacement and addressed in static revision `0.40.0-trackers1` with 16 passing JS/browser tests. Phone tracker-art acceptance remains pending.
