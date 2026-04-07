using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserWirePhotoWorkspaceHeroMenus(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh,
            Action<string> libraryToast,
            Action refreshPhotoWorkspaceHeroBanner)
        {
            if (ws == null || panes?.PhotoWorkspaceHeaderMenuHit == null || panes.PhotoWorkspaceHeroBannerStrip == null || panes.PhotoWorkspaceTitleReadabilityBorder == null) return;
            ContextMenu BuildMenu() => BuildPhotoWorkspaceGameCoverContextMenu(ws, libraryWindow, showFolder, renderTiles, runScopedCoverRefresh, libraryToast, refreshPhotoWorkspaceHeroBanner);
            void Attach(FrameworkElement el)
            {
                el.ContextMenuOpening += (_, __) => { el.ContextMenu = BuildMenu(); };
            }
            Attach(panes.PhotoWorkspaceHeaderMenuHit);
            Attach(panes.PhotoWorkspaceHeroBannerStrip);
            Attach(panes.PhotoWorkspaceTitleReadabilityBorder);
        }

        ContextMenu BuildPhotoWorkspaceGameCoverContextMenu(
            LibraryBrowserWorkingSet ws,
            Window libraryWindow,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh,
            Action<string> libraryToast,
            Action refreshPhotoWorkspaceHeroBanner)
        {
            var menu = new ContextMenu();
            var folder = ws?.Current;
            if (folder == null || IsLibraryBrowserTimelineView(folder)) return menu;
            var actionFolders = GetLibraryBrowserActionFolders(folder);
            if (actionFolders.Count == 0 && folder != null)
            {
                var primary = GetLibraryBrowserPrimaryFolder(folder) ?? BuildLibraryBrowserDisplayFolder(folder);
                if (primary != null) actionFolders = new List<LibraryFolderInfo> { primary };
            }

            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };

            var setCoverItem = new MenuItem { Header = "Set Custom Cover...", IsEnabled = actionFolders.Count > 0 };
            setCoverItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(pickedCover)) return;
                foreach (var targetFolder in actionFolders) SaveCustomCover(targetFolder, pickedCover);
                showFolder(folder);
                renderTiles?.Invoke();
                libraryToast?.Invoke("Cover saved");
                Log("Custom cover set for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var clearCoverItem = new MenuItem
            {
                Header = "Clear Custom Cover",
                IsEnabled = actionFolders.Any(targetFolder => !string.IsNullOrWhiteSpace(CustomCoverPath(targetFolder)))
            };
            clearCoverItem.Click += delegate
            {
                foreach (var targetFolder in actionFolders.Where(targetFolder => !string.IsNullOrWhiteSpace(CustomCoverPath(targetFolder)))) ClearCustomCover(targetFolder);
                showFolder(folder);
                renderTiles?.Invoke();
                libraryToast?.Invoke("Cover cleared");
                Log("Custom cover cleared for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art", IsEnabled = actionFolders.Count > 0 };
            fetchFolderCoverItem.Click += delegate
            {
                showFolder(folder);
                runScopedCoverRefresh?.Invoke(actionFolders, BuildLibraryBrowserActionScopeLabel(folder), true, false, true);
            };

            var setBannerItem = new MenuItem { Header = "Set Custom Banner...", IsEnabled = actionFolders.Count > 0 };
            setBannerItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var picked = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(picked)) return;
                foreach (var targetFolder in actionFolders) SaveCustomHero(targetFolder, picked);
                showFolder(folder);
                refreshPhotoWorkspaceHeroBanner?.Invoke();
                libraryToast?.Invoke("Banner saved");
                Log("Custom banner set for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var clearBannerItem = new MenuItem
            {
                Header = "Clear Custom Banner",
                IsEnabled = actionFolders.Any(targetFolder => !string.IsNullOrWhiteSpace(CustomHeroPath(targetFolder)))
            };
            clearBannerItem.Click += delegate
            {
                foreach (var targetFolder in actionFolders.Where(targetFolder => !string.IsNullOrWhiteSpace(CustomHeroPath(targetFolder)))) ClearCustomHero(targetFolder);
                showFolder(folder);
                refreshPhotoWorkspaceHeroBanner?.Invoke();
                libraryToast?.Invoke("Custom banner cleared");
                Log("Custom banner cleared for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var fetchBannerItem = new MenuItem { Header = "Fetch Banner Art", IsEnabled = actionFolders.Count > 0 };
            fetchBannerItem.Click += delegate
            {
                if (libraryWindow == null) return;
                showFolder(folder);
                var scopeLabel = BuildLibraryBrowserActionScopeLabel(folder);
                var folders = actionFolders.ToList();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var targetFolder in folders)
                            await ResolveLibraryHeroBannerWithDownloadAsync(targetFolder, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        await libraryWindow.Dispatcher.InvokeAsync(() => LogException("Fetch banner art | " + scopeLabel, ex));
                        return;
                    }

                    await libraryWindow.Dispatcher.InvokeAsync(() =>
                    {
                        refreshPhotoWorkspaceHeroBanner?.Invoke();
                        libraryToast?.Invoke("Banner art updated");
                    });
                });
            };

            menu.Items.Add(openMyCoversItem);
            menu.Items.Add(setCoverItem);
            menu.Items.Add(clearCoverItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(fetchFolderCoverItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(setBannerItem);
            menu.Items.Add(clearBannerItem);
            menu.Items.Add(fetchBannerItem);
            return menu;
        }

        void LibraryBrowserRefreshPhotoWorkspaceHeroBanner(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            LibraryBrowserFolderView info)
        {
            if (panes?.PhotoWorkspaceHeroBannerImage == null || libraryWindow == null) return;
            if (ws?.WorkspaceMode != LibraryWorkspaceMode.Photo)
            {
                panes.PhotoWorkspaceHeroBannerImage.Source = null;
                return;
            }
            if (info == null || IsLibraryBrowserTimelineView(info))
            {
                panes.PhotoWorkspaceHeroBannerImage.Source = null;
                return;
            }

            var displayFolder = BuildLibraryBrowserDisplayFolder(info);
            var infoCapture = info;

            void ApplyBannerSource(string path, bool fileExists)
            {
                if (!SameLibraryBrowserSelection(ws.Current, infoCapture)) return;
                panes.PhotoWorkspaceHeroBannerImage.Uid = Guid.NewGuid().ToString("N");
                if (!fileExists || string.IsNullOrWhiteSpace(path))
                {
                    panes.PhotoWorkspaceHeroBannerImage.Source = null;
                    return;
                }

                QueueImageLoad(
                    panes.PhotoWorkspaceHeroBannerImage,
                    path,
                    CalculateLibraryBannerArtDecodeWidth(panes.PhotoWorkspaceHeroBannerRoot, ResolveLibraryDpiScale(panes.PhotoWorkspaceHeroBannerImage)),
                    delegate(BitmapImage loaded) { panes.PhotoWorkspaceHeroBannerImage.Source = loaded; },
                    true,
                    delegate { return SameLibraryBrowserSelection(ws.Current, infoCapture); });
            }

            var localPath = GetLibraryHeroBannerPathForDisplayOnly(displayFolder);
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                ApplyBannerSource(localPath, true);
                return;
            }

            ApplyBannerSource(null, false);
            _ = Task.Run(async () =>
            {
                try
                {
                    var downloaded = await ResolveLibraryHeroBannerWithDownloadAsync(displayFolder, CancellationToken.None).ConfigureAwait(false);
                    var ok = !string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded);
                    _ = libraryWindow.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                    {
                        ApplyBannerSource(downloaded, ok);
                    }));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _ = libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        LogException("Photo workspace banner art | " + (infoCapture.Name ?? "?"), ex);
                    }));
                }
            });
        }
    }
}
