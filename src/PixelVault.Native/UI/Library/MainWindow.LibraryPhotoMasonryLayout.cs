using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal sealed class LibraryDetailMediaLayoutInfo
        {
            internal long LastWriteUtcTicks;
            internal long FileLength;
            internal int PixelWidth;
            internal int PixelHeight;
            internal bool IsVideo;
        }

        readonly object _libraryDetailMediaLayoutInfoSync = new object();
        readonly Dictionary<string, LibraryDetailMediaLayoutInfo> _libraryDetailMediaLayoutInfoCache = new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);

        internal sealed class LibraryDetailMasonryPlacement
        {
            public string File;
            public double X;
            public double Y;
            public int Width;
            public int Height;
        }

        internal sealed class LibraryDetailMasonryChunk
        {
            public double CanvasWidth;
            public double CanvasHeight;
            public List<LibraryDetailMasonryPlacement> Placements;
        }

        LibraryDetailMediaLayoutInfo ResolveLibraryDetailMediaLayoutInfo(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(file.Trim());
            }
            catch
            {
                return null;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists) return null;
            }
            catch
            {
                return null;
            }

            lock (_libraryDetailMediaLayoutInfoSync)
            {
                LibraryDetailMediaLayoutInfo cached;
                if (_libraryDetailMediaLayoutInfoCache.TryGetValue(fullPath, out cached)
                    && cached != null
                    && cached.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks
                    && cached.FileLength == fileInfo.Length)
                {
                    return cached;
                }
            }

            var resolved = new LibraryDetailMediaLayoutInfo
            {
                LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                FileLength = fileInfo.Length,
                IsVideo = IsVideo(fullPath)
            };

            try
            {
                if (resolved.IsVideo)
                {
                    var videoInfo = TryLoadCachedVideoClipInfo(fullPath) ?? EnsureVideoClipInfo(fullPath);
                    if (videoInfo != null)
                    {
                        resolved.PixelWidth = Math.Max(0, videoInfo.Width);
                        resolved.PixelHeight = Math.Max(0, videoInfo.Height);
                    }
                }
                else
                {
                    using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        var decoder = BitmapDecoder.Create(
                            stream,
                            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.None);
                        var frame = decoder.Frames.FirstOrDefault();
                        if (frame != null)
                        {
                            resolved.PixelWidth = Math.Max(0, frame.PixelWidth);
                            resolved.PixelHeight = Math.Max(0, frame.PixelHeight);
                        }
                    }
                }
            }
            catch
            {
            }

            lock (_libraryDetailMediaLayoutInfoSync)
            {
                _libraryDetailMediaLayoutInfoCache[fullPath] = resolved;
            }
            return resolved;
        }

        Dictionary<string, LibraryDetailMediaLayoutInfo> BuildLibraryDetailMediaLayoutInfoMap(IEnumerable<string> files)
        {
            var map = new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (files ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var info = ResolveLibraryDetailMediaLayoutInfo(file);
                if (info != null) map[file] = info;
            }
            return map;
        }

        internal static double ResolveLibraryDetailAspectRatio(string file, IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile = null)
        {
            LibraryDetailMediaLayoutInfo info;
            if (mediaLayoutByFile != null
                && !string.IsNullOrWhiteSpace(file)
                && mediaLayoutByFile.TryGetValue(file, out info)
                && info != null
                && info.PixelWidth > 0
                && info.PixelHeight > 0)
            {
                return Math.Max(0.72d, Math.Min(2.35d, (double)info.PixelWidth / Math.Max(1d, info.PixelHeight)));
            }

            var ratios = new[] { 1.24d, 1.5d, 1.68d, 1.78d, 1.92d };
            var r = ratios[LibraryDetailFileLayoutHash(file) % ratios.Length];
            return Math.Max(0.72d, Math.Min(2.35d, r));
        }

        internal static int EstimateLibraryDetailSingleTileHeight(
            string file,
            int tileWidth,
            bool includeTimelineFooter,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile = null)
        {
            if (string.IsNullOrWhiteSpace(file) || tileWidth <= 0) return 260;
            var footer = includeTimelineFooter ? 112 : 14;
            var aspectRatio = ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
            var inner = (int)Math.Ceiling(tileWidth / Math.Max(0.72d, aspectRatio));
            var minInner = includeTimelineFooter ? 132 : 118;
            var maxInner = includeTimelineFooter ? 440 : 380;
            inner = Math.Max(minInner, Math.Min(maxInner, inner));
            return Math.Max(includeTimelineFooter ? 248 : 180, inner + footer);
        }

        /// <summary>Column-based masonry with occasional 2-column &quot;hero&quot; spans when there are at least two columns.</summary>
        internal static List<LibraryDetailMasonryChunk> BuildLibraryDetailMasonryChunks(
            IReadOnlyList<string> files,
            double availableWidth,
            int gapPx,
            int baseWidth,
            int minWidth,
            int maxWidth,
            bool includeTimelineFooter,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile = null)
        {
            var chunks = new List<LibraryDetailMasonryChunk>();
            if (files == null || files.Count == 0) return chunks;

            var gap = Math.Max(0, gapPx);
            var avail = Math.Max(120d, availableWidth);
            var minColW = Math.Max(minWidth, 240);
            var maxColW = Math.Max(minColW, Math.Min(maxWidth, Math.Max(minWidth, baseWidth)));

            var cols = Math.Max(1, (int)Math.Floor((avail + gap) / (minColW + gap)));
            cols = Math.Min(cols, Math.Max(1, files.Count));
            while (cols > 1)
            {
                var testColW = (avail - (cols - 1) * gap) / cols;
                if (testColW + 0.5 >= minColW) break;
                cols--;
            }

            var columnWidthF = (avail - (cols - 1) * gap) / Math.Max(1, cols);
            var columnWidth = (int)Math.Floor(columnWidthF);
            columnWidth = Math.Max(minWidth, Math.Min(maxColW, columnWidth));
            if (columnWidth > (int)Math.Floor(avail))
                columnWidth = (int)Math.Floor(avail);

            const int maxItemsPerChunk = 42;
            const double maxChunkPaintHeight = 3400d;

            var filtered = new List<string>();
            foreach (var f in files)
            {
                if (!string.IsNullOrWhiteSpace(f)) filtered.Add(f);
            }
            if (filtered.Count == 0) return chunks;

            var colHeights = new double[cols];
            var placements = new List<LibraryDetailMasonryPlacement>();
            var lastHeroIndex = -100;
            var ordinal = 0;

            void FlushChunk()
            {
                if (placements.Count == 0) return;
                var h = 0d;
                for (var c = 0; c < cols; c++)
                    if (colHeights[c] > h) h = colHeights[c];
                if (h > gap && placements.Count > 0) h = Math.Max(0, h - gap);
                chunks.Add(new LibraryDetailMasonryChunk
                {
                    CanvasWidth = avail,
                    CanvasHeight = Math.Max(1, h),
                    Placements = placements
                });
                placements = new List<LibraryDetailMasonryPlacement>();
                for (var c = 0; c < cols; c++) colHeights[c] = 0;
            }

            foreach (var file in filtered)
            {
                var chunkTop = 0d;
                for (var c = 0; c < cols; c++)
                    if (colHeights[c] > chunkTop) chunkTop = colHeights[c];
                if (placements.Count > 0 && (placements.Count >= maxItemsPerChunk || chunkTop >= maxChunkPaintHeight))
                    FlushChunk();

                var aspectRatio = ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
                var allowHero = cols >= 2 && filtered.Count >= Math.Max(4, cols + 1);
                var isWideEnoughForHero = aspectRatio >= 1.55d;
                var useHero = cols >= 2
                    && allowHero
                    && isWideEnoughForHero
                    && (
                        ordinal == 0
                        || (ordinal - lastHeroIndex >= 6 && ordinal < filtered.Count - 2));

                if (useHero)
                {
                    var heroW = (int)Math.Min(Math.Floor(avail), 2 * columnWidth + gap);
                    var heroH = EstimateLibraryDetailSingleTileHeight(file, heroW, includeTimelineFooter, mediaLayoutByFile);
                    var bestJ = 0;
                    var bestTop = double.MaxValue;
                    for (var j = 0; j <= cols - 2; j++)
                    {
                        var top = Math.Max(colHeights[j], colHeights[j + 1]);
                        if (top < bestTop)
                        {
                            bestTop = top;
                            bestJ = j;
                        }
                    }
                    var x = bestJ * (columnWidth + gap);
                    placements.Add(new LibraryDetailMasonryPlacement
                    {
                        File = file,
                        X = x,
                        Y = bestTop,
                        Width = heroW,
                        Height = heroH
                    });
                    var newTop = bestTop + heroH + gap;
                    colHeights[bestJ] = newTop;
                    colHeights[bestJ + 1] = newTop;
                    lastHeroIndex = ordinal;
                }
                else
                {
                    var j = 0;
                    for (var c = 1; c < cols; c++)
                        if (colHeights[c] < colHeights[j]) j = c;
                    var y = colHeights[j];
                    var w = columnWidth;
                    if (aspectRatio < 0.9d && cols >= 3)
                    {
                        w = Math.Max(minWidth, (int)Math.Round(columnWidth * 0.9d / 12d) * 12);
                    }
                    w = Math.Min(w, (int)Math.Floor(avail));
                    var h = EstimateLibraryDetailSingleTileHeight(file, w, includeTimelineFooter, mediaLayoutByFile);
                    var x = j * (columnWidth + gap);
                    if (w < columnWidth && cols > 1)
                        x += Math.Max(0, (columnWidth - w) / 2);
                    placements.Add(new LibraryDetailMasonryPlacement
                    {
                        File = file,
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h
                    });
                    colHeights[j] = y + h + gap;
                }

                ordinal++;
            }

            FlushChunk();
            return chunks;
        }
    }
}
