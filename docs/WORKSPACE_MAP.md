# PixelVault Workspace Map

Active workspace note:

- use `C:\Codex` as the real workspace root
- ignore `A:\Codex` if it appears in a shell or session context

## Active Development

- `C:\Codex\src\PixelVault.Native`: live PixelVault app source and SDK project
- `C:\Codex\src\PixelVault.Native\Indexing`: game-index, folder-cache, and library-index logic
- `C:\Codex\src\PixelVault.Native\Import`: intake, rename, metadata, move, and undo workflow logic
- `C:\Codex\src\PixelVault.Native\MediaTools`: ExifTool and FFmpeg execution helpers
- `C:\Codex\src\PixelVault.Native\Metadata`: shared metadata builders and library-edit helpers
- `C:\Codex\src\PixelVault.Native\Models`: import and index model types
- `C:\Codex\src\PixelVault.Native\Storage`: SQLite/cache path and persistence helpers
- `C:\Codex\src\PixelVault.Native\UI`: Library virtualization and other UI-specific helpers
- `C:\Codex\scripts`: developer scripts, including release publishing
- `C:\Codex\assets`: shared branding and interface assets
- `C:\Codex\tools`: bundled runtime tools such as `ExifTool` and `FFmpeg`

## Documentation

- `C:\Codex\docs\HANDOFF.md`: current stop point and next-step summary
- `C:\Codex\docs\POLICY.md`: durable working rules
- `C:\Codex\docs\PROJECT_CONTEXT.md`: broader architecture and product context
- `C:\Codex\docs\CHANGELOG.md`: published version history
- `C:\Codex\docs\CURRENT_BUILD.txt`: path pointer to the live published executable
- `C:\Codex\docs\LIBRARY_WORKFLOW_VERIFICATION.md`: repeatable checks for Library regrouping, delete, and metadata-editor behavior

## Runtime And Releases

- `C:\Codex\dist`: versioned published builds
- `C:\Codex\PixelVaultData`: live shared data, indexes, caches, and logs
- `C:\Codex\PixelVault.lnk`: shortcut that should always point at the newest published build

## Historical Material

- `C:\Codex\legacy\GameCaptureManager`: older PowerShell-based workflow files kept for reference
- `C:\Codex\archive`: backups and old artifacts not needed for routine app work
