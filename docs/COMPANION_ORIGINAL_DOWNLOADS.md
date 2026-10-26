# Companion original photo downloads

Implemented on NAS Companion0.10.0, October26,2026. This extends [Companion Screenshot Library](plans/PV-PLN-WEB-002-companion-screenshot-library.md) without advancing the variable-layout increment.

## Using it

Open a photo, tap to show information, then choose **Download original**. The control displays the original file size and remains hidden with the other photo information initially. Gallery/viewer browsing still uses the lightweight JPEG derivatives; originals are fetched only for a requested download. The full original is stored on the NAS, so the PC can be off.

The download is the **unchanged source file**, including its original pixel resolution, encoding/bit depth, embedded EXIF/XMP/PNG text/ICC data and filename. No resizing, metadata stripping, orientation rewrite or image re-encoding is performed. HTTP attachment delivery supports range requests, a content-hash ETag, correct original media type and the source last-write time as Last-Modified. A download is bound to the original asset hash shown by that viewer snapshot; a changed original asks for a refresh.

On iPhone, Safari downloads are available in **Files → Downloads** ([Apple Support](https://support.apple.com/en-us/102440)). This is a file download, not a direct Photos-library write. The OS controls a downloaded file's filesystem dates. PixelVault-only groups and other database fields remain app data; they are not injected into the original. No claim that an app-only field or an absent capture timestamp is embedded in the image.

## Storage and synchronization

- Current explicit pilot:233 Director's Cut photos across Steam121/Xbox102/PS5 10; all18 group assignments retained.7videos excluded as before.
- Original store adds233 content-addressed files,1,607,576,615bytes (~1.50GiB), alongside the unchanged408 derivatives/94,969,378bytes. Originals have a separate4GiB quota and64MiB per-file cap. Staged bytes are streamed through a bounded buffer and verified before being renamed into the store. Interrupted uploads never become downloadable files.
- The manifest becomes visible only after every referenced derivative and original is verified. Re-preparing the same content is idempotent. Older photo tools cannot strip populated Original descriptors from matching records.
- Metadata descriptors are optional/null-omitted to retain legacy serialization. They carry file name, byte length/type/hash and source last-write ticks. The PC-only original-source map contains local paths and stays under the protected connector data folder; it is not published.
- The one-shot0.10.0 pilot tool reads original bytes and rejects source changes before upload. It does not make another full PC copy of the original collection. The installed automatic connector remains0.8.0; it has no media sync loop. New captures/original changes still require explicit photo sync. No deletion mirroring/automatic eviction.

Operator command: `dotnet "C:/Codex/Companion Preview/connector-0.10.0/PixelVault.Companion.Connector.dll" --sync-screenshot-pilot "C:/Codex/PixelVaultCompanionConnectorData/connector.json" steam 1850570 500`.

Owner endpoint: `/api/v1/screenshots/steam/1850570/{photoId}/original?asset={contentAddressedAsset}`. Existing owner authentication, same-workspace allowlist and private data storage apply. Uploads use existing PC bearer pairing and respect library/automatic pause. Original downloads remain readable while the PC is offline, paused or revoked. Raw storage is never a static public directory.

## Verification

69server+9connector+26JS cases pass. New coverage exercises an8MiB streamed upload, partial upload rejection/cleanup, atomic activation, downgrade protection, Unicode Content-Disposition filenames, correct length/type, exact bytes, range206, stale asset rejection, authentication and read after pause/revocation/service restart. Export coverage adds embedded PNG tEXt and verifies unchanged original bytes and dimensions, with paths/settings absent from the published catalogue.

Actual phone-sized browser UI downloaded a PNG and JPEG with their original filenames; downloaded SHA256 values match their source asset hashes. Info initially hidden, no overflow, and existing subtle close control retained. Proof: `C:/Codex/tmp/companion-download-original-mobile-0.10.0.png`. Physical iPhone touch/download behavior is not claimed from a desktop viewport test.

The actual PC tool uploaded all233 originals, downloaded and hashed all233 from the NAS, checked all408 derivatives and compared every metadata row. NAS-only verifier with **network disabled** read and verified all641 media files while the normal connector was stopped. Original source audit digest remains`872DC7122288B1105E05577E701436DAE152A4220791B33D1D8FEE554B214A51`; originals unchanged.

All1143 prior stable entries matched before publication. After publication,1142 remain identical; only the screenshot catalogue changed, with a per-row comparison confirming every existing photo field unchanged and only Original descriptors/export time added. Exactly233 original files added; existing owner, tracker/achievement data and derivatives remain intact. Normal connector0.8.0 restarted.

Pre-upgrade NAS/protected-PC backup:`pre-0.10.0-20261026T191248Z.tar`; SHA256`F4D1541D7F6AB4DD926D1B3BB0A4A397ECC53131C34C0A34FF671E64C87E7EE2`. Protected PC directory:`C:/Codex/Companion Backups/pre-0.10.0-20261026T191248Z/`. NAS image:`sha256:53924ebb8ca3b4bd98e16e61601c9e6d47a940517d225e88c8dccf8f5a660d5a`. Future full media backups include the originals; downloads are not a replacement for backup policy.
