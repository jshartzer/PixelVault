using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
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

        internal static int EstimateLibraryDetailSingleTileHeight(string file, int tileWidth, bool includeTimelineFooter)
        {
            if (string.IsNullOrWhiteSpace(file) || tileWidth <= 0) return 260;
            var footer = includeTimelineFooter ? 196 : 182;
            var ratios = new[] { 0.58, 0.62, 0.66, 0.72, 0.78 };
            var r = ratios[LibraryDetailFileLayoutHash(file) % ratios.Length];
            var inner = (int)Math.Ceiling(tileWidth * r);
            return Math.Max(220, inner + footer);
        }

        /// <summary>Column-based masonry with occasional 2-column &quot;hero&quot; spans when there are at least two columns.</summary>
        internal static List<LibraryDetailMasonryChunk> BuildLibraryDetailMasonryChunks(
            IReadOnlyList<string> files,
            double availableWidth,
            int gapPx,
            int baseWidth,
            int minWidth,
            int maxWidth,
            bool includeTimelineFooter)
        {
            var chunks = new List<LibraryDetailMasonryChunk>();
            if (files == null || files.Count == 0) return chunks;

            var gap = Math.Max(0, gapPx);
            var avail = Math.Max(120d, availableWidth);
            var minColW = Math.Max(minWidth, 240);
            var maxColW = Math.Max(minColW, Math.Min(maxWidth, Math.Max(minWidth, baseWidth)));

            var cols = Math.Max(1, (int)Math.Floor((avail + gap) / (minColW + gap)));
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

                var useHero = cols >= 2
                    && filtered.Count >= 2
                    && (
                        ordinal == 0
                        || (ordinal - lastHeroIndex >= 6 && (LibraryDetailFileLayoutHash(file + "|" + ordinal) % 11) == 5 && ordinal < filtered.Count - 1));

                if (useHero)
                {
                    var heroW = (int)Math.Min(Math.Floor(avail), 2 * columnWidth + gap);
                    var heroH = EstimateLibraryDetailSingleTileHeight(file, heroW, includeTimelineFooter);
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
                    var w = ResolveLibraryVariableDetailTileWidth(file, columnWidth, minWidth, Math.Min(maxWidth, columnWidth));
                    w = Math.Min(w, (int)Math.Floor(avail));
                    var h = EstimateLibraryDetailSingleTileHeight(file, w, includeTimelineFooter);
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
