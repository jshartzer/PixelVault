using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVault.Companion.Export;
using Xunit;

public sealed class ScreenshotLibraryExportTests
{
    [Fact]
    public void LibraryUsesCurrentInventoryMergesCanonicalGamesRetainsGroupsDatesAndXmpWithoutSourceWrites()
    {
        var testRoot=Path.Combine(Path.GetTempPath(),"ScreenshotLibrary-"+Guid.NewGuid().ToString("N"));var source=Path.Combine(testRoot,"source");var root=Path.Combine(testRoot,"captures");var output=Path.Combine(testRoot,"output");Directory.CreateDirectory(Path.Combine(source,"cache"));Directory.CreateDirectory(root);SQLitePCL.Batteries_V2.Init();
        try
        {
            File.WriteAllText(Path.Combine(source,"PixelVault.settings.ini"),"library="+root+"\nsteam_web_api_key=private-secret\n");
            var images=Enumerable.Range(0,5).Select(i=>Path.Combine(root,i+(i==3?".jxr":".png"))).ToArray();using(var bitmap=new Bitmap(64,32))foreach(var file in images)if(!file.EndsWith(".jxr"))bitmap.Save(file,ImageFormat.Png);
            var hdr=new float[64*32*4];for(var y=0;y<32;y++)for(var x=0;x<64;x++){var i=(y*64+x)*4;hdr[i]=hdr[i+1]=hdr[i+2]=.1f+8f*x/63;hdr[i+3]=1;}
            var encoder=new System.Windows.Media.Imaging.WmpBitmapEncoder{ImageQualityLevel=1f};encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(64,32,96,96,System.Windows.Media.PixelFormats.Rgba128Float,null,hdr,64*16)));using(var stream=File.Create(images[3]))encoder.Save(stream);
            var xmp=images[0]+".xmp";File.WriteAllText(xmp,"<x:xmpmeta>Keep source tags</x:xmpmeta>");var beforeXmp=File.ReadAllBytes(xmp);
            var safe=Regex.Replace(Regex.Replace(root.Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");var index=Path.Combine(source,"cache","pixelvault-index-"+safe+".sqlite");
            using(var c=new SqliteConnection($"Data Source={index};Pooling=False"))
            {
                c.Open();using var command=c.CreateCommand();command.CommandText="CREATE TABLE game_index(root TEXT,game_id TEXT,name TEXT,platform_label TEXT,steam_app_id TEXT,storage_group_id TEXT);CREATE TABLE photo_index(root TEXT,file_path TEXT,game_id TEXT,console_label TEXT,capture_utc_ticks INTEGER);INSERT INTO game_index VALUES($r,'G1','One','Steam','10','SG1'),($r,'G2','One','Xbox','','SG1'),($r,'G3','One copy','Steam','10','SG3'),($r,'G4','Other','Steam','11','SG4');";command.Parameters.AddWithValue("$r",root);command.ExecuteNonQuery();
                for(var i=0;i<5;i++){using var row=c.CreateCommand();row.CommandText="INSERT INTO photo_index VALUES($r,$f,$g,$c,$t)";row.Parameters.AddWithValue("$r",root);row.Parameters.AddWithValue("$f",images[i]);row.Parameters.AddWithValue("$g",i==4?"G1":"G"+(i+1));row.Parameters.AddWithValue("$c",i==1?"Xbox":"Steam");row.Parameters.AddWithValue("$t",i==1?0:638900000000000000+i);row.ExecuteNonQuery();}
            }
            string Folder(int index,string platform,string steam){var fields=Enumerable.Repeat("",16).ToArray();fields[0]="G"+(index+1);fields[1]=root;fields[2]=index==3?"Other":"One";fields[5]=platform;fields[6]=images[index];fields[7]=steam;fields[15]="0";return string.Join('\t',fields);}
            File.WriteAllLines(Path.Combine(source,"cache","library-folders-"+safe+".cache"),[Folder(0,"Steam","10"),Folder(1,"Xbox",""),Folder(2,"Steam","10"),Folder(3,"Steam","11")]);
            var groups=Path.Combine(source,"capture-groups.sqlite");using(var c=new SqliteConnection($"Data Source={groups};Pooling=False")){c.Open();using var command=c.CreateCommand();command.CommandText="CREATE TABLE capture_group_member(root TEXT,game TEXT,file TEXT,name TEXT);INSERT INTO capture_group_member VALUES($r,'G1','0.png','Legacy'),($r,'storage:SG1','0.png','Saved group'),($r,'storage:SG3','2.png','Second scope');";command.Parameters.AddWithValue("$r",root);command.ExecuteNonQuery();}
            var beforeIndex=File.ReadAllBytes(index);var beforeGroups=File.ReadAllBytes(groups);var approved=new GameCatalogue(1,new string('A',64),DateTime.UtcNow,[new("steam","10","Combined","Steam",null,null,new()),new("steam","11","Other","Steam",null,null,new())]);
            var exported=ScreenshotSnapshotExporter.ExportLibrary(source,approved.WorkspaceId,approved,output);Assert.Equal(2,exported.Count);Assert.Equal(4,exported.Sum(c=>c.Photos.Count));var one=exported.Single(c=>c.GameId=="10");Assert.Equal("Combined",one.Title);Assert.Equal(3,one.Eligible);Assert.Contains(one.Photos,p=>p.Console=="Xbox"&&p.CaptureDay is null);Assert.Contains(one.Photos,p=>p.Group=="Saved group");Assert.Contains(one.Photos,p=>p.Group=="Second scope");
            var hdrPhoto=exported.Single(c=>c.GameId=="11").Photos.Single();Assert.Equal("image/vnd.ms-photo",hdrPhoto.Original!.ContentType);
            var hdrDirectory=Directory.GetFiles(output,"catalogue.json",SearchOption.AllDirectories).Single(f=>File.ReadAllText(f).Contains("\"gameId\":\"11\""));using(var preview=new Bitmap(Path.Combine(Path.GetDirectoryName(hdrDirectory)!,"assets",hdrPhoto.Preview))){Assert.InRange(preview.GetPixel(32,16).R,60,220);Assert.True(preview.GetPixel(55,16).R>preview.GetPixel(32,16).R+10);}
            var sidecar=one.Photos.Single(p=>p.Metadata is not null).Metadata!;Assert.Equal("0.png.xmp",sidecar.FileName);Assert.Equal("application/rdf+xml",sidecar.ContentType);Assert.Equal(beforeXmp.Length,sidecar.Bytes);Assert.All(one.Photos,p=>Assert.NotNull(p.Original));
            var repeated=ScreenshotSnapshotExporter.ExportLibrary(source,approved.WorkspaceId,approved,output);Assert.Equal(one.Photos,repeated.Single(c=>c.GameId=="10").Photos);Assert.Equal(beforeIndex,File.ReadAllBytes(index));Assert.Equal(beforeGroups,File.ReadAllBytes(groups));Assert.Equal(beforeXmp,File.ReadAllBytes(xmp));
            foreach(var file in Directory.EnumerateFiles(output,"catalogue.json",SearchOption.AllDirectories)){var publicText=File.ReadAllText(file);Assert.DoesNotContain(root.Replace("\\","\\\\"),publicText);Assert.DoesNotContain("private-secret",publicText);Assert.DoesNotContain("Keep source tags",publicText);}
        }
        finally{Directory.Delete(testRoot,true);}
    }
}
