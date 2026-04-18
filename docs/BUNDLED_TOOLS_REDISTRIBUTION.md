# Bundled & optional external tools (**PV-PLN-DIST-001** §5.9)

PixelVault **invokes** third-party programs you configure:

| Tool | Default product posture | License / notices in repo |
|------|-------------------------|---------------------------|
| **ExifTool** | Often shipped beside the app as **`tools\exiftool.exe`** (fallback when Path Settings unset). | **`tools-licenses\exiftool-gpl-3.0-COPYING.txt`** merged into **`tools\licenses\`** on publish. |
| **FFmpeg** | **Optional add-on** — **not** bundled. Users install FFmpeg separately (or rely on **`PATH`**) and set **Path Settings → FFmpeg**. | **Not** shipped from this repo. If **you** redistribute **`ffmpeg.exe`**, follow **your** build’s license (GPL/LGPL/etc.) yourself. |

Shipping **`Publish-PixelVault.ps1`**, **`Publish-Velopack.ps1`**, or **`Build-PixelVault-AppTesting.ps1`** requires matching redistribution rules for anything you **actually place** under **`tools\`**. This document is an **engineering worksheet** — not legal advice.

---

## Ship automation (repo)

Publish scripts dot-source **`scripts/Merge-BundledToolLicenses.ps1`** and merge **`tools-licenses\`** into **`<publish output>\tools\licenses\`** every time.

| File | Purpose |
|------|---------|
| **`tools-licenses/README.txt`** | Operator instructions (also copied into **`tools\licenses\`**). |
| **`tools-licenses/exiftool-gpl-3.0-COPYING.txt`** | Full **GNU GPL v3** — appropriate for typical Windows **`exiftool.exe`** from **[exiftool.org](https://exiftool.org/)**. Replace if your **`exiftool.exe`** build differs. |

---

## Vendor notes (FFmpeg — user-installed)

When users point PixelVault at **`ffmpeg.exe`**, they choose the build (e.g. **[gyan.dev](https://www.gyan.dev/ffmpeg/builds/)**, **[BtbN](https://github.com/BtbN/FFmpeg-Builds)**). PixelVault does **not** embed FFmpeg and does **not** ship its license text — **Partner Center / Store** submissions should describe optional invocation of a user-supplied executable per your counsel.

---

## Release packaging checklist

- [x] **Automated:** **`tools\licenses\`** merged from **`tools-licenses\`** (**`Merge-BundledToolLicenses.ps1`**).
- [ ] **Per release:** Confirm **`exiftool.exe`** version if present under **`tools\`** (`exiftool.exe -ver`). Swap **`exiftool-*-COPYING.txt`** if the binary’s license differs.
- [ ] **User-visible attribution** for **ExifTool** if required beyond **`tools\licenses\`** (About, readme).
- [ ] **Partner Center / Desktop Bridge:** disclose **ExifTool** when bundled; **FFmpeg** as optional user tool per your listing strategy.

**Fallback:** **Path Settings** overrides bundled **`tools\exiftool.exe`** when a valid path is set (**`SettingsService`**).
