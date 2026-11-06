using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PixelVault.Companion;

public static class LocalServicePreparation
{
    public static object Prepare(string configuration,string package,string output)
    {
        var options=JsonDocument.Parse(File.ReadAllText(configuration)).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()!;options.ValidateRoots();
        var marker=Path.Combine(options.ApplicationDataRoot,"local-store-preparation.json");
        if(!options.DirectLocal||!File.Exists(marker)||JsonDocument.Parse(File.ReadAllText(marker)).RootElement.GetProperty("purpose").GetString()!="pc-companion-rehearsal")throw new InvalidDataException("Prepare the service against a marked isolated rehearsal store first.");
        package=Path.GetFullPath(package);output=Path.GetFullPath(output);if(Directory.Exists(output))throw new InvalidDataException("Use a new protected service-plan directory.");
        foreach(var file in new[]{"PixelVault.Companion.exe",Path.Combine("readers","PixelVault.exe"),Path.Combine("media-worker","PixelVault.Companion.Media.dll")})LocalApplicationData.RequireFile(Path.Combine(package,file),package);
        using var workspace=new TrackerWorkspace(options);
        var paths=workspace.Definitions().Select(d=>workspace.Repository.Source(d.Id,workspace.Account(d.Id))).OfType<PixelVaultNative.TrackerSourceBinding>().Select(b=>Directory.Exists(b.Location)?b.Location:Path.GetDirectoryName(b.Location)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(paths.Any(p=>!Path.IsPathFullyQualified(p)||p.StartsWith(@"\\",StringComparison.Ordinal)))throw new InvalidDataException("The service's approved save sources must be local absolute paths.");
        Directory.CreateDirectory(output);var state=Path.Combine(output,"state");Directory.CreateDirectory(state);var cache=Path.Combine(output,"cache");Directory.CreateDirectory(cache);
        File.Copy(Path.Combine(options.DataRoot,"workspace.json"),Path.Combine(state,"workspace.json"));
        var accounts=Path.Combine(options.DataRoot,"achievement-accounts.json");if(File.Exists(accounts))File.Copy(accounts,Path.Combine(state,"achievement-accounts.json"));
        options.DataRoot=state;options.CacheRoot=cache;options.LocalWorkersEnabled=true;options.LocalProviderRefreshEnabled=false;options.ReaderWorkerPath=Path.Combine(package,"readers","PixelVault.exe");options.PhotoWorkerPath=Path.Combine(package,"media-worker","PixelVault.Companion.Media.dll");options.ServiceName="PixelVaultCompanionPhase4";
        options.PhotoDerivativeReuseRoot="";options.AllowHttp=true;
        var settings=Path.Combine(output,"service.json");
        File.WriteAllText(settings,JsonSerializer.Serialize(new Dictionary<string,object>{{"Urls","http://127.0.0.1:8788;http://192.168.1.69:8788"},{"AllowedHosts","localhost;127.0.0.1;192.168.1.69;DESKTOP"},{"Companion",options}},new JsonSerializerOptions{WriteIndented=true,IgnoreReadOnlyProperties=true}));
        var files=Directory.EnumerateFiles(package,"*",SearchOption.AllDirectories).Select(f=>new{Path=Path.GetRelativePath(package,f),Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}).ToArray();
        var plan=new{Version=1,Purpose="pc-companion-rehearsal",ServiceName=options.ServiceName,Executable=Path.Combine(package,"PixelVault.Companion.exe"),PackageRoot=package,Settings=settings,SettingsSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settings))),ApplicationRoot=options.ApplicationDataRoot,StateRoot=state,CacheRoot=cache,ReadOnlySaveRoots=paths,PhotoRoot=LocalPhotoIndex.Root(options.ApplicationDataRoot),Network=new{Address="192.168.1.69",Interface="Ethernet 4",RemoteSubnet="192.168.1.0/24",Port=8788},Files=files};
        File.WriteAllText(Path.Combine(output,"plan.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
        return new{prepared=true,service=options.ServiceName,port=8788,approvedSaveRoots=paths.Length,packageFiles=files.Length,installed=false,automaticUpdates=false};
    }
}
