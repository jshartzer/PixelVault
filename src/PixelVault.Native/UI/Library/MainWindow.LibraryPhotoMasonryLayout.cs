using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
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

        const int LibraryDetailMediaLayoutParallelThreshold = 16;

        Dictionary<string, LibraryDetailMediaLayoutInfo> BuildLibraryDetailMediaLayoutInfoMap(IEnumerable<string> files)
        {
            var list = (files ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0)
                return new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);

            if (list.Count < LibraryDetailMediaLayoutParallelThreshold)
            {
                var map = new Dictionary<string, LibraryDetailMediaLayoutInfo>(list.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var file in list)
                {
                    var info = ResolveLibraryDetailMediaLayoutInfo(file);
                    if (info != null) map[file] = info;
                }
                return map;
            }

            var concurrent = new ConcurrentDictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);
            var dop = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
            Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = dop }, file =>
            {
                var info = ResolveLibraryDetailMediaLayoutInfo(file);
                if (info != null) concurrent[file] = info;
            });
            return new Dictionary<string, LibraryDetailMediaLayoutInfo>(concurrent, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Source width ÷ height (pixel dimensions when known, else stable hash mix across portrait and landscape). Used for tile height = width ÷ aspect.</summary>
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
                var r = (double)info.PixelWidth / Math.Max(1d, info.PixelHeight);
                return Math.Max(0.25d, Math.Min(4.0d, r));
            }

            var ratios = new[] { 0.56d, 0.65d, 0.78d, 1.0d, 1.25d, 1.55d, 1.78d, 2.0d };
            var rHash = ratios[LibraryDetailFileLayoutHash(file) % ratios.Length];
            return Math.Max(0.25d, Math.Min(4.0d, rHash));
        }

        /// <inheritdoc cref="ResolveLibraryDetailAspectRatio"/>
        internal static double ResolveLibraryDetailNaturalAspectRatio(string file, IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile = null)
        {
            return ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
        }

        internal static int EstimateLibraryDetailSingleTileHeight(
            string file,
            int tileWidth,
            bool includeTimelineFooter,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile = null)
        {
            if (string.IsNullOrWhiteSpace(file) || tileWidth <= 0) return 260;
            var aspectRatio = ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
            var h = (int)Math.Ceiling(tileWidth / aspectRatio);
            return Math.Max(1, h);
        }

        /// <summary>
        /// Justified rows: every row uses one shared height so tile bottoms align — no dead space beside a shorter neighbor
        /// (unlike column-based masonry + hero spans). Widths split proportionally to aspect ratio within each row.
        /// </summary>
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

            var filtered = new List<string>();
            foreach (var f in files)
            {
                if (!string.IsNullOrWhiteSpace(f)) filtered.Add(f);
            }
            if (filtered.Count == 0) return chunks;

            var cols = Math.Max(1, (int)Math.Floor((avail + gap) / (minColW + gap)));
            cols = Math.Min(cols, Math.Max(1, filtered.Count));
            while (cols > 1)
            {
                var testColW = (avail - (cols - 1) * gap) / cols;
                if (testColW + 0.5 >= minColW) break;
                cols--;
            }

            const int maxItemsPerChunk = 42;
            const double maxChunkPaintHeight = 3400d;

            var placements = new List<LibraryDetailMasonryPlacement>();
            var yInChunk = 0d;
            var index = 0;

            void FlushChunk()
            {
                if (placements.Count == 0) return;
                var h = yInChunk > gap ? yInChunk - gap : yInChunk;
                chunks.Add(new LibraryDetailMasonryChunk
                {
                    CanvasWidth = avail,
                    CanvasHeight = Math.Max(1, h),
                    Placements = placements
                });
                placements = new List<LibraryDetailMasonryPlacement>();
                yInChunk = 0d;
            }

            while (index < filtered.Count)
            {
                if (placements.Count >= maxItemsPerChunk || (placements.Count > 0 && yInChunk >= maxChunkPaintHeight))
                    FlushChunk();

                var rowCount = Math.Min(cols, filtered.Count - index);
                var innerW = avail - (rowCount - 1) * gap;
                if (innerW < 1) innerW = 1;

                var aspects = new double[rowCount];
                double sumA = 0;
                for (var i = 0; i < rowCount; i++)
                {
                    var a = ResolveLibraryDetailAspectRatio(filtered[index + i], mediaLayoutByFile);
                    aspects[i] = Math.Max(0.25, a);
                    sumA += aspects[i];
                }
                if (sumA <= 1e-9) sumA = rowCount;

                var rowH = innerW / sumA;
                var hInt = Math.Max(1, (int)Math.Ceiling(rowH));
                var widths = JustifiedRowWidthsInt(innerW, aspects);

                double x = 0;
                for (var i = 0; i < rowCount; i++)
                {
                    placements.Add(new LibraryDetailMasonryPlacement
                    {
                        File = filtered[index + i],
                        X = x,
                        Y = yInChunk,
                        Width = widths[i],
                        Height = hInt
                    });
                    x += widths[i] + gap;
                }

                yInChunk += hInt + gap;
                index += rowCount;
            }

            FlushChunk();
            return chunks;
        }

        /// <summary>Integer tile widths for one row that sum to <paramref name="innerWidth"/> (tile area only, not gaps).</summary>
        static int[] JustifiedRowWidthsInt(double innerWidth, double[] aspects)
        {
            var n = aspects.Length;
            if (n == 0) return Array.Empty<int>();
            var target = Math.Max(n, (int)Math.Floor(innerWidth + 1e-9));
            if (n == 1) return new[] { target };

            var sum = 0d;
            for (var i = 0; i < n; i++) sum += Math.Max(0.25, aspects[i]);
            if (sum <= 1e-9)
            {
                var eq = Math.Max(1, target / n);
                var w = new int[n];
                var u = 0;
                for (var i = 0; i < n - 1; i++)
                {
                    w[i] = eq;
                    u += eq;
                }
                w[n - 1] = Math.Max(1, target - u);
                return w;
            }

            var weights = new double[n];
            for (var i = 0; i < n; i++) weights[i] = Math.Max(0.25, aspects[i]) / sum;

            var result = new int[n];
            var remaining = target;
            var weightLeft = 1d;
            for (var i = 0; i < n - 1; i++)
            {
                var wi = (int)Math.Floor(remaining * (weights[i] / weightLeft) + 1e-9);
                wi = Math.Max(1, wi);
                if (wi > remaining - (n - 1 - 1)) wi = Math.Max(1, remaining - (n - 1 - i));
                result[i] = wi;
                remaining -= wi;
                weightLeft -= weights[i];
            }
            result[n - 1] = Math.Max(1, remaining);
            return result;
        }
    }
}
