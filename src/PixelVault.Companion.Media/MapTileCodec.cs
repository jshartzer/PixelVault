using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

internal static class MapTileCodec
{
    internal static void Generate(Stream input,long length,string output,int width,int height)
    {
        if(length is <1 or >160L*1024*1024 || width<=0 || height<=0 || (long)width*height>100_000_000)throw new InvalidDataException();
        var bytes=new byte[(int)length];input.ReadExactly(bytes);
        using var source=new MemoryStream(bytes);using var image=Image.FromStream(source);
        if(image.RawFormat.Guid!=ImageFormat.Png.Guid&&image.RawFormat.Guid!=ImageFormat.Jpeg.Guid)throw new InvalidDataException();
        if(image.Width!=width||image.Height!=height)throw new InvalidDataException();
        var maxZoom=Math.Max(0,(int)Math.Ceiling(Math.Log2(Math.Max(width,height)/512d)));
        Directory.CreateDirectory(output);
        for(var z=0;z<=maxZoom;z++)
        {
            var divisor=1<< (maxZoom-z);var w=(width+divisor-1)/divisor;var h=(height+divisor-1)/divisor;
            using var level=new Bitmap(w,h,PixelFormat.Format24bppRgb);
            using(var graphics=Graphics.FromImage(level))
            {
                graphics.Clear(Color.Black);graphics.CompositingMode=CompositingMode.SourceCopy;
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                using var wrap=new ImageAttributes();wrap.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(image,new Rectangle(0,0,w,h),0,0,width,height,GraphicsUnit.Pixel,wrap);
            }
            for(var y=0;y<(h+511)/512;y++)for(var x=0;x<(w+511)/512;x++)
            {
                // Keep edge tiles at the advertised size; browser tile layers otherwise stretch them.
                using var tile=new Bitmap(512,512,PixelFormat.Format24bppRgb);
                using(var graphics=Graphics.FromImage(tile))
                {
                    graphics.Clear(Color.Black);graphics.CompositingMode=CompositingMode.SourceCopy;
                    var crop=new Rectangle(x*512,y*512,Math.Min(512,w-x*512),Math.Min(512,h-y*512));
                    graphics.DrawImage(level,new Rectangle(0,0,crop.Width,crop.Height),crop,GraphicsUnit.Pixel);
                }
                var codec=ImageCodecInfo.GetImageEncoders().Single(c=>c.FormatID==ImageFormat.Jpeg.Guid);
                using var parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,90L);
                tile.Save(Path.Combine(output,$"{z}-{x}-{y}.jpg"),codec,parameters);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new{Fingerprint=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),Width=width,Height=height}));
    }
}
