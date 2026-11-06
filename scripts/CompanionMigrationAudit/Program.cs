using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// Standalone inventory helper: no application constructors, migrations or commands.
// Connections are read-only/query-only. SQLite may need writable WAL sidecars.
// Output must be a new directory outside the live root.
if (args.Length is not (2 or 3) || args.Length==3&&args[2]!="--include-auth-keys") throw new ArgumentException("Usage: Snapshot <NAS-data-root> <new-output-root> [--include-auth-keys]");
SQLitePCL.Batteries_V2.Init();
var source = Path.GetFullPath(args[0]);
var target = Path.GetFullPath(args[1]);
var relative = Path.GetRelativePath(source, target);
if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
    throw new ArgumentException("Output must be outside the live data root.");
if (Directory.Exists(target)) throw new IOException("Output must be new.");
Directory.CreateDirectory(target);
var started = DateTime.UtcNow;
var databases = new List<object>();
foreach (var name in new[] { "companion.sqlite", "achievement-tracking/pixelvault-tracking.sqlite" })
{
    var file = Path.Combine(source, name);
    if (!File.Exists(file)) throw new FileNotFoundException("Required source database missing.", name);
    var output = Path.Combine(target, name);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    input.Open();
    using (var guard = input.CreateCommand()) { guard.CommandText = "PRAGMA query_only=ON"; guard.ExecuteNonQuery(); }
    using var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = output, Pooling = false }.ToString());
    copy.Open();
    for(var attempt=0;;attempt++)
    {
        try{input.BackupDatabase(copy);break;}
        catch(SqliteException error)when(error.SqliteErrorCode is 5 or 6&&attempt<29){Thread.Sleep(100);}
    }
    using var check = copy.CreateCommand(); check.CommandText = "PRAGMA quick_check";
    if (check.ExecuteScalar() as string != "ok") throw new InvalidDataException("Snapshot integrity failed.");
    databases.Add(new { Name = name, CapturedUtc = DateTime.UtcNow, Bytes = new FileInfo(output).Length });
}
var files = new List<object>();
var paths = new[] { "workspace.json", "achievement-accounts.json", "achievement-artwork/manifest.json", "library/catalog.json" }
    .Select(n => Path.Combine(source, n)).Where(File.Exists).ToList();
var media = Path.Combine(source, "screenshots");
if (Directory.Exists(media))
    paths.AddRange(Directory.EnumerateFiles(media, "*.json", SearchOption.AllDirectories).Where(p =>
    {
        var rel = Path.GetRelativePath(media, p).Replace('\\', '/');
        var parts = rel.Split('/');
        return parts.Length == 2 && parts[1] == "catalogue.json"
            || parts.Length == 3 && parts[1] is "catalogues" or "removals";
    }));
foreach (var file in paths.Order(StringComparer.Ordinal))
{
    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked metadata rejected.");
    byte[]? bytes = null;
    for (var attempt = 0; attempt < 3; attempt++)
    {
        var first = File.ReadAllBytes(file); var second = File.ReadAllBytes(file);
        if (first.AsSpan().SequenceEqual(second)) { bytes = first; break; }
    }
    if (bytes is null) throw new IOException("Metadata changed repeatedly; retry the inventory.");
    using var parsed = JsonDocument.Parse(bytes);
    var name = Path.GetRelativePath(source, file);
    var output = Path.Combine(target, name);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllBytes(output, bytes);
    files.Add(new { Name = name, Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), CapturedUtc = DateTime.UtcNow });
}
var keys = Path.Combine(source, "keys");
if(args.Length==3&&Directory.Exists(keys))
{
    var keyTarget=Path.Combine(target,"keys");Directory.CreateDirectory(keyTarget);
    foreach(var key in Directory.EnumerateFiles(keys,"*.xml"))
    {
        if((File.GetAttributes(key)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked key files rejected.");
        var value=File.ReadAllBytes(key);if(!value.AsSpan().SequenceEqual(File.ReadAllBytes(key)))throw new IOException("A key changed during capture.");
        var name=Path.Combine("keys",Path.GetFileName(key));File.WriteAllBytes(Path.Combine(target,name),value);
        files.Add(new{Name=name,Bytes=value.Length,Sha256=Convert.ToHexString(SHA256.HashData(value)),CapturedUtc=DateTime.UtcNow});
    }
}
var keyInventory = Directory.Exists(keys) ? Directory.GetFiles(keys).Select(p => new { Name = Path.GetFileName(p), Bytes = new FileInfo(p).Length }).ToArray() : [];
File.WriteAllText(Path.Combine(target, "snapshot-manifest.json"), JsonSerializer.Serialize(new {
    Version = 1, StartedUtc = started, CompletedUtc = DateTime.UtcNow,
    Consistency = "Per-database SQLite backup snapshots; cross-store inventory is not an atomic cutover snapshot.",
    Databases = databases, Metadata = files, Keys = keyInventory, PhotoPayloadFilesCopied = 0
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Read-only app/metadata inventory captured; no photo payloads copied.");
