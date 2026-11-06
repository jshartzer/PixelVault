# Companion performance review — November 3, 2026

Review of Companion 0.46.2 with browser photo-detail update. The owner reports comparable slowness on phone and desktop. The initial review was read-only. The subsequently authorized implementation and validation are recorded below.

## Evidence and limits

- Live loopback HTTP measurements of the installed HTML, scripts and styles: three requests per resource, advertising gzip/Brotli support.
- Current-source domain benchmarks against an isolated copy of the canonical application databases and metadata: 885 game entries, 37 trackers, 5,937 photos. SQLite databases were copied with the backup API. Image originals were opened read-only; generated images went to a separate diagnostic cache. No reader jobs were run.
- Usually six samples per domain operation; the largest tracker detail has three. Warm medians below exclude the first call. Timings exclude HTTP, JSON serialization and browser rendering unless explicitly stated. Initial process/projection costs are not incurred on every browser reload.
- Four recent 4K screenshots were sampled for image size/conversion. This is a small sample, not a library-wide distribution. Four cover references include a shared source, so one first request was already cached.
- Actual signed-in browser profiling was unavailable because browser access is disabled in Work settings. No production authentication was bypassed. Phone Wi-Fi timings, browser long tasks, layout/paint, memory and end-to-end navigation times remain unmeasured. Node computation timings do not substitute for those measurements.
- Raw evidence and reproducible probe: `artifacts/performance-review-20261103/`. The application/state snapshots and configuration there are private diagnostic data, not release/source-package input.

## Measured costs

| Operation | Result | Uncompressed JSON / image size |
|---|---:|---:|
| Game list | 103 ms warm median | 571 KB |
| Tracker list | 509 ms warm median | 149 KB |
| Connection status | 45 ms warm median | 405 bytes |
| Library preferences | 86 ms warm median | 38 bytes |
| Largest tracker detail: Growth of a Legend, 1,080 items | 2.73–2.85 seconds across three calls | 2.09 MB |
| Death Stranding achievements | 43 ms warm median | 64 KB |
| Timeline, 60 photos | 9.6 ms warm median; 1.10 s first isolated inventory read | 43 KB |
| Recent 30 photos | 8.8 ms warm median | 21 KB |
| First application projection in a new diagnostic process | 2.19 seconds | Not a page-load measurement |
| Uncached screenshot thumbnail generation | 297–1,146 ms | 12–31 KB |
| Uncached screenshot preview generation | 317–469 ms | 359–861 KB |
| Cached screenshot read, including reading image bytes | 1.6–4.1 ms | Same bytes as above |
| Cached cover resolution/read-path lookup | 29–38 ms, excluding response transmission | 22–31 KB in sample |

The installed shell requires 23 HTML/JS/CSS requests totaling 467,896 bytes. All 23 returned `no-cache, no-store, must-revalidate`; none returned a Content-Encoding despite the request accepting compression. Local gzip estimation totals 136,709 bytes, about 71% smaller. This is a transfer-size estimate, not a measured speedup. Warm individual loopback static requests were generally around 1–2 ms; the server delivering these files is not itself a multi-second bottleneck.

## Ranked findings

### 1. Full-library polling repeats expensive work every two seconds — high impact, high confidence

`wwwroot/app.js:90` sets a two-second direct-local timer and passes `refreshLibrary:true`. This bypasses the home-screen early return at `:764`, despite the adjacent comment saying library refresh happens only on entry/resume. `:767` fetches the entire game and tracker lists; `:773` builds home markup and refreshes recent photos again.

For this dataset, a completed home poll returns about 742 KB of JSON across connection, games, trackers and recent photos. At the configured cadence that is approximately 22.3 MB and 120 requests per visible, idle minute, before images and headers. This is a calculated rate from payload sizes and code, not an observed network capture. Polling pauses while hidden and avoids overlapping itself; those protections should remain.

On tracker screens, polling additionally loads the full tracker detail. An expensive detail request therefore repeats while the user reads the screen. Unchanged-data comparisons happen after fetching, parsing and reconstructing the response.

