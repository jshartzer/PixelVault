using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PixelVault.Companion.Export;
using Xunit;

public sealed class ScreenshotSnapshotTests
{
    [Fact]
    public void PilotReusesStorageScopeDatesAndCanonicalGroupsWithoutPublishingPathsOrChangingSources()
    {
        var root=Path.Combine(Path.GetTempPath(),"ScreenshotPilot-"+Guid.NewGuid().ToString("N"));var source=Path.Combine(root,"source");var library=Path.Combine(root,"captures");var output=Path.Combine(root,"export");
        Directory.CreateDirectory(Path.Combine(source,"cache"));Directory.CreateDirectory(library);SQLitePCL.Batteries_V2.Init();
        try
        {
            File.WriteAllText(Path.Combine(source,"PixelVault.settings.ini"),"library="+library+"\nsteam_web_api_key=private-secret\n");
            var a=Path.Combine(library,"a.png");var b=Path.Combine(library,"b.jpg");var other=Path.Combine(library,"other.png");
            using(var bitmap=new Bitmap(1920,1080)){bitmap.Save(a,ImageFormat.Png);bitmap.Save(b,ImageFormat.Jpeg);bitmap.Save(other,ImageFormat.Png);}
            AddText(a,"Description\0Embedded note and tags: original metadata must survive.");
            var original=SHA256.HashData(File.ReadAllBytes(a));
            var safe=Regex.Replace(Regex.Replace(library.Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
            var index=Path.Combine(source,"cache","pixelvault-index-"+safe+".sqlite");
            using(var c=new SqliteConnection($"Data Source={index};Pooling=False"))
            {c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE game_index(root TEXT,game_id TEXT,name TEXT,platform_label TEXT,steam_app_id TEXT,storage_group_id TEXT); CREATE TABLE photo_index(root TEXT,file_path TEXT,game_id TEXT,console_label TEXT,capture_utc_ticks INTEGER); INSERT INTO game_index VALUES($r,'G1','Game','Steam','10','SG1'),($r,'G2','Game','Xbox','','SG1'),($r,'G3','Game 2','Steam','11','SG2'); INSERT INTO photo_index VALUES($r,$a,'G1','Steam',638900000000000000),($r,$b,'G2','Xbox',0),($r,$o,'G3','Steam',638900000000000000),($r,'clip.mp4','G1','Steam',0);";cmd.Parameters.AddWithValue("$r",library);cmd.Parameters.AddWithValue("$a",a);cmd.Parameters.AddWithValue("$b",b);cmd.Parameters.AddWithValue("$o",other);cmd.ExecuteNonQuery();}
            var groups=Path.Combine(source,"capture-groups.sqlite");
            using(var c=new SqliteConnection($"Data Source={groups};Pooling=False"))
            {c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE capture_group_member(root TEXT,game TEXT,file TEXT,name TEXT); INSERT INTO capture_group_member VALUES($r,'G1','a.png','Old group'),($r,'storage:SG1','a.png','Canonical'),($r,'G2','b.jpg','Xbox group');";cmd.Parameters.AddWithValue("$r",library);cmd.ExecuteNonQuery();}
            var beforeIndex=File.ReadAllBytes(index);var beforeGroups=File.ReadAllBytes(groups);
            var first=ScreenshotSnapshotExporter.Export(source,new string('A',64),"steam","10",output);
            Assert.Equal(2,first.Eligible);Assert.Equal(1,first.SkippedVideos);Assert.Equal(new[]{"Steam","Xbox"},first.Photos.Select(p=>p.Console));Assert.Equal("Canonical",first.Photos[0].Group);Assert.Equal("Xbox group",first.Photos[1].Group);Assert.Null(first.Photos[1].CaptureDay);Assert.Equal(638900000000000000,first.Photos[0].CaptureUtcTicks);
            using(var thumb=Image.FromFile(Path.Combine(output,"assets",first.Photos[0].Thumbnail))){Assert.Equal(480,thumb.Width);Assert.Equal(270,thumb.Height);Assert.All(thumb.PropertyIdList,id=>Assert.Contains(id,new[]{20624,20625}));}
            var second=ScreenshotSnapshotExporter.Export(source,new string('A',64),"steam","10",output);Assert.Equal(first.Photos,second.Photos);
            var payload=File.ReadAllText(Path.Combine(output,"catalogue.json"));Assert.DoesNotContain(library,payload);Assert.DoesNotContain("private-secret",payload);Assert.DoesNotContain("a.png",payload);
            Assert.Equal(original,SHA256.HashData(File.ReadAllBytes(a)));Assert.Equal(beforeIndex,File.ReadAllBytes(index));Assert.Equal(beforeGroups,File.ReadAllBytes(groups));
            var full=ScreenshotSnapshotExporter.Export(source,new string('A',64),"steam","10",output,includeOriginals:true);var descriptor=full.Photos[0].Original!;
            Assert.Equal(Convert.ToHexString(original).ToLowerInvariant()+".png",descriptor.Asset);Assert.Equal(new FileInfo(a).Length,descriptor.Bytes);Assert.Equal("a.png",descriptor.FileName);Assert.Equal("image/png",descriptor.ContentType);Assert.Equal(1920,full.Photos[0].Width);
            var map=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(output,"original-sources.private.json")))!;Assert.Equal(File.ReadAllBytes(a),File.ReadAllBytes(map[descriptor.Asset]));Assert.Contains("Embedded note",System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(map[descriptor.Asset])));
            var publicManifest=File.ReadAllText(Path.Combine(output,"catalogue.json"));Assert.DoesNotContain(library.Replace("\\","\\\\"),publicManifest);Assert.DoesNotContain("private-secret",publicManifest);
            var saved=File.ReadAllText(Path.Combine(output,"catalogue.json"));File.WriteAllText(a,"corrupt image");Assert.ThrowsAny<Exception>(()=>ScreenshotSnapshotExporter.Export(source,new string('A',64),"steam","10",output));Assert.Equal(saved,File.ReadAllText(Path.Combine(output,"catalogue.json")));
        }
        finally{Directory.Delete(root,true);}
    }
    static void AddText(string path,string text)
    {
        var image=File.ReadAllBytes(path);var data=System.Text.Encoding.Latin1.GetBytes(text);var type=System.Text.Encoding.ASCII.GetBytes("tEXt");var chunk=new byte[data.Length+12];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0,4),data.Length);type.CopyTo(chunk,4);data.CopyTo(chunk,8);uint crc=0xffffffff;
        foreach(var b in chunk.AsSpan(4,data.Length+4)){crc^=b;for(var i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(data.Length+8),~crc);
        File.WriteAllBytes(path,image[..^12].Concat(chunk).Concat(image[^12..]).ToArray());
    }
}
