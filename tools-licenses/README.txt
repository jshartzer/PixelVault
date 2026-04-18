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

  ffmpeg-lgpl-2.1-COPYING.txt   GNU LGPL v2.1 full text — required for typical LGPL FFmpeg builds.
  exiftool-gpl-3.0-COPYING.txt  GNU GPL v3 full text — Windows **exiftool.exe** from exiftool.org is GPLv3.

If your FFmpeg build uses different licensing or you ship a GPLv3 FFmpeg, replace these files with the
**exact COPYING / LICENSE files** packaged with **your** FFmpeg **and** ExifTool downloads before release.

Authoritative worksheet (versions, URLs, channels): **`docs/BUNDLED_TOOLS_REDISTRIBUTION.md`**.
