# Bundled external tools — redistribution worksheet (**PV-PLN-DIST-001** §5.9)

PixelVault **invokes** third-party binaries (**ExifTool**, **FFmpeg**) from **Path Settings** or, when unset, from **`tools\`** next to **`PixelVault.exe`** (**`MainWindow.StartupInitialization`**, **`SettingsService`**).

Shipping those binaries with **`Publish-PixelVault.ps1`**, **`Publish-Velopack.ps1`**, or **`Build-PixelVault-AppTesting.ps1`** requires matching each vendor’s **license** and **notice** rules. This document is an **engineering worksheet** — not legal advice.

---

## Ship automation (repo)

Publish scripts dot-source **`scripts/Merge-BundledToolLicenses.ps1`** and merge **`tools-licenses\`** into **`<publish output>\tools\licenses\`** every time (even when the local repo **`tools\`** folder has no binaries). Committed texts today:

| File | Purpose |
|------|---------|
| **`tools-licenses/README.txt`** | Short operator instructions (also copied into **`tools\licenses\`**). |
| **`tools-licenses/exiftool-gpl-3.0-COPYING.txt`** | Full **GNU GPL v3** text — appropriate for **Windows standalone `exiftool.exe`** from **[exiftool.org](https://exiftool.org/)** as published by Phil Harvey. |
| **`tools-licenses/ffmpeg-lgpl-2.1-COPYING.txt`** | Full **GNU LGPL v2.1** text — typical for **shared FFmpeg** Windows builds (e.g. **[gyan.dev](https://www.gyan.dev/ffmpeg/builds/)**, **[BtbN GitHub Actions](https://github.com/BtbN/FFmpeg-Builds)**). **If your `ffmpeg.exe` is GPL-only or statically linked differently, replace this file with the COPYING from your exact build artifact before release.** |

---

## Vendor matrix (fill versions at release time)

| Tool | Binary expected in **`tools\`** | Version check | Upstream | License (typical) | Redistribution |
|------|----------------------------------|----------------|----------|-------------------|----------------|
| **ExifTool** | **`exiftool.exe`** | `exiftool.exe -ver` | [exiftool.org](https://exiftool.org/) | **GPL v3** for the Windows stand-alone executable (see upstream README). | Allowed with full license text — use committed **`exiftool-gpl-3.0-COPYING.txt`** unless your build differs. |
| **FFmpeg** | **`ffmpeg.exe`** | `ffmpeg.exe -version` | [ffmpeg.org](https://ffmpeg.org/) — [legal / license FAQ](https://ffmpeg.org/legal.html) | Most public **shared** Windows builds are **LGPL v2.1**; **GPL** builds exist. | Allowed for LGPL shared builds with license text — use committed **`ffmpeg-lgpl-2.1-COPYING.txt`** only when it matches **your** build’s license. |

**Channels:** zip **`dist/PixelVault-*`** and **Velopack** both receive **`tools\licenses\`** via the same merge step. **Microsoft Store / Desktop Bridge** still needs Partner Center disclosure for **`runFullTrust`** helpers.

---

## Release packaging checklist

- [x] **Automated:** **`tools\licenses\`** merged into publish output from **`tools-licenses\`** (**`Merge-BundledToolLicenses.ps1`**).
- [ ] **Per release:** Confirm **`exiftool.exe`** / **`ffmpeg.exe`** versions and replace COPYING files if your binaries are not “stock” GPLv3 ExifTool + LGPL shared FFmpeg.
- [ ] **User-visible attribution** if upstream requires it beyond file drop (About box, **`tools\licenses\README.txt`** is a start; extend if counsel requires).
- [ ] **Partner Center / Desktop Bridge:** disclose bundled executables per submission guidance.

**Fallback (product):** users can point **Path Settings** at externally installed tools if a channel forbids bundling — **`SettingsService`** already prefers saved paths over **`tools\`** when valid.
