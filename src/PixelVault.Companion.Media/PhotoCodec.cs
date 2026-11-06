using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Security.Cryptography;
namespace PixelVault.Media;
public static class PhotoCodec
{
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static Bitmap DecodeJxr(byte[] bytes)
    {
        using var input=new MemoryStream(bytes);var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoder.Frames[0];
        if((long)frame.PixelWidth*frame.PixelHeight>100_000_000)throw new InvalidDataException("A capture exceeds the pixel limit.");
        BitmapSource converted;
        // WIC float/fixed RGB is scRGB when no explicit color context is supplied.
        // https://learn.microsoft.com/en-us/windows/win32/wic/-wic-codec-native-pixel-formats
        if(frame.Format.ToString()=="Default"||frame.Format.ToString().Contains("Float",StringComparison.Ordinal)||frame.Format.ToString().Contains("Half",StringComparison.Ordinal)||frame.Format.ToString().Contains("Fixed",StringComparison.Ordinal))
        {
            var floats=new FormatConvertedBitmap(frame,PixelFormats.Rgba128Float,null,0);var linear=new float[checked(frame.PixelWidth*frame.PixelHeight*4)];floats.CopyPixels(linear,frame.PixelWidth*16,0);
            var pixels=ToneMapHdr(linear);converted=BitmapSource.Create(frame.PixelWidth,frame.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,frame.PixelWidth*4);
        }
        else converted=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(converted));
        using var png=new MemoryStream();encoder.Save(png);png.Position=0;using var decoded=Image.FromStream(png);return new Bitmap(decoded);
    }
    public static byte[] ToneMapHdr(float[] linear)
    {
        static double Channel(float f)=>float.IsFinite(f)?Math.Max(0,f):0;
        static double Luminance(float[] a,int i)=>.2126*Channel(a[i])+.7152*Channel(a[i+1])+.0722*Channel(a[i+2]);
        var samples=new List<double>();var step=Math.Max(1,linear.Length/4/32768);
        for(var pixel=0;pixel<linear.Length/4;pixel+=step)samples.Add(Luminance(linear,pixel*4));
        var exposure=.18/Math.Exp(samples.Average(l=>Math.Log(1e-4+l)));samples.Sort();var white=Math.Max(1,samples[Math.Min(samples.Count-1,(int)(samples.Count*.995))]*exposure);var whiteSquared=white*white;
        var transfer=Enumerable.Range(0,65536).Select(i=>{var c=i/65535.0;return (byte)Math.Round(255*(c<=.0031308?12.92*c:1.055*Math.Pow(c,1/2.4)-.055));}).ToArray();
        byte Srgb(double c)=>transfer[(int)(Math.Clamp(c,0,1)*65535)];
        var result=new byte[linear.Length];
        for(var i=0;i<linear.Length;i+=4){var luminance=Luminance(linear,i);var x=luminance*exposure;var mapped=x*(1+x/whiteSquared)/(1+x);var scale=luminance>1e-8?mapped/luminance:0;result[i]=Srgb(Channel(linear[i+2])*scale);result[i+1]=Srgb(Channel(linear[i+1])*scale);result[i+2]=Srgb(Channel(linear[i])*scale);result[i+3]=255;}
        return result;
    }

    public static string Derivative(Image image,int max,string output)
    {
        var scale=Math.Min(1,(double)max/Math.Max(image.Width,image.Height));using var bitmap=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(bitmap)){g.Clear(System.Drawing.Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,0,0,bitmap.Width,bitmap.Height);}
        using var stream=new MemoryStream();var codec=ImageCodecInfo.GetImageEncoders().Single(c=>c.MimeType=="image/jpeg");using var parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,max==480?80L:88L);bitmap.Save(stream,codec,parameters);
        var bytes=stream.ToArray();if(bytes.Length>6*1024*1024)throw new InvalidDataException("A viewing copy exceeds the pilot limit.");var name=Hash(bytes)+".jpg";var file=Path.Combine(output,"assets",name);if(!File.Exists(file))File.WriteAllBytes(file,bytes);return name;
    }
}
