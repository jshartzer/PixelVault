using System;
using System.Collections.Generic;
using System.Threading;

namespace PixelVaultNative
{
    internal interface ILibraryScanner
    {
        /// <summary>Scan (or rescan) library metadata index for the whole library or one game folder.</summary>
        /// <returns>Count of entries updated from embedded reads.</returns>
        int ScanLibraryMetadataIndex(
            string root,
            string folderPath,
            bool forceRescan,
            Action<int, int, string> progress,
            CancellationToken cancellationToken = default);
    }
}
