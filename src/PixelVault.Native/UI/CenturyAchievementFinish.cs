using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PixelVaultNative
{
    /// <summary>Presentation only; never changes an achievement's progress or earned state.</summary>
    internal static class CenturyAchievementFinish
    {
        static readonly DependencyProperty AppliedProperty = DependencyProperty.RegisterAttached("Applied", typeof(bool), typeof(CenturyAchievementFinish));

        internal static bool Apply(Border icon, GameAchievementsFetchService.AchievementRow row)
        {
            if (icon == null || string.IsNullOrEmpty(row?.CenturyDefinitionId) || !row.ProgressKnown || !row.Unlocked) return false;
            if ((bool)icon.GetValue(AppliedProperty)) return true;
            if (SystemParameters.HighContrast) return true;
            if (icon.Child is not Image image) return false;
            icon.SetValue(AppliedProperty, true);
            icon.BorderBrush = Gold(); icon.BorderThickness = new Thickness(3); icon.Padding = new Thickness(0);
            image.Width = image.Height = double.NaN; image.Stretch = Stretch.UniformToFill;
            image.HorizontalAlignment = HorizontalAlignment.Stretch; image.VerticalAlignment = VerticalAlignment.Stretch;
            image.RenderTransformOrigin = new Point(.5, .5); image.RenderTransform = new ScaleTransform(1.16, 1.16);
            var foil = new CenturyPrismaticEffect(); image.Effect = foil;
            icon.Child = null;
            var face = new Grid { ClipToBounds = true, IsHitTestVisible = false };
            face.Children.Add(image); icon.Child = face;
            face.SizeChanged += (_, __) => face.Clip = new RectangleGeometry(new Rect(face.RenderSize),
                Math.Max(0, icon.CornerRadius.TopLeft - 3), Math.Max(0, icon.CornerRadius.TopLeft - 3));
            bool CanMove() => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
            void Reset()
            {
                foil.BeginAnimation(CenturyPrismaticEffect.SweepProperty, null);
                foil.Light = new Point(.4, .35);
            }
            icon.MouseMove += (_, e) =>
            {
                if (!CanMove()) return;
                var p = e.GetPosition(icon);
                foil.Light = new Point(.18 + Math.Clamp(p.X / Math.Max(1, icon.ActualWidth), 0, 1) * .64,
                    .18 + Math.Clamp(p.Y / Math.Max(1, icon.ActualHeight), 0, 1) * .64);
            };
            icon.MouseLeave += (_, __) => Reset();
            // Observe the click without handling it: existing details/navigation still work.
            icon.PreviewMouseLeftButtonDown += (_, __) =>
            {
                if (CanMove()) foil.BeginAnimation(CenturyPrismaticEffect.SweepProperty,
                    new DoubleAnimation(-.5, 1.9, TimeSpan.FromMilliseconds(1350)) { FillBehavior = FillBehavior.Stop });
            };
            icon.Unloaded += (_, __) => Reset();
            icon.IsVisibleChanged += (_, __) => { if (!icon.IsVisible) Reset(); };
            return true;
        }

        static Brush Gold()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            var colors = new[] { "#875619", "#D6A64F", "#FFF0BD", "#D6A64F", "#875619", "#FFF0BD", "#D6A64F", "#875619" };
            var offsets = new[] { 0d, .15, .28, .37, .48, .66, .8, 1 };
            for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), offsets[i]));
            brush.Freeze(); return brush;
        }
    }
}
