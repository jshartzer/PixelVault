using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal void LibraryBrowserPaletteOpenSettings() => ShowSettingsWindow();

        internal void LibraryBrowserPaletteOpenHealthDashboard(Window owner) =>
            HealthDashboardWindow.ShowDialog(owner ?? this, BuildSettingsShellDependencies());

        internal void LibraryBrowserPaletteOpenGameIndex() => OpenGameIndexEditor();

        internal void LibraryBrowserPaletteOpenPhotoIndex() => OpenPhotoIndexEditor();

        internal void LibraryBrowserPaletteOpenFilenameRules() => OpenFilenameConventionEditor();

        internal void LibraryBrowserPaletteOpenPhotographyGallery(Window owner) => ShowPhotographyGallery(owner);

        internal void LibraryBrowserPaletteOpenSavedCoversFolder() => OpenSavedCoversFolder();

        internal void LibraryBrowserPaletteRunImport(bool withReview) => RunWorkflow(withReview);

        internal void LibraryBrowserPaletteOpenManualIntake() => OpenManualIntakeWindow();

        internal void LibraryBrowserPaletteShowIntakePreview() => ShowIntakePreviewWindow(importSearchSubfoldersForRename);

        internal void LibraryBrowserPaletteOpenBackgroundImports() => BackgroundIntakeActivityWindow.ShowOrBringToFront(this);

        internal void LibraryBrowserPaletteExportStarred(Window owner) => ExportStarredLibraryCapturesToFolder(owner);
    }
}
