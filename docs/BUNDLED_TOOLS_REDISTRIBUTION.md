# Bundled external tools — redistribution worksheet (**PV-PLN-DIST-001** §5.9)

PixelVault **invokes** third-party binaries (**ExifTool**, **FFmpeg**) from **Path Settings** or, when unset, from **`tools\`** next to **`PixelVault.exe`** (**`MainWindow.StartupInitialization`**, **`SettingsService`**).

Shipping those binaries with **`Publish-PixelVault.ps1`**, **`Publish-Velopack.ps1`**, or **`Build-PixelVault-AppTesting.ps1`** requires matching each vendor’s **license** and **notice** rules. This document is an **engineering worksheet** — not legal advice.

---

## Ship automation (repo)

Publish scripts dot-source **`scripts/Merge-BundledToolLicenses.ps1`** and merge **`tools-licenses\`** into **`<publish output>\tools\licenses\`** every time (even when the local repo **`tools\`** folder has no binaries). Committed texts today:

| File | Purpose |
|------|---------|
| **`tools-licenses/README.txt`** | Short operator instructions (also copied into **`tools\licenses\`**). |
| **`tools-licenses/exiftool-gpl-3.0-COPYING.txt`** | Full **GNU GPL v3** text — appropriate for **Windows standalone `exiftool.exe`** from **[exiftool.org](https://exiftool.org/)** (verified **13.45** — GPLv3 per upstream **`LICENSE`**). |
| **`tools-licenses/ffmpeg-gpl-3.0-COPYING.txt`** | Full **GNU GPL v3** text — aligned with **gyan.dev “essentials”** Windows builds that use **`--enable-gpl`** / **`--enable-version3`** (GPL aggregate, not LGPL-only). **If you ship an LGPL shared FFmpeg build instead, replace this file with the LGPL v2.1 COPYING from that build.** |

---

## Vendor matrix (fill versions at release time)

| Tool | Binary expected in **`tools\`** | Version check | Upstream | License (this repo’s bundle) | Redistribution |
|------|----------------------------------|----------------|----------|------------------------------|----------------|
| **ExifTool** | **`exiftool.exe`** | `exiftool.exe -ver` | [exiftool.org](https://exiftool.org/) | **GPL v3** (standalone Windows build). | Use **`exiftool-gpl-3.0-COPYING.txt`**. |
| **FFmpeg** | **`ffmpeg.exe`** | `ffmpeg.exe -version` | [ffmpeg.org](https://ffmpeg.org/) — builds e.g. **[gyan.dev](https://www.gyan.dev/ffmpeg/builds/)** | **GPL v3** essentials build (**`--enable-gpl`**, static linking flags as published by vendor — **not** LGPL-only). | Use **`ffmpeg-gpl-3.0-COPYING.txt`**. |

**Channels:** zip **`dist/PixelVault-*`** and **Velopack** both receive **`tools\licenses\`** via the same merge step. **Microsoft Store / Desktop Bridge** still needs Partner Center disclosure for **`runFullTrust`** helpers.

---

## Release packaging checklist

- [x] **Automated:** **`tools\licenses\`** merged into publish output from **`tools-licenses\`** (**`Merge-BundledToolLicenses.ps1`**).
- [ ] **Per release:** Reconfirm **`exiftool.exe`** / **`ffmpeg.exe`** versions and build flavor (`ffmpeg -version` legal line). Swap **`ffmpeg-*-COPYING.txt`** if you change FFmpeg variant (e.g. LGPL shared build from **[BtbN](https://github.com/BtbN/FFmpeg-Builds)**).
- [ ] **User-visible attribution** if upstream requires it beyond file drop (About box, **`tools\licenses\README.txt`** is a start; extend if counsel requires).
- [ ] **Partner Center / Desktop Bridge:** disclose bundled executables per submission guidance.

**Fallback (product):** users can point **Path Settings** at externally installed tools if a channel forbids bundling — **`SettingsService`** already prefers saved paths over **`tools\`** when valid.
