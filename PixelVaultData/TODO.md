# PixelVault To Do

## Open
- Batch ExifTool metadata writes for imports and library edits so metadata updates run through fewer external tool calls.
- Add bounded parallel library scan workers so the app uses more of the host PC without overwhelming the NAS.
- Virtualize and lazy-load the library UI, including background thumbnail and video poster generation, so browsing stays responsive with large libraries.
- Expand FFmpeg-backed video handling so clip support goes beyond poster generation and becomes a first-class library workflow.

## Modular Refactor
- Split the live app into a modular monolith while keeping one desktop executable and one shared runtime data model.
- Extract `PixelVault.UI` responsibilities so windows, controls, and interaction wiring stop living directly inside `MainWindow`.
- Extract `PixelVault.Import` responsibilities for rename, review, move, sort, and undo workflow orchestration.
- Extract `PixelVault.Metadata` responsibilities for tag parsing, platform detection, capture-time rules, and shared metadata models.
- Extract `PixelVault.MediaTools` wrappers for `ExifTool` and `FFmpeg` execution so external process handling is isolated.
- Extract `PixelVault.Indexing` responsibilities for game-index, photo-index, rebuild, and folder-grouping logic.
- Extract `PixelVault.Storage` responsibilities for SQLite access, settings persistence, cache paths, and filesystem-backed state.
- Extract `PixelVault.Covers` responsibilities for Steam and SteamGridDB lookups, cover-fetching, and cache behavior.
- Refactor in safe order: pure models and helpers first, then storage/indexing, then media-tool wrappers, then import orchestration, and UI wiring last.

