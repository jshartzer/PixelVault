using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>Static command metadata for the library palette (Slice E). Handlers come from <see cref="LibraryBrowserPaletteContext"/> at runtime.</summary>
    internal static class LibraryCommandPaletteRegistry
    {
        internal readonly record struct CommandSpec(string Id, string Title, string Hint, string Keywords);

        /// <summary>Display order defines default ordering in the palette.</summary>
        internal static readonly CommandSpec[] All =
        {
            new("refresh_folders", "Refresh folders", "Rescan library tree", "reload scan library"),
            new("clear_search", "Clear folder search", "Reset search and show all folders", "search clear filter"),
            new("settings", "Settings", "App preferences and paths", "preferences options"),
            new("health", "Setup & health", "Paths, tools, diagnostics", "diagnostics troubleshooting"),
            new("game_index", "Game index", "Master game rows", "steam grid"),
            new("photo_index", "Photo index", "Per-file index cache", "sqlite metadata"),
            new("filename_rules", "Renaming rules", "Filename convention editor", "parsing pattern filename"),
            new("photography_gallery", "Photography gallery", "Tagged captures", "screenshots"),
            new("saved_covers", "Saved covers folder", "Open bundled covers on disk", "covers disk"),
            new("import", "Import", "Move queue into library", "upload intake"),
            new("import_comment", "Import and comment", "Review then import", "intake preview workflow"),
            new("manual_intake", "Manual intake", "Unmatched files metadata", "manual metadata"),
            new("intake_preview", "Intake queue preview", "Upload queue summary", "queue badge"),
            new("export_starred", "Export Starred", "Copy starred to export folder", "star export"),
            new("refresh_covers", "Refresh all covers", "Re-fetch art for whole library", "grid steam fetch"),
            new("shortcuts", "Keyboard shortcuts", "Same as F1", "keys help"),
            new("quick_edit_panel", "Quick edit panel", "Side drawer for fast edits (shell)", "drawer sidebar"),
            new("sort_alpha", "Sort folders · Alphabetical", "Folder list order", "sort a-z name abc"),
            new("sort_captured", "Sort folders · Date captured", "Folder list order", "sort when taken"),
            new("sort_added", "Sort folders · Date added", "Folder list order", "sort recent library"),
            new("sort_photos", "Sort folders · Most photos", "Folder list order", "sort count captures"),
            new("filter_all", "Filter · All games", "Show every folder", "filter none reset"),
            new("filter_completed", "Filter · 100% achievements", "Completed games only", "medal 100 percent"),
            new("filter_crossplatform", "Filter · Cross-platform", "Multi-platform folders", "cross play"),
            new("filter_large", "Filter · 25+ captures", "Folders with many screenshots", "large library"),
            new("filter_missing_id", "Filter · Missing ID", "No game-index id, or Steam-tagged with missing App ID or Grid ID", "steam appid stid game row"),
            new("filter_no_cover", "Filter · No cover path", "Folders without a saved cover path", "missing cover art"),
            new("group_all", "Group · All games", "Single folder list", "grouping flat"),
            new("group_console", "Group · By console", "Platform sections", "grouping xbox playstation"),
            new("group_timeline", "Group · Timeline", "Calendar view", "grouping dates"),
            new("group_folders", "Group · Folders", "Exit timeline to folder cards", "grouping grid"),
            new("workspace_open_captures", "Workspace · Open captures", "Photo view for the selected game (double-click a cover)", "photos screenshots game workspace"),
            new("workspace_back_to_folders", "Workspace · Back to folder list", "Leave captures view (Esc also works)", "folders grid close photos")
        };

        internal static void ValidateInvariants()
        {
            var ids = All.Select(s => s.Id).ToList();
            if (ids.Count != ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                throw new InvalidOperationException("LibraryCommandPaletteRegistry: duplicate command Id.");

            foreach (var s in All)
            {
                if (string.IsNullOrWhiteSpace(s.Id)) throw new InvalidOperationException("LibraryCommandPaletteRegistry: empty Id.");
                if (string.IsNullOrWhiteSpace(s.Title)) throw new InvalidOperationException("LibraryCommandPaletteRegistry: empty Title for " + s.Id + ".");
            }
        }

        /// <summary>Maps registry <see cref="CommandSpec.Id"/> to the runtime action (skipped when action is null).</summary>
        internal static Dictionary<string, Action> BuildHandlerMap(LibraryBrowserPaletteContext ctx)
        {
            var d = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            if (ctx == null) return d;

            void Bind(string id, Action a)
            {
                if (a != null) d[id] = a;
            }

            Bind("refresh_folders", ctx.RefreshLibraryFolders);
            Bind("clear_search", ctx.ClearLibrarySearch);
            Bind("settings", ctx.OpenSettings);
            Bind("health", ctx.OpenHealthDashboard);
            Bind("game_index", ctx.OpenGameIndex);
            Bind("photo_index", ctx.OpenPhotoIndex);
            Bind("filename_rules", ctx.OpenFilenameRules);
            Bind("photography_gallery", ctx.OpenPhotographyGallery);
            Bind("saved_covers", ctx.OpenSavedCoversFolder);
            Bind("import", ctx.RunImportQuick);
            Bind("import_comment", ctx.RunImportWithReview);
            Bind("manual_intake", ctx.OpenManualIntake);
            Bind("intake_preview", ctx.OpenIntakePreview);
            Bind("export_starred", ctx.ExportStarred);
            Bind("refresh_covers", ctx.RefreshAllCovers);
            Bind("shortcuts", ctx.ShowKeyboardShortcuts);
            Bind("quick_edit_panel", ctx.ToggleQuickEditDrawer);
            Bind("sort_alpha", ctx.SortFoldersAlpha);
            Bind("sort_captured", ctx.SortFoldersDateCaptured);
            Bind("sort_added", ctx.SortFoldersDateAdded);
            Bind("sort_photos", ctx.SortFoldersMostPhotos);
            Bind("filter_all", ctx.FilterFoldersAll);
            Bind("filter_completed", ctx.FilterFolders100Percent);
            Bind("filter_crossplatform", ctx.FilterFoldersCrossPlatform);
            Bind("filter_large", ctx.FilterFolders25PlusCaptures);
            Bind("filter_missing_id", ctx.FilterFoldersMissingId);
            Bind("filter_no_cover", ctx.FilterFoldersNoCover);
            Bind("group_all", ctx.GroupFoldersAllGames);
            Bind("group_console", ctx.GroupFoldersByConsole);
            Bind("group_timeline", ctx.GroupFoldersTimeline);
            Bind("group_folders", ctx.GroupFoldersFolderGrid);
            Bind("workspace_open_captures", ctx.EnterPhotoWorkspace);
            Bind("workspace_back_to_folders", ctx.ExitPhotoWorkspace);
            return d;
        }
    }
}
