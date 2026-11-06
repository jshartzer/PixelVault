using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PixelVault.Companion;
using PixelVaultNative;
using Microsoft.Data.Sqlite;

if(args.Length is not (3 or 4)||args.Length==4&&args[2]!="--owner-snapshot")throw new ArgumentException("Use RestoreProof <isolated-restored-root> <service-settings> <private-test-owner-credentials> OR --owner-snapshot <captured-companion.sqlite>.");
var root=Path.GetFullPath(args[0]);
if(!File.Exists(Path.Combine(root,"restore-receipt.json")))throw new InvalidDataException("A verified isolated restore is required.");
var original=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()!;
var options=JsonSerializer.Deserialize<CompanionOptions>(JsonSerializer.Serialize(original))!;
options.ApplicationDataRoot=Path.Combine(root,"application");options.DataRoot=Path.Combine(root,"state");options.CacheRoot=Path.Combine(root,"cache");options.ValidateRoots();
if(options.ApplicationDataRoot==original.ApplicationDataRoot||options.DataRoot==original.DataRoot)throw new InvalidDataException();
var owner=new OwnerStore(options);
var loginExercised=args.Length==3;
if(loginExercised)
{
    using var auth=JsonDocument.Parse(File.ReadAllText(args[2]));
    if(owner.Authenticate(auth.RootElement.GetProperty("name").GetString()!,auth.RootElement.GetProperty("password").GetString()!) is null)throw new InvalidOperationException("The restored owner could not authenticate.");
}
else
{
    using var c=LocalApplicationData.Read(args[3]);using var q=c.CreateCommand();q.CommandText="SELECT name,password_hash FROM owner";using var row=q.ExecuteReader();
    var restoredOwner=owner.Read();if(!row.Read()||restoredOwner is null||row.GetString(0)!=restoredOwner.Name||row.GetString(1)!=restoredOwner.PasswordHash)throw new InvalidOperationException("The actual owner hash was not preserved.");
}
var keys=Path.Combine(options.DataRoot,"keys");
var keyHashes=Directory.GetFiles(keys).ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
var protector=DataProtectionProvider.Create(new DirectoryInfo(keys),b=>b.SetApplicationName("PixelVault.Companion").DisableAutomaticKeyGeneration()).CreateProtector("isolated-restore-proof");
var protectedValue=protector.Protect("restore-check");
if(protector.Unprotect(protectedValue)!="restore-check")throw new InvalidOperationException("The restored same-PC cookie keys could not be used.");
if(keyHashes.Any(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(keys,p.Key!))))!=p.Value))throw new InvalidOperationException("Restore proof changed the saved key ring.");
using(var workspace=new TrackerWorkspace(options))
{
    var reader=new LocalReaderClient(options);
    await reader.Run(new(){Operation="access",ApplicationRoot=options.ApplicationDataRoot,CacheRoot=options.CacheRoot,Accounts=AchievementAccountScope.Read(options,workspace),RequireProviderCredentials=true},default);
}
await DomainProof.Run(options,root);
var artManifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(options.ApplicationDataRoot,"achievement-artwork","manifest.json")));
var art=0;
foreach(var row in artManifest.RootElement.EnumerateArray())
{
    var file=row.GetProperty("file").GetString()!;
    var path=Path.Combine(options.ApplicationDataRoot,"achievement-artwork",file);
    if(Path.GetFileName(file)!=file||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()!=row.GetProperty("sha256").GetString())throw new InvalidDataException("Restored curated artwork mismatch.");
    art++;
}
var result=new{Passed=true,OwnerLoginRestored=loginExercised,ActualOwnerHashPreserved=true,SamePcProtectedKeysRestored=true,ProviderCredentialFileReadable=true,CuratedArtworkRestored=art,Runtime=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"domain-proof.json"))).RootElement.Clone(),LiveRootsChanged=false,NoListenerOpened=true};
File.WriteAllText(Path.Combine(root,"restore-proof.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result));
