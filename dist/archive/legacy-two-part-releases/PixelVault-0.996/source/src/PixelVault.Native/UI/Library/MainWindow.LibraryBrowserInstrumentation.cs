using System.Diagnostics;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        Stopwatch libraryBrowserSessionFirstPaintStopwatch;
        bool libraryBrowserFirstFolderListPaintLogged;
        bool libraryBrowserFirstDetailPaintLogged;

        void MarkLibraryBrowserSessionFirstPaintTracking()
        {
            libraryBrowserSessionFirstPaintStopwatch = Stopwatch.StartNew();
            libraryBrowserFirstFolderListPaintLogged = false;
            libraryBrowserFirstDetailPaintLogged = false;
        }

        void LogLibraryBrowserFirstFolderListPaintOnce(string detail)
        {
            if (libraryBrowserFirstFolderListPaintLogged || libraryBrowserSessionFirstPaintStopwatch == null) return;
            libraryBrowserFirstFolderListPaintLogged = true;
            LogPerformanceSample("LibraryBrowserFirstFolderListPaint", libraryBrowserSessionFirstPaintStopwatch, detail ?? string.Empty, 0);
        }

        void LogLibraryBrowserFirstDetailPaintOnce(string detail)
        {
            if (libraryBrowserFirstDetailPaintLogged || libraryBrowserSessionFirstPaintStopwatch == null) return;
            libraryBrowserFirstDetailPaintLogged = true;
            LogPerformanceSample("LibraryBrowserFirstDetailPaint", libraryBrowserSessionFirstPaintStopwatch, detail ?? string.Empty, 0);
        }
    }
}
