# PV-PLN-WEB-003 — Phase 3: direct PC photos

October 31, 2026. **Implemented and rehearsed on isolated application data. Production still runs on the NAS.** [Migration plan](PV-PLN-WEB-003-pc-hosted-companion.md), [Phase 2 shared stores](PV-PLN-WEB-003-phase-2-shared-stores.md).

## Result

Companion's opt-in `direct-local` mode now reads the Native inventory, photo index, storage families and capture groups. Galleries, recent photos, the timeline, console filters, grouping and downloads use the existing web contracts. No connector upload or copied photo catalogue is needed for a new indexed capture.

The real rehearsal matched **5,797 visible photos across 282 collections**, including every existing ID, game/edition, console, capture date/day, group, width/height, filename and original length. All **18 XMP sidecars** remain downloadable. The identity store retains **5,851 identities and all 54 removals**, including the four identities present only in retained removal metadata. The 50 other removed rows are absent from the current PC inventory; they remain hidden.

Originals and XMP are opened from the existing capture library. **No original or sidecar was copied into Companion storage, staged as a conversion input, or added to a backup.** Sample validation read about 65.4 MB of requested originals, rather than hashing/exporting the entire library. Nothing calls the NAS or opens Native UI.

## Durable metadata and identity

- `ApplicationDataRoot/local-photos/identities.sqlite` is durable application metadata. It contains the approved root, original workspace epoch, old IDs, private relative source mappings, file identities, removals and dimensions. **Include it in Phase 5 app backups.** It is separate from the derivative cache.
- Preparation preserves the original workspace epoch even though switching the workspace manifest to `direct-local` changes the tracker workspace hash. Existing NAS photo IDs remain valid.
- File identity uses the Windows volume/file ID plus creation time. An unambiguous rename/move within its indexed edition/game retains the old photo ID; simultaneous hard-link candidates are not guessed. In-place and atomic replacements retain the path's ID and receive a new source version. Known removal identities/file identities stay hidden on reassignment.
- Missing files, inventory omissions and disconnected drives do not erase identities or infer deletions. Retained missing photos are marked unavailable. Native removal is accepted only from its exact successful `Library delete` log event after the identity was registered, with a healthy approved root, safe parents and absence from disk, index and inventory.
- The migration cannot recover an NTFS identity for a photo already absent at capture time. Its old opaque removal identity remains retained. Final cutover must seed from fresh evidence and review any new unavailable originals.

The preparation command requires the staged rehearsal marker; web requests never initialize the identity schema or create a replacement if it disappears. Its existing-map replay leaves that map intact. This command is not a live cutover tool.

## Reads, streams and cache

The metadata projection refreshes at most once per second from current Native stores. Filesystem notifications identify changed source paths; a notification overflow forces a complete metadata check, and a one-minute sweep is the fallback. This checks file metadata, never bulk image contents. New/replaced files require two matching observations at least one second apart. Interrupted imports stay unavailable until settled. Each actual download/cache request revalidates its opened source, including on a cache hit.

Original resolution is authenticated and scoped by the opaque game/photo ID. The server rejects traversal, UNC/network roots and reparse points through the full ancestor chain. It checks the opened handle's resolved path and file identity/version, then holds that read handle without write/delete sharing until the response completes. Exact bytes, original filenames, range requests and XMP MIME types are retained. The direct-mode `asset` is an opaque source-version token; the HTTP strong ETag is the SHA-256 of the requested bytes, computed only for that requested file. Windows paths and account IDs never enter the photo response.

Only requested JPEG derivatives enter `CacheRoot/photos`. The Windows package includes a separate `media-worker` executable assembly. Checked original bytes flow to it through a pipe, with no original temporary file or unchecked worker-side path reopen. Two conversions may run at once, with a one-minute timeout. The worker shares the exporter's existing full-frame resizing, EXIF orientation and HDR/JXR Reinhard tone mapping. Existing limits remain 64 MiB per original, 1 MiB per XMP and 100 million decoded pixels; thumbnails use a 480-pixel maximum edge and previews 2,560.

Compatible existing connector derivatives can be reused from an explicitly configured PC cache. The source-index identity, dimensions, derivative references and HDR conversion version must agree; the requested original and derivative hashes are verified before reuse. Otherwise the requested size is regenerated. The cache defaults to a 1 GiB JPEG quota, evicts old unused files, respects held streams, and cleans abandoned conversion files. In-flight conversion inputs are bounded in memory. Cache deletion loses no source mapping, removal, group or photo. New captures can have unknown dimensions until their first requested conversion records them. Gallery image URLs carry the derivative version, and existing tiles update when that version changes.

## Validation

- The complete Companion regression suite passed all 107 tests, including five direct-photo cases. These exercise Native groups/dates, authentication/scope, exact originals/XMP, HTTP range/ETag/version handling, new indexed captures without upload, stable rename/restart IDs, cache regeneration, changed bytes with restored size/mtime, unavailable files versus explicit deletion, junction escapes, offline-root preservation, overlapping-root rejection, parallel requests, quota eviction and held source streams.
- All 29 connector tests passed after extracting the shared codec, including existing HDR tone-map/export behavior. The 15 migration/reconciliation tests passed.
- Ten browser/JavaScript photo checks passed, including the real headless gallery/timeline refresh test. Existing tiles now request a changed thumbnail version while keeping filters, paging and grouping.
- The protected `PhotoProof` result verifies the full real catalogue and removal set. PNG, JPG, JPEG and two HDR/JXR samples download with the recorded original hashes. XMP sample bytes match too. Forced regeneration with connector reuse disabled produces valid previews; all ten rebuilt thumbnails/previews have the same dimensions and **identical decoded RGB pixels** as the existing PC thumbnails. Some JPEG container bytes differ, which is acceptable for rebuildable derivatives.
- Windows and Linux Companion packages built in an ignored test output directory. The Windows package includes the media worker; Linux retains the legacy NAS mode without acquiring a Windows UI dependency. No installed binary or startup configuration was replaced.

Representative domain timings from the real rehearsal, not phone/network measurements:

| Operation | Measured |
|---|---:|
| Cold catalogue, 5,797 photos | 2,000 ms |
| Warm catalogue | <1 ms |
| Scheduled metadata refresh | 384 ms |
| Cached thumbnail/preview | 1–15 ms in the sampled requests |
| Forced PNG/JPEG thumbnail conversion | 152–304 ms |
| Forced HDR/JXR thumbnail conversion | 3,107–3,305 ms |

The proof and pixel-comparison JSON, metadata registry and rebuildable cache remain in the protected rehearsal area. The original captured evidence is retained. All 311 files in the original capture manifest still match their recorded hashes. Sampled originals were read in place; production application data, NAS hosting, connector recovery, routing, credentials and drive-backup configuration were not changed.

## Next boundary

**Phase 3's isolated direct-media gate is met.** Phase 4 implements dependable local hosting and background save/provider readers, independent of Native UI. Phase 5 implements and restores app-only backups, including photo identity metadata and excluding media/cache payloads. Phase 6 repeats reconciliation from fresh evidence, preserves live authentication/keys and performs the actual phone/hosting switch. The NAS remains the live Companion authority until then.
