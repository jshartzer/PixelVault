using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        DateTime GetLibraryFolderNewestDate(LibraryFolderInfo folder)
        {
            if (folder == null) return DateTime.MinValue;
            if (folder.NewestCaptureUtcTicks > 0)
            {
                try
                {
                    return new DateTime(folder.NewestCaptureUtcTicks, DateTimeKind.Utc).ToLocalTime();
                }
                catch
                {
                    folder.NewestCaptureUtcTicks = 0;
                }
            }
            var newestSource = (folder.FilePaths ?? new string[0])
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            if (string.IsNullOrWhiteSpace(newestSource) && !string.IsNullOrWhiteSpace(folder.PreviewImagePath) && File.Exists(folder.PreviewImagePath))
            {
                newestSource = folder.PreviewImagePath;
            }
            var newest = string.IsNullOrWhiteSpace(newestSource) ? DateTime.MinValue : ResolveIndexedLibraryDate(libraryRoot, newestSource);
            if (newest > DateTime.MinValue) folder.NewestCaptureUtcTicks = newest.ToUniversalTime().Ticks;
            return newest;
        }
        bool PopulateMissingLibraryFolderSortKeys(IEnumerable<LibraryFolderInfo> folders)
        {
            bool changed = false;
            foreach (var folder in (folders ?? Enumerable.Empty<LibraryFolderInfo>()).Where(entry => entry != null))
            {
                if (folder.NewestCaptureUtcTicks <= 0)
                {
                    var newest = GetLibraryFolderNewestDate(folder);
                    if (newest > DateTime.MinValue)
                    {
                        folder.NewestCaptureUtcTicks = newest.ToUniversalTime().Ticks;
                        changed = true;
                    }
                }

                if (folder.NewestRecentSortUtcTicks <= 0)
                {
                    var paths = folder.FilePaths;
                    if (paths != null && paths.Length > 0)
                    {
                        long maxR = 0;
                        foreach (var path in paths)
                        {
                            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                            var t = ResolveLibraryFileRecentSortUtcTicks(libraryRoot, path, null);
                            if (t > maxR) maxR = t;
                        }
                        if (maxR > 0)
                        {
                            folder.NewestRecentSortUtcTicks = maxR;
                            changed = true;
                        }
                    }
                }
            }
            return changed;
        }
    }
}
