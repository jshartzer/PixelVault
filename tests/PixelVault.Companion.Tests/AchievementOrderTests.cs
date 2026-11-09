using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion.Local;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectLocalOriginalOrderUsesRefreshedPositionsAndPreservesGuides(bool refreshed)
    {
        using var fixture = new DirectCase();
        using (var connection = new SqliteConnection("Data Source=" + fixture.Guide + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO achievement_definition VALUES(2,'steam','42','TWO','Zulu','Description',1,0,'','Story','',0,1)";
            command.ExecuteNonQuery();
        }
        var rows = new[]
        {
            new { Provider="steam", ProviderGameId="42", ProviderAchievementId="ONE", SortKey=refreshed?1:0, ProgressKnown=true, Unlocked=false },
            new { Provider="steam", ProviderGameId="42", ProviderAchievementId="TWO", SortKey=0, ProgressKnown=true, Unlocked=true }
        };
        File.WriteAllText(Path.Combine(fixture.App,"achievement-hub","account.json"), JsonSerializer.Serialize(new
        {
            Provider="steam", Account=Approved, Games=new Dictionary<string,object> { ["42"]=new { UpdatedUtc=1, Rows=rows } }
        }));
        var game = Assert.Single(LocalAchievementReader.Read(fixture.App,"test",new() { ["steam"]=Approved }));
        Assert.Equal(refreshed ? new[] { "TWO", "ONE" } : new[] { "ONE", "TWO" }, game.Rows.Select(r=>r.Id));
        Assert.Equal("Keep this guide",game.Rows.Single(r=>r.Id=="ONE").Guide);
        Assert.True(game.Rows.Single(r=>r.Id=="TWO").Unlocked);
    }
}
