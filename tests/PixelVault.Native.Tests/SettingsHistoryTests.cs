using System.Text;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class SettingsHistoryTests
{
    [Fact]
    public void RecentHistory_LargeLogIsBoundedAndKeepsNewestUnicodeLines()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                for (var i = 0; i < 250000; i++) writer.WriteLine("Older capture import: " + new string('x', 64));
                writer.WriteLine("Latest capture: 日本語 🎮 café");
            }
            var size = new FileInfo(path).Length;
            var text = TroubleshootingLog.ReadRecentHistory(path);
            Assert.True(text.Length < 66000);
            Assert.Contains("Recent history excerpt", text);
            Assert.EndsWith("Latest capture: 日本語 🎮 café" + Environment.NewLine, text);
            Assert.DoesNotContain("\uFFFD", text);
            Assert.Equal(size, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RecentHistory_SmallFilePreservesContentAndMissingFileIsEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "First\r\nSecond café\r\n", new UTF8Encoding(true));
            Assert.Equal("First\r\nSecond café\r\n", TroubleshootingLog.ReadRecentHistory(path));
            File.Delete(path);
            Assert.Equal(string.Empty, TroubleshootingLog.ReadRecentHistory(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RecentHistory_OversizedSingleLineIsStillBoundedAndUnicodeSafe()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, string.Concat(Enumerable.Repeat("🎮", 20000)), new UTF8Encoding(false));
            var text = TroubleshootingLog.ReadRecentHistory(path, 1001);
            Assert.True(text.Length < 1100);
            Assert.DoesNotContain("\uFFFD", text);
            Assert.EndsWith("🎮", text);
        }
        finally { File.Delete(path); }
    }
}
