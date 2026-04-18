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
        /// <summary>Cancels the previous photo-workspace banner download wait when the user changes selection quickly.</summary>
        readonly object _photoWorkspaceHeroBannerDownloadLock = new object();
        CancellationTokenSource _photoWorkspaceHeroBannerDownloadCts;

        CancellationTokenSource ReplacePhotoWorkspaceHeroBannerDownloadCts(CancellationTokenSource next)
        {
            lock (_photoWorkspaceHeroBannerDownloadLock)
            {
                var previous = _photoWorkspaceHeroBannerDownloadCts;
                _photoWorkspaceHeroBannerDownloadCts = next;
                return previous;
            }
        }

        void CancelAndDisposeCts(CancellationTokenSource cts)
        {
            if (cts == null) return;
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
            try
            {
                cts.Dispose();
            }
            catch
            {
            }
        }

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
            if (panes.PhotoWorkspaceHeroLogoHost != null) Attach(panes.PhotoWorkspaceHeroLogoHost);
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
            var lookupFolder = GetLibraryBrowserPrimaryFolder(folder) ?? BuildLibraryBrowserDisplayFolder(folder);

            void QueuePhotoWorkspaceHeaderArtFetch(List<LibraryFolderInfo> targetFolders, string scopeLabel, bool forceRefreshCachedHeaderArt, string completionToast, string logContext)
            {
                if (libraryWindow == null || targetFolders == null || targetFolders.Count == 0) return;
                showFolder(folder);
                var folders = targetFolders.ToList();
                if (forceRefreshCachedHeaderArt)
                {
                    foreach (var title in folders
                        .Where(targetFolder => targetFolder != null && !string.IsNullOrWhiteSpace(targetFolder.Name))
                        .Select(targetFolder => targetFolder.Name.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        coverService.PurgeCachedHeroDownloads(title);
                        coverService.PurgeCachedLogoDownloads(title);
                    }
                    refreshPhotoWorkspaceHeroBanner?.Invoke();
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var fetchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                        foreach (var targetFolder in folders)
                        {
                            await ResolveLibraryHeroBannerWithDownloadAsync(targetFolder, fetchTimeout.Token).ConfigureAwait(false);
                            await ResolveLibraryHeroLogoWithDownloadAsync(targetFolder, fetchTimeout.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        await libraryWindow.Dispatcher.InvokeAsync(() => LogException(logContext + " | " + scopeLabel, ex));
                        return;
                    }

                    await libraryWindow.Dispatcher.InvokeAsync(() =>
                    {
                        refreshPhotoWorkspaceHeroBanner?.Invoke();
                        libraryToast?.Invoke(completionToast);
                    });
                });
            }

            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };

            var setCoverItem = new MenuItem { Header = "Set Custom Cover...", IsEnabled = actionFolders.Count > 0 };
            setCoverItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.jxr;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
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

            var chooseCoverItem = new MenuItem { Header = "Choose Cover...", IsEnabled = actionFolders.Count > 0 && HasSteamGridDbApiToken() };
            chooseCoverItem.Click += async delegate
            {
                await ChooseLibraryAssetFromSteamGridDbAsync(
                    libraryWindow,
                    folder,
                    lookupFolder,
                    actionFolders,
                    LibraryAssetPickerKind.Cover,
                    showFolder,
                    renderTiles,
                    refreshPhotoWorkspaceHeroBanner,
                    libraryToast).ConfigureAwait(true);
            };

            var setBannerItem = new MenuItem { Header = "Set Custom Banner...", IsEnabled = actionFolders.Count > 0 };
            setBannerItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var picked = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.jxr;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(picked)) return;
                foreach (var targetFolder in actionFolders) SaveCustomHero(targetFolder, picked);
                showFolder(folder);
                refreshPhotoWorkspaceHeroBanner?.Invoke();
                libraryToast?.Invoke("Banner saved");
                Log("Custom banner set for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var setLogoItem = new MenuItem { Header = "Set Custom Logo...", IsEnabled = actionFolders.Count > 0 };
            setLogoItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var picked = PickFile(string.Empty, "Image Files|*.png;*.jpg;*.jpeg;*.jxr;*.bmp;*.gif;*.ico|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(picked)) return;
                foreach (var targetFolder in actionFolders) SaveCustomLogo(targetFolder, picked);
                showFolder(folder);
                refreshPhotoWorkspaceHeroBanner?.Invoke();
                libraryToast?.Invoke("Logo saved");
                Log("Custom logo set for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var chooseBannerItem = new MenuItem { Header = "Choose Banner...", IsEnabled = actionFolders.Count > 0 && HasSteamGridDbApiToken() };
            chooseBannerItem.Click += async delegate
            {
                await ChooseLibraryAssetFromSteamGridDbAsync(
                    libraryWindow,
                    folder,
                    lookupFolder,
                    actionFolders,
                    LibraryAssetPickerKind.Banner,
                    showFolder,
                    renderTiles,
                    refreshPhotoWorkspaceHeroBanner,
                    libraryToast).ConfigureAwait(true);
            };

            var chooseLogoItem = new MenuItem { Header = "Choose Logo / Icon...", IsEnabled = actionFolders.Count > 0 && HasSteamGridDbApiToken() };
            chooseLogoItem.Click += async delegate
            {
                await ChooseLibraryAssetFromSteamGridDbAsync(
                    libraryWindow,
                    folder,
                    lookupFolder,
                    actionFolders,
                    LibraryAssetPickerKind.LogoIcon,
                    showFolder,
                    renderTiles,
                    refreshPhotoWorkspaceHeroBanner,
                    libraryToast).ConfigureAwait(true);
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

            var clearLogoItem = new MenuItem
            {
                Header = "Clear Custom Logo",
                IsEnabled = actionFolders.Any(targetFolder => !string.IsNullOrWhiteSpace(CustomLogoPath(targetFolder)))
            };
            clearLogoItem.Click += delegate
            {
                foreach (var targetFolder in actionFolders.Where(targetFolder => !string.IsNullOrWhiteSpace(CustomLogoPath(targetFolder)))) ClearCustomLogo(targetFolder);
                showFolder(folder);
                refreshPhotoWorkspaceHeroBanner?.Invoke();
                libraryToast?.Invoke("Custom logo cleared");
                Log("Custom logo cleared for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };

            var fetchBannerItem = new MenuItem { Header = "Fetch Banner Art", IsEnabled = actionFolders.Count > 0 };
            fetchBannerItem.Click += delegate
            {
                QueuePhotoWorkspaceHeaderArtFetch(
                    actionFolders,
                    BuildLibraryBrowserActionScopeLabel(folder),
                    false,
                    "Banner art updated",
                    "Fetch banner art");
            };

            var refreshHeaderArtItem = new MenuItem { Header = "Refresh Title / Banner Art", IsEnabled = actionFolders.Count > 0 };
            refreshHeaderArtItem.Click += delegate
            {
                QueuePhotoWorkspaceHeaderArtFetch(
                    actionFolders,
                    BuildLibraryBrowserActionScopeLabel(folder),
                    true,
                    "Title / banner art refreshed",
                    "Refresh title / banner art");
            };

            menu.Items.Add(openMyCoversItem);
            menu.Items.Add(new Separator());

            menu.Items.Add(setCoverItem);
            menu.Items.Add(clearCoverItem);
            menu.Items.Add(fetchFolderCoverItem);
            menu.Items.Add(chooseCoverItem);
            menu.Items.Add(new Separator());

            menu.Items.Add(setBannerItem);
            menu.Items.Add(clearBannerItem);
            menu.Items.Add(fetchBannerItem);
            menu.Items.Add(chooseBannerItem);
            menu.Items.Add(new Separator());

            menu.Items.Add(setLogoItem);
            menu.Items.Add(clearLogoItem);
            menu.Items.Add(chooseLogoItem);
            menu.Items.Add(new Separator());

            menu.Items.Add(refreshHeaderArtItem);
            return menu;
        }

        void LibraryBrowserRefreshPhotoWorkspaceHeroBanner(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            LibraryBrowserFolderView info)
        {
            if (panes?.PhotoWorkspaceHeroBannerImage == null || panes.PhotoWorkspaceHeroLogoImage == null || libraryWindow == null) return;
            void ApplyLogoMode(bool showLogo)
            {
                if (panes.PhotoWorkspaceHeroLogoHost != null)
                    panes.PhotoWorkspaceHeroLogoHost.Visibility = showLogo ? Visibility.Visible : Visibility.Collapsed;
                if (panes.DetailTitle != null)
                    panes.DetailTitle.Visibility = showLogo ? Visibility.Collapsed : Visibility.Visible;
            }
            if (ws?.WorkspaceMode != LibraryWorkspaceMode.Photo)
            {
                panes.PhotoWorkspaceHeroBannerImage.Source = null;
                panes.PhotoWorkspaceHeroLogoImage.Source = null;
                ApplyLogoMode(false);
                return;
            }
            if (info == null || IsLibraryBrowserTimelineView(info))
            {
                panes.PhotoWorkspaceHeroBannerImage.Source = null;
                panes.PhotoWorkspaceHeroLogoImage.Source = null;
                ApplyLogoMode(false);
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
                    CalculateLibraryBannerArtDecodeWidth(panes.PhotoWorkspaceHeroBannerRoot, libraryWindow, ResolveLibraryDpiScale(panes.PhotoWorkspaceHeroBannerImage)),
                    delegate(BitmapImage loaded) { panes.PhotoWorkspaceHeroBannerImage.Source = loaded; },
                    true,
                    delegate { return SameLibraryBrowserSelection(ws.Current, infoCapture); });
            }

            void ApplyLogoSource(string path, bool fileExists)
            {
                if (!SameLibraryBrowserSelection(ws.Current, infoCapture)) return;
                panes.PhotoWorkspaceHeroLogoImage.Uid = Guid.NewGuid().ToString("N");
                if (!fileExists || string.IsNullOrWhiteSpace(path))
                {
                    panes.PhotoWorkspaceHeroLogoImage.Source = null;
                    ApplyLogoMode(false);
                    return;
                }

                ApplyLogoMode(true);
                QueueImageLoad(
                    panes.PhotoWorkspaceHeroLogoImage,
                    path,
                    CalculateLibraryBannerArtDecodeWidth(panes.PhotoWorkspaceHeroLogoHost, libraryWindow, ResolveLibraryDpiScale(panes.PhotoWorkspaceHeroLogoImage)),
                    delegate(BitmapImage loaded)
                    {
                        panes.PhotoWorkspaceHeroLogoImage.Source = loaded;
                        ApplyLogoMode(true);
                    },
                    true,
                    delegate { return SameLibraryBrowserSelection(ws.Current, infoCapture); });
            }

            var localPath = GetLibraryHeroBannerPathForDisplayOnly(displayFolder);
            var localLogoPath = GetLibraryHeroLogoPathForDisplayOnly(displayFolder);
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                ApplyBannerSource(localPath, true);
            else
                ApplyBannerSource(null, false);

            if (!string.IsNullOrWhiteSpace(localLogoPath) && File.Exists(localLogoPath))
                ApplyLogoSource(localLogoPath, true);
            else
                ApplyLogoSource(null, false);

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath)
                && !string.IsNullOrWhiteSpace(localLogoPath) && File.Exists(localLogoPath))
                return;

            var downloadCts = new CancellationTokenSource();
            CancelAndDisposeCts(ReplacePhotoWorkspaceHeroBannerDownloadCts(downloadCts));
            var downloadToken = downloadCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var downloaded = await ResolveLibraryHeroBannerWithDownloadAsync(displayFolder, downloadToken).ConfigureAwait(false);
                    var downloadedLogo = await ResolveLibraryHeroLogoWithDownloadAsync(displayFolder, downloadToken).ConfigureAwait(false);
                    var ok = !string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded);
                    var logoOk = !string.IsNullOrWhiteSpace(downloadedLogo) && File.Exists(downloadedLogo);
                    _ = libraryWindow.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                    {
                        ApplyBannerSource(downloaded, ok);
                        ApplyLogoSource(downloadedLogo, logoOk);
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

        /// <summary>When <see cref="libraryRefreshHeroBannerCacheOnNextLibraryOpen"/> is set, purges auto-cached hero files and SteamGridDB logos for loaded library titles (custom heroes/logos preserved).</summary>
        internal void TryRunPendingLibraryHeroBannerCacheRefresh(IReadOnlyList<LibraryFolderInfo> folders)
        {
            if (!libraryRefreshHeroBannerCacheOnNextLibraryOpen) return;
            if (folders == null || folders.Count == 0) return;

            libraryRefreshHeroBannerCacheOnNextLibraryOpen = false;
            SaveSettings();

            var titles = folders
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Name))
                .Select(f => f.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in titles)
            {
                coverService.PurgeCachedHeroDownloads(t);
                coverService.PurgeCachedLogoDownloads(t);
            }
            Log("Cleared auto-cached hero banners and logos for " + titles.Count + " library titles. Custom banners/logos unchanged; open captures or Fetch Banner Art to re-download.");
        }
    }
}
