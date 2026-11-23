# Circuit Halo completion covers

Selected November 16, 2026 from the coded achievement-frame concepts. Circuit Halo replaces the Heritage treatment in Native library/profile covers and Companion library covers. Published November 16, 2026 in Native 0.129.005 and Companion browser revision `circuit-halo-20261116` (backend 0.52.3).

## Appearance

All three completion tiers share dark enamel edges, interrupted illuminated rails, square corner brackets, diagonal upper-right/lower-left cuts, a small tier inscription and an inset lower-right icon tab. Gold uses a trophy, emerald a faceted gem and diamond a diamond outline. The labels remain Gold (base), Emerald (base and DLC), and Diamond (base, DLC and PV).

At a 220px cover width, the rail is 7px, the outer cut is 20px and the inner cut is 16px. The icon tab measures 55 by 34px and sits 7px inside the lower-right edge. Geometry scales with the cover between two-thirds and twice these dimensions. The retained Native drawing and shared ornaments contain no running animation.

`scripts/Build-CircuitHaloAssets.py` generates nine `circuit-*.svg` ornaments in Companion's `wwwroot/completion-frames` and the matching `CircuitHaloCompletionOrnaments.g.cs` drawing data. Edit the generator, then regenerate both outputs. The Native project explicitly includes the generated class.

## Behavior

Completion classification remains based on the existing verified saved achievement rules. Frame presentation does not write achievement or tracker progress. Only diamond receives the existing color-preserving foil and perspective; gold and emerald keep ordinary artwork. Reduced motion retains static foil. High contrast uses system colors and a visible `100%` fallback.

Native library/profile cover scenes receive the two cut corners only while a completion frame is present. The clip follows cover resizing, preserves an existing clip, and restores it when completion is removed or high contrast is enabled. Perspective projects the clipped source scene, while the original input host continues to route cover clicks. Completion-case cabinets and Centurion achievement finishes retain their separate geometry.

Companion clips artwork and decorative children rather than the outer link or sibling progress/pin controls. The same markup includes accessible completion text. Cache tokens for the library script and completion stylesheet ensure the new markup and assets load together after deployment.

## Idle frame blinking

The PC cover view previously cleared its verified tier cache on background updates and painted `None` whenever a cover was rebuilt. Its asynchronous saved-achievement read later restored the frame, causing an idle blink.

The decoration factory now paints the last verified tier synchronously, retains the existing drawing for an unchanged tier, and invalidates freshness separately from visible state. Cache identity includes library root, store instance, provider, account and game. Store revisions, refresh generations and unload generations reject stale results. A verified downgrade still removes the frame; missing or failed reads retain the last verified presentation. Unverified provider index flags never create a frame. Loaded callbacks still initialize cover hosts constructed after the decoration factory.

## Verification and publication

Published to both apps November 16, 2026. Native 0.129.005 is installed at `<runtime-root>\dist\PixelVault-0.129.005`; the main shortcut and current junction select it. Companion browser revision `circuit-halo-20261116` is live in `Companion Production/app-0.52.3/wwwroot`. Backend/service configuration and its reader pinned to Native 0.128.021 remain unchanged.

The final scoped Native Release test run passed all 49 completion/foil/classification/showcase/lifecycle tests. Seventeen earlier isolated actual-code WPF render checks and all 27 Companion library/showcase/completion/control tests passed. WPF/browser renders are captured under `<runtime-root>\artifacts\circuit-halo` and `<runtime-root>\artifacts\achievement-frame-concepts-20261116`.

Publication used the released 0.129.000 source plus only the approved Circuit Halo/frame-lifecycle delta, excluding concurrent unshipped Moon Sweepers source. The maintained publisher's `-SourceSnapshotRoot` validates the audited `release-source.json` manifest, maintained-file provenance and every input before/after compilation. All 1,209 packaged input hashes match; all 15 embedded mail icons match their source assets. Strict distribution-layout and isolated packaged-worker bootstrap/invalid-input checks passed.

The web deployment changed only the frame stylesheet, completion markup, two HTML cache references, nine SVG ornaments and their compressed variants. All 66 local/LAN asset requests matched decoded hashes across identity/gzip/Brotli; shell and health checks passed. Unrelated web/backend files, settings and service process were preserved.

After both publications, the requested `<runtime-root>\scripts\Build-PixelVault-AppTesting.ps1` completed. Published and App Testing executables share SHA256 `344918D37CCF606F52E4D387722DB9CC7E1F109E4859DA9A45D7BBA88175775A`. Both data-root sidecars remain `<runtime-root>\PixelVaultData`; previous releases and web backups are retained. No interactive GUI/live migration smoke was performed. Restart the PC app and refresh Companion to load the new frames.

Release evidence: `<runtime-root>\artifacts\circuit-halo-release-20261116\verification.json`. [Published Notion release](https://app.notion.com/p/41a73adc59b681819c28d513bb9c6a63); Smoke Tested remains unchecked.
