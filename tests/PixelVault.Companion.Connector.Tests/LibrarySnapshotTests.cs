using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVault.Companion.Export;
using Xunit;

public sealed class LibrarySnapshotTests
{
    [Fact]
    public async Task IndexedGamesAndCaptureDatesRefreshWithoutCopyingPrivateSettingsOrWritingTheIndex()
    {
        var root=Path.Combine(Path.GetTempPath(),"PixelVaultLibraryTests",Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"desktop");var output=Path.Combine(root,"export");
        Directory.CreateDirectory(Path.Combine(source,"cache"));
        try
        {
            File.WriteAllText(Path.Combine(source,"PixelVault.settings.ini"),"library=QA\nsteam_web_api_key=private-test-key\n");
            var index=Path.Combine(source,"cache","pixelvault-index-qa.sqlite");
            SQLitePCL.Batteries_V2.Init();
            using(var c=new SqliteConnection($"Data Source={index};Pooling=False"))
            {c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE photo_index(root TEXT,file_path TEXT,capture_utc_ticks INTEGER); INSERT INTO photo_index VALUES('QA','capture.png',638900000000000000)";cmd.ExecuteNonQuery();}
            var original=File.ReadAllBytes(index);
            var inventory=Path.Combine(source,"cache","library-folders-qa.cache");
            string Row(string id,string title){var fields=Enumerable.Repeat("",16).ToArray();fields[0]=id;fields[1]="private-folder";fields[2]=title;fields[3]="1";fields[5]="Steam";fields[6]="capture.png";fields[7]=id;fields[15]="0";return string.Join('\t',fields);}
            File.WriteAllText(inventory,Row("10","First game"));
            var manifest=new WorkspaceManifest(1,new(){["steam"]="test-account"},DateTime.MinValue);
            var first=await LibrarySnapshotExporter.ExportAsync(source,manifest,output,false);
            Assert.Single(first.Games);Assert.Equal(638900000000000000,first.Games[0].Stats.LastScreenshotUtc);
            File.AppendAllText(inventory,"\n"+Row("20","Newly indexed game"));
            var next=await LibrarySnapshotExporter.ExportAsync(source,manifest,output,false,true);
            Assert.Equal(2,next.Games.Count);Assert.Equal(first.WorkspaceId,next.WorkspaceId);
            var payload=File.ReadAllText(Path.Combine(output,"catalog.json"));
            Assert.DoesNotContain("private-test-key",payload);Assert.DoesNotContain("private-folder",payload);Assert.DoesNotContain("test-account",payload);
            Assert.Equal(original,File.ReadAllBytes(index));
        }
        finally{Directory.Delete(root,true);}
    }
}
