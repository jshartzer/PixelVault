using System;
using System.Collections.Generic;
using System.IO;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Stable signature of direct child folder <em>names</em> under <see cref="root"/> (count + hash); omits directory mtimes used in the full inventory stamp.</summary>
        internal string BuildLibraryFolderStructuralStamp(string root)
        {
            unchecked
            {
                var folderCount = 0;
                var names = new List<string>();
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    folderCount++;
                    names.Add(Path.GetFileName(dir) ?? string.Empty);
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);
                var nameHash = 17;
                for (var i = 0; i < names.Count; i++)
                    nameHash = (nameHash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(names[i]);
                return folderCount + "|" + nameHash;
            }
        }

        string BuildLibraryFolderInventoryStamp(string root)
        {
            unchecked
            {
                long latestDirTicks = 0;
                var folderCount = 0;
                var names = new List<string>();
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    folderCount++;
                    var dirTicks = Directory.GetLastWriteTimeUtc(dir).Ticks;
                    if (dirTicks > latestDirTicks) latestDirTicks = dirTicks;
                    names.Add(Path.GetFileName(dir) ?? string.Empty);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                var nameHash = 17;
                for (var i = 0; i < names.Count; i++)
                    nameHash = (nameHash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(names[i]);

                // Keep the startup cache stamp tied to the visible library folder inventory, not index-db writes.
                // Import/scan flows explicitly rewrite the folder cache when they change the library, so folding the
                // SQLite file timestamp into the stamp only causes unnecessary NAS-wide rebuilds on startup.
                // Enumeration is single-pass; we sort direct child *names* only (same order as sorting full paths
                // under one parent) so compares stay cheaper than OrderBy on long NAS paths.
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
