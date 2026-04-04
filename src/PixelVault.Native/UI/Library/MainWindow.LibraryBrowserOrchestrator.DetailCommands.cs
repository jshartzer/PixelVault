using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserDeleteSelectedCaptures(
            LibraryBrowserWorkingSet ws,
            Func<List<string>> getSelectedDetailFiles,
            Action renderTiles,
            Action renderSelectedFolder,
            Action<bool> refreshLibraryFoldersAsync)
        {
            if (ws.Current == null)
            {
                MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selectedFiles = getSelectedDetailFiles()
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("Select one or more captures to delete.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var confirm = MessageBox.Show(
                "Delete " + selectedFiles.Count + " selected capture(s) from the library?\n\nThis removes the file" + (selectedFiles.Count == 1 ? string.Empty : "s") + " from disk and removes the photo index record" + (selectedFiles.Count == 1 ? string.Empty : "s") + ".",
                "Delete Capture",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            var removedFiles = new List<string>();
            var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            foreach (var file in selectedFiles)
            {
                try
                {
                    var directory = Path.GetDirectoryName(file) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(directory)) touchedDirectories.Add(directory);
                    DeleteMetadataSidecarIfPresent(file);
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        removedFiles.Add(file);
                        Log("Library delete: " + file);
                    }
                    else
                    {
                        removedFiles.Add(file);
                    }
                }
                catch (Exception deleteEx)
                {
                    failures.Add(Path.GetFileName(file) + ": " + deleteEx.Message);
                    Log("Library delete failed for " + file + ". " + deleteEx.Message);
                }
            }

            if (removedFiles.Count > 0)
            {
                librarySession.RemoveLibraryMetadataIndexEntries(removedFiles);
            }
            foreach (var directory in touchedDirectories) TryDeleteEmptyDirectory(directory);
            ApplyRemovedFilesToLibraryBrowserState(ws, removedFiles);
            ws.SelectedDetailFiles.Clear();
            ws.DetailSelectionAnchorIndex = -1;
            var currentSelection = CloneLibraryBrowserFolderView(ws.Current);
            ws.Current = currentSelection == null || string.IsNullOrWhiteSpace(currentSelection.PrimaryFolderPath)
                ? currentSelection
                : CloneLibraryBrowserFolderView(currentSelection);
            if (renderTiles != null) renderTiles();
            if (ws.Current != null) renderSelectedFolder();
            if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(true);
            status.Text = removedFiles.Count == 0
                ? "No captures deleted"
                : (failures.Count == 0
                    ? "Deleted " + removedFiles.Count + " capture(s)"
                    : "Deleted " + removedFiles.Count + " capture(s) with " + failures.Count + " failure(s)");
            if (failures.Count > 0)
            {
                MessageBox.Show(
                    "Some files could not be deleted." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(8).ToArray()),
                    "PixelVault",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        void LibraryBrowserOpenSingleFileMetadataEditor(
            LibraryBrowserWorkingSet ws,
            string filePath,
            Func<List<string>> getVisibleDetailFilesOrdered,
            Func<List<string>> getSelectedDetailFiles,
            Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder,
            Func<LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder,
            Action<bool> refreshLibraryFoldersAsync)
        {
            if (ws.Current == null)
            {
                MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            EnsureExifTool();
            var visibleFiles = getVisibleDetailFilesOrdered();
            var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
            var selectedFiles = getSelectedDetailFiles();
            HashSet<string> wantedFiles;
            if (selectedFiles.Count > 0 && (string.IsNullOrWhiteSpace(filePath) || ws.SelectedDetailFiles.Contains(filePath)))
                wantedFiles = new HashSet<string>(selectedFiles, StringComparer.OrdinalIgnoreCase);
            else if (selectedFiles.Count == 0 && string.IsNullOrWhiteSpace(filePath))
                wantedFiles = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
            else if (!string.IsNullOrWhiteSpace(filePath) && visibleSet.Contains(filePath))
                wantedFiles = new HashSet<string>(new[] { filePath }, StringComparer.OrdinalIgnoreCase);
            else
            {
                MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (wantedFiles.Count == 0)
            {
                MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var displayFolder = getDisplayFolder(ws.Current);
            var actionFolder = getActionFolder(ws.Current) ?? displayFolder;
            var selectedItems = BuildLibraryMetadataItems(displayFolder)
                .Where(item => wantedFiles.Contains(item.FilePath))
                .ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("That capture could not be loaded for metadata editing.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selectedTitle = selectedItems.Count == 1
                ? Path.GetFileName(selectedItems[0].FilePath)
                : (visibleFiles.Count > 0 && selectedItems.Count == visibleFiles.Count
                    ? (ws.Current.Name + " (all " + selectedItems.Count + " captures)")
                    : (ws.Current.Name + " (" + selectedItems.Count + " selected)"));
            status.Text = selectedItems.Count == 1 ? "Editing selected capture metadata" : "Editing selected capture metadata";
            Log("Opening library metadata editor for " + selectedItems.Count + " selected capture(s) in " + ws.Current.Name + ".");
            if (!ShowManualMetadataWindow(selectedItems, true, selectedTitle))
            {
                status.Text = "Library metadata unchanged";
                return;
            }
            var currentSelection = CloneLibraryBrowserFolderView(ws.Current);
            RunLibraryMetadataWorkflowWithProgress(actionFolder, selectedItems, delegate
            {
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.Current = currentSelection == null || string.IsNullOrWhiteSpace(currentSelection.PrimaryFolderPath)
                    ? currentSelection
                    : CloneLibraryBrowserFolderView(currentSelection);
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            });
        }

        void LibraryBrowserOpenLibraryMetadataForFolder(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView focusFolder,
            Action<LibraryBrowserFolderView> showFolder,
            Action refreshDetailSelectionUi,
            Action openMetadataForCurrentSelection)
        {
            if (focusFolder == null)
            {
                MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            showFolder(focusFolder);
            ws.SelectedDetailFiles.Clear();
            ws.DetailSelectionAnchorIndex = -1;
            if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
            openMetadataForCurrentSelection();
        }
    }
}
