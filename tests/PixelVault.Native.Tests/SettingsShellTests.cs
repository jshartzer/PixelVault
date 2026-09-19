using System.Diagnostics;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVaultNative;
using Xunit;
using Xunit.Abstractions;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class SettingsShellTests(ITestOutputHelper output)
{
    [Fact]
    public void SettingsOpensWithoutReadingHistoryAndPreservesDraftsAcrossTabs()
    {
        OnSta(() =>
        {
            var deps = Dependencies();
            var historyReads = 0;
            var saves = 0;
            string? savedDestination = null;
            deps.LogFilePath = () => { historyReads++; return "missing.log"; };
            deps.LoadLogView = () => throw new Exception("Opening settings must not read the full log.");
            deps.SaveSettings = () => saves++;
            deps.SetDestinationRoot = value => savedDestination = value;
            var window = new Window();
            try
            {
                var timer = Stopwatch.StartNew();
                var root = Build(deps, window);
                Layout(root, 1152, 750);
                output.WriteLine($"Settings construction and first layout: {timer.ElapsedMilliseconds} ms");
                Assert.Equal(0, historyReads);
                var tabs = root.Children.OfType<TabControl>().Single();
                Assert.Equal(Dock.Top, tabs.TabStripPlacement);
                Assert.Equal(new[] { "Folders & tools", "Accounts", "Import & tray", "Library", "Diagnostics", "Run history" }, tabs.Items.Cast<TabItem>().Select(t => (string)t.Header));
                Assert.Null(((TabItem)tabs.Items[5]).Content);
                var folderGrid = PageGrid((TabItem)tabs.Items[0]);
                var destination = folderGrid.Children.OfType<TextBox>().Single(b => Grid.GetRow(b) == 1);
                destination.Text = @"C:\Draft destination";
                tabs.SelectedIndex = 1;
                tabs.SelectedIndex = 2;
                tabs.SelectedIndex = 3;
                Assert.Equal(0, historyReads);
                tabs.SelectedIndex = 4;
                tabs.SelectedIndex = 0;
                Assert.Equal(@"C:\Draft destination", destination.Text);
                Assert.Equal(0, saves);
                Assert.Equal(1, historyReads); // Diagnostics displays the path; it does not read its contents.
                var save = root.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().First();
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, saves);
                Assert.Equal(destination.Text, savedDestination);
                Assert.Equal("Saved", save.Content);

                // Real WPF templates/layout, including the minimum supported window size.
                foreach (var width in new[] { 1152d, 852d })
                {
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        Layout(root, width, width < 900 ? 460 : 750);
                        Assert.True(tabs.ActualWidth > 0);
                        SavePreview(root, $"settings-{width}-{i}.png");
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    static Grid Build(SettingsShellDependencies deps, Window window) =>
        (Grid)typeof(SettingsShellHost).GetMethod("BuildUi", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new SettingsShellHost(deps), new object[] { window })!;

    static Grid PageGrid(TabItem tab) => ((StackPanel)((Border)((ScrollViewer)tab.Content).Content).Child).Children.OfType<Grid>().Single();

    static void Layout(Grid root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    static void SavePreview(Grid root, string name)
    {
        var dir = Environment.GetEnvironmentVariable("PIXELVAULT_SETTINGS_PREVIEW");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth + root.Margin.Left + root.Margin.Right), (int)(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, name));
        encoder.Save(file);
    }

    static SettingsShellDependencies Dependencies()
    {
        var deps = new SettingsShellDependencies { AppVersion = "Test", LogsRoot = @"C:\Logs" };
        foreach (var property in typeof(SettingsShellDependencies).GetProperties().Where(p => typeof(Delegate).IsAssignableFrom(p.PropertyType)))
        {
            var invoke = property.PropertyType.GetMethod("Invoke")!;
            var args = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
            Expression body = invoke.ReturnType == typeof(string) ? Expression.Constant(string.Empty) : Expression.Default(invoke.ReturnType);
            property.SetValue(deps, Expression.Lambda(property.PropertyType, body, args).Compile());
        }
        deps.Brush = color => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        deps.Btn = LibraryButtonChrome.Btn;
        deps.SourceRootsEditorText = () => @"C:\Captures";
        deps.GetDestinationRoot = () => @"C:\Processed captures";
        deps.GetLibraryRoot = () => @"C:\Library";
        return deps;
    }
}
