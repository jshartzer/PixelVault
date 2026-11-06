using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public void MapStartupReadinessChecksOwnAccessAndPreservesOriginalsAndDatabases()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        var images = Path.Combine(fixture.App, "game-maps", "images"); Directory.CreateDirectory(images);
        var original = Path.Combine(images, new string('a', 64) + ".png"); File.WriteAllBytes(original, [1, 2, 3]);
        var paths = new[] { MapDatabase(fixture.App), fixture.Case.Desktop.DatabasePath, original };
        var hashes = paths.Select(p => SHA256.HashData(File.ReadAllBytes(p))).ToArray();
        using var writer = new SqliteConnection("Data Source=" + MapDatabase(fixture.App) + ";Pooling=False"); writer.Open();
        var result = LocalMapReadiness.Verify(new() { ApplicationDataRoot = fixture.App, CacheRoot = fixture.Cache });
        Assert.False(string.IsNullOrWhiteSpace(result.Identity)); Assert.Equal(2, result.ReadableMapFiles); Assert.True(result.CacheWritable);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Cache, "maps")));
        writer.Close();
        for (var i = 0; i < paths.Length; i++) Assert.Equal(hashes[i], SHA256.HashData(File.ReadAllBytes(paths[i])));
    }

    [Fact]
    public void MapStartupReadinessDoesNotCreateAMissingMapLibraryOrCache()
    {
        using var fixture = new DirectCase();
        var result = LocalMapReadiness.Verify(new() { ApplicationDataRoot = fixture.App, CacheRoot = fixture.Cache });
        Assert.Equal(0, result.ReadableMapFiles); Assert.False(result.CacheWritable);
        Assert.False(Directory.Exists(Path.Combine(fixture.App, "game-maps"))); Assert.False(Directory.Exists(Path.Combine(fixture.Cache, "maps")));
    }

    [Fact]
    public void MapStartupReadinessRejectsUnavailableStoreWithoutCreatingReplacement()
    {
        using var fixture = new DirectCase(); Directory.CreateDirectory(Path.Combine(fixture.App, "game-maps"));
        Assert.ThrowsAny<IOException>(() => LocalMapReadiness.Verify(new() { ApplicationDataRoot = fixture.App, CacheRoot = fixture.Cache }));
        Assert.False(File.Exists(MapDatabase(fixture.App))); Assert.False(Directory.Exists(Path.Combine(fixture.Cache, "maps")));
    }
}
