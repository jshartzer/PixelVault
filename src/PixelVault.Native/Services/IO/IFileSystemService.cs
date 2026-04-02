using System;
using System.Collections.Generic;
using System.IO;

namespace PixelVaultNative
{
    /// <summary>Seam for library scan, import, and cache paths. Default implementation delegates to <see cref="System.IO"/>.</summary>
    internal interface IFileSystemService
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        IEnumerable<string> EnumerateDirectories(string path);
        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

        IReadOnlyList<string> ReadAllLines(string path);
        void WriteAllLines(string path, IReadOnlyList<string> lines);
        void DeleteFile(string path);
        void MoveFile(string sourceFileName, string destFileName);
        void CreateDirectory(string path);
        DateTime GetLastWriteTime(string path);
    }
}
