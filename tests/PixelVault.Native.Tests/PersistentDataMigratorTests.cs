using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// PV-PLN-UI-001 Step 11: guard rails for <see cref="PersistentDataMigrator"/> so the first-run
/// copy rules (length + <c>LastWriteTimeUtc</c>), the <c>dist/PixelVault-VERSION → PixelVaultData</c>
/// probe order, the "PixelVaultData authoritative once it exists" invariant, and the savedCovers
/// README seed all stay byte-identical after future refactors. Each test spins a fresh temp
/// fixture so file IO never leaks into CI.
/// </summary>
public sealed class PersistentDataMigratorTests : IDisposable
{
    readonly string _root;
    readonly FileSystemService _fs;
    readonly List<string> _log;

    public PersistentDataMigratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pv_pdmig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _fs = new FileSystemService();
        _log = new List<string>();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    void LogLine(string message) => _log.Add(message);

    // ------------------------------------------------------------------
    // ResolvePersistentDataRoot
    // ------------------------------------------------------------------

    [Fact]
    public void ResolvePersistentDataRoot_FallsBackToAppRoot_WhenNoParentLayoutMatches()
    {
        var appRoot = Directory.CreateDirectory(Path.Combine(_root, "bare-app")).FullName;

        var result = PersistentDataMigrator.ResolvePersistentDataRoot(appRoot, LogLine);

        Assert.Equal(appRoot, result);
        Assert.Empty(_log);
    }

