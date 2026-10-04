using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementCounterTests
{
    static Dictionary<string, object> Node(params (string Key, object Value)[] fields) => fields.ToDictionary(x => x.Key, x => x.Value);
    static byte[] Schema(object minimum, object target, string operation = "statvalue", bool duplicate = false)
    {
        var achievement = Node(("name", "ACH_COUNTER"), ("progress", Node(("min_val", minimum), ("max_val", target),
            ("value", Node(("operation", operation), ("operand1", "STAT_COUNTER"))))));
        var bits = Node(("0", achievement));
        if (duplicate) bits.Add("1", achievement);
        var root = Node(("123", Node(("stats", Node(("1", Node(("bits", bits))))))));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        void String(string s) { writer.Write(Encoding.UTF8.GetBytes(s)); writer.Write((byte)0); }
        void Write(Dictionary<string, object> node)
        {
            foreach (var (name, value) in node)
            {
                writer.Write((byte)(value is Dictionary<string, object> ? 0 : value is int ? 2 : value is float ? 3 : 1));
                String(name);
                if (value is Dictionary<string, object> child) Write(child);
                else if (value is int n) writer.Write(n);
                else if (value is float f) writer.Write(f);
                else String((string)value);
            }
            writer.Write((byte)8);
        }
        Write(root);
        return stream.ToArray();
    }

    [Fact]
    public void SteamDefinitionMappings_ReadStringIntegerAndFloatBoundsAndRequireMatchingGame()
    {
        foreach (var pair in new (object, object)[] { ("0", "100"), (0, 100), (0f, 100f) })
        {
            var bytes = Schema(pair.Item1, pair.Item2);
            var mapping = Assert.Single(SteamAchievementCounters.ParseMappings(bytes, 123)).Value;
            Assert.Equal("STAT_COUNTER", mapping.Stat);
            Assert.Equal(0, mapping.Minimum); Assert.Equal(100, mapping.Target);
            Assert.Empty(SteamAchievementCounters.ParseMappings(bytes, 124));
        }
    }

    [Fact]
    public void SteamDefinitions_RejectUnsupportedExpressionsAmbiguityAndMalformedFiles()
    {
        Assert.Empty(SteamAchievementCounters.ParseMappings(Schema(0, 100, "add"), 123));
        Assert.Empty(SteamAchievementCounters.ParseMappings(Schema(0, 100, duplicate: true), 123));
        foreach (var pair in new (object, object)[] { (0, 0), (10, 5), (-1, 100), ("NaN", "100"), (0, "Infinity") })
            Assert.Empty(SteamAchievementCounters.ParseMappings(Schema(pair.Item1, pair.Item2), 123));
        var data = Schema(0, 100);
        for (var i = 0; i < data.Length; i++)
            Assert.Empty(SteamAchievementCounters.ParseMappings(data.Take(i).ToArray(), 123));
        Assert.Empty(SteamAchievementCounters.ParseMappings(new byte[] { 99, 0 }, 123));
        Assert.Empty(SteamAchievementCounters.ParseMappings(new byte[8 * 1024 * 1024 + 1], 123));
    }

    [Fact]
    public void UserStats_AreAccountScopedAndNeverDefaultMissingInvalidOrDuplicateValuesToZero()
    {
        const string json = """
            {"playerstats":{"steamID":"123456","stats":[
                {"name":"zero","value":0},{"name":"fraction","value":2.5},
                {"name":"negative","value":-1},{"name":"string","value":"25"},
                {"name":"missing"},{"name":"duplicate","value":2},{"name":"duplicate","value":3}]}}
            """;
        var stats = SteamAchievementCounters.ParseUserStats(json, 123456);
        Assert.Equal(2, stats.Count); Assert.Equal(0, stats["zero"]); Assert.Equal(2.5, stats["fraction"]);
        Assert.Empty(SteamAchievementCounters.ParseUserStats(json, 987654));
        Assert.Empty(SteamAchievementCounters.ParseUserStats("{\"playerstats\":{\"steamID\":\"123456\",\"success\":false}}", 123456));
        Assert.Empty(SteamAchievementCounters.ParseUserStats("{}", 123456));
    }

    [Theory]
    [InlineData(0, 0, 100, 0)]
    [InlineData(37, 0, 100, 37)]
    [InlineData(50, 10, 90, 50)]
    [InlineData(5, 10, 90, 0)]
    [InlineData(150, 0, 100, 100)]
    [InlineData(1.5, 0, 3, 50)]
    public void Percent_UsesDefinedRangeAndClampsOnlyTheBar(double current, double min, double target, double expected)
    {
        var row = Counter("A", current, target); row.CounterMinimum = min;
        Assert.Equal(expected, AchievementCollectionStats.CounterPercent(row));
        Assert.Equal(current, row.CounterCurrent);
        Assert.False(row.Unlocked);
    }

    [Fact]
    public void UnknownAndInvalidCounters_AreUnavailable()
    {
        var row = Counter("A", 1, 100);
        row.ProgressKnown = false; Assert.Null(AchievementCollectionStats.CounterPercent(row));
        row.ProgressKnown = true;
        foreach (var value in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            row.CounterCurrent = value;
            Assert.Null(AchievementCollectionStats.CounterPercent(row));
        }
        row.CounterCurrent = 1; row.CounterTarget = 0;
        Assert.Null(AchievementCollectionStats.CounterPercent(row));
        row.CounterTarget = null;
        Assert.Null(AchievementCollectionStats.CounterPercent(row));
    }

    [Fact]
    public void ApplyingSnapshots_ClearsStaleCountersAndDoesNotChangeUnlockState()
    {
        var row = Counter("A", 20, 100); row.Provider = "steam"; row.SteamApiName = "ACH_COUNTER";
        var mappings = SteamAchievementCounters.ParseMappings(Schema(0, 100), 123);
        SteamAchievementCounters.Apply(new[] { row }, mappings, new Dictionary<string, double> { ["STAT_COUNTER"] = 120 });
        Assert.Equal(120, row.CounterCurrent); Assert.False(row.Unlocked);
        SteamAchievementCounters.Apply(new[] { row }, mappings, new Dictionary<string, double>());
        Assert.Null(row.CounterCurrent); Assert.Null(row.CounterTarget);
        row.ProgressKnown = false;
        SteamAchievementCounters.Apply(new[] { row }, mappings, new Dictionary<string, double> { ["STAT_COUNTER"] = 120 });
        Assert.Null(row.CounterCurrent);
    }

    [Fact]
    public void SortingUsesPercentageNotRawCountAndKeepsMissingLastInBothDirections()
    {
        var rows = new[] { Counter("A", 5, 10), Counter("B", 40, 100), Row("C", true, true), Counter("D", 50, 100), Counter("E", 0, 100) };
        Assert.Equal("ADBEC", string.Concat(AchievementCollectionStats.Sort(rows, AchievementCollectionSort.ProgressHighestFirst, 0).Select(r => r.Title)));
        Assert.Equal("EBADC", string.Concat(AchievementCollectionStats.Sort(rows, AchievementCollectionSort.ProgressLowestFirst, 0).Select(r => r.Title)));
        Assert.Equal("ABCDE", string.Concat(rows.Select(r => r.Title)));
    }

    [Fact]
    public void BarsAppearInListAndOnlyInProgressSortedGridAndDisappearWhenDataIsMissing()
    {
        OnSta(() =>
        {
            var window = CreateWindow(() => Task.FromResult(Result()));
            try
            {
                var row = Counter("Counter", 37, 100);
                Border Build(string method) => (Border)typeof(AchievementsInfoWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { row, "PixelVault.Tests" })!;
                Assert.Single(Descendants(Build("BuildAchievementRowCard")).OfType<ProgressBar>());
                Assert.Empty(Descendants(Build("BuildAchievementGridCard")).OfType<ProgressBar>());
                foreach (var sort in new[] { AchievementCollectionSort.ProgressHighestFirst, AchievementCollectionSort.ProgressLowestFirst })
                {
                    Field<ComboBox>(window, "_sort").SelectedIndex = (int)sort;
                    Assert.Equal(37, Assert.Single(Descendants(Build("BuildAchievementGridCard")).OfType<ProgressBar>()).Value);
                }
                Field<ComboBox>(window, "_sort").SelectedIndex = (int)AchievementCollectionSort.RarestFirst;
                Assert.Empty(Descendants(Build("BuildAchievementGridCard")).OfType<ProgressBar>());
                row.CounterCurrent = null;
                Assert.Empty(Descendants(Build("BuildAchievementRowCard")).OfType<ProgressBar>());
            }
            finally { window.Close(); }
        });
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    static GameAchievementsFetchService.AchievementRow Counter(string name, double current, double target)
    {
        var row = Row(name, true, false);
        row.CounterCurrent = current; row.CounterMinimum = 0; row.CounterTarget = target;
        return row;
    }
}
