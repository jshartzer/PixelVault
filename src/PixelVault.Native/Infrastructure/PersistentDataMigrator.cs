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

        /// <summary>Process env override (highest precedence). Set to an absolute directory path.</summary>
        internal const string PixelVaultDataRootEnvVar = "PIXELVAULT_DATA_ROOT";

        /// <summary>Optional file beside the EXE: one line <c>DataRoot=C:\path</c> (see <c>docs/DISTRIBUTION_STORAGE.md</c>).</summary>
        internal const string DataRootSidecarFileName = "PixelVault.data-root.ini";

        /// <summary>
        /// Probes for the writable data root. Order:
        /// (1) <c>PIXELVAULT_DATA_ROOT</c> environment variable;
        /// (2) <c>PixelVault.data-root.ini</c> beside the EXE (<c>DataRoot=</c>);
        /// (3) <c>dist/PixelVault-VERSION</c> / <c>dist/PixelVault-current</c> → sibling <c>PixelVaultData</c>;
        /// (4) dev checkout walk (<c>PixelVaultData</c> + <c>src/PixelVault.Native</c>);
        /// (5) restricted install dir (Program Files / WindowsApps) → <c>%LocalAppData%\PixelVault</c>;
        /// (6) fallback: app directory (portable).
        /// </summary>
        /// <remarks>
        /// PV-PLN-DIST-001 §5.8: installed builds must not assume a writable install directory.
        /// See <c>docs/DISTRIBUTION_STORAGE.md</c>.
        /// </remarks>
        public static string ResolvePersistentDataRoot(string currentAppRoot, Action<string>? log)
        {
            try
            {
                var envRoot = TryNormalizeWritableRootOverride(
                    Environment.GetEnvironmentVariable(PixelVaultDataRootEnvVar),
                    log,
                    PixelVaultDataRootEnvVar);
                if (envRoot != null) return envRoot;

                var sidecarRoot = TryReadDataRootSidecar(currentAppRoot, log);
                if (sidecarRoot != null) return sidecarRoot;

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

        /// <summary>Normalizes an absolute override path; returns null if empty or invalid.</summary>
        internal static string? TryNormalizeWritableRootOverride(string? value, Action<string>? log, string sourceLabel)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                var trimmed = value.Trim().Trim('"');
                if (trimmed.Length == 0) return null;
                return Path.GetFullPath(trimmed);
            }
            catch (Exception ex)
            {
                log?.Invoke(sourceLabel + ": invalid path — " + ex.Message);
                return null;
            }
        }

        internal static string? TryReadDataRootSidecar(string appRoot, Action<string>? log)
        {
            if (string.IsNullOrWhiteSpace(appRoot)) return null;
            try
            {
                var sidecar = Path.Combine(appRoot, DataRootSidecarFileName);
                if (!File.Exists(sidecar)) return null;
                foreach (var raw in File.ReadAllLines(sidecar))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                        continue;
                    if (line.StartsWith("DataRoot=", StringComparison.OrdinalIgnoreCase))
                    {
                        var pathPart = line.Substring("DataRoot=".Length).Trim();
                        return TryNormalizeWritableRootOverride(pathPart, log, DataRootSidecarFileName);
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke(DataRootSidecarFileName + ": " + ex.Message);
            }
            return null;
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