**Action:** keep a lightweight status/change-revision poll; fetch only changed resources relevant to the active route. Preserve freshness on entry/resume, manual refresh, pending drafts and workspace/account changes. Add a direct-local regression test: the existing library-refresh fixture lacks `workspace.mode='direct-local'` and therefore does not exercise the aggressive production timer.

### 2. Photos, covers and banners cannot benefit from normal browser caching — high impact, high confidence

`Program.cs:207` assigns `no-cache, no-store` to API responses. Achievement icons and map tiles explicitly override this; cover/banner (`:299`) and photo (`:378`) endpoints do not. The photo endpoint also supplies no ETag. These image-header findings are from code, not authenticated live response measurements.

`screenshots.js:81,147` removes image sources offscreen and on route reset. Returning to a tile, opening its photo, or moving back to a game can transfer the same bytes again. Cached server reads being fast does not eliminate repeated network transfer and image decoding.

The recent sharpness fix correctly selects the full 2560px preview when the 480px full-frame image cannot cover a square tile at the display density. In four sampled photos, previews totaled 28.6 times the thumbnail bytes. That creates a strong case for caching and additional sizes; reverting to blurry images is unnecessary.

**Action:** use private browser caching with content-versioned URLs and validators. Include original replacement/removal, account/workspace changes and sign-out behavior in the design. Add intermediate derivatives, such as 960/1280px, selected by actual cropped pixel requirements; retain 2560px when genuinely needed. Preserve lazy loading. After this, consider bounded next/previous-photo prefetch and background preparation of recent derivatives.

### 3. Large tracker detail reconstructs history on every read — high impact, directly measured

`TrackerRepository.Commands.cs:112` invokes `VersionEvents` and `LatestInlineReports`. For the 1,080-item premium-orders tracker, each query expands **251,807 historical changes**. Independent SQLite probes measured roughly 1.2–1.3 seconds for version events and 1.6 seconds for latest reports. Those query measurements use Python SQLite and should not be added to the C# timings as an exact decomposition; they corroborate the same hot path.

The scoped history index is already used. The cost lies in expanding history JSON repeatedly, not simply a missing tracker/account index. `app.js:139` awaits tracker detail before changing screens, so this server delay directly delays navigation. The 2.09 MB response adds transfer/parse cost; item definitions are included both in the definition and in states.

**Action:** cache or maintain a derived editor projection keyed by authoritative changes, including undo and definition/rule changes. Reuse unchanged history-derived versions and protection state. Separate the lightweight browsing/polling response from full editing payloads or load detail pages on demand. Preserve conflict/ABA protection, undo, source protection and account scope; do not prune history as a performance shortcut.

### 4. Tracker summaries repeatedly reload all definitions — high impact, directly measured

`TrackerWorkspace.cs:79` loops over definitions and calls `Repository.Load` for each tracker. `TrackerRepository.cs:198` calls `Definition`, which calls `Definitions` again. For 37 trackers, the list rebuilds the full definition collection once for the outer enumeration and again for each tracker, with database connections and state reads.

This endpoint takes about half a second warm and is on both startup and idle polling paths. It also sends per-item update progress every time.

**Action:** read definitions and all required states once per request in a consistent transaction; build summaries/update versions from that snapshot. Serve a small change-version response when unchanged. The repository's existing batched overview/read patterns provide a starting point.

### 5. Startup blocks useful content behind optional requests — medium/high impact, high confidence

`app.js:76–96` awaits session, then antiforgery, then all six bootstrap requests before routing. The 509 ms tracker list is a gate even when the requested screen primarily needs game cards. `/preferences/library` returns only 38 bytes but takes 86 ms because `LibraryPreferences.cs:29` resolves aliases through `GameLibrary.GroupAliases()`, which builds the complete game list again alongside `/games`.

**Action:** render the shell and cached/available library early; load tracker badges and connection status progressively. Parallelize independent authentication bootstrap reads where valid. Avoid recomputing the full game list just to resolve saved pins, and share one versioned bootstrap read model. Retain loading states, authorization and workspace identity checks.

### 6. Versioned static assets are always downloaded, and map code loads on every page — medium impact, measured

All versioned JS/CSS use the same no-store policy as HTML. Leaflet alone is 147,552 bytes, roughly 32% of the measured shell bytes, even when the user never opens a map. Maps code and styles add more.

