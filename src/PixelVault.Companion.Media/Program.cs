using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PixelVault.Media;

// One requested derivative per process. No Native UI, catalogue scan, or original-file export.
try
{
    using var input=Console.OpenStandardInput();var header=new List<byte>();int next;
    while((next=input.ReadByte())>=0&&next!='\n'){if(header.Count>=32768)throw new InvalidDataException();header.Add((byte)next);}
    var request=JsonSerializer.Deserialize<Request>(header.ToArray())??throw new InvalidDataException();
    if(request.Operation=="map-tiles")
    {
        MapTileCodec.Generate(input,request.Bytes,request.Output,request.Width,request.Height);
        return;
    }
    if(request.Max is not (480 or 960 or 1280 or 2560)||request.Bytes is <1 or >64L*1024*1024)throw new InvalidDataException();
    var bytes=new byte[(int)request.Bytes];input.ReadExactly(bytes);var fingerprint=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    using var stream=new MemoryStream(bytes);using var image=request.Extension.Equals(".jxr",StringComparison.OrdinalIgnoreCase)?PhotoCodec.DecodeJxr(bytes):Image.FromStream(stream);
    if((long)image.Width*image.Height>100_000_000)throw new InvalidDataException();
    if(image.PropertyIdList.Contains(0x112))
    {
        var orientation=image.GetPropertyItem(0x112)?.Value?[0]??1;
        image.RotateFlip(orientation switch{2=>RotateFlipType.RotateNoneFlipX,3=>RotateFlipType.Rotate180FlipNone,4=>RotateFlipType.Rotate180FlipX,5=>RotateFlipType.Rotate90FlipX,6=>RotateFlipType.Rotate90FlipNone,7=>RotateFlipType.Rotate270FlipX,8=>RotateFlipType.Rotate270FlipNone,_=>RotateFlipType.RotateNoneFlipNone});
    }
    Directory.CreateDirectory(Path.Combine(request.Output,"assets"));
    var asset=PhotoCodec.Derivative(image,request.Max,request.Output);
    Console.WriteLine(JsonSerializer.Serialize(new{Asset=asset,Width=image.Width,Height=image.Height,Fingerprint=fingerprint}));
}
catch{Console.Error.WriteLine("The requested preview could not be generated.");Environment.ExitCode=1;}
sealed record Request(string Extension,long Bytes,string Output,int Max,string? Operation=null,int Width=0,int Height=0);
