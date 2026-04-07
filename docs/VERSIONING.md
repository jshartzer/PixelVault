# PixelVault versioning

## Rules (source of truth)

Bump sizing and the **`M.AAA.BBB`** string format live in **`docs/POLICY.md`** under **Versioning Policy** (three segments, **`AAA`** / **`BBB`** zero-padded to three digits pre-1.0 and beyond).

## Release checklist

When you ship a new build:

1. Set **`const string AppVersion`** in `src/PixelVault.Native/PixelVault.Native.cs` to the new version (for example **`0.998.005`** or **`1.000.000`** — never go back to the legacy two-part **`0.xxx`** form for new releases).
2. Prepend a **`## M.AAA.BBB`** section to **`docs/CHANGELOG.md`** describing user-visible changes.
3. Run **`scripts/Publish-PixelVault.ps1`** from the repo root (omit **`-Version`** to publish the version taken from `AppVersion` in source):

   ```powershell
   cd C:\Codex
   .\scripts\Publish-PixelVault.ps1 -Configuration Release
   ```

   Use **`-Force`** if `dist\PixelVault-M.AAA.BBB` already exists and you intend to overwrite it.

4. Update **`docs/CURRENT_BUILD.txt`** with the new version and `dist\PixelVault-M.AAA.BBB\PixelVault.exe` path.
5. Update **`docs/HANDOFF.md`** in the **Current Published Build** section to match.
6. Point **`PixelVault.lnk`** at the new `PixelVault.exe` if you use that shortcut.
7. If you track releases in Notion, follow **`docs/DOC_SYNC_POLICY.md`** (Releases entry, smoke status).

The publish script writes **`dist/PixelVault-M.AAA.BBB`**, refreshes the **`dist/PixelVault-current`** junction, copies `CHANGELOG.md` into the dist folder, and warns if the repo changelog lacks a header for that version.

**Pruning older `dist` folders:** By default **`-KeepLatest 10`** removes older **`PixelVault-*`** directories after a publish. Sorting is by **folder last-write time** (newest kept), not by **`System.Version`**, so **`M.AAA.BBB`** renumbers such as **`0.075.000`** are not mistaken for “older” than **`0.989`**.

## App testing folder (not a release)

For a **fixed-path** build you can pin in Explorer or the taskbar—**`dotnet publish`** on **`src\PixelVault.Native`** plus **`assets`** and **`tools`** from the repo (it does **not** pull from **`dist\`**). Same layout as a publish but **no** version bump, **no** `dist\PixelVault-*`, **no** `PixelVault-current`, **no** repo-root shortcut updates:

```powershell
cd C:\Codex
.\scripts\Build-PixelVault-AppTesting.ps1
```

Default output: **`C:\Codex\App Testing\`** (overwritten each run). Optional: **`-OutputPath`**, **`-IncludeSourceBundle`**, **`-IncludeBootstrapSettings`**, **`-SelfContained`**. Creates **`PixelVault.lnk`** inside that folder unless **`-ShortcutName ""`**.
