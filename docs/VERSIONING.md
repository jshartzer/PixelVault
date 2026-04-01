# PixelVault versioning

## Rules (source of truth)

Bump sizing and product meaning of versions live in **`docs/POLICY.md`** under **Versioning Policy** (decimal versions, how large a change should bump by, and what counts as a release).

## Release checklist

When you ship a new build:

1. Set **`const string AppVersion`** in `src/PixelVault.Native/PixelVault.Native.cs` to the new version (for example `0.827`).
2. Prepend a **`## x.yyy`** section to **`docs/CHANGELOG.md`** describing user-visible changes.
3. Run **`scripts/Publish-PixelVault.ps1`** from the repo root (omit **`-Version`** to publish the version taken from `AppVersion` in source):

   ```powershell
   cd C:\Codex
   .\scripts\Publish-PixelVault.ps1 -Configuration Release
   ```

   Use **`-Force`** if `dist\PixelVault-x.yyy` already exists and you intend to overwrite it.

4. Update **`docs/CURRENT_BUILD.txt`** with the new version and `dist\PixelVault-x.yyy\PixelVault.exe` path.
5. Update **`docs/HANDOFF.md`** in the **Current Published Build** section to match.
6. Point **`PixelVault.lnk`** at the new `PixelVault.exe` if you use that shortcut.
7. If you track releases in Notion, follow **`docs/DOC_SYNC_POLICY.md`** (Releases entry, smoke status).

The publish script writes **`dist/PixelVault-x.yyy`**, refreshes the **`dist/PixelVault-current`** junction, copies `CHANGELOG.md` into the dist folder, and warns if the repo changelog lacks a header for that version.
