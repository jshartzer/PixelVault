using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public void CanonicalSchemaPreparationRequiresFrozenMatchingInputsAndPreservesRecords()
    {
        using var data=new DirectCase();
        var guard=Path.Combine(data.Case.Base,"canonical-guard.json");
        var note=Path.Combine(data.App,"freeze-check.txt");File.WriteAllText(note,"original");
        var before=File.ReadAllText(Path.Combine(data.App,"PixelVault.settings.ini"));
        object Files()=>new[]{"achievement-tracking/pixelvault-tracking.sqlite","guides/pixelvault-guides.sqlite","freeze-check.txt"}.Select(p=>new{Path=p,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data.App,p))))}).ToArray();
        void Guard(bool stopped)=>File.WriteAllText(guard,JsonSerializer.Serialize(new{Purpose="pc-companion-cutover-frozen",WritersStopped=stopped,ApplicationRoot=data.App,Files=Files()}));
        Guard(false);Assert.Throws<InvalidDataException>(()=>DirectStorePreparation.InitializeCanonical(guard));
        Guard(true);File.WriteAllText(note,"changed");Assert.Throws<InvalidDataException>(()=>DirectStorePreparation.InitializeCanonical(guard));
        Guard(true);DirectStorePreparation.InitializeCanonical(guard);
        Assert.Equal(before,File.ReadAllText(Path.Combine(data.App,"PixelVault.settings.ini")));
    }

    [Fact]
    public void CanonicalSchemaPreparationRefusesMissingRequiredFilesAndEscapingPaths()
    {
        using var data=new DirectCase();var guard=Path.Combine(data.Case.Base,"bad-guard.json");
        File.WriteAllText(guard,JsonSerializer.Serialize(new{Purpose="pc-companion-cutover-frozen",WritersStopped=true,ApplicationRoot=data.App,Files=Array.Empty<object>()}));
        Assert.Throws<InvalidDataException>(()=>DirectStorePreparation.InitializeCanonical(guard));
        var escape=Path.Combine(data.App,"..","outside.txt");File.WriteAllText(escape,"unchanged");
        var entries=new[]{"achievement-tracking/pixelvault-tracking.sqlite","guides/pixelvault-guides.sqlite","../outside.txt"}.Select(p=>new{Path=p,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data.App,p))))});
        File.WriteAllText(guard,JsonSerializer.Serialize(new{Purpose="pc-companion-cutover-frozen",WritersStopped=true,ApplicationRoot=data.App,Files=entries}));
        Assert.Throws<InvalidDataException>(()=>DirectStorePreparation.InitializeCanonical(guard));Assert.Equal("unchanged",File.ReadAllText(escape));
    }
}
