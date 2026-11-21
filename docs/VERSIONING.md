# PixelVault versioning

## Rules (source of truth)

Bump sizing and the **`M.AAA.BBB`** string format live in **`docs/POLICY.md`** under **Versioning Policy** (three segments, **`AAA`** / **`BBB`** zero-padded to three digits pre-1.0 and beyond).

## Release checklist

When you ship a new build, work from `C:\Users\justc\.codex\worktrees\pixelvault-test-source\Codex` as recorded in `CURRENT_BUILD.txt`. `C:\Codex\src` is an older checkout; guarded publish scripts reject it. Updating source/docs alone is not a release.

1. Set **`const string AppVersion`** in `src/PixelVault.Native/PixelVault.Native.cs` to the new version (for example **`0.998.005`** or **`1.000.000`** — never go back to the legacy two-part **`0.xxx`** form for new releases).
2. Prepend a **`## M.AAA.BBB`** section to **`docs/CHANGELOG.md`** describing user-visible changes.
3. Run **`scripts/Publish-PixelVault.ps1`** from the repo root (omit **`-Version`** to publish the version taken from `AppVersion` in source):

   ```powershell
   cd "C:\Users\justc\.codex\worktrees\pixelvault-test-source\Codex"
   .\scripts\Publish-PixelVault.ps1 -Configuration Release -OutputRoot "C:\Codex\dist"
   ```

   Use **`-Force`** if `dist\PixelVault-M.AAA.BBB` already exists and you intend to overwrite it.

4. After verifying deployment, update **`docs/CURRENT_BUILD.txt`** in the maintained source and synchronize `C:\Codex\docs\CURRENT_BUILD.txt` with the new version and deployed executable path.
5. Update **`docs/HANDOFF.md`** in the **Current Published Build** section to match.
6. Point **`C:\Codex\PixelVault.lnk`** at the verified new `PixelVault.exe`. The publish script creates a separate shortcut in its source checkout.
7. If you track releases in Notion, follow **`docs/DOC_SYNC_POLICY.md`** (Releases entry, smoke status).

The publish script writes **`dist/PixelVault-M.AAA.BBB`**, refreshes the **`dist/PixelVault-current`** junction, copies `CHANGELOG.md` into the dist folder, and warns if the repo changelog lacks a header for that version.

**Pruning older `dist` folders:** By default **`-KeepLatest 10`** removes older **`PixelVault-*`** directories after a publish. Sorting is by **folder last-write time** (newest kept), not by **`System.Version`**, so **`M.AAA.BBB`** renumbers such as **`0.075.000`** are not mistaken for “older” than **`0.989`**.

## Audited source snapshots

When a release must isolate approved changes from unrelated work in the maintained checkout, `Publish-PixelVault.ps1 -SourceSnapshotRoot <snapshot>` accepts an audited `release-source.json` manifest. Run the publisher from the maintained source. The manifest records baseline release, target version, complete relative input paths/hashes, maintained-file hashes and audited partial edits. The publisher rejects missing, extra, changed, linked or escaping inputs, verifies baseline/version/source ownership, and rechecks inputs after compilation. It bundles the exact snapshot and provenance; it does not change maintained-source ownership. Keep the packaged snapshot immutable and record later deployment status in the maintained/runtime docs. Circuit Halo 0.129.005 is the reference release under `artifacts/circuit-halo-release-20261116`.

## App testing folder (not a release)

The fixed App Testing helper copies the current **published** release from `C:\Codex\dist\PixelVault-current`; it does not compile a source checkout. It retains existing testing data/settings and does not publish a new version or repoint the main runtime shortcut.

```powershell
pwsh -File C:\Codex\scripts\Build-PixelVault-AppTesting.ps1
```

Default output: **`C:\Codex\App Testing\`**. Optional `-OutputPath` selects another testing folder. `-IncludeSourceBundle` includes the release's source snapshot; it does not make that snapshot the maintained source.
