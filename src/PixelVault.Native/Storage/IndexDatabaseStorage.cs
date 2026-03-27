using System.IO;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string BuildLibraryFolderInventoryStamp(string root)
        {
            long latestDirTicks = 0;
            int folderCount = 0;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                folderCount++;
                var dirTicks = Directory.GetLastWriteTimeUtc(dir).Ticks;
                if (dirTicks > latestDirTicks) latestDirTicks = dirTicks;
            }

            var metadataPath = IndexDatabasePath(root);
            if (!File.Exists(metadataPath)) metadataPath = LibraryMetadataIndexPath(root);
            long metadataTicks = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
            long metadataLength = File.Exists(metadataPath) ? new FileInfo(metadataPath).Length : 0;
            return folderCount + "|" + latestDirTicks + "|" + metadataTicks + "|" + metadataLength;
        }

        string LibraryFolderCachePath(string root)
        {
            return Path.Combine(cacheRoot, "library-folders-" + SafeCacheName(root) + ".cache");
        }

        string IndexDatabasePath(string root)
        {
            return Path.Combine(cacheRoot, "pixelvault-index-" + SafeCacheName(root) + ".sqlite");
        }

        string LibraryMetadataIndexPath(string root)
        {
            return Path.Combine(cacheRoot, "library-metadata-index-" + SafeCacheName(root) + ".cache");
        }
    }
}
