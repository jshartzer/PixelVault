Bundled third-party tools — license texts (PV-PLN-DIST-001 §5.9)
================================================================

**ExifTool** may be invoked from **Path Settings**, or fall back to:

  <app root>\tools\exiftool.exe

Place **`exiftool.exe`** under the repo **`tools\`** folder (gitignored locally) before publish if you ship it beside the app.

**FFmpeg** is **optional** and **not** bundled with PixelVault releases. Users install FFmpeg separately (or use **PATH**) and set **Path Settings → FFmpeg** when they want video thumbnails, clip previews, and richer clip metadata. **No** FFmpeg license text is merged from this repo — comply with your own FFmpeg build’s license when you distribute **`ffmpeg.exe`** yourself.

What ships in **`tools\licenses\`** after publish
-------------------------------------------------
Publish scripts merge this folder into **`<dist>\tools\licenses\`**:

  README.txt                     This file.
  exiftool-gpl-3.0-COPYING.txt   GNU GPL v3 full text — Windows **exiftool.exe** from exiftool.org.

If your shipped **ExifTool** build uses a different license, replace **exiftool-gpl-3.0-COPYING.txt** with the exact COPYING from that build.

Authoritative worksheet: **`docs/BUNDLED_TOOLS_REDISTRIBUTION.md`**.
