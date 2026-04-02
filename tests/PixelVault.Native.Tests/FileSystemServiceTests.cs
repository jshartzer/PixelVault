using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class FileSystemServiceTests
{
    [Fact]
    public void FileExists_Delegates_To_File_System()
    {
        var path = Path.Combine(Path.GetTempPath(), "pv-fs-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(path, "x");
            var fs = new FileSystemService();
            Assert.True(fs.FileExists(path));
            Assert.False(fs.FileExists(path + ".missing"));
            Assert.False(fs.FileExists(string.Empty));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DirectoryExists_And_EnumerateFiles_Work_On_Temp_Directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pv-fs-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "a");
        try
        {
            var fs = new FileSystemService();
            Assert.True(fs.DirectoryExists(dir));
            Assert.False(fs.DirectoryExists(Path.Combine(dir, "nope")));
            Assert.Contains(file, fs.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            try { File.Delete(file); }
            catch { /* ignore */ }

            try { Directory.Delete(dir); }
            catch { /* ignore */ }
        }
    }
}
