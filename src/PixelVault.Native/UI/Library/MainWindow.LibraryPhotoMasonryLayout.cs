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
        /// Square-first quilt rows: every row uses a shared cell height so the feed stays solid and the density control
        /// directly changes the visible tile size. Individual tiles can claim extra horizontal cells for variation.
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
            var requestedCell = Math.Max(120, baseWidth <= 0 ? 260 : baseWidth);

            var filtered = new List<string>();
            foreach (var f in files)
            {
                if (!string.IsNullOrWhiteSpace(f)) filtered.Add(f);
            }
            if (filtered.Count == 0) return chunks;

            var cols = Math.Max(1, (int)Math.Round((avail + gap) / (requestedCell + gap), MidpointRounding.AwayFromZero));
            cols = Math.Min(cols, Math.Max(1, filtered.Count));
            var minCell = Math.Max(120d, Math.Min(requestedCell, Math.Max(120, minWidth)));
            while (cols > 1)
            {
                var testCell = (avail - (cols - 1) * gap) / cols;
                if (testCell + 0.5 >= minCell) break;
                cols--;
            }
            var cellSize = Math.Max(1, (int)Math.Floor((avail - ((cols - 1) * gap)) / cols));

            const int maxItemsPerChunk = 48;
            const double maxChunkPaintHeight = 3400d;
            var maxRowsPerChunk = Math.Max(4, (int)Math.Floor((maxChunkPaintHeight + gap) / Math.Max(1d, cellSize + gap)));

            var placements = new List<LibraryDetailMasonryPlacement>();
            var yInChunk = 0d;
            var rowsInChunk = 0;
            var index = 0;
            var rowIndex = 0;

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
                rowsInChunk = 0;
            }

            while (index < filtered.Count)
            {
                if (placements.Count >= maxItemsPerChunk || rowsInChunk >= maxRowsPerChunk)
                    FlushChunk();

                var remaining = filtered.Count - index;
                var rowItemCount = ChooseLibraryQuiltRowItemCount(cols, rowIndex, remaining);
                var rowFiles = filtered.Skip(index).Take(rowItemCount).ToList();
                var spans = BuildLibraryQuiltRowUnitSpans(rowFiles, cols, rowIndex, mediaLayoutByFile);

                double x = 0;
                for (var i = 0; i < rowFiles.Count; i++)
                {
                    var spanUnits = Math.Max(1, spans[i]);
                    var width = (spanUnits * cellSize) + ((spanUnits - 1) * gap);
                    placements.Add(new LibraryDetailMasonryPlacement
                    {
                        File = rowFiles[i],
                        X = x,
                        Y = yInChunk,
                        Width = width,
                        Height = cellSize
                    });
                    x += width + gap;
                }

                yInChunk += cellSize + gap;
                rowsInChunk++;
                index += rowFiles.Count;
                rowIndex++;
            }

            FlushChunk();
            return chunks;
        }

        static int ChooseLibraryQuiltRowItemCount(int columns, int rowIndex, int remainingItems)
        {
            if (remainingItems <= 0) return 0;
            if (columns <= 2) return Math.Min(columns, remainingItems);
            int target;
            switch (rowIndex % 4)
            {
                case 1:
                    target = Math.Max(2, columns - 1);
                    break;
                case 2:
                    target = Math.Max(2, columns - 2);
                    break;
                default:
                    target = columns;
                    break;
            }
            return Math.Max(1, Math.Min(target, remainingItems));
        }

        static List<int> BuildLibraryQuiltRowUnitSpans(
            IReadOnlyList<string> rowFiles,
            int columns,
            int rowIndex,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile)
        {
            var spans = new List<int>();
            if (rowFiles == null || rowFiles.Count == 0) return spans;

            for (var i = 0; i < rowFiles.Count; i++) spans.Add(1);
            var remainingUnits = Math.Max(0, columns - rowFiles.Count);
            if (remainingUnits == 0) return spans;

            var candidateIndexes = Enumerable.Range(0, rowFiles.Count)
                .OrderByDescending(index => ScoreLibraryQuiltSpanCandidate(rowFiles[index], rowIndex, index, rowFiles.Count, mediaLayoutByFile))
                .ThenBy(index => Math.Abs((((rowFiles.Count - 1) / 2d) - index)))
                .ToList();
            var maxSpans = new int[rowFiles.Count];
            foreach (var index in candidateIndexes)
                maxSpans[index] = DetermineLibraryQuiltMaxSpan(rowFiles[index], rowFiles.Count, columns, mediaLayoutByFile);

            var safety = 0;
            while (remainingUnits > 0 && candidateIndexes.Count > 0 && safety < 128)
            {
                var expanded = false;
                foreach (var index in candidateIndexes)
                {
                    if (remainingUnits <= 0) break;
                    if (spans[index] >= maxSpans[index]) continue;
                    spans[index]++;
                    remainingUnits--;
                    expanded = true;
                }
                if (!expanded) break;
                safety++;
            }

            if (remainingUnits > 0)
            {
                while (remainingUnits > 0)
                {
                    var distributed = false;
                    foreach (var index in Enumerable.Range(0, rowFiles.Count).OrderBy(index => Math.Abs((((rowFiles.Count - 1) / 2d) - index))))
                    {
                        spans[index]++;
                        remainingUnits--;
                        distributed = true;
                        if (remainingUnits == 0) break;
                    }
                    if (!distributed) break;
                }
            }

            return spans;
        }

        static int DetermineLibraryQuiltMaxSpan(
            string file,
            int rowItemCount,
            int columns,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile)
        {
            var aspect = ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
            if (rowItemCount <= 1) return columns;
            if (rowItemCount == 2 && columns >= 5)
            {
                if (aspect >= 1.8d) return 4;
                return 3;
            }
            if (aspect < 0.82d && rowItemCount > 2) return 1;
            if (aspect >= 1.8d) return Math.Min(3, columns);
            if (aspect >= 1.18d) return Math.Min(2, columns);
            return Math.Min(2, columns);
        }

        static double ScoreLibraryQuiltSpanCandidate(
            string file,
            int rowIndex,
            int index,
            int rowItemCount,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile)
        {
            var aspect = ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
            var centerBias = 1d - Math.Abs((((rowItemCount - 1) / 2d) - index));
            var rhythmBias = ((rowIndex + index) % 3 == 0) ? 0.15d : 0d;
            return aspect + (centerBias * 0.18d) + rhythmBias;
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
