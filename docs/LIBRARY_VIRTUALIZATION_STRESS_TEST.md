# Library Virtualization Stress Test

Use this when touching the Library folder virtualization, the detail-row lazy loader, or any resize-driven render behavior.

## Seed Data

Generate a disposable stress dataset with:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Codex\scripts\New-LibraryVirtualizationStressData.ps1
```

This creates:

- `C:\Codex\tmp_verify\library-virtualization-stress\library`
- `C:\Codex\tmp_verify\library-virtualization-stress\source`

Point PixelVault at the generated `library` path in `Path Settings`.

## Folder Browser

1. Open Library and scroll deep into the left-side folder grid.
2. Resize the window wider and narrower a few times.
3. Move the folder-size slider across a few values.

Expected:

- the folder browser stays near the same scroll position instead of snapping back to the top
- visible rows reflow cleanly into the new column count
- no blank spacer gap lingers after resizing

## Mixed-Media Detail Folder

1. Open `Mega Mix`.
2. Scroll well past the first screen of captures.
3. Resize the window width and move the capture-size slider.

Expected:

- the detail pane stays near the same capture region after layout-only changes
- rows continue lazy-loading as you approach the bottom
- video and image tiles both keep rendering without a full jump back to the first date group

## Large Folder Sweep

1. Click through several `Stress Game ###` folders quickly.
2. Return to one of the earlier folders.
3. Scroll a few screens, then keep moving between folders.

Expected:

- folder switches still reset the detail pane to the top of the newly selected folder
- returning to another folder does not leave stale tiles from the previous selection behind
- thumbnails and video posters continue loading for the visible rows only

## Notes

- If `FFmpeg` is available at `C:\Codex\tools\ffmpeg.exe`, the stress data includes short MP4 clips for mixed-media coverage.
- Use `Refresh` and `Rebuild` once after generating the dataset if you want to verify index rebuild behavior under the larger library shape.
