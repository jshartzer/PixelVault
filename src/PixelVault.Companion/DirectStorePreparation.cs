using Microsoft.Data.Sqlite;
using PixelVaultNative;
using System.Text.Json;
using System.Security.Cryptography;

namespace PixelVault.Companion;

/// <summary>Explicit schema owner for isolated migration rehearsals. Never invoked by web requests.</summary>
public static class DirectStorePreparation
{
    public static void InitializeCanonical(string guardFile)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(guardFile));var guard=document.RootElement;
        if(guard.GetProperty("Purpose").GetString()!="pc-companion-cutover-frozen"||!guard.GetProperty("WritersStopped").GetBoolean())throw new InvalidDataException("A frozen cutover guard is required.");
        var root=guard.GetProperty("ApplicationRoot").GetString()!;
        if(!Path.IsPathFullyQualified(root)||!Directory.Exists(root))throw new InvalidDataException("An existing canonical application root is required.");
        var required=new[]{"achievement-tracking/pixelvault-tracking.sqlite","guides/pixelvault-guides.sqlite"};
        var files=guard.GetProperty("Files").EnumerateArray().ToArray();
        foreach(var name in required)if(!files.Any(f=>f.GetProperty("Path").GetString()==name))throw new InvalidDataException("The frozen schema-owner inputs are incomplete.");
        foreach(var entry in files)
        {
            var name=entry.GetProperty("Path").GetString()!;var path=Path.GetFullPath(Path.Combine(root,name));var relative=Path.GetRelativePath(root,path);
            if(Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar))throw new InvalidDataException("A frozen input escaped the canonical root.");
            LocalApplicationData.RequireFile(path,root);
            if(!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(entry.GetProperty("Sha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A frozen application input changed.");
        }
        InitializeStores(root);
    }
    public static void Initialize(string applicationRoot)
    {
        applicationRoot=Path.GetFullPath(applicationRoot);
        var marker=Path.Combine(applicationRoot,"local-store-preparation.json");
        if(!File.Exists(marker)||JsonDocument.Parse(File.ReadAllText(marker)).RootElement.GetProperty("purpose").GetString()!="pc-companion-rehearsal")
            throw new InvalidDataException("Shared schema preparation requires an explicitly staged rehearsal store.");
        InitializeStores(applicationRoot);
    }
    static void InitializeStores(string applicationRoot)
    {
        if(!File.Exists(Path.Combine(applicationRoot,"guides","pixelvault-guides.sqlite")))throw new InvalidDataException("The canonical guide database is missing.");
        new TrackerRepository(applicationRoot,pooling:false).InitializeSharedSchema();
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(applicationRoot,"guides","pixelvault-guides.sqlite"),Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());c.Open();
        AchievementGroupCommands.Initialize(c);
    }
}
