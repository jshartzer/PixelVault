using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelVaultNative
{
    internal sealed class CenturyPrismaticEffect : ShaderEffect
    {
        static readonly PixelShader Shader = LoadShader();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(CenturyPrismaticEffect), 0);
        public static readonly DependencyProperty LightProperty = DependencyProperty.Register("Light", typeof(Point), typeof(CenturyPrismaticEffect),
            new UIPropertyMetadata(new Point(.4, .35), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty SweepProperty = DependencyProperty.Register("Sweep", typeof(double), typeof(CenturyPrismaticEffect),
            new UIPropertyMetadata(-1d, PixelShaderConstantCallback(1)));
        public static readonly DependencyProperty ColorRetentionProperty = DependencyProperty.Register("ColorRetention", typeof(double), typeof(CenturyPrismaticEffect),
            new UIPropertyMetadata(0d, PixelShaderConstantCallback(2)));
        public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
        public Point Light { get => (Point)GetValue(LightProperty); set => SetValue(LightProperty, value); }
        public double Sweep { get => (double)GetValue(SweepProperty); set => SetValue(SweepProperty, value); }
        public double ColorRetention { get => (double)GetValue(ColorRetentionProperty); set => SetValue(ColorRetentionProperty, value); }
        internal CenturyPrismaticEffect()
        {
            PixelShader = Shader;
            UpdateShaderValue(InputProperty); UpdateShaderValue(LightProperty); UpdateShaderValue(SweepProperty);
            UpdateShaderValue(ColorRetentionProperty);
        }
        static PixelShader LoadShader()
        {
            var shader = new PixelShader();
            using var stream = typeof(CenturyPrismaticEffect).Assembly.GetManifestResourceStream("PixelVault.CenturyPrismatic.ps")
                ?? throw new InvalidOperationException("Century prismatic shader resource is missing.");
            shader.SetStreamSource(stream); shader.Freeze(); return shader;
        }
    }
}
