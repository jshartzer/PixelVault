# Completed: MainWindow extraction (Phases A–F)

**Status:** Complete (Apr 2026)  
**Index:** `C:\Codex\docs\completed-projects\README.md`  
**Notion:** [Completed projects](https://www.notion.so/33873adc59b681c6a7ffe22e8bcab2a5) · [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) (complete)

## Outcomes

- **Phase E:** `LibraryBrowserHost`, `ILibraryBrowserShell` / `LibraryBrowserShellBridge`, `LibraryBrowserShowOrchestration`; library browser show path decoupled from the `MainWindow` type for orchestration.
- **Phase F:** `SettingsShellHost` / `SettingsShellDependencies`, `MainWindow.SettingsShell` bridge, `MainWindow.SettingsPersistence`; photography gallery index/cache and Steam picker in `MainWindow.PhotographyAndSteam.cs`.
- **Phases A–D** (intake, editors, changelog, progress, etc.) per full roadmap.

## Canonical references

- **Full execution checklist, file map, phase notes:** `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md`
- **Product-level Phase 3 framing:** `C:\Codex\docs\ROADMAP.md` (ongoing shrink of `PixelVault.Native.cs` and services may continue under Phase 3 beyond this initiative)
- **Shipped notes:** `C:\Codex\docs\CHANGELOG.md` (e.g. 0.850 — MainWindow extraction / Phase F)

## Follow-up (not part of this completed initiative)

- Further line-count reduction in `PixelVault.Native.cs` (image cache, intake, etc.)
- Deeper `IImportService` / `IFileSystemService` work per service and roadmap docs
