using System;
using System.Diagnostics;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Emits one PERF line when <paramref name="stopwatch"/> elapsed ms is ≥ <paramref name="thresholdMilliseconds"/> (0 = always log).</summary>
        void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds = 80)
        {
            if (stopwatch == null) return;
            if (stopwatch.ElapsedMilliseconds < thresholdMilliseconds) return;
            var sessionSegment = troubleshootingLoggingEnabled ? " | S=" + _diagnosticsSessionId : string.Empty;
            var detailSegment = string.IsNullOrWhiteSpace(detail) ? string.Empty : " | " + detail;
            Log("PERF | " + area + " | " + stopwatch.ElapsedMilliseconds + " ms | T=" + Environment.CurrentManagedThreadId + sessionSegment + detailSegment);
        }

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

