# Velopack §5.3 spike — VM checklist (**PV-PLN-DIST-001**)

Use on a **clean Windows VM** (or disposable machine), not only your dev box — satisfies roadmap **§5.3** and gate **§4.1** “fresh‑machine install.”

**Prerequisites**

- Built artifacts from **`pwsh -File C:\Codex\scripts\Publish-Velopack.ps1`** (installer/setup under **`dist\Velopack\<semver>\`** — see **`docs/VELOPACK.md`**).
- Know where **`PixelVaultData`** / `%LocalAppData%\PixelVault` should land for your test (**`docs/DISTRIBUTION_STORAGE.md`**).

---

## A. Clean install + launch

1. Copy the **Velopack release folder** for one version (**N**) to the VM (USB share, zip, etc.).
2. Run the generated **Setup** / installer for **N** (exact filename varies by **`vpk`** output).
3. Launch **PixelVault** from Start menu or implied shortcut.
4. Confirm **library root** prompt or Settings load; create minimal smoke content if needed (import one file **or** skip if only testing survival).
5. Confirm **persistent data** lives under the expected writable root (**Health** dashboard **App data folder**, or **`%LocalAppData%\PixelVault`** when installed under restricted dirs).

---

## B. Uninstall

1. **Windows Settings → Apps → Installed apps** → uninstall PixelVault (or Velopack-provided uninstaller if offered).
2. Decide what you **expect** left behind (**user data** under `%LocalAppData%` is typical). Note actual behavior for **`CHANGELOG`** / support docs — do **not** assume wipe of libraries on user drives.

---

## C. Update **N → N+1** (settings / data preserved)

1. Install release **N** as in **A**.
2. Change something durable: toggle a **Settings** value, or drop a sentinel file under **`PixelVaultData`** / cache as allowed by your layout docs.
3. Produce **N+1** from a higher **`AppVersion`** publish + **`Publish-Velopack.ps1`** pack.
4. Simulate how users obtain updates (**re-run installer**, staged feed + app update flow, or **`UpdateManager`** once wired). Apply **N+1**.
5. Relaunch — confirm **settings/sentinel** survived and **mutable data** still resolves (no accidental empty profile).

---

## Cross-checks

- **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** — optional library smoke after installer path works.
- **`docs/BUNDLED_TOOLS_REDISTRIBUTION.md`** — publish output includes **`tools\licenses\`** for **ExifTool** when you ship **`tools\exiftool.exe`**; **FFmpeg** is **not** bundled — install separately on the VM if you need video smoke tests.