**Action:** keep HTML revalidated so releases are discovered; cache genuinely versioned/fingerprinted JS/CSS for longer. Ensure every changed asset gets a new identity before enabling long-lived caching. Enable compression for suitable text responses and lazy-load the map package when Maps is opened. The measured 71% gzip reduction supports compression, but does not predict phone load-time savings by itself.

### 7. Artwork requests still scan shared metadata; new images queue for conversion — medium impact

`LocalApplicationData.cs:63–77` computes a filesystem stamp before checking its cached projection. That enumerates achievement snapshots and all 848 cover files under a shared lock. Cover resolution enters this path even for a cached derivative; measured warm cover lookups were 29–38 ms. The exact fraction due to the stamp was not separately profiled.

Photo and cover generators each allow two conversions and launch a new .NET worker process per cache miss (`LocalPhotoLibrary.Cache.cs:10,46`; `LocalArtworkCache.cs:12,78`). First image costs and competing requests can therefore queue. Warm screenshot reads are already inexpensive; prioritize avoiding requests and cold conversions before micro-optimizing their 2–4 ms read path.

**Action:** cache the metadata stamp with bounded reconciliation/change notifications while preserving immediate authorization and actual-file validation. Pre-generate a bounded set of recent/visible images. Only consider a persistent isolated worker after measuring process startup separately from decode/resize and establishing memory/cancellation behavior.

### 8. Secondary navigation/rendering opportunities — lower priority until browser profiling

- Home renders all 715 grouped game cards; grouping/sorting/badge generation took about 7 ms warm in Node, excluding DOM/layout/paint. This does not establish a browser rendering bottleneck. Profile the real DOM before choosing virtualization/content visibility. Avoid regenerating unchanged markup during polling first.
- Returning home renders cached cards promptly, then refetches games/trackers. Existing cached-screen behavior is useful and should be preserved.
- Achievement screens can show their prior snapshot, then fetch again; first rendering also waits for IndexedDB artwork reads (`achievements.js:177–182`). Warm API time is comparatively small. Render text independently of artwork storage if browser traces show that wait matters.
- Map opening chains listing, manifest, pin detail and progress requests (`maps.js:119,134,93,97`). Independent reads can be parallelized, and the current map instance can potentially be retained across navigation. No map endpoint or interaction timings were measured in this review.
- Photo viewing does not prefetch adjacent previews, and every route resets the image source. Add limited prefetch only after image caching/size selection, to avoid making bandwidth contention worse.

## Recommended implementation order

1. Stop full-data idle polling; add change revisions and preserve active-screen freshness.
2. Fix private image caching, then add intermediate photo sizes while retaining the sharpness improvement.
3. Batch tracker summaries and eliminate repeated history reconstruction for unchanged details.
4. Render startup progressively; remove duplicate preference/library work.
5. Cache versioned static assets, compress text and load Maps code on demand.
6. Profile remaining browser long tasks, layout, image decode, transitions and cold-image queues on desktop and phone.

Acceptance should compare the same dataset and routes before/after: cold and warm home, profile, large tracker, achievements, timeline, next/previous photo and Maps; 60 seconds of unchanged visible idle; hide/resume; and navigation under actual updates. Track time to useful content, bytes/requests, server work, long tasks and frame stability. Validate draft retention, progress freshness, undo/version conflict guards, image replacement/removal and account isolation. Preserve original image quality and existing authoritative stores.

## Evidence files

- `domain-timings.json`: domain operation samples, payload bytes and image read/generation timings.
- `history-query-timings.json`: the two history query samples, row counts and query plans.
- `static-http.json`: installed static response sizes, headers, timing samples and gzip estimates.
- `client-computation.json`: isolated pure JS grouping/sorting/badge timings, excluding browser rendering.
- `Probe/Program.cs`, `Probe/Probe.csproj`: the isolated component benchmark.


## Implemented performance changes — Companion 0.47.0

The approved changes cover each actionable review area:

