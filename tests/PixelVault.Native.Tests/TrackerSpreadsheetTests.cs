using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerSpreadsheetTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-Spreadsheet-" + Guid.NewGuid().ToString("N"));
    public TrackerSpreadsheetTests() => Directory.CreateDirectory(dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    static GameAchievementsFetchService.AchievementRow Achievement(string game = "1850570", string id = "062") => new()
    { Provider = "steam", ProviderGameId = game, ProviderAchievementId = id, Title = "Achievement " + id };

    [Fact]
    public void LinkCanBeChangedAndRemovedWithoutLosingAnyProgress()
    {
        var store = new AchievementTrackingStore(dir); var id = AchievementTrackingCatalog.DeathStrandingId;
        var item = store.Definitions().Single(d => d.Id == id).Items[0].Id;
        store.SetManualProgress(id, "account", item, 1); store.SaveNote(id, "account", item, "Keep this");
        store.SetAchievementLink(id, Achievement(id: "063"));
        Assert.Equal("063", new AchievementTrackingStore(dir).Definitions().Single(d => d.Id == id).AchievementId);
        store.SetAchievementLink(id, null!);
        Assert.Null(new AchievementTrackingStore(dir).Definitions().Single(d => d.Id == id).AchievementId);
        var state = store.Load(id, "account").First(); Assert.True(state.Complete); Assert.Equal("Keep this", state.Note);
        Assert.Throws<InvalidDataException>(() => store.SetAchievementLink(id, Achievement("another-game")));
        Assert.Null(store.Definitions().Single(d => d.Id == id).AchievementId);
    }

    [Fact]
    public void CsvHandlesQuotedCommasNewlinesAndEscapedQuotesAndMapsArbitraryHeaders()
    {
        var path = Path.Combine(dir, "items.csv");
        File.WriteAllText(path, "Thing,Family,Needed,Guide,Key\r\n\"Name, one\",Tools,3,\"Line 1\nLine \"\"2\"\"\",one\r\nOther,,1,,two\r\n");
        var sheet = TrackerSpreadsheetImport.Read(path).Single();
        var items = TrackerSpreadsheetImport.Items(sheet, 0, 1, 2, 3, 4);
        Assert.Equal(2, items.Count); Assert.Equal("Name, one", items[0].Name); Assert.Equal(3, items[0].Target);
        Assert.Equal("Line 1\nLine \"2\"", items[0].Detail); Assert.Equal("one", items[0].Id); Assert.Equal("Checklist", items[1].Category);
        var store = new AchievementTrackingStore(dir);
        store.AddDefinition(new AchievementTrackerDefinition { Id = "custom.import", Provider = "steam", GameId = "42", GameTitle = "A game", Title = "Imported", Items = items });
        Assert.All(store.Load("custom.import", "account"), s => Assert.Null(s.Current));
    }

    [Theory]
    [InlineData("Name,Target\nItem,-1", 1)]
    [InlineData("Name,Target\nItem,1.5", 1)]
    [InlineData("Name,Target\n,2", 1)]
    [InlineData("Name,Target\nItem,1000001", 1)]
    [InlineData("Name,ID\nItem,x\nOther,x", -1)]
    public void InvalidRowsAreRejectedBeforeCreatingAnything(string csv, int target)
    {
        var sheet = new TrackerImportSheet { Rows = TrackerSpreadsheetImport.Csv(csv) };
        Assert.Throws<InvalidDataException>(() => TrackerSpreadsheetImport.Items(sheet, 0, targetColumn: target, idColumn: target < 0 ? 1 : -1));
        Assert.False(File.Exists(Path.Combine(dir, "achievement-tracking", "pixelvault-tracking.sqlite")));
    }

    [Theory]
    [InlineData("Name\n\"Unfinished")]
    [InlineData("Name\n\"Quoted\"trailing")]
    [InlineData("Name\nBa\"d")]
    public void MalformedCsvIsRejected(string csv) => Assert.Throws<InvalidDataException>(() => TrackerSpreadsheetImport.Csv(csv));

    string Workbook(bool formula = false)
    {
        var path = Path.Combine(dir, "test.xlsx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Part(string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Part("xl/workbook.xml", $"<workbook xmlns='{ns}' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Equipment' r:id='r1'/><sheet name='Collectibles' r:id='r2'/></sheets></workbook>");
        Part("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Target='worksheets/sheet1.xml'/><Relationship Id='r2' Target='/xl/worksheets/sheet2.xml'/></Relationships>");
        Part("xl/sharedStrings.xml", $"<sst xmlns='{ns}'><si><t>Name</t></si><si><r><t>Power </t></r><r><t>Skeleton</t></r></si></sst>");
        Part("xl/worksheets/sheet1.xml", $"<worksheet xmlns='{ns}'><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='C1' t='inlineStr'><is><t>Target</t></is></c></row><row r='2'><c r='A2' t='s'><v>1</v></c><c r='C2'>{(formula ? "<f>1+2</f>" : "")}<v>3</v></c></row></sheetData></worksheet>");
        Part("xl/worksheets/sheet2.xml", $"<worksheet xmlns='{ns}'><sheetData><row><c r='A1' t='inlineStr'><is><t>Item</t></is></c></row><row><c r='A2' t='inlineStr'><is><t>Chip 1</t></is></c></row></sheetData></worksheet>");
        return path;
    }

    [Fact]
    public void XlsxReadsWorksheetsRichSharedStringsInlineStringsAndSparseCells()
    {
        var sheets = TrackerSpreadsheetImport.Read(Workbook()); Assert.Equal(2, sheets.Count);
        var item = Assert.Single(TrackerSpreadsheetImport.Items(sheets[0], 0, targetColumn: 2));
        Assert.Equal("Power Skeleton", item.Name); Assert.Equal(3, item.Target);
        Assert.Equal("Chip 1", TrackerSpreadsheetImport.Items(sheets[1], 0).Single().Name);
    }

    [Fact]
    public void FormulaCellsMustBeReplacedWithValuesEvenWhenCached()
    {
        var sheet = TrackerSpreadsheetImport.Read(Workbook(true))[0];
        Assert.Throws<InvalidDataException>(() => TrackerSpreadsheetImport.Items(sheet, 0, targetColumn: 2));
        Assert.Single(TrackerSpreadsheetImport.Items(sheet, 0)); // Unmapped formulas are not used.
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [Fact]
    public void UniversalCreationPrefillsAchievementClearsOnGameChangeAndOffersGameWideOption()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var games = new[] { new AchievementHubGame { Provider = "steam", Id = "1850570", Title = "Death Stranding" }, new AchievementHubGame { Provider = "steam", Id = "42", Title = "Other" } };
            var selected = AchievementTrackingCatalog.DeathStranding();
            var dialog = new AchievementTrackerEditWindow(null!, games, selected, "062", new[] { Achievement(), Achievement("42", "other") }, spreadsheet: true);
            try
            {
                var root = (FrameworkElement)dialog.Content; root.Measure(new Size(620, 680)); root.Arrange(new Rect(0, 0, 620, 680)); root.UpdateLayout();
                var picker = Descendants(root).OfType<TrackerAchievementPicker>().Single(); Assert.Equal("062", picker.Achievement.ProviderAchievementId);
                var game = Descendants(root).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Checklist game");
                game.SelectedIndex = 1; Assert.Null(picker.Achievement);
                Assert.DoesNotContain(picker.Items.OfType<ComboBoxItem>(), i => (i.Tag as GameAchievementsFetchService.AchievementRow)?.ProviderAchievementId == "062");
                Assert.Contains(Descendants(root).OfType<DataGrid>(), g => AutomationProperties.GetName(g) == "Spreadsheet checklist preview");
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void SpreadsheetFormLoadsMapsAndPreviewsBeforeCreation()
    {
        var workbook = Workbook();
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var games = new[] { new AchievementHubGame { Provider = "steam", Id = "1850570", Title = "Death Stranding Director's Cut" } };
            var dialog = new AchievementTrackerEditWindow(null!, games, AchievementTrackingCatalog.DeathStranding(), "062", new[] { Achievement() }, spreadsheet: true);
            try
            {
                var root = (FrameworkElement)dialog.Content;
                root.Measure(new Size(632, 690)); root.Arrange(new Rect(0, 0, 632, 690)); root.UpdateLayout();
                var form = Descendants(root).OfType<TrackerSpreadsheetForm>().Single();
                var task = form.LoadFileAsync(workbook); var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => dialog.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                if (!task.IsCompleted) Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
                Assert.Equal(3, form.BuildItems().Single().Target);
                Assert.Single(Descendants(root).OfType<DataGrid>().Single().Items);
                var output = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(632, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, "spreadsheet-import.png")); encoder.Save(file);
                }
            }
            finally { dialog.Close(); }
        });
    }
}
