# Higher resolution achievement artwork

## Century achievement badges

Century goals use original PixelVault illustrations in a shared engraved black-and-ivory style, with weathered silver frames and restrained cyan accents. Each goal has an unlocked badge; locked goals continue to use the shared locked badge. `all-interviews-unlocked.png` depicts a remote interview, and `all-mails-unlocked.png` depicts a courier mail pouch and correspondence. Both are 512×512 so they retain detail in the profile shelf and achievement views without oversized downloads. Companion's Century image cache identity must be incremented when these bundled resources change.

Native 0.085.000 and Companion 0.20.0 use a shared artwork resolver. Six verified 240×240 Death Stranding Director's Cut PS5 trophy PNGs replace their matching 64×64 Steam images: Homo Faber, Fount of Knowledge, Trail-Blazer, Master Builder, Growth of a Legend and The Past Guides the Present. Public source URLs and SHA-256 hashes are recorded in `src/PixelVault.Tracking/Artwork/manifest.json`. These images are bundled, so both apps work offline. Artwork remains the property of its respective rights holders.

Every Native achievement badge uses these replacements when available: achievement list/grid views, detail windows, hub cards, pins, guides, game profiles, tracker cards/headers and achievement tiles in the photo timeline. The shared loader reads later imports from the application's resolved data root, prefers them before cached provider thumbnails, and retains the source image resolution. Achievements use their provider-supplied locked artwork until earned, including when progress is unavailable. Achievement views apply higher-resolution replacements only after earning the achievement. Tracker cards and headers always use the unlocked art and prefer high-resolution replacements regardless of achievement state. When no locked artwork is supplied, Native grayscales the original provider icon. Stored older Steam CDN links resolve to the same artwork. Companion syncs both original unlocked and locked icon URLs and uses its state-aware icon endpoint for achievement lists/details, with a separate authenticated unlocked-art endpoint for tracker cards/headers. Explicitly selected generic tracker icons take precedence. The resolver matches the original provider artwork URL; it does not change achievement identities, unlock state, tracker links, progress, notes or history.

## Import another game

On **Native 0.085.000**, open the game profile and use **Import achievement artwork…** beside the **Achievements** heading. The action becomes available after the game's Steam achievement definitions load. It uses only that game's achievement names, descriptions, identities and original image URLs; it does not pass account credentials or player progress to the importer.

Choose the matching Exophase trophy list URL once (the dialog remembers it), select **Preview matches**, then **Import matched images**. Starting with 0.082.000, run Preview again on the same source to re-evaluate older entries held only because their descriptions differed. The source selector also accepts a browser-saved HTML page, a reviewed JSON mapping, or bundled reviewed artwork when available. Death Stranding's reviewed source is already supplied. The preview/results panel shows accepted, review and failed entries. A progress bar animates while preparing/reading the source, then shows the processed-image count and current achievement during downloads and checks. Failed images also advance the processed count. Completion appears after installation; stopping or a fatal error stops the animation without showing 100%. Source changes invalidate the preview; import uses its accepted snapshot. Open Native views refresh immediately after installation. Imports are installed on this PC, then connector 0.19.0 automatically publishes approved-game artwork to the paired NAS when automatic updates are enabled.

The importer is embedded in the app. Python with Pillow must be available on the PC; missing dependencies show an actionable message. **Stop import**, Escape, or closing the dialog cancels the process. Downloads are staged per run, then merged into the active manifest only after validation. Other games survive the merge. Cancellation and runs with no successful images leave installed artwork intact; valid images from a partially successful batch can still be installed. Reports and source snapshots are under `<data root>/achievement-artwork/imports/<Steam app ID>/<run ID>/`; shared downloads remain in `.downloads/`. Imports are serialized in the app to avoid simultaneous manifest replacement. Do not run the command-line importer against the same active manifest concurrently.

Run `scripts/Import-AchievementArtwork.py` on the PC with Python and Pillow (`python -m pip install Pillow`). Select the correct game edition and its Exophase trophy list once. The importer reads a Native achievement-hub JSON cache (or an exported array of achievement rows), automatically pairs trophy names with larger image URLs, and matches **unique achievement names** within the selected game. It first tries exact titles, preserving punctuation while normalizing case, whitespace and typographic quote/ellipsis differences. If no exact title exists, it tries a unique match ignoring punctuation. This keeps distinct titles such as `BB...` and `"BB"` separate. Descriptions remain reference text; differences or missing descriptions do not block a match. Duplicate names on either side, missing names and unmatched entries go to `import-report.json` for review. It currently imports PSN trophy PNGs for Steam achievements; games without comparable trophy artwork retain their provider images.

