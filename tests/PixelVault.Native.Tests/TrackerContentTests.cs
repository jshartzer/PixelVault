using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class TrackerContentTests
{
    static AchievementTrackerDefinition Definition(string? content = null) => new() { Id = "content.fixture", Provider = "steam", GameId = "42", GameTitle = "Fixture", Title = "Lore", Items = new() { new() { Id = "a", Name = "A record", Content = content, Detail = "Author · Date · Place" } } };

    [Fact] public void AbsentContentDoesNotChangeExistingDefinitionSerialization()
    {
        var definition = Definition();
        Assert.DoesNotContain("\"Content\"", JsonSerializer.Serialize(definition, AchievementTrackingCatalog.Json));
        var old = "{\"Id\":\"a\",\"Name\":\"Old record\"}";
        Assert.Null(JsonSerializer.Deserialize<AchievementTrackerItem>(old)!.Content);
    }

    [Fact] public void ContentSurvivesPortableDefinitionAndDurableStorageWithoutCreatingProgress()
    {
        var root = Path.Combine(Path.GetTempPath(), "PV-Content-" + Guid.NewGuid().ToString("N"));
        try
        {
            var d = Definition("First line\n<color name=yellow>Literal text</color>\nUnicode: café");
            var imported = TrackerDefinitionTransfer.Parse(TrackerDefinitionTransfer.Serialize(d));
            Assert.Equal(d.Items[0].Content, imported.Items[0].Content);
            var store = new TrackerRepository(root, pooling: false);store.AddDefinition(imported);
            Assert.Equal(d.Items[0].Content, new TrackerRepository(root, false).Definitions().Single(x => x.Id == d.Id).Items[0].Content);
            var state = Assert.Single(store.Load(d.Id, "player"));Assert.Null(state.Current);Assert.False(state.IsKnown);Assert.Empty(state.Note);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact] public void OversizedContentIsRejectedAtBothImportAndRepositoryBoundaries()
    {
        Assert.Throws<InvalidDataException>(() => AchievementTrackingCatalog.Validate(Definition(new string('a', 100001))));
        var many = Definition();many.Items = Enumerable.Range(0, 21).Select(i => new AchievementTrackerItem { Id = i.ToString(), Name = "Record", Content = new string('a', 100000) }).ToList();
        Assert.Throws<InvalidDataException>(() => AchievementTrackingCatalog.Validate(many));
        Assert.Throws<InvalidDataException>(() => TrackerDefinitionTransfer.Serialize(many));
        AchievementTrackingCatalog.Validate(Definition(new string('a', 100000)));
    }

    [Fact] public void ViewerShowsLiteralSelectableTextWithoutWritingProgress() => OnSta(() =>
    {
        var item = Definition("<script>literal reference text</script>\nA second line").Items[0];
        var window = new TrackerContentWindow(null!, item);
        try
        {
            var editor = Assert.Single(Tree((DependencyObject)window.Content).OfType<TextBox>());
            Assert.True(editor.IsReadOnly);Assert.Equal(item.Content, editor.Text);Assert.Equal(TextWrapping.Wrap, editor.TextWrapping);
            Assert.Contains(Tree((DependencyObject)window.Content).OfType<Button>(), b => Equals(b.Content, "Save copy…"));
            Assert.Equal("<script>literal reference text</script>\nA second line", item.Content);
        }
        finally { window.Close(); }
    });

    static IEnumerable<DependencyObject> Tree(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) foreach (var descendant in Tree(child)) yield return descendant;
    }
}
