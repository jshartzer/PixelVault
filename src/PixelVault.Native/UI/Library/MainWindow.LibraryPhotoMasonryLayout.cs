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

        readonly struct LibraryDetailQuiltShape
        {
            public readonly int ColSpan;
            public readonly int RowSpan;

            public LibraryDetailQuiltShape(int colSpan, int rowSpan)
            {
                ColSpan = Math.Max(1, colSpan);
                RowSpan = Math.Max(1, rowSpan);
            }
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
        /// True cell-based quilt: tiles occupy a grid of square cells and can span multiple cells horizontally and vertically.
        /// This keeps the feed continuous while allowing rectangles and occasional hero tiles to dominate the rhythm.
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
            var canvasWidth = Math.Max(1d, (cols * cellSize) + ((cols - 1) * gap));

            const double maxChunkPaintHeight = 3400d;
            var maxRowsPerChunk = Math.Max(4, (int)Math.Floor((maxChunkPaintHeight + gap) / Math.Max(1d, cellSize + gap)));
            var index = 0;

            while (index < filtered.Count)
            {
                var occupied = new List<bool[]>();
                var placements = new List<LibraryDetailMasonryPlacement>();
                var placedThisChunk = 0;
                while (index + placedThisChunk < filtered.Count)
                {
                    int anchorX;
                    int anchorY;
                    if (!TryFindLibraryQuiltAnchorCell(occupied, cols, maxRowsPerChunk, out anchorX, out anchorY))
                        break;

                    var file = filtered[index + placedThisChunk];
                    var ordinal = index + placedThisChunk;
                    var shapes = BuildLibraryQuiltShapePreferenceOrder(file, ordinal, cols, mediaLayoutByFile);
                    var placed = false;
                    foreach (var shape in shapes)
                    {
                        if (anchorX + shape.ColSpan > cols) continue;
                        if (anchorY + shape.RowSpan > maxRowsPerChunk) continue;
                        if (!LibraryQuiltAreaIsFree(occupied, anchorX, anchorY, shape)) continue;
                        EnsureLibraryQuiltRows(occupied, anchorY + shape.RowSpan, cols);
                        MarkLibraryQuiltOccupied(occupied, anchorX, anchorY, shape);
                        placements.Add(new LibraryDetailMasonryPlacement
                        {
                            File = file,
                            X = anchorX * (cellSize + gap),
                            Y = anchorY * (cellSize + gap),
                            Width = (shape.ColSpan * cellSize) + ((shape.ColSpan - 1) * gap),
                            Height = (shape.RowSpan * cellSize) + ((shape.RowSpan - 1) * gap)
                        });
                        placed = true;
                        placedThisChunk++;
                        break;
                    }

                    if (!placed)
                    {
                        EnsureLibraryQuiltRows(occupied, anchorY + 1, cols);
                        occupied[anchorY][anchorX] = true;
                        placements.Add(new LibraryDetailMasonryPlacement
                        {
                            File = file,
                            X = anchorX * (cellSize + gap),
                            Y = anchorY * (cellSize + gap),
                            Width = cellSize,
                            Height = cellSize
                        });
                        placedThisChunk++;
                    }
                }

                if (placements.Count == 0) break;
                var usedRows = placements
                    .Select(placement => LibraryQuiltPlacementBottomRow(placement, cellSize, gap))
                    .DefaultIfEmpty(1)
                    .Max();
                var canvasHeight = Math.Max(1d, (usedRows * cellSize) + ((usedRows - 1) * gap));
                chunks.Add(new LibraryDetailMasonryChunk
                {
                    CanvasWidth = canvasWidth,
                    CanvasHeight = canvasHeight,
                    Placements = placements
                });
                index += placedThisChunk;
            }

            return chunks;
        }

        static int LibraryQuiltPlacementBottomRow(LibraryDetailMasonryPlacement placement, int cellSize, int gap)
        {
            var pitch = Math.Max(1, cellSize + gap);
            return Math.Max(1, (int)Math.Round((placement.Y + placement.Height + gap) / pitch, MidpointRounding.AwayFromZero));
        }

        static IReadOnlyList<LibraryDetailQuiltShape> BuildLibraryQuiltShapePreferenceOrder(
            string file,
            int ordinal,
            int columns,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile)
        {
            var aspect = ResolveLibraryDetailAspectRatio(file, mediaLayoutByFile);
            var layoutSeed = LibraryDetailFileLayoutHash(file);
            var compactVariation = columns >= 3;
            var pattern = Math.Abs(layoutSeed + (ordinal * 31)) % (compactVariation ? 6 : 4);
            var preferHero = columns >= 3 && ((layoutSeed + ordinal) % (compactVariation ? 6 : 9) == 2);
            var preferSquareAccent = compactVariation
                ? pattern == 0 || pattern == 3 || ((layoutSeed + ordinal) % 5 == 1)
                : ((layoutSeed + ordinal) % 5 == 1);
            var preferSquareFirst = compactVariation && (pattern == 1 || pattern == 4);
            var preferRectangleFirst = pattern == 2 || pattern == 5;
            var shapes = new List<LibraryDetailQuiltShape>();

            void AddShape(int colSpan, int rowSpan)
            {
                if (colSpan > columns) return;
                if (shapes.Any(existing => existing.ColSpan == colSpan && existing.RowSpan == rowSpan)) return;
                shapes.Add(new LibraryDetailQuiltShape(colSpan, rowSpan));
            }

            if (aspect >= 1.55d)
            {
                if (preferSquareFirst) AddShape(1, 1);
                if (preferHero) AddShape(2, 2);
                if (preferRectangleFirst || !preferSquareFirst) AddShape(2, 1);
                AddShape(1, 1);
                AddShape(2, 1);
                if (!preferHero) AddShape(2, 2);
            }
            else if (aspect >= 1.05d)
            {
                if (preferSquareFirst || preferSquareAccent) AddShape(1, 1);
                if (preferHero) AddShape(2, 2);
                if (preferRectangleFirst || !preferSquareFirst) AddShape(2, 1);
                AddShape(1, 1);
                AddShape(2, 1);
                if (!preferHero) AddShape(2, 2);
            }
            else if (aspect >= 0.78d)
            {
                if (preferHero) AddShape(2, 2);
                if (preferSquareAccent) AddShape(1, 1);
                if (preferRectangleFirst && !preferSquareFirst) AddShape(2, 1);
                AddShape(1, 1);
                AddShape(2, 1);
            }
            else
            {
                if (preferSquareFirst || preferSquareAccent) AddShape(1, 1);
                if (preferHero) AddShape(2, 2);
                if (preferRectangleFirst && !preferSquareFirst) AddShape(2, 1);
                AddShape(1, 1);
                AddShape(2, 1);
                if (!preferHero) AddShape(2, 2);
            }

            AddShape(1, 1);
            return shapes;
        }

        static bool TryFindLibraryQuiltAnchorCell(
            List<bool[]> occupiedRows,
            int columns,
            int maxRows,
            out int cellX,
            out int cellY)
        {
            cellX = 0;
            cellY = 0;
            for (var y = 0; y < maxRows; y++)
            {
                EnsureLibraryQuiltRows(occupiedRows, y + 1, columns);
                for (var x = 0; x < columns; x++)
                {
                    if (occupiedRows[y][x]) continue;
                    cellX = x;
                    cellY = y;
                    return true;
                }
            }

            return false;
        }

        static bool LibraryQuiltAreaIsFree(List<bool[]> occupiedRows, int x, int y, LibraryDetailQuiltShape shape)
        {
            for (var row = y; row < y + shape.RowSpan; row++)
            {
                if (row >= occupiedRows.Count) continue;
                var rowData = occupiedRows[row];
                for (var col = x; col < x + shape.ColSpan; col++)
                {
                    if (rowData[col]) return false;
                }
            }
            return true;
        }

        static void EnsureLibraryQuiltRows(List<bool[]> occupiedRows, int rowCount, int columns)
        {
            while (occupiedRows.Count < rowCount)
                occupiedRows.Add(new bool[columns]);
        }

        static void MarkLibraryQuiltOccupied(List<bool[]> occupiedRows, int x, int y, LibraryDetailQuiltShape shape)
        {
            for (var row = y; row < y + shape.RowSpan; row++)
            {
                var rowData = occupiedRows[row];
                for (var col = x; col < x + shape.ColSpan; col++)
                    rowData[col] = true;
            }
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
