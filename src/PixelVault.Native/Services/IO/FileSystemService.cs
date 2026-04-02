using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    internal sealed class FileSystemService : IFileSystemService
    {
        public bool FileExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        public bool DirectoryExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? Array.Empty<string>() : Directory.EnumerateDirectories(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return string.IsNullOrWhiteSpace(path) ? Array.Empty<string>() : Directory.EnumerateFiles(path, searchPattern ?? "*.*", searchOption);
        }

        public IReadOnlyList<string> ReadAllLines(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Array.Empty<string>();
            return File.ReadAllLines(path);
        }

        public void WriteAllLines(string path, IReadOnlyList<string> lines)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var arr = lines == null ? Array.Empty<string>() : lines as string[] ?? lines.ToArray();
            File.WriteAllLines(path, arr);
        }

        public void DeleteFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) File.Delete(path);
        }

        public void MoveFile(string sourceFileName, string destFileName)
        {
            File.Move(sourceFileName, destFileName);
        }

        public void CreateDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
        }

        public DateTime GetLastWriteTime(string path)
        {
            return File.GetLastWriteTime(path);
        }
    }
}
