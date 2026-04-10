using System;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        double ResolveScrollViewerLayoutWidth(ScrollViewer scrollViewer, double fallback = 0)
        {
            if (scrollViewer == null) return Math.Max(0d, fallback);
            var paddingWidth = scrollViewer.Padding.Left + scrollViewer.Padding.Right;
            var viewportWidth = scrollViewer.ViewportWidth;
            var actualWidth = scrollViewer.ActualWidth;
            var resolved = Math.Max(viewportWidth, Math.Max(0d, actualWidth - paddingWidth));
            if (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                resolved = Math.Max(0d, resolved - SystemParameters.VerticalScrollBarWidth);
            }
            if (resolved <= 0d && scrollViewer.Parent is FrameworkElement parent)
            {
                resolved = Math.Max(0d, parent.ActualWidth - paddingWidth);
            }
            return resolved <= 0d ? Math.Max(0d, fallback) : resolved;
        }
        int NormalizeLibraryFolderTileSize(int value) => SettingsService.NormalizeLibraryFolderTileSize(value);
        int NormalizeLibraryFolderGridColumnCount(int value) => SettingsService.NormalizeLibraryFolderGridColumnCount(value);
        int NormalizeLibraryPhotoGridColumnCount(int value) => SettingsService.NormalizeLibraryPhotoGridColumnCount(value);
        int NormalizeLibraryPhotoRailFolderGridColumnCount(int value) => SettingsService.NormalizeLibraryPhotoRailFolderGridColumnCount(value);
        (int Columns, int TileSize) CalculateResponsiveLibraryFolderLayout(ScrollViewer scrollViewer, bool photoWorkspaceRail = false)
        {
            // Match folder cover tile Margin right (horizontal rhythm between columns).
            const int gapPx = 12;
            const int layoutMinTile = 48;
            const int layoutMaxTile = 1000;
            const double moreColumnsSlackBudgetPx = 8;
            var viewportWidth = ResolveScrollViewerLayoutWidth(scrollViewer);
            viewportWidth = Math.Max(120, viewportWidth - 18);
            var maxColumnsFitByMinTile = (int)Math.Floor((viewportWidth + gapPx) / (layoutMinTile + gapPx));
            var libraryFolderMaxColumnsAuto = photoWorkspaceRail
                ? 2
                : (libraryFolderFillPaneWidth
                    ? Math.Min(12, Math.Max(4, maxColumnsFitByMinTile))
                    : 4);
            var fixedColsRaw = photoWorkspaceRail
                ? NormalizeLibraryPhotoRailFolderGridColumnCount(libraryPhotoRailFolderGridColumnCount)
                : NormalizeLibraryFolderGridColumnCount(libraryFolderGridColumnCount);
            // Photo rail: column toggle is only 1 or 2; unset (0) behaves like two-up.
            var fixedCols = photoWorkspaceRail && fixedColsRaw == 0 ? 2 : fixedColsRaw;
            var userCap = NormalizeLibraryFolderTileSize(photoWorkspaceRail ? libraryPhotoRailFolderTileSize : libraryFolderTileSize);
            if (photoWorkspaceRail && fixedCols > 0)
                userCap = layoutMaxTile;
            int TileWidthForColumns(int c, int rawEqualSplit, int floorTile)
            {
                var tileWidth = Math.Max(floorTile, Math.Min(layoutMaxTile, rawEqualSplit));
                tileWidth = (int)(Math.Round(tileWidth / 16d) * 16);
                tileWidth = Math.Max(floorTile, Math.Min(layoutMaxTile, Math.Min(rawEqualSplit, tileWidth)));
                while (tileWidth > floorTile && c * tileWidth + (c - 1) * gapPx > viewportWidth)
                    tileWidth = Math.Max(floorTile, tileWidth - 16);
                return tileWidth;
            }
            if (fixedCols > 0)
            {
                var c = Math.Min(fixedCols, photoWorkspaceRail ? 2 : 12);
                while (c > 1)
                {
                    var rawProbe = (int)Math.Floor((viewportWidth - ((c - 1) * gapPx)) / (double)c);
                    if (rawProbe >= layoutMinTile) break;
                    c--;
                }
                c = Math.Max(1, c);
                var rawFill = (int)Math.Floor((viewportWidth - ((c - 1) * gapPx)) / (double)c);
                var capped = Math.Max(layoutMinTile, Math.Min(userCap, rawFill));
                var bestW = TileWidthForColumns(c, capped, layoutMinTile);
                return (c, bestW);
            }
            var maxColumnsCeiling = viewportWidth >= 360 ? libraryFolderMaxColumnsAuto : 1;
            var preferDenseGridInSplitPane = viewportWidth < 900;
            bool ShouldPreferLayout(double slack, int c, int tileW, double prevSlack, int prevC, int prevTileW)
            {
                if (slack < prevSlack - 0.5) return true;
                if (preferDenseGridInSplitPane && slack <= prevSlack + moreColumnsSlackBudgetPx && c > prevC) return true;
                if (Math.Abs(slack - prevSlack) < 0.5)
                {
                    if (c > prevC) return true;
                    if (c == prevC && tileW > prevTileW) return true;
                    if (c == prevC && tileW != prevTileW && Math.Abs(tileW - userCap) < Math.Abs(prevTileW - userCap)) return true;
                }
                return false;
            }
            var bestColumns = 1;
            var bestTileW = layoutMinTile;
            var bestSlack = double.MaxValue;
            for (var c = 1; c <= maxColumnsCeiling; c++)
            {
                var rawTile = (int)Math.Floor((viewportWidth - ((c - 1) * gapPx)) / (double)c);
                if (rawTile < layoutMinTile) continue;
                var tileWidth = TileWidthForColumns(c, rawTile, layoutMinTile);
                var used = c * tileWidth + (c - 1) * gapPx;
                var slack = viewportWidth - used;
                if (!(slack > -0.5)) continue;
                if (!ShouldPreferLayout(slack, c, tileWidth, bestSlack, bestColumns, bestTileW)) continue;
                bestSlack = slack;
                bestColumns = c;
                bestTileW = tileWidth;
            }
            {
                var span = viewportWidth - (bestColumns - 1) * gapPx;
                var rawFill = (int)Math.Floor(span / (double)bestColumns);
                var capped = Math.Max(layoutMinTile, Math.Min(userCap, rawFill));
                bestTileW = TileWidthForColumns(bestColumns, capped, layoutMinTile);
            }
            return (bestColumns, bestTileW);
        }
        (int Columns, int TileSize) CalculateResponsiveLibraryDetailLayout(ScrollViewer scrollViewer, bool applySavedPhotoTileSizePreference)
        {
            const int gapPx = 8;
            const double moreColumnsSlackBudgetPx = 16d;
            const double libraryDetailPhotoTileSizeScale = 1.75d;
            var viewportWidth = ResolveScrollViewerLayoutWidth(scrollViewer);
            viewportWidth = Math.Max(160, viewportWidth - 24);
            var maxColumnsCeiling = viewportWidth >= 1280d ? 8 : viewportWidth >= 1040d ? 6 : viewportWidth >= 820d ? 5 : viewportWidth >= 560d ? 4 : viewportWidth >= 380d ? 3 : 2;
            if (viewportWidth < 300d) maxColumnsCeiling = 1;
            var minTile = (int)Math.Round((viewportWidth < 420d ? 156 : (viewportWidth < 900d ? 176 : 208)) * libraryDetailPhotoTileSizeScale);
            var layoutMaxTile = applySavedPhotoTileSizePreference
                ? Math.Max((int)Math.Round(900d * libraryDetailPhotoTileSizeScale), SettingsService.LibraryPhotoTileScrollHardMax + 200)
                : (int)Math.Round(900d * libraryDetailPhotoTileSizeScale);

            int ClampRoundedTile(int rawEqualSplit)
            {
                var clamped = Math.Max(minTile, Math.Min(layoutMaxTile, rawEqualSplit));
                var roundedDown = (int)(Math.Floor(clamped / 12d) * 12);
                if (roundedDown < minTile) roundedDown = clamped;
                return Math.Max(minTile, Math.Min(rawEqualSplit, roundedDown));
            }

            var bestColumns = 1;
            var bestTileWidth = ClampRoundedTile((int)Math.Floor(viewportWidth));
            var bestSlack = Math.Max(0d, viewportWidth - bestTileWidth);

            if (applySavedPhotoTileSizePreference)
            {
                var fixedPhotoCols = NormalizeLibraryPhotoGridColumnCount(libraryPhotoGridColumnCount);
                if (fixedPhotoCols > 0)
                {
                    var c = Math.Min(fixedPhotoCols, maxColumnsCeiling);
                    while (c > 1)
                    {
                        var rawFillProbe = (int)Math.Floor((viewportWidth - ((c - 1) * gapPx)) / (double)c);
                        if (rawFillProbe >= minTile) break;
                        c--;
                    }
                    c = Math.Max(1, c);
                    bestColumns = c;
                    var rawFillFixed = (int)Math.Floor((viewportWidth - ((bestColumns - 1) * gapPx)) / (double)bestColumns);
                    var cappedFixed = Math.Max(minTile, Math.Min(layoutMaxTile, rawFillFixed));
                    bestTileWidth = ClampRoundedTile(cappedFixed);
                }
                else
                {
                    bestTileWidth = ClampRoundedTile(minTile);
                    for (var columns = 1; columns <= maxColumnsCeiling; columns++)
                    {
                        var rawFill = (int)Math.Floor((viewportWidth - ((columns - 1) * gapPx)) / (double)columns);
                        if (rawFill < minTile) continue;
                        var capped = Math.Max(minTile, Math.Min(layoutMaxTile, rawFill));
                        var tileW = ClampRoundedTile(capped);
                        if (tileW > bestTileWidth || (tileW == bestTileWidth && columns < bestColumns))
                        {
                            bestTileWidth = tileW;
                            bestColumns = columns;
                        }
                    }
                }
            }
            else
            {
                var autoMaxCols = viewportWidth >= 1100d ? 4 : (viewportWidth >= 560d ? 3 : (viewportWidth >= 360d ? 2 : 1));
                for (var columns = 1; columns <= autoMaxCols; columns++)
                {
                    var rawEqualSplit = (int)Math.Floor((viewportWidth - ((columns - 1) * gapPx)) / columns);
                    if (rawEqualSplit < minTile) continue;
                    var tileWidth = ClampRoundedTile(rawEqualSplit);
                    var usedWidth = (columns * tileWidth) + ((columns - 1) * gapPx);
                    var slack = Math.Max(0d, viewportWidth - usedWidth);
                    if (slack + moreColumnsSlackBudgetPx < bestSlack)
                    {
                        bestColumns = columns;
                        bestTileWidth = tileWidth;
                        bestSlack = slack;
                        continue;
                    }

                    if (slack <= bestSlack + moreColumnsSlackBudgetPx && columns > bestColumns)
                    {
                        bestColumns = columns;
                        bestTileWidth = tileWidth;
                        bestSlack = slack;
                    }
                }
            }

            return (bestColumns, bestTileWidth);
        }
    }
}
