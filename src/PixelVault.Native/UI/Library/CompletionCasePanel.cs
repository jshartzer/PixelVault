using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    internal sealed class CompletionCasePanel : Panel
    {
        const double Inset = 44, MinimumGap = 18;
        readonly bool emerald;
        readonly bool diamond;
        double PaddingTop => diamond ? 50 : CompletionCaseArtwork.TopPadding;
        readonly double coverWidth;
        readonly DecorationVisual foreground = new DecorationVisual();
        DrawingGroup drawing;
        Size drawingSize;
        int drawingColumns, drawingCount;
        double drawingPitch, gap = MinimumGap;
        internal int Columns { get; private set; } = 1;
        internal int ShelfCount { get; private set; } = 1;
        internal double ShelfPitch { get; private set; }
        internal Rect OrnamentBounds { get; private set; }
        internal CompletionCasePanel(bool emerald, double coverWidth)
        {
            this.emerald = emerald; this.coverWidth = coverWidth; ClipToBounds = false; SnapsToDevicePixels = true; UseLayoutRounding = true;
            Margin = new Thickness(0, 0, 0, 27); AddVisualChild(foreground);
        }
        internal CompletionCasePanel(PixelVault.Tracking.AchievementCompletionTier tier, double width) : this(tier == PixelVault.Tracking.AchievementCompletionTier.BaseAndDlc, width) { diamond = tier == PixelVault.Tracking.AchievementCompletionTier.BaseAndDlcAndPv; }
        protected override int VisualChildrenCount => base.VisualChildrenCount + 1;
        protected override Visual GetVisualChild(int index) => index == base.VisualChildrenCount ? foreground : base.GetVisualChild(index);
        sealed class DecorationVisual : DrawingVisual { protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) => null; }
        internal Rect ShelfInterior(int row, double width)
        {
            var upper = PaddingTop + CompletionCaseArtwork.Header + row * ShelfPitch + 6;
            var lower = PaddingTop + CompletionCaseArtwork.Header + (row + 1) * ShelfPitch - 39;
            return new Rect(Inset, upper, Math.Max(0, width - Inset * 2), Math.Max(0, lower - upper));
        }
        protected override Size MeasureOverride(Size available)
        {
            var width = double.IsInfinity(available.Width) ? coverWidth + Inset * 2 : available.Width;
            Columns = Math.Max(1, (int)Math.Floor((Math.Max(0, width - Inset * 2) + MinimumGap) / (coverWidth + MinimumGap)));
            gap = Columns > 1 ? Math.Min(32, Math.Max(MinimumGap, (width - Inset * 2 - Columns * coverWidth) / (Columns - 1))) : MinimumGap;
            var cardHeight = coverWidth * 1.5; foreach (UIElement card in InternalChildren) { card.Measure(new Size(coverWidth, double.PositiveInfinity)); cardHeight = Math.Max(cardHeight, card.DesiredSize.Height); }
            ShelfPitch = cardHeight + CompletionCaseArtwork.ShelfSpace; ShelfCount = Math.Max(1, (InternalChildren.Count + Columns - 1) / Columns);
            return new Size(width, PaddingTop + CompletionCaseArtwork.Header + ShelfCount * ShelfPitch + CompletionCaseArtwork.Footer);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            for (var row = 0; row < ShelfCount; row++)
            {
                var count = Math.Min(Columns, InternalChildren.Count - row * Columns); var interior = ShelfInterior(row, finalSize.Width);
                var cardHeight = ShelfPitch - CompletionCaseArtwork.ShelfSpace; var y = interior.Top + Math.Max(0, (interior.Height - cardHeight) / 2);
                for (var col = 0; col < count; col++) InternalChildren[row * Columns + col].Arrange(new Rect(Inset + col * (coverWidth + gap), y, coverWidth, cardHeight));
            }
            InvalidateVisual(); return finalSize;
        }
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context); if (RenderSize.Width < 90 || RenderSize.Height <= 0) return;
            if (drawing == null || drawingSize != RenderSize || drawingColumns != Columns || drawingCount != InternalChildren.Count || drawingPitch != ShelfPitch)
            {
                drawing = CompletionCaseArtwork.Draw(emerald, RenderSize.Width, RenderSize.Height, ShelfCount, Columns, ShelfPitch, coverWidth, gap, Inset, VisualTreeHelper.GetDpi(this).PixelsPerDip, out var ornaments, diamond);
                using (var layer = foreground.RenderOpen()) layer.DrawDrawing(ornaments); OrnamentBounds = ornaments.Bounds;
                drawingSize = RenderSize; drawingColumns = Columns; drawingCount = InternalChildren.Count; drawingPitch = ShelfPitch;
            }
            context.DrawDrawing(drawing);
            // Reflect loaded artwork, including asynchronous updates, without reflecting its caption.
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                if (InternalChildren[i] is not Grid card || card.Children.Count == 0) continue;
                var art = card is HolofoilCover finish ? finish.ReflectionVisual : card.Children[0] as Visual;
                if (art == null) continue;
                var row = i / Columns; var offset = VisualTreeHelper.GetOffset(card);
                var reflection = new VisualBrush(art) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Viewbox = new Rect(0, 1 - 29 / card.RenderSize.Height, 1, 29 / card.RenderSize.Height), Stretch = Stretch.Fill, RelativeTransform = new ScaleTransform(1, -1, .5, .5) };
                var y = PaddingTop + CompletionCaseArtwork.Header + (row + 1) * ShelfPitch - 39;
                var fade = new LinearGradientBrush(Color.FromArgb(150, 0, 0, 0), Colors.Transparent, new Point(0, 0), new Point(0, 1));
                context.PushOpacity(.24); context.PushOpacityMask(fade); context.DrawRectangle(reflection, null, new Rect(offset.X, y, coverWidth, 29)); context.Pop(); context.Pop();
            }
        }
    }
}
