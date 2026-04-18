#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 11: static helpers for the first-run persistent-data migration and the
    /// "open folder in Explorer" shell glue. Bodies are ported verbatim from the <see cref="MainWindow"/>
    /// block (lines ~1634–1786) so behavior must stay byte-identical — path probe order, copy
    /// thresholds (length + <c>LastWriteTimeUtc</c>), the savedCovers README text, and the
    /// primary/fallback <see cref="Process"/> strategy in <see cref="OpenFolder"/> are all load-bearing.
    ///
    /// <para>
    /// <see cref="MainWindow"/> keeps thin one-line forwarders for:
    /// <see cref="MainWindow.ResolvePersistentDataRoot"/> (still passed as a <see cref="Func{T, TResult}"/>
    /// to <c>ComputePersistentStorageLayout</c>), <see cref="MainWindow.OpenFolder"/> (bound as an
    /// <see cref="Action{String}"/> into <c>SettingsShellDependencies</c> / <c>PhotoIndexEditorHost</c>
    /// / <c>GameIndexEditorHost</c> / <c>HealthDashboardWindow</c>), and the saved-covers entry
    /// points reached from the library palette, settings shell, photo hero, folder tile, and
    /// nav chrome.
    /// </para>
    /// </summary>
    internal static class PersistentDataMigrator
    {
        static readonly Regex LegacyReleaseFolderRegex = new Regex(@"^PixelVault-\d+\.\d+$", RegexOptions.IgnoreCase);
        const string SavedCoversReadmeBody =
            "My Covers (permanent stash)\r\n" +
            "\r\n" +
            "Save or copy cover images here (JPG, PNG, GIF, BMP). Subfolders are fine.\r\n" +
            "In the library, right-click a game folder, choose Set Custom Cover — the file picker starts here (use Open My Covers Folder in the same menu if you want Explorer).\r\n" +
            "This folder is not part of the cache; PixelVault will not delete it when refreshing covers.\r\n";

        /// <summary>
        /// Writable application data root (settings, SQLite cache, logs, saved covers). Same layout as
        /// repo <c>PixelVaultData/</c>: <c>PixelVault.settings.ini</c>, <c>cache/</c>, <c>logs/</c>, <c>saved-covers/</c>.
        /// </summary>
        internal const string LocalAppDataPixelVaultFolderName = "PixelVault";

        /// <summary>
        /// Probes for an external <c>PixelVaultData</c> root so data is shared across release folders.
        /// Order: (1) if we're running from a <c>dist/PixelVault-VERSION</c> or <c>dist/PixelVault-current</c>
        /// layout, use <c>&lt;dist parent&gt;/PixelVaultData</c>; (2) walk upwards until we find a folder
        /// that contains both <c>PixelVaultData/</c> and <c>src/PixelVault.Native/</c> (dev-checkout);
        /// (3) if the EXE directory is under a restricted install location (Program Files, WindowsApps),
        /// use <c>%LocalAppData%\PixelVault</c> so mutable state is never written beside a read-only install;
        /// (4) otherwise fall back to the current app root (portable / arbitrary writable folder).
        /// </summary>
        /// <remarks>
        /// PV-PLN-DIST-001 §5.8: installed builds must not assume a writable install directory.
        /// See <c>docs/DISTRIBUTION_STORAGE.md</c>.
        /// </remarks>
        public static string ResolvePersistentDataRoot(string currentAppRoot, Action<string>? log)
        {
            try
            {
                var currentDir = new DirectoryInfo(currentAppRoot);
                if (currentDir != null
                    && currentDir.Parent != null
                    && string.Equals(currentDir.Parent.Name, "dist", StringComparison.OrdinalIgnoreCase)
                    && currentDir.Parent.Parent != null
                    && (LegacyReleaseFolderRegex.IsMatch(currentDir.Name)
                        || string.Equals(currentDir.Name, "PixelVault-current", StringComparison.OrdinalIgnoreCase)))
                {
                    return Path.Combine(currentDir.Parent.Parent.FullName, "PixelVaultData");
                }

                var probe = currentDir;
                while (probe != null)
                {
                    var pixelVaultDataPath = Path.Combine(probe.FullName, "PixelVaultData");
                    var sourceProjectPath = Path.Combine(probe.FullName, "src", "PixelVault.Native");
                    if (Directory.Exists(pixelVaultDataPath) && Directory.Exists(sourceProjectPath))
                    {
                        return pixelVaultDataPath;
                    }
                    probe = probe.Parent;
                }

                if (LooksLikeRestrictedInstallDirectory(currentAppRoot))
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        LocalAppDataPixelVaultFolderName);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("ResolvePersistentDataRoot: " + ex.Message);
            }
            return currentAppRoot;
        }

        /// <summary>
        /// Program Files / WindowsApps-style locations are typically not writable for app data beside the EXE.
        /// </summary>
        internal static bool LooksLikeRestrictedInstallDirectory(string appRoot)
        {
            if (string.IsNullOrWhiteSpace(appRoot)) return false;
            string full;
            try
            {
                full = Path.GetFullPath(appRoot.TrimEnd(Path.DirectorySeparatorChar));
            }
            catch
            {
                return false;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (var anchor in new[] { programFiles, programFilesX86 })
            {
                if (string.IsNullOrEmpty(anchor)) continue;
                var root = anchor.TrimEnd(Path.DirectorySeparatorChar);
                if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Desktop Bridge / MSIX extracted layouts commonly live under ...\WindowsApps\...
            if (full.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        /// <summary>
        /// Copies settings / cache / logs from a previously-installed release (or sibling
        /// <c>dist/PixelVault-*</c> folders) into the authoritative <c>PixelVaultData</c> root when
        /// those slots are missing. Shared <c>PixelVaultData</c> stays authoritative once it exists —
        /// release-local caches can only bootstrap missing files; they never overwrite newer shared
        /// index data. No-op when <paramref name="dataRoot"/> matches <paramref name="appRoot"/>.
        /// </summary>
        public static void MigrateFromLegacyVersions(
            string appRoot,
            string dataRoot,
            string settingsPath,
            string cacheRoot,
            string logsRoot,
            IFileSystemService fileSystemService)
        {
            if (fileSystemService == null) throw new ArgumentNullException(nameof(fileSystemService));
            if (string.Equals(dataRoot, appRoot, StringComparison.OrdinalIgnoreCase)) return;
            CopyIfNewerOrMissing(Path.Combine(appRoot, "PixelVault.settings.ini"), settingsPath, fileSystemService);
            // Shared PixelVaultData becomes authoritative once it exists; release-local caches
            // can help bootstrap missing files, but they should never roll newer shared index data back.
            CopyDirectoryContentsIfMissing(Path.Combine(appRoot, "cache"), cacheRoot, fileSystemService);
            CopyDirectoryContentsIfMissing(Path.Combine(appRoot, "logs"), logsRoot, fileSystemService);
            var currentDir = new DirectoryInfo(appRoot);
            var distDir = currentDir == null ? null : currentDir.Parent;
            if (distDir == null || !distDir.Exists) return;
            foreach (var dir in distDir.GetDirectories("PixelVault-*").OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar), appRoot, StringComparison.OrdinalIgnoreCase)) continue;
                CopyIfNewerOrMissing(Path.Combine(dir.FullName, "PixelVault.settings.ini"), settingsPath, fileSystemService);
                CopyDirectoryContentsIfMissing(Path.Combine(dir.FullName, "cache"), cacheRoot, fileSystemService);
                CopyDirectoryContentsIfMissing(Path.Combine(dir.FullName, "logs"), logsRoot, fileSystemService);
            }
        }

        internal static void CopyIfNewerOrMissing(string sourcePath, string destinationPath, IFileSystemService fileSystemService)
        {
            if (!File.Exists(sourcePath)) return;
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
            if (File.Exists(destinationPath))
            {
                var sourceInfo = new FileInfo(sourcePath);
                var destinationInfo = new FileInfo(destinationPath);
                if (destinationInfo.Length == sourceInfo.Length && destinationInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc) return;
            }
            fileSystemService.CopyFile(sourcePath, destinationPath, true);
        }

        /// <summary>
        /// Recursively copies files from <paramref name="sourceDirectory"/> into
        /// <paramref name="destinationDirectory"/> when the destination is missing or older
        /// (same length + <c>LastWriteTimeUtc</c> &gt;= source skips the copy). Currently unused
        /// by any caller but preserved for parity with the pre-extraction surface and future
        /// index-refresh flows.
        /// </summary>
        internal static void CopyDirectoryContentsIfNewer(string sourceDirectory, string destinationDirectory, IFileSystemService fileSystemService)
        {
            if (!Directory.Exists(sourceDirectory)) return;
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = sourceFile.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                var destinationFile = Path.Combine(destinationDirectory, relative);
                var destinationFolder = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationFolder)) Directory.CreateDirectory(destinationFolder);
                if (File.Exists(destinationFile))
                {
                    var sourceInfo = new FileInfo(sourceFile);
                    var destinationInfo = new FileInfo(destinationFile);
                    if (destinationInfo.Length == sourceInfo.Length && destinationInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc) continue;
                }
                fileSystemService.CopyFile(sourceFile, destinationFile, true);
            }
        }

        internal static void CopyDirectoryContentsIfMissing(string sourceDirectory, string destinationDirectory, IFileSystemService fileSystemService)
        {
            if (!Directory.Exists(sourceDirectory)) return;
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = sourceFile.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                var destinationFile = Path.Combine(destinationDirectory, relative);
                if (File.Exists(destinationFile)) continue;
                var destinationFolder = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationFolder)) Directory.CreateDirectory(destinationFolder);
                fileSystemService.CopyFile(sourceFile, destinationFile, false);
            }
        }

        /// <summary>
        /// Opens <paramref name="path"/> in Explorer. Tries the default shell association first
        /// (respects "Open with" overrides) then falls back to <c>explorer.exe</c> directly if that
        /// throws. Logs the fallback reason through <paramref name="log"/>. No-op when the path
        /// is missing — matches pre-extraction behavior.
        /// </summary>
        public static void OpenFolder(string path, Action<string>? log)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            var fullPath = Path.GetFullPath(path);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex)
            {
                log?.Invoke("OpenFolder primary open failed; trying explorer. " + ex.Message);
                Process.Start(new ProcessStartInfo("explorer.exe", fullPath) { UseShellExecute = true });
            }
        }

        /// <summary>
        /// Writes the standard "My Covers" README into <paramref name="savedCoversRoot"/> when it
        /// isn't already there. Fire-and-forget — failures are logged but never thrown so startup
        /// never fails because a user dropped the folder on a read-only share.
        /// </summary>
        public static void EnsureSavedCoversReadme(string savedCoversRoot, Action<string>? log)
        {
            try
            {
                var readme = Path.Combine(savedCoversRoot, "README.txt");
                if (File.Exists(readme)) return;
                File.WriteAllText(readme, SavedCoversReadmeBody);
            }
            catch (Exception ex)
            {
                log?.Invoke("EnsureSavedCoversReadme: " + ex.Message);
            }
        }

        /// <summary>
        /// Ensures <paramref name="savedCoversRoot"/> (and its README) exist, then opens the folder
        /// in Explorer. Creation failures are logged but don't block the open attempt.
        /// </summary>
        public static void OpenSavedCoversFolder(string savedCoversRoot, Action<string>? log)
        {
            try
            {
                Directory.CreateDirectory(savedCoversRoot);
                EnsureSavedCoversReadme(savedCoversRoot, log);
            }
            catch (Exception ex)
            {
                log?.Invoke("OpenSavedCoversFolder setup: " + ex.Message);
            }
            OpenFolder(savedCoversRoot, log);
        }
    }
}
