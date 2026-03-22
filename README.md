# PixelVault

PixelVault is a desktop app for organizing, renaming, tagging, and moving game captures with live-editable settings.

## What it does

- Renames Steam screenshots by replacing AppIDs with game names
- Updates PNG and JPEG metadata based on the timestamp in the filename
- Moves screenshots and clips into your captures folder
- Shows a preview/count summary before you run anything
- Writes persistent session logs to `logs\`
- Lets you change folders, extensions, regex, `exiftool` path, recursion, preview mode, and move conflict handling on the fly
- Saves your settings to a local config file

## Run it

Open PowerShell in `A:\Codex` and run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\GameCaptureManager.ps1
```

Or double-click `Start Game Capture Manager.cmd`.

## Portable layout

- `GameCaptureManager.ps1`: main PixelVault desktop app
- `GameCaptureManager.Core.psm1`: workflow engine
- `GameCaptureManager.config.json`: saved settings
- `assets\PixelVault.ico`: app icon
- `assets\PixelVault.png`: header logo
- `assets\PixelVault-Concept-01.svg` to `PixelVault-Concept-03.svg`: icon concept files
- `tools\exiftool.exe`: bundled metadata tool
- `logs\`: run history logs

You can move the whole folder to another location and keep the app, logs, config, and bundled tool together.

## Notes

- The app logic lives in `GameCaptureManager.ps1` and `GameCaptureManager.Core.psm1`.
- The config file is saved as `GameCaptureManager.config.json` next to the app script.
- The app assumes `pwsh` is available in your PATH.
