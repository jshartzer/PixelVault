using System.Security.Principal;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

public sealed record LocalMapAccess(string Identity, int ReadableMapFiles, bool CacheWritable);

/// <summary>Small startup access check, executed by the service itself. No decoding, schema work or progress writes.</summary>
public static class LocalMapReadiness
{
    public static LocalMapAccess Verify(CompanionOptions options)
    {
        var identity = Environment.UserName;
        if (OperatingSystem.IsWindows())
        {
            using var windowsIdentity = WindowsIdentity.GetCurrent();
            identity = windowsIdentity.Name;
        }
        var root = Path.Combine(options.ApplicationDataRoot, "game-maps");
        if (!Directory.Exists(root)) return new(identity, 0, false);
        var files = 0;
        void Read(string directory, string name, bool immutable = true)
        {
            var opened = LocalPhotoFile.Open(directory, name, immutable);
            using var stream = opened.Stream;
            if (stream.ReadByte() < 0) throw new IOException("A saved map file is empty.");
            files++;
        }
        Read(root, "maps.sqlite", false);
        var images = Path.Combine(root, "images");
        if (Directory.Exists(images))
            foreach (var file in Directory.EnumerateFiles(images))
                if (Regex.IsMatch(Path.GetFileName(file), "^[A-Fa-f0-9]{64}\\.(png|jpg)$")) Read(images, Path.GetFileName(file));
        var cache = Path.Combine(options.CacheRoot, "maps");
        Directory.CreateDirectory(cache); LocalPhotoFile.CheckRoot(cache);
        var probe = Path.Combine(cache, "access-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var output = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            output.WriteByte(1); output.Position = 0;
            if (output.ReadByte() != 1) throw new IOException("The map cache is unavailable.");
        }
        finally { if (File.Exists(probe)) File.Delete(probe); }
        return new(identity, files, true);
    }
}
