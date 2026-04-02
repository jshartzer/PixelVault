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

    [Fact]
    public void ReadAllLines_Returns_Empty_For_Missing_Path()
    {
        var fs = new FileSystemService();
        Assert.Empty(fs.ReadAllLines(Path.Combine(Path.GetTempPath(), "pv-missing-" + Guid.NewGuid().ToString("N") + ".txt")));
        Assert.Empty(fs.ReadAllLines(string.Empty));
    }

    [Fact]
    public void WriteAllLines_And_ReadAllLines_Roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "pv-lines-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var fs = new FileSystemService();
            fs.WriteAllLines(path, new[] { "a", "b", "c" });
            Assert.Equal(new[] { "a", "b", "c" }, fs.ReadAllLines(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MoveFile_And_DeleteFile_Work()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pv-move-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(dir, "src.txt");
        var dst = Path.Combine(dir, "dst.txt");
        Directory.CreateDirectory(dir);
        File.WriteAllText(src, "x");
        try
        {
            var fs = new FileSystemService();
            fs.MoveFile(src, dst);
            Assert.False(File.Exists(src));
            Assert.True(File.Exists(dst));
            fs.DeleteFile(dst);
            Assert.False(File.Exists(dst));
        }
        finally
        {
            try { if (File.Exists(src)) File.Delete(src); } catch { /* ignore */ }
            try { if (File.Exists(dst)) File.Delete(dst); } catch { /* ignore */ }
            try { Directory.Delete(dir); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GetLastWriteTime_And_CreateDirectory_Work()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pv-mkdir-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(dir, "nested");
        try
        {
            var fs = new FileSystemService();
            fs.CreateDirectory(nested);
            Assert.True(Directory.Exists(nested));
            var file = Path.Combine(nested, "f.txt");
            File.WriteAllText(file, "z");
            var t = fs.GetLastWriteTime(file);
            Assert.True(t <= DateTime.Now.AddMinutes(5) && t >= DateTime.Now.AddHours(-1));
        }
        finally
        {
            try
            {
                var file = Path.Combine(nested, "f.txt");
                if (File.Exists(file)) File.Delete(file);
            }
            catch { /* ignore */ }
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
