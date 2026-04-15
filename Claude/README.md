# Claude Review Packet

This folder contains the main PixelVault files involved in deciding:

- which captures/photos are shown in the right-hand detail pane
- which folder covers are shown in the library list
- which hero/banner images are shown in photo workspace

## Review Goal

Please review this packet for:

- where the app decides what capture files are eligible to display
- where layout behavior is determined for photo view and Timeline
- where folder cover selection priority is decided
- where hero/banner selection priority is decided
- places where layout settings can conflict with actual rendering behavior
- opportunities to simplify the capture/timeline layout pipeline

## Files

### `MainWindow.LibraryBrowserRender.DetailPane.cs`

Primary capture-detail renderer.

- Loads the current folder/timeline selection
- Builds the visible snapshot of captures
- Applies timeline date filtering
- Chooses the row layout strategy
- Creates the detail rows that ultimately display capture tiles

If you want to understand why certain captures appear, or why layout controls do or do not visibly affect the result, start here.

### `MainWindow.LibraryFolderCacheIo.cs`

Folder media enumeration and filtering.

- Resolves which files belong to a library folder entry
- Filters media by platform when needed
- Pulls from cached folder image/media lists when available

If the wrong files are entering the display pipeline, this is one of the first files to inspect.

### `MainWindow.LibraryBrowserViewModel.cs`

Library view construction and shared view logic.

- Builds the effective display folder for normal folder view and Timeline
- Supplies timeline/date helper logic
- Supplies some shared layout and grouping helpers

If the issue is more about how the app interprets the active library node than how it draws it, this file matters.

### `MainWindow.LibraryBrowserOrchestrator.FolderTile.cs`

Folder tile renderer for the library list.

- Builds the folder cover tiles shown in the left/browser area
- Uses the folder art path selected for display
- Wires context actions around covers

If you want to know why a specific folder cover is showing, this is one of the main files.

### `MainWindow.LibraryBrowserPhotoHero.cs`

Photo-workspace hero/banner behavior.

- Resolves and refreshes the wide banner/hero image for the selected game
- Handles custom banner actions and banner refresh behavior

If the issue is about the large banner in photo workspace, review this together with `CoverService.cs`.

### `LibraryVirtualization.cs`

Capture tile creation and interaction behavior.

- Builds the actual capture tile UI once a file has already been chosen
- Handles tile visuals, metadata affordances, and tile interactions

This file is less about which files are chosen and more about how each chosen file is displayed.

### `CoverService.cs`

Cover and banner storage/resolution layer.

- Resolves custom covers
- Resolves cached downloaded covers
- Resolves custom hero banners
- Resolves cached hero/banner art
- Handles save/clear/download behavior for cover assets

If the question is "why did this cover/banner win over another one?", this is a key file.

## Suggested Read Order

1. `MainWindow.LibraryBrowserRender.DetailPane.cs`
2. `MainWindow.LibraryFolderCacheIo.cs`
3. `MainWindow.LibraryBrowserViewModel.cs`
4. `MainWindow.LibraryBrowserOrchestrator.FolderTile.cs`
5. `MainWindow.LibraryBrowserPhotoHero.cs`
6. `CoverService.cs`
7. `LibraryVirtualization.cs`

## Current Concern Areas

- Capture/timeline layout behavior has recently been adjusted and may still have competing assumptions between exact columns, adaptive sizing, and per-row rendering.
- Timeline and normal photo view are intended to feel more consistent, but the render path still has multiple branches.
- Cover display involves UI-layer selection plus service-layer resolution, so review both before drawing conclusions.
