Bundled third-party tools — license texts (PV-PLN-DIST-001 §5.9)
================================================================

PixelVault may invoke **ExifTool** and **FFmpeg** from **Path Settings**, or fall back to:

  <app root>\tools\exiftool.exe
  <app root>\tools\ffmpeg.exe

Place vendor binaries under the repo **`tools\`** folder (gitignored locally) before running
**`scripts/Publish-PixelVault.ps1`** or **`scripts/Publish-Velopack.ps1`**.

What ships in **`tools\licenses\`** after publish
-------------------------------------------------
Publish scripts merge this folder into **`<dist>\tools\licenses\`** next to any copied binaries:

  exiftool-gpl-3.0-COPYING.txt  GNU GPL v3 full text — Windows **exiftool.exe** from exiftool.org (e.g. 13.x).
  ffmpeg-gpl-3.0-COPYING.txt    GNU GPL v3 full text — matches **gyan.dev “essentials”** builds built with
                                **--enable-gpl** (GPL-enabled codecs; not LGPL-only). If you ship **LGPL shared**
                                FFmpeg instead, replace this file with **LGPL v2.1** COPYING from your build.

If your shipped binaries use different licensing, replace with the **exact COPYING / LICENSE** from those builds.

Authoritative worksheet (versions, URLs, channels): **`docs/BUNDLED_TOOLS_REDISTRIBUTION.md`**.
