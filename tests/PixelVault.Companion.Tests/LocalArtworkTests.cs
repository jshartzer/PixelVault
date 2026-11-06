using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task LocalArtworkCreatesJpegPreservesOriginalAndUsesCacheUntilSourceChanges()
    {
        if(!OperatingSystem.IsWindows())return;
        using var fixture=new PhotoCase();var options=fixture.Options;
        var directory=Path.Combine(options.ApplicationDataRoot,"cache","covers");Directory.CreateDirectory(directory);
        var original=Path.Combine(directory,"cover.png");File.WriteAllBytes(original,PhotoCase.Png);
        var cache=new LocalArtworkCache(options);
        var first=await cache.Get((original,"image/png"),"cover",default);
        Assert.StartsWith(Path.Combine(options.CacheRoot,"library-artwork"),first.Path);Assert.Equal("image/jpeg",first.ContentType);
        Assert.Equal(PhotoCase.Png,File.ReadAllBytes(original));
        options.PhotoWorkerPath="";
        var second=await cache.Get((original,"image/png"),"cover",default);Assert.Equal(first,second);
        // A changed source cannot silently return the earlier cached image.
        File.SetLastWriteTimeUtc(original,DateTime.UtcNow.AddMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>cache.Get((original,"image/png"),"cover",default));
    }

    [Fact]
    public async Task LocalArtworkRejectsFilesOutsideCoverStoreAndLeavesCopiedModeUnchanged()
    {
        if(!OperatingSystem.IsWindows())return;
        using var fixture=new PhotoCase();var options=fixture.Options;
        var external=Path.Combine(options.ApplicationDataRoot,"external.png");File.WriteAllBytes(external,PhotoCase.Png);
        await Assert.ThrowsAsync<IOException>(()=>new LocalArtworkCache(options).Get((external,"image/png"),"cover",default));
        options.StorageMode="isolated-copy";
        Assert.Equal((external,"image/png"),await new LocalArtworkCache(options).Get((external,"image/png"),"cover",default));
    }
}