- Direct-local polling uses a five-second status/revision check and fetches changed resources only. Hidden-page pauses, navigation/resume freshness, retry after failure, draft protection and account/workspace checks remain. Twelve unchanged polls in the direct-local fixture made twelve status requests and no full-data requests, versus the previous two-second full-data timer. This is a controlled fixture result, not a production packet capture.
- Covers, banners and photos allow private browser storage and ETag revalidation. Authorization and the current original are checked before a 304; removed/replaced originals cannot be served from a fresh browser cache. HTML and unrecognized asset versions retain no-store. Current JS/CSS filenames receive runtime content hashes and immutable caching; text responses use HTTP compression. HTTPS compression stays disabled. Leaflet loads on map entry.
- Local photos expose a responsive-size capability and use 480/960/1280/2560px derivatives selected by tile crop and display density. Older publication formats retain compatible thumbnail/preview selection. Failed larger images fall back to thumbnails. The viewer prefetches at most two adjacent previews and honors Save-Data. Background work prepares thumbnail/960px derivatives for at most four recent captures, sequentially through the existing conversion limits and cache quota.
- Tracker lists load definitions once in a transaction. Editor reads parse history once and cache up to 32 account-scoped editors until the tracker database changes. Windows revision detection includes file identity/change metadata for both database and WAL, including writes with restored mtime, and holds no file handles between checks. Undo/version/source protections remain authoritative. Non-Windows revision checks hash database/WAL contents. Browsing requests opt into compact states, reusing item definitions; existing full responses remain the default for other clients.
- Authentication bootstrap reads run together; the library renders before optional tracker/status reads. Preference aliases no longer rebuild game totals. Cover-directory stamps are cached with watcher invalidation and a one-second reconciliation bound; original-file validation remains immediate.
- Browser profiling identified layout cost in the 715-card grid. Content visibility skips offscreen layout/paint with width-derived placeholder heights, retaining cards, navigation, controls and scroll anchors. Achievement text renders before artwork storage resolves, with bounded artwork waiting and asynchronous canvas encoding. Map listing, manifest, pins, viewer resources and progress load concurrently; failed progress still permits the map to open and retry. Existing map tiles remain intact during progress refresh.

### Same-data follow-up measurements

Warm medians exclude first calls. These component timings exclude HTTP and JSON serialization; small samples and machine load affect exact values.

| Operation | Before | After |
|---|---:|---:|
| Game list | 103 ms | 35 ms |
| Tracker list | 509 ms | 31 ms |
| Connection status | 45 ms | 21 ms |
| Library preferences | 86 ms | 16 ms |
| Death Stranding achievements | 43 ms | 14 ms |
| Large tracker detail, unchanged | 2,811 ms | 22 ms |
| Large tracker detail, first read | about 2.7–2.9 s | 1.42 s |
| Large tracker browsing JSON | 2,088,704 bytes | 988,374 bytes |
| Cached cover lookup | 29–38 ms | 1.8–5.4 ms |

Warm timeline reads measured 18 ms after versus 10 ms before, and initial catalogue loading remained about 1.15 seconds. These small component timings are not evidence of a timeline-list speedup. The photo improvements target image transfer, reuse and conversion demand. Application projection startup remained roughly 2–3 seconds; it is a service startup cost, not a browser reload cost.

Across four sampled 4K screenshots, 960px derivatives totaled 367,746 bytes and 1280px derivatives 627,084 bytes versus 2,163,930 bytes for full previews: about 83% and 71% less transfer respectively when those sizes satisfy the cropped display requirement. Verified output dimensions were 480×270, 960×540, 1280×720 and 2560×1440. New intermediate generation took 238–712 ms; cached reads took about 1–4 ms. No claim is made that cold decoding itself became faster.

A local Chromium fixture used the real 715 grouped cards with placeholder cover images, no API delays and no background timer. At 390px width with 4× CPU slowdown, time to useful library content was 1,387 → 957 ms, accumulated layout across home/profile/back was 1,274 → 352 ms, and profile transitions were [453,251,260] → [315,105,115] ms. At 1280px with normal CPU, useful content was 282 → 248 ms and layout 173 → 79 ms. These are one comparison run, including browser automation overhead, not physical-phone or Wi-Fi acceptance measurements. Long tasks remain (largest after: 465 ms in the throttled fixture); the entire library still has DOM nodes.

### Validation and remaining measurement limits

