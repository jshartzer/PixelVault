using System.Buffers.Binary;
using System.IO;
using PixelVaultNative;
using Xunit;
namespace PixelVaultNative.Tests;
public sealed class DeathStrandingMailReaderTests
{
 static byte[] Payload(ulong mail){var refs=DeathStrandingInterviewReader.References().ToArray();var b=new byte[10+refs.Length*13];Convert.FromHexString("879ca01f131c7ece01").CopyTo(b,0);b[9]=(byte)refs.Length;for(int i=0;i<refs.Length;i++){var r=b.AsSpan(10+i*13,13);BinaryPrimitives.WriteUInt32LittleEndian(r,refs[i].Key);r[4]=refs[i].Value;BinaryPrimitives.WriteUInt64LittleEndian(r[5..],refs[i].Key==38?mail:refs[i].Key==39?70UL:0UL);}return b;}
 [Fact] public void MailAndInterviewCountersRemainIndependent(){var b=Payload(139);Assert.Equal(139,DeathStrandingMailReader.Read(b,"account",1,"test","").Items.Single().Current);Assert.Equal(70,DeathStrandingInterviewReader.ReadCount(b));Assert.Equal("038",DeathStrandingMailReader.Definition().AchievementId);}
 [Fact] public void UnsupportedAndAmbiguousTablesAreRejected(){Assert.Throws<NotSupportedException>(()=>DeathStrandingMailReader.Read(Payload(241),"account",1,"test",""));var b=Payload(139);Assert.Throws<NotSupportedException>(()=>DeathStrandingMailReader.Read(b.Concat(b).ToArray(),"account",1,"test",""));var index=Array.FindIndex(DeathStrandingInterviewReader.References().ToArray(),r=>r.Key==38);b[10+index*13+4]=1;Assert.Throws<NotSupportedException>(()=>DeathStrandingMailReader.Read(b,"account",1,"test",""));}
 [Fact] public async Task PackagedWorkerRefreshesTheControlledMailPairAndPreservesNotes(){
 var worker=Environment.GetEnvironmentVariable("PIXELVAULT_DS_MAIL_WORKER");if(string.IsNullOrWhiteSpace(worker))return;
 var evidence=Environment.GetEnvironmentVariable("PIXELVAULT_DS_MAIL_REVIEW")!;var dir=Path.Combine(Path.GetTempPath(),"pv-mail-control-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
 try{const string account="76561197997629753";var saves=Path.Combine(dir,"saves");Directory.CreateDirectory(Path.Combine(saves,"manualsave1"));var checkpoint=Path.Combine(saves,"manualsave1","checkpoint.dat");File.Copy(Path.Combine(evidence,"manualsave11","input.dat"),checkpoint);File.SetLastWriteTimeUtc(checkpoint,DateTime.UtcNow.AddMinutes(-2));
 var store=new AchievementTrackingStore(dir);var d=DeathStrandingMailReader.Definition();store.AddDefinition(d);var source=new DeathStrandingTrackerSource(Path.Combine(dir,"achievement-tracking"),worker);var service=new TrackerRefreshService(store,new[]{source});service.Configure(d.Id,account,source.Id,"1",saves,gameDirectory:@"E:\SteamLibrary\steamapps\common\DEATH STRANDING DIRECTORS CUT");service.Apply(await service.PrepareAsync(d.Id,account,default),false,null!);Assert.Equal(138,store.Load(d.Id,account).Single().Current);store.SaveNote(d.Id,account,d.Items.Single().Id,"Keep mail note");store.SaveAutomation(d.Id,account,store.Source(d.Id,account).Id,TrackerAutomationMode.SaveChanges,5,false);
 var now=DateTime.UtcNow;using var monitor=new TrackerAutomationMonitor(store,service,()=>now);monitor.SetAccounts(account,"");async Task Tick(){now=now.AddSeconds(5);await monitor.TickAsync();now=now.AddSeconds(5);await monitor.TickAsync();}await Tick();File.Copy(Path.Combine(evidence,"manualsave12","input.dat"),checkpoint,true);File.SetLastWriteTimeUtc(checkpoint,DateTime.UtcNow.AddMinutes(-1));await Tick();Assert.Equal(139,store.Load(d.Id,account).Single().Current);Assert.Equal("Keep mail note",store.Load(d.Id,account).Single().Note);Assert.Equal(0,store.Automation(d.Id,account).State.Failures);Assert.False(store.Automation(d.Id,account).State.ReviewPending);
 }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}
