using System;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string BuildLibraryFolderInventoryStamp(string root)
        {
            unchecked
            {
                long latestDirTicks = 0;
                int folderCount = 0;
                int nameHash = 17;

                foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    folderCount++;
                    var dirTicks = Directory.GetLastWriteTimeUtc(dir).Ticks;
                    if (dirTicks > latestDirTicks) latestDirTicks = dirTicks;
                    nameHash = (nameHash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(Path.GetFileName(dir) ?? string.Empty);
                }

                // Keep the startup cache stamp tied to the visible library folder inventory, not index-db writes.
                // Import/scan flows explicitly rewrite the folder cache when they change the library, so folding the
                // SQLite file timestamp into the stamp only causes unnecessary NAS-wide rebuilds on startup.
                return folderCount + "|" + latestDirTicks + "|" + nameHash;
            }
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