First plan an import (no image downloads or active manifest changes):

```powershell
python C:\Codex\scripts\Import-AchievementArtwork.py `
  --catalogue C:\Codex\PixelVaultData\achievement-hub\<cache-file>.json `
  --game <Steam-app-ID> `
  --page-url https://www.exophase.com/game/<correct-edition>-psn/trophies/ `
  --output C:\Codex\tmp\artwork-import
```

Then repeat with `--apply` to download accepted matches. For a blocked page, save its rendered trophy list HTML from your browser and add `--html C:\path\trophies.html`. Saved pages must retain either the original image links or image URLs. The importer stops on access restrictions; it does not bypass authentication, bot checks or redirects. There is no verified supported public API dependency. Requests are serial, spaced at least one second apart, cached and bounded; do not schedule frequent rescans.

To accept reviewed exceptions, supply `--mapping C:\path\reviewed.json` instead of `--html`. This is an explicit mapping array, for example:

```json
[
  {
    "title": "Homo Faber",
    "achievementId": "062",
    "sourceUrl": "https://m.exophase.com/psn/awards/l/8714e15.png"
  }
]
```

Review the game edition, name, description and artwork before creating mappings. IDs must match the selected game's cached achievement rows, and names must agree. To reproduce the six already reviewed mappings, use `--game 1850570`, the Director's Cut trophy page and `--mapping C:\Codex\src\PixelVault.Tracking\Artwork\manifest.json`.

## Install imports

Use `<data root>/achievement-artwork` as `--output` to install directly. For the PC this is `C:\Codex\PixelVaultData\achievement-artwork`. To install on a mounted NAS share, run the same import with `--output <mounted Companion data root>\achievement-artwork`; the verified downloads can be reused by copying the staging folder's `.downloads` cache first. Running against each target merges with that target's existing manifest, retaining other games. The PC connector (0.19.0+) now synchronizes PC imports to the NAS automatically. Manual installation to each data root is only needed for installations without an enabled, paired connector.

For an SSH-only NAS, copy a staged artwork folder as a batch into a temporary directory, then merge its manifest with the existing `<Companion data root>/achievement-artwork/manifest.json` by `originalUrl`. Copy the immutable hash-named PNGs before atomically replacing the manifest; retain existing entries for other games. Grant the container's UID/GID read access. Never copy the private achievement-hub catalogue to the NAS or put it in a public source package. The current six replacements are bundled in both builds and require no separate NAS import.

Each replacement must decode as a square PNG, be larger than the actual downloaded Steam original, fit within 2048×2048 and 768 KiB, and have a recorded SHA-256 hash. Asset names are content hashes. The importer writes assets first, then atomically merges the manifest; failed downloads retain the prior entry. `import-report.json` records applied, skipped/review and failed entries, and returns nonzero for failures/no accepted matches. Plans may contain review entries alongside accepted matches; review entries are never automatically applied.

Apps read imports when an image is requested; reopen the Native tracker view or reload the browser after importing. Companion icon responses require cache revalidation. Invalid/missing imports fall back to bundled artwork, then the original provider image and the existing vector fallback. No app restart or database migration is required for later imports. Artwork is published after the achievement metadata on the next connector pass (normally within 30 seconds), and active web achievement/tracker views pick it up on their next safe refresh (normally another 15 seconds). Keep the PC connector running and automatic updates enabled. No original save files or player credentials are sent with artwork. Only approved game/achievement identities whose original provider icon matches the NAS snapshot may receive images. Paused/offline updates retry later; missing local entries do not delete NAS artwork. Content hashes and a NAS inventory make retries/restarts idempotent and allow missing/corrupt NAS files to be repaired.

Verification: Python importer tests cover saved HTML pairing, missing artwork, name matching despite description differences, duplicate-name ambiguity, explicit mappings, allowed sources, downgrade rejection, repeat imports and failure preservation. Shared resolver/service tests verify hashes, full image dimensions, game scope, imported precedence and corrupt/path-invalid fallback. Native UI tests verify 240-pixel artwork and manually selected icon precedence.