    [Fact]
    public void ResolvePersistentDataRoot_PrefersDistSiblingPixelVaultData_WhenReleaseVersionFolder()
    {
        var dist = Directory.CreateDirectory(Path.Combine(_root, "dist")).FullName;
        var release = Directory.CreateDirectory(Path.Combine(dist, "PixelVault-0.076")).FullName;
        var expected = Path.Combine(_root, "PixelVaultData");

        var result = PersistentDataMigrator.ResolvePersistentDataRoot(release, LogLine);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePersistentDataRoot_PrefersDistSiblingPixelVaultData_WhenCurrentShimFolder()
    {
        var dist = Directory.CreateDirectory(Path.Combine(_root, "dist")).FullName;
        var release = Directory.CreateDirectory(Path.Combine(dist, "PixelVault-current")).FullName;
        var expected = Path.Combine(_root, "PixelVaultData");

        var result = PersistentDataMigrator.ResolvePersistentDataRoot(release, LogLine);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePersistentDataRoot_WalksUpToDevCheckout_WhenPixelVaultDataAndSourceTreeCoexist()
    {
        var repoRoot = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        var pixelVaultData = Directory.CreateDirectory(Path.Combine(repoRoot, "PixelVaultData")).FullName;
        Directory.CreateDirectory(Path.Combine(repoRoot, "src", "PixelVault.Native"));
        var appRoot = Directory.CreateDirectory(Path.Combine(repoRoot, "src", "PixelVault.Native", "bin", "Debug", "net8.0-windows")).FullName;

        var result = PersistentDataMigrator.ResolvePersistentDataRoot(appRoot, LogLine);

        Assert.Equal(pixelVaultData, result);
    }

    [Fact]
    public void ResolvePersistentDataRoot_IgnoresNonMatchingReleaseFolderNames()
    {
        var dist = Directory.CreateDirectory(Path.Combine(_root, "dist")).FullName;
        // Not matching the "PixelVault-<digits>.<digits>" pattern and not "PixelVault-current".
        var release = Directory.CreateDirectory(Path.Combine(dist, "PixelVault-experimental")).FullName;

        var result = PersistentDataMigrator.ResolvePersistentDataRoot(release, LogLine);

        Assert.Equal(release, result);
    }

    // ------------------------------------------------------------------
    // CopyIfNewerOrMissing
    // ------------------------------------------------------------------

    [Fact]
    public void CopyIfNewerOrMissing_CreatesDestination_WhenMissing()
    {
        var source = Path.Combine(_root, "src.ini");
        var destination = Path.Combine(_root, "sub", "dst.ini");
        File.WriteAllText(source, "alpha");

        PersistentDataMigrator.CopyIfNewerOrMissing(source, destination, _fs);

        Assert.True(File.Exists(destination));
        Assert.Equal("alpha", File.ReadAllText(destination));
    }

    [Fact]
    public void CopyIfNewerOrMissing_SkipsCopy_WhenDestinationIsSameSizeAndAsNewOrNewer()
    {
        var source = Path.Combine(_root, "src.ini");
        var destination = Path.Combine(_root, "dst.ini");
        File.WriteAllText(source, "same");
        File.WriteAllText(destination, "same");
        var future = DateTime.UtcNow.AddHours(1);
        File.SetLastWriteTimeUtc(destination, future);

        PersistentDataMigrator.CopyIfNewerOrMissing(source, destination, _fs);

        // Length + timestamp >= source → copy skipped; destination timestamp preserved.
        Assert.Equal(future, File.GetLastWriteTimeUtc(destination));
    }

    [Fact]
    public void CopyIfNewerOrMissing_OverwritesStaleDestination_WhenSourceIsNewer()
    {
        var source = Path.Combine(_root, "src.ini");
        var destination = Path.Combine(_root, "dst.ini");
        File.WriteAllText(destination, "old");
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow.AddDays(-2));
        File.WriteAllText(source, "new");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow);

        PersistentDataMigrator.CopyIfNewerOrMissing(source, destination, _fs);

        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public void CopyIfNewerOrMissing_OverwritesDestinationOfDifferentSize_EvenWhenOlderTimestamp()
    {
        var source = Path.Combine(_root, "src.ini");
        var destination = Path.Combine(_root, "dst.ini");
        File.WriteAllText(source, "short");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-5));
        File.WriteAllText(destination, "a much longer destination body");
        File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);

        PersistentDataMigrator.CopyIfNewerOrMissing(source, destination, _fs);

        // Different length forces the copy regardless of timestamp.
        Assert.Equal("short", File.ReadAllText(destination));
    }

    [Fact]
    public void CopyIfNewerOrMissing_NoOp_WhenSourceDoesNotExist()
    {
        var destination = Path.Combine(_root, "dst.ini");

        PersistentDataMigrator.CopyIfNewerOrMissing(Path.Combine(_root, "missing.ini"), destination, _fs);

        Assert.False(File.Exists(destination));
    }

    // ------------------------------------------------------------------
    // CopyDirectoryContentsIfMissing
    // ------------------------------------------------------------------

    [Fact]
    public void CopyDirectoryContentsIfMissing_CopiesTreeRecursively_WhenDestinationEmpty()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        File.WriteAllText(Path.Combine(source, "top.txt"), "one");
        var nested = Directory.CreateDirectory(Path.Combine(source, "sub")).FullName;
        File.WriteAllText(Path.Combine(nested, "inner.txt"), "two");
        var destination = Path.Combine(_root, "dst");

        PersistentDataMigrator.CopyDirectoryContentsIfMissing(source, destination, _fs);

        Assert.Equal("one", File.ReadAllText(Path.Combine(destination, "top.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(destination, "sub", "inner.txt")));
    }

    [Fact]
    public void CopyDirectoryContentsIfMissing_KeepsExistingDestinationFiles_EvenWhenStaler()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(_root, "dst")).FullName;
        File.WriteAllText(Path.Combine(source, "shared.txt"), "from-source");
        File.WriteAllText(Path.Combine(destination, "shared.txt"), "authoritative");

        PersistentDataMigrator.CopyDirectoryContentsIfMissing(source, destination, _fs);

        // "Missing"-only semantics: PixelVaultData stays authoritative.
        Assert.Equal("authoritative", File.ReadAllText(Path.Combine(destination, "shared.txt")));
    }

    [Fact]
    public void CopyDirectoryContentsIfMissing_NoOp_WhenSourceDirectoryDoesNotExist()
    {
        var destination = Path.Combine(_root, "dst");

        PersistentDataMigrator.CopyDirectoryContentsIfMissing(Path.Combine(_root, "missing"), destination, _fs);

        Assert.False(Directory.Exists(destination));
    }

    // ------------------------------------------------------------------
    // MigrateFromLegacyVersions
    // ------------------------------------------------------------------

    [Fact]
    public void MigrateFromLegacyVersions_NoOp_WhenDataRootEqualsAppRoot()
    {
        var appRoot = Directory.CreateDirectory(Path.Combine(_root, "shared-app")).FullName;
        File.WriteAllText(Path.Combine(appRoot, "PixelVault.settings.ini"), "k=v");

        // Same path case-insensitively → migration does nothing.
        PersistentDataMigrator.MigrateFromLegacyVersions(
            appRoot,
            appRoot.ToUpperInvariant(),
            Path.Combine(appRoot, "PixelVault.settings.ini"),
            Path.Combine(appRoot, "cache"),
            Path.Combine(appRoot, "logs"),
            _fs);

        Assert.False(Directory.Exists(Path.Combine(appRoot, "cache")));
        Assert.False(Directory.Exists(Path.Combine(appRoot, "logs")));
    }

    [Fact]
    public void MigrateFromLegacyVersions_CopiesSettingsAndCachesFromAppRoot_WhenDataRootIsSeparate()
    {
        var appRoot = Directory.CreateDirectory(Path.Combine(_root, "dist", "PixelVault-0.076")).FullName;
        var dataRoot = Directory.CreateDirectory(Path.Combine(_root, "PixelVaultData")).FullName;
        File.WriteAllText(Path.Combine(appRoot, "PixelVault.settings.ini"), "k=legacy");
        Directory.CreateDirectory(Path.Combine(appRoot, "cache"));
        File.WriteAllText(Path.Combine(appRoot, "cache", "covers.db"), "cache-bytes");
        Directory.CreateDirectory(Path.Combine(appRoot, "logs"));
        File.WriteAllText(Path.Combine(appRoot, "logs", "PixelVault-native.log"), "legacy-log");

        var settingsPath = Path.Combine(dataRoot, "PixelVault.settings.ini");
        var cacheRoot = Path.Combine(dataRoot, "cache");
        var logsRoot = Path.Combine(dataRoot, "logs");

        PersistentDataMigrator.MigrateFromLegacyVersions(appRoot, dataRoot, settingsPath, cacheRoot, logsRoot, _fs);

        Assert.Equal("k=legacy", File.ReadAllText(settingsPath));
        Assert.Equal("cache-bytes", File.ReadAllText(Path.Combine(cacheRoot, "covers.db")));
        Assert.Equal("legacy-log", File.ReadAllText(Path.Combine(logsRoot, "PixelVault-native.log")));
    }

    [Fact]
    public void MigrateFromLegacyVersions_DoesNotOverwriteAuthoritativeCache_WhenAlreadyPresent()
    {
        var appRoot = Directory.CreateDirectory(Path.Combine(_root, "dist", "PixelVault-0.076")).FullName;
        var dataRoot = Directory.CreateDirectory(Path.Combine(_root, "PixelVaultData")).FullName;
        Directory.CreateDirectory(Path.Combine(appRoot, "cache"));
        File.WriteAllText(Path.Combine(appRoot, "cache", "covers.db"), "legacy");
        Directory.CreateDirectory(Path.Combine(dataRoot, "cache"));
        File.WriteAllText(Path.Combine(dataRoot, "cache", "covers.db"), "authoritative");

        PersistentDataMigrator.MigrateFromLegacyVersions(
            appRoot,
            dataRoot,
            Path.Combine(dataRoot, "PixelVault.settings.ini"),
            Path.Combine(dataRoot, "cache"),
            Path.Combine(dataRoot, "logs"),
            _fs);

        Assert.Equal("authoritative", File.ReadAllText(Path.Combine(dataRoot, "cache", "covers.db")));
    }

    [Fact]
    public void MigrateFromLegacyVersions_FillsMissingSlots_FromSiblingDistReleases()
    {
        var distDir = Directory.CreateDirectory(Path.Combine(_root, "dist")).FullName;
        var currentAppRoot = Directory.CreateDirectory(Path.Combine(distDir, "PixelVault-0.076")).FullName;
        var siblingAppRoot = Directory.CreateDirectory(Path.Combine(distDir, "PixelVault-0.075")).FullName;
        var dataRoot = Directory.CreateDirectory(Path.Combine(_root, "PixelVaultData")).FullName;
        Directory.CreateDirectory(Path.Combine(siblingAppRoot, "cache"));
        File.WriteAllText(Path.Combine(siblingAppRoot, "cache", "sidecar.json"), "from-sibling");
        File.WriteAllText(Path.Combine(siblingAppRoot, "PixelVault.settings.ini"), "k=sibling");

        var settingsPath = Path.Combine(dataRoot, "PixelVault.settings.ini");
        var cacheRoot = Path.Combine(dataRoot, "cache");
        var logsRoot = Path.Combine(dataRoot, "logs");

        PersistentDataMigrator.MigrateFromLegacyVersions(currentAppRoot, dataRoot, settingsPath, cacheRoot, logsRoot, _fs);

        Assert.Equal("from-sibling", File.ReadAllText(Path.Combine(cacheRoot, "sidecar.json")));
        Assert.Equal("k=sibling", File.ReadAllText(settingsPath));
    }

    // ------------------------------------------------------------------
    // EnsureSavedCoversReadme
    // ------------------------------------------------------------------

    [Fact]
    public void EnsureSavedCoversReadme_WritesBody_WhenMissing()
    {
        var savedCovers = Directory.CreateDirectory(Path.Combine(_root, "SavedCovers")).FullName;

        PersistentDataMigrator.EnsureSavedCoversReadme(savedCovers, LogLine);

        var readmePath = Path.Combine(savedCovers, "README.txt");
        Assert.True(File.Exists(readmePath));
        var body = File.ReadAllText(readmePath);
        Assert.Contains("My Covers (permanent stash)", body);
        Assert.Contains("Set Custom Cover", body);
    }

    [Fact]
    public void EnsureSavedCoversReadme_DoesNotOverwrite_WhenAlreadyPresent()
    {
        var savedCovers = Directory.CreateDirectory(Path.Combine(_root, "SavedCovers")).FullName;
        var readmePath = Path.Combine(savedCovers, "README.txt");
        File.WriteAllText(readmePath, "user-edited");

        PersistentDataMigrator.EnsureSavedCoversReadme(savedCovers, LogLine);

        Assert.Equal("user-edited", File.ReadAllText(readmePath));
    }

    [Fact]
    public void EnsureSavedCoversReadme_SwallowsExceptionsAndLogs_WhenRootIsInvalid()
    {
        // Passing a path made of invalid characters provokes a Path.Combine / IO failure that the
        // migrator swallows into the log callback — startup on a quirky share must not fail.
        var invalid = "\0\0\0";

        PersistentDataMigrator.EnsureSavedCoversReadme(invalid, LogLine);

        Assert.Contains(_log, line => line.StartsWith("EnsureSavedCoversReadme: "));
    }
}
