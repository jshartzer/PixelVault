using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserApplySortGroupPillState(Button button, bool active)
        {
            if (button == null) return;
            if (active) ApplyLibraryPillChrome(button, "#3A4652", "#566676", "#455463", "#2C3742", "#F4F7FA");
            else ApplyLibraryPillChrome(button, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
        }

        Func<List<string>> LibraryBrowserCreateVisibleDetailFilesOrdered(
            LibraryBrowserWorkingSet ws,
            Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder)
        {
            return delegate
            {
                if (ws.Current == null) return new List<string>();
                if (ws.DetailFilesDisplayOrder != null && ws.DetailFilesDisplayOrder.Count > 0)
                {
                    return ws.DetailFilesDisplayOrder
                        .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                return GetFilesForLibraryFolderEntry(getDisplayFolder(ws.Current), false)
                    .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            };
        }

        Func<List<string>> LibraryBrowserCreateSelectedDetailFiles(
            LibraryBrowserWorkingSet ws,
            Func<List<string>> getVisibleDetailFilesOrdered)
        {
            return delegate
            {
                if (ws.Current == null) return new List<string>();
                var visibleFiles = getVisibleDetailFilesOrdered();
                var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                foreach (var stale in ws.SelectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) ws.SelectedDetailFiles.Remove(stale);
                return visibleFiles.Where(path => ws.SelectedDetailFiles.Contains(path)).ToList();
            };
        }

        Action<string, ModifierKeys> LibraryBrowserCreateUpdateDetailSelection(
            LibraryBrowserWorkingSet ws,
            Func<List<string>> getVisibleDetailFilesOrdered,
            Action refreshDetailSelectionUi)
        {
            return delegate(string filePath, ModifierKeys mods)
            {
                LibraryBrowserApplyDetailSelectionChange(ws, filePath, mods, getVisibleDetailFilesOrdered, refreshDetailSelectionUi);
            };
        }

        void LibraryBrowserApplyDetailSelectionChange(
            LibraryBrowserWorkingSet ws,
            string filePath,
            ModifierKeys mods,
            Func<List<string>> getVisibleDetailFilesOrdered,
            Action refreshDetailSelectionUi)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                if ((mods & ModifierKeys.Control) == 0 && (mods & ModifierKeys.Shift) == 0)
                {
                    ws.SelectedDetailFiles.Clear();
                    ws.DetailSelectionAnchorIndex = -1;
                }
                if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                return;
            }
            var visibleFiles = getVisibleDetailFilesOrdered();
            var idx = -1;
            for (var i = 0; i < visibleFiles.Count; i++)
            {
                if (string.Equals(visibleFiles[i], filePath, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return;

            var ctrl = (mods & ModifierKeys.Control) != 0;
            var shift = (mods & ModifierKeys.Shift) != 0;

            if (shift && ws.DetailSelectionAnchorIndex >= 0 && ws.DetailSelectionAnchorIndex < visibleFiles.Count)
            {
                var a = ws.DetailSelectionAnchorIndex;
                var b = idx;
                if (a > b)
                {
                    var t = a;
                    a = b;
                    b = t;
                }
                ws.SelectedDetailFiles.Clear();
                for (var i = a; i <= b; i++) ws.SelectedDetailFiles.Add(visibleFiles[i]);
            }
            else if (ctrl)
            {
                if (ws.SelectedDetailFiles.Contains(filePath)) ws.SelectedDetailFiles.Remove(filePath);
                else ws.SelectedDetailFiles.Add(filePath);
                ws.DetailSelectionAnchorIndex = idx;
            }
            else
            {
                ws.SelectedDetailFiles.Clear();
                ws.SelectedDetailFiles.Add(filePath);
                ws.DetailSelectionAnchorIndex = idx;
            }
            if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
        }

        Action LibraryBrowserCreateRefreshDetailSelectionUi(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Func<List<string>> getSelectedDetailFiles)
        {
            return delegate
            {
                var selectedFiles = getSelectedDetailFiles();
                var timelineMode = IsLibraryBrowserTimelineMode();
                foreach (var tile in ws.DetailTiles)
                {
                    var file = tile == null ? string.Empty : tile.Tag as string;
                    var isSelected = !string.IsNullOrWhiteSpace(file) && ws.SelectedDetailFiles.Contains(file);
                    tile.Background = isSelected ? Brush("#1D2730") : Brush("#10181D");
                    tile.BorderBrush = isSelected ? Brush("#D46C63") : Brush("#2B3A44");
                    tile.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                }
                panes.DeleteSelectedButton.IsEnabled = selectedFiles.Count > 0;
                panes.ThumbLabel.Text = selectedFiles.Count > 0 ? selectedFiles.Count + " selected" : (timelineMode ? "Timeline" : "Screenshots");
                if (panes.EditMetadataButton != null)
                {
                    panes.EditMetadataButton.IsEnabled = !ws.LibraryFoldersLoading && ws.Current != null && (!timelineMode || selectedFiles.Count > 0);
                }
                if (panes.OpenFolderButton != null)
                {
                    panes.OpenFolderButton.IsEnabled = !timelineMode && ws.Current != null && !ws.LibraryFoldersLoading;
                }
                if (panes.RefreshThisFolderButton != null)
                {
                    panes.RefreshThisFolderButton.IsEnabled = !timelineMode && ws.Current != null && !ws.LibraryFoldersLoading;
                }
                if (panes.ExitTimelineButton != null)
                {
                    panes.ExitTimelineButton.IsEnabled = !ws.LibraryFoldersLoading;
                }
            };
        }
    }
}