- Companion suite: 198 tests passed, including authoritative source rules, undo/conflict guards, account isolation, store movement, externally changed WAL with restored mtime, compact state equivalence, derivative selection, HTTP validators and static compression/fingerprints.
- Related Native tracker/refresh/protection suite: 36 tests passed. The installed Native application is not upgraded by this Companion release.
- 92 frontend unit/browser checks covered startup, polling, navigation, map gestures/recovery, progress refresh, photos, controls, artwork and completion presentation. A map fullscreen assertion was synchronized with the asynchronous fullscreen event; its follow-up and other affected fixtures pass. Existing stale Centurian label/icon assertions were aligned with the already-shipped presentation. Offscreen badge assertions read DOM text rather than visibility-dependent innerText.
- Inspected cover-control screenshots at mobile widths; frame decoration, badges and controls remain visible. Package builds succeeded for Windows and Linux, with a Windows photo worker and source-only archive.
- Physical-phone/network acceptance and authenticated production browser traces remain unmeasured. A persistent conversion worker, retaining a live map across route changes and a separate bootstrap endpoint were conditional design options, not required changes; the measured fixes do not require these extra mechanisms. Further work should follow remaining real-device measurements.

Follow-up evidence: `domain-before.json`, `domain-after.json`, `derivative-dimensions.json`, `browser-render-timings.json`, `performance-final-server-tests.txt`, `performance-native-tests.txt`, `performance-all-browser-final.txt`, `performance-final-followup.txt`, and `build-release-final.txt` under the diagnostic directory. Deployment has a sealed plan and a separate `deployment-verification.json` receipt; package creation alone does not establish that the service was updated.


### Production deployment — November 4, 2026

Companion 0.47.0 was installed successfully at 13:13:51 UTC. The previous elevation request had not started the updater; the retried request completed with exit code 0. The service runs from `Companion Production/app-0.47.0` with its existing service identity and automatic start. Local and LAN health report 0.47.0. Only the configured photo worker path changed; application/account data roots, reader and existing firewall restrictions were preserved.

Independent live HTTP verification checked 23 HTML/JS/CSS resources, including the deferred Leaflet assets: each served asset matches the package and its content hash, current JS/CSS use immutable caching, HTML remains no-store, and gzip responses decompress to the same bytes. The complete checked set totals 474,062 bytes uncompressed and 198,915 bytes with the deployed compression settings (about 58% smaller); this includes deferred resources and is not the initial page transfer total. An unauthenticated library request returns 401. Evidence: `deployment-verification.json`, `live-http-verification.json`, `elevation-result.json` and `update-output.txt`. Refreshing the browser loads the new shell and asset identities. Physical-phone acceptance remains unmeasured.

## November 5 regression follow-up — Companion 0.48.1

November 5 — **Companion 0.48.1 web performance fixes live**. Corrected home-route undefined `nextOrder`; account/source/status reads now query provider identity without loading unrelated catalogues, with fresh deletion and approved-account checks. Defer closed tracker row controls; preserve note/rule drafts and retained cover image nodes; prioritize the first six covers and essential startup requests. Native stays 0.124.000. Verified 190 installed files, served app.js fingerprint, loopback/LAN health, unchanged service settings/data roots/worker paths and firewall restrictions. All 78 targeted checks pass (17 Companion, 56 shared repository via Native, 5 browser). No deployment pending; reload the web app.

Current-data domain benchmarks: warm Tetramon detail median ~119→18 ms, Ghost ~61→4.5 ms, premium orders ~79→3.3 ms. Full initial premium-orders history still costs ~1.3 seconds. Browser fixture reduces initial Tetramon DOM from 5,058 to 1,098 elements and 360 to zero hidden forms, creating them on expansion. Cached cover server reads were ~2 ms; image network/device timing is not established by these tests. Phone-sized browser timing uses 4x CPU throttling and placeholder images, not physical-phone acceptance. Details remain ~2.8 MB before compression; no data or pull-rate text removed. Evidence: `artifacts/web-slowdown-20261105/{comparison,server-before-images,server-after,browser-before,browser-after,deployment-verification,independent-verification}.json`. Live service: `Companion Production/app-0.48.1`; updater `scripts/Update-PixelVaultWebPerformance.ps1`.

