using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    internal sealed class FileSystemService : IFileSystemService
    {
        /// <summary>
        /// SD/USB imports (notably Nintendo Switch album dumps) often carry <see cref="FileAttributes.ReadOnly"/>,
        /// which blocks <see cref="File.Delete"/> and in-place metadata tools on Windows.
        /// </summary>
        internal static void TryClearReadOnlyForFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) == 0) return;
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
            catch
            {
                // Best-effort; callers still attempt delete/write and surface errors if needed.
            }
        }

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
            if (string.IsNullOrWhiteSpace(path)) return;
            if (File.Exists(path)) TryClearReadOnlyForFile(path);
            File.Delete(path);
        }

        public void MoveFile(string sourceFileName, string destFileName)
        {
            File.Move(sourceFileName, destFileName);
            TryClearReadOnlyForFile(destFileName);
        }

        public void CopyFile(string sourceFileName, string destFileName, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(destFileName)) return;
            File.Copy(sourceFileName, destFileName, overwrite);
            TryClearReadOnlyForFile(destFileName);
        }

        public void CreateDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
        }

        public DateTime GetCreationTime(string path)
        {
            return File.GetCreationTime(path);
        }

        public DateTime GetLastWriteTime(string path)
        {
            return File.GetLastWriteTime(path);
        }
    }
}
