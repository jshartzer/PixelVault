using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class TradingCardFoilTests
{
    [Fact]
    public void BundledShaderUsesTheSoftwareCompatibleModel()
    {
        using var resource=typeof(TradingCardFoilEffect).Assembly.GetManifestResourceStream("PixelVault.TradingCardFoil.ps");
        Assert.NotNull(resource);using var reader=new System.IO.BinaryReader(resource!);
        Assert.Equal(0xffff0200u,reader.ReadUInt32());
    }
    static void Save(byte[] pixels,string name)
    {
        var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;
        System.IO.Directory.CreateDirectory(output);var bitmap=BitmapSource.Create(96,144,96,96,PixelFormats.Pbgra32,null,pixels,96*4);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=System.IO.File.Create(System.IO.Path.Combine(output,name));png.Save(file);
    }
    static byte[] Render(Point light, Color color)
    {
        var card = new Border { Width = 96, Height = 144, Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(12), Effect = new TradingCardFoilEffect { Light = light, Size = new Point(96, 144) } };
        card.Measure(new Size(96, 144)); card.Arrange(new Rect(0, 0, 96, 144)); card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(96, 144, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var bytes = new byte[96 * 144 * 4]; bitmap.CopyPixels(bytes, 96 * 4, 0);
        return bytes;
    }

    [Fact]
    public void BundledShaderRendersInSoftwareAndLightMovementChangesReflection() => OnSta(() =>
    {
        var first = Render(new Point(.2, .2), Color.FromRgb(90, 90, 90));
        var second = Render(new Point(.8, .7), Color.FromRgb(90, 90, 90));
        Save(first,"foil-light-left.png");Save(second,"foil-light-right.png");
        var changed = Enumerable.Range(0, first.Length / 4).Count(i => Math.Abs(first[i * 4] - second[i * 4]) > 8);
        Assert.True(changed > 1000, "Moving the light should move a reflection across the cover.");
        var opaque = Enumerable.Range(0, first.Length / 4).Where(i => first[i * 4 + 3] == 255).ToList();
        Assert.Contains(opaque, i => first[i * 4] < 85);
        Assert.Contains(opaque, i => first[i * 4] > 135);
    });

    [Fact]
    public void HoneycombSubstrateCreatesSpatialTextureAtFixedLight() => OnSta(() =>
    {
        var pixels = Render(new Point(.5, .5), Color.FromRgb(120, 120, 120));
        var opaque = Enumerable.Range(0, pixels.Length / 4).Where(i => pixels[i * 4 + 3] == 255).ToList();
        var neighborDiff = opaque.Count(i =>
        {
            var x = i % 96;
            var y = i / 96;
            if (x >= 95 || y >= 143) return false;
            var j = i + 1;
            return Math.Abs(pixels[i * 4] - pixels[j * 4]) > 3;
        });
        Assert.True(neighborDiff > 400, "Honeycomb emboss should create visible local variation across the card face.");
    });

    [Fact]
    public void FoilPreservesRoundedTransparencyAndPremultipliedAlpha() => OnSta(() =>
    {
        var pixels = Render(new Point(.5, .5), Color.FromArgb(128, 80, 110, 140));
        Assert.Equal(0, pixels[3]);
        Assert.InRange(pixels[(72 * 96 + 48) * 4 + 3], 127, 129);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.True(pixels[i] <= pixels[i + 3]);
            Assert.True(pixels[i + 1] <= pixels[i + 3]);
            Assert.True(pixels[i + 2] <= pixels[i + 3]);
        }
    });
}
