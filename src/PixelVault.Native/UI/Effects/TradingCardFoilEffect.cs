using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    internal sealed class TradingCardFoilEffect : ShaderEffect
    {
        static readonly PixelShader Shader = CreateShader();
        static readonly Brush GrainBrush = CreateGrain();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(TradingCardFoilEffect), 0);
        public static readonly DependencyProperty GrainProperty = RegisterPixelShaderSamplerProperty("Grain", typeof(TradingCardFoilEffect), 1, SamplingMode.NearestNeighbor);
        public static readonly DependencyProperty LightProperty = DependencyProperty.Register("Light", typeof(Point), typeof(TradingCardFoilEffect),
            new UIPropertyMetadata(new Point(0.5, 0.5), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register("Size", typeof(Point), typeof(TradingCardFoilEffect),
            new UIPropertyMetadata(new Point(300, 450), PixelShaderConstantCallback(1)));
        public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
        public Brush Grain { get => (Brush)GetValue(GrainProperty); set => SetValue(GrainProperty, value); }
        public Point Light { get => (Point)GetValue(LightProperty); set => SetValue(LightProperty, value); }
        public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
        public TradingCardFoilEffect()
        {
            PixelShader = Shader;
            Grain = GrainBrush;
            UpdateShaderValue(InputProperty); UpdateShaderValue(GrainProperty);
            UpdateShaderValue(LightProperty); UpdateShaderValue(SizeProperty);
        }
        static PixelShader CreateShader()
        {
            var shader = new PixelShader();
            using var stream = typeof(TradingCardFoilEffect).Assembly.GetManifestResourceStream("PixelVault.TradingCardFoil.ps");
            if (stream == null) throw new InvalidOperationException("The trading-card foil shader resource is missing.");
            shader.SetStreamSource(stream);
            shader.Freeze();
            return shader;
        }
        static Brush CreateGrain()
        {
            const int size = 128;
            const double hexCellsAcross = 9.0;
            var pixels = new byte[size * size * 4];
            var random = new Random(714);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var hexDist = HoneycombDistance(x + 0.5, y + 0.5, size / hexCellsAcross);
                    var ridge = SmoothStep(0.37, 0.44, hexDist);
                    var face = 1.0 - ridge;
                    var cellHash = CellHash(x, y, size / hexCellsAcross);
                    var micro = random.NextDouble();
                    var combined = face * (0.42 + cellHash * 0.14 + (micro - 0.5) * 0.16) + ridge * (0.72 + cellHash * 0.08);
                    var value = (byte)Math.Max(32, Math.Min(240, Math.Round(combined * 255)));
                    var index = ((y * size) + x) * 4;
                    pixels[index] = pixels[index + 1] = pixels[index + 2] = value;
                    pixels[index + 3] = 255;
                }
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            brush.Freeze(); return brush;
        }
        static double HoneycombDistance(double x, double y, double cellSize)
        {
            var scale = 1.0 / Math.Max(1.0, cellSize);
            var px = x * scale;
            var py = y * scale;
            var sx = 1.0;
            var sy = Math.Sqrt(3.0);
            var ax = Mod(px, sx) - sx * 0.5;
            var ay = Mod(py, sy) - sy * 0.5;
            var bx = Mod(px - sx * 0.5, sx) - sx * 0.5;
            var by = Mod(py - sy * 0.5, sy) - sy * 0.5;
            var da = ax * ax + ay * ay;
            var db = bx * bx + by * by;
            var gx = da < db ? ax : bx;
            var gy = da < db ? ay : by;
            return Math.Sqrt(gx * gx + gy * gy);
        }
        static double CellHash(int x, int y, double cellSize)
        {
            var cx = (int)Math.Floor(x / cellSize);
            var cy = (int)Math.Floor(y / (cellSize * Math.Sqrt(3.0)));
            var seed = (cx * 73856093) ^ (cy * 19349663);
            return (seed & 0xFFFF) / 65535.0;
        }
        static double Mod(double value, double period) => value - period * Math.Floor(value / period);
        static double SmoothStep(double edge0, double edge1, double value)
        {
            if (value <= edge0) return 0;
            if (value >= edge1) return 1;
            var t = (value - edge0) / (edge1 - edge0);
            return t * t * (3 - 2 * t);
        }
    }
}
