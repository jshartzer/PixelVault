using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementGuideReadingTests
{
    [Fact]
    public void ReadTags_PreservesAuthoredLabelsAndDoesNotRepeatMissable()
    {
        Assert.Equal(new[] { "Online", "MISSABLE", "collectible" },
            AchievementGuideWindow.GuideReadTags("Online, MISSABLE, online, collectible", true));
        Assert.Equal(new[] { "Grind", "Missable" }, AchievementGuideWindow.GuideReadTags("Grind", true));
        Assert.Empty(AchievementGuideWindow.GuideReadTags(null!, false));
    }

    [Fact]
    public void ReadEditCancelSave_PreservesAuthorshipAndPersistsOnlySavedChanges()
    {
        WithGuide((window, service, rows) =>
        {
            const string notes = "First paragraph.\n\nSecond paragraph with a useful route.";
            Assert.Equal(notes, Field<TextBlock>(window, "_readGuide").Text);
            Assert.Equal(Visibility.Collapsed, Field<StackPanel>(window, "_editorFields").Visibility);
            Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "_reader").Visibility);
            Assert.Equal("Fixture source", ((TextBlock)Field<Button>(window, "_readSource").Content).Text);
            Click(window, "_edit");
            Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "_editorFields").Visibility);
            Field<TextBox>(window, "_guideText").Text = "Discard this draft.";
            Field<TextBox>(window, "_sourceTitle").Text = "Discard title";
            Click(window, "_cancelEdit");
            Assert.Equal(notes, Field<TextBlock>(window, "_readGuide").Text);
            Assert.Equal("Fixture source", Field<TextBox>(window, "_sourceTitle").Text);
            Assert.Equal(notes, service.SyncDefinitionsAndLoadGuides("fixture", rows)[0].GuideText);

            Click(window, "_edit");
            Field<TextBox>(window, "_guideText").Text = "Saved replacement.";
            Field<TextBox>(window, "_tags").Text = "online, grind";
            Click(window, "_save");
            Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "_reader").Visibility);
            Assert.Equal("Saved replacement.", Field<TextBlock>(window, "_readGuide").Text);
            var saved = service.SyncDefinitionsAndLoadGuides("fixture", rows)[0];
            Assert.Equal("Saved replacement.", saved.GuideText);
            Assert.Equal("Fixture source", saved.SourceTitle);
            Assert.Equal("online, grind", saved.Tags);
        });
    }

    [Fact]
    public void DirtyGuide_SurvivesSearchAndCanceledNavigationOrClosing()
    {
        WithGuide((window, _, __) =>
        {
            Click(window, "_edit");
            Field<TextBox>(window, "_guideText").Text = "Keep this unsaved work.";
            Field<TextBox>(window, "_search").Text = "no fixture can match this";
            Assert.Equal("Keep this unsaved work.", Field<TextBox>(window, "_guideText").Text);
            Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "_editorFields").Visibility);
            Field<TextBox>(window, "_search").Text = "";
            var list = Field<ListBox>(window, "_achievementList");
            var selected = list.SelectedItem;
            list.SelectedItem = list.Items.OfType<FrameworkElement>().Last(item => item.Tag != null);
            Assert.Same(selected, list.SelectedItem);
            window.Close(); // The injected confirmation chooses Cancel.
            Assert.Equal("Keep this unsaved work.", Field<TextBox>(window, "_guideText").Text);
            Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "_editorFields").Visibility);
            Click(window, "_cancelEdit");
        });
    }

    [Fact]
    public void ReadCategoryButton_SavesSelectedCategoryAndKeepsDlcAndNotes_WithoutDiscardingEdits()
    {
        WithGuide((window, service, rows) =>
        {
            service.AssignAchievementDlc(rows[0], "Expansion");
            typeof(AchievementGuideWindow).GetMethod("ReloadEntries", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object?[] { service.LoadGuideEntry("steam", "123", "ONE").AchievementId, null });
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var button = Field<Button>(window, "_categoryButton");
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = button.ContextMenu;
            Assert.Equal(new[] { "Story", "Actions" }, menu.Items.OfType<MenuItem>().Take(2).Select(item => (string)item.Header));
            var notes = service.LoadGuideEntry("steam", "123", "ONE").GuideText;
            Field<HashSet<string>>(window, "_collapsedGroups").Add("Actions");
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Actions")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            var saved = service.LoadGuideEntry("steam", "123", "ONE");
            Assert.Equal("Actions", saved.GroupName); Assert.Equal("Expansion", saved.DlcName); Assert.Equal(notes, saved.GuideText);
            Assert.Contains("ACTIONS", Field<TextBlock>(window, "_groupPillText").Text);
            Assert.Equal("One", Field<TextBlock>(window, "_officialTitle").Text);
            AchievementDlcTests.SavePreview(window, "category-guide-1000.png", 1000, 850);
            AchievementDlcTests.SavePreview(window, "category-guide-840.png", 840, 850);
            Assert.Empty(service.LoadGuideEntry("steam", "123", "TWO").GroupName);
            var list = Field<ListBox>(window, "_achievementList");
            list.SelectedItem = list.Items.OfType<FrameworkElement>().Last(item => item.Tag != null);
            Assert.Equal("Two", Field<TextBlock>(window, "_officialTitle").Text);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            menu = button.ContextMenu;
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Story")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            Assert.Equal("Story", service.LoadGuideEntry("steam", "123", "TWO").GroupName);
            Assert.Equal("Actions", service.LoadGuideEntry("steam", "123", "ONE").GroupName);
            Click(window, "_edit");
            Field<TextBox>(window, "_guideText").Text = "Unsaved notes";
            Assert.False(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Unsaved notes", Field<TextBox>(window, "_guideText").Text);
            Assert.Equal(notes, service.LoadGuideEntry("steam", "123", "ONE").GuideText);
        });
    }

    [Fact]
    public void UnsavedSaveChoice_PersistsBeforeNavigatingToAnotherGuide()
    {
        WithGuide((window, service, rows) =>
        {
            Click(window, "_edit");
            Field<TextBox>(window, "_guideText").Text = "Saved during navigation.";
            var list = Field<ListBox>(window, "_achievementList");
            list.SelectedItem = list.Items.OfType<FrameworkElement>().Last(item => item.Tag != null);
            Assert.Equal("Two", Field<TextBlock>(window, "_officialTitle").Text);
            Assert.Equal("Saved during navigation.", service.SyncDefinitionsAndLoadGuides("fixture", rows)[0].GuideText);
        }, MessageBoxResult.Yes);
    }

    [Fact]
    public void EmptyGuide_OffersAddAndReturnsToReadingAfterCancel()
    {
        WithGuide((window, _, __) =>
        {
            var list = Field<ListBox>(window, "_achievementList");
            list.SelectedItem = list.Items.OfType<FrameworkElement>().Last(item => item.Tag != null);
            Assert.Equal("Add Guide", Field<Button>(window, "_edit").Content);
            Assert.Equal("No guide added yet.", Field<TextBlock>(window, "_readGuide").Text);
            Assert.Equal(Visibility.Collapsed, Field<Button>(window, "_readSource").Visibility);
            Click(window, "_edit");
            Field<TextBox>(window, "_guideText").Text = "Draft";
            Click(window, "_cancelEdit");
            Assert.Equal("No guide added yet.", Field<TextBlock>(window, "_readGuide").Text);
        });
    }

    static void Click(object window, string name) =>
        Field<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    static void WithGuide(Action<AchievementGuideWindow, AchievementGuideService, List<GameAchievementsFetchService.AchievementRow>> action,
        MessageBoxResult unsavedChoice = MessageBoxResult.Cancel)
    {
        OnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "PixelVault-reading-tests-" + Guid.NewGuid().ToString("N"));
            var service = new AchievementGuideService(path);
            var rows = new List<GameAchievementsFetchService.AchievementRow>
            {
                new() { Provider = "steam", ProviderGameId = "123", ProviderAchievementId = "ONE", Title = "One", ProgressKnown = true },
                new() { Provider = "steam", ProviderGameId = "123", ProviderAchievementId = "TWO", Title = "Two", ProgressKnown = true }
            };
            var entry = service.SyncDefinitionsAndLoadGuides("fixture", rows)[0];
            service.SaveGuide(new AchievementGuideEdit
            {
                AchievementId = entry.AchievementId,
                GuideText = "First paragraph.\n\nSecond paragraph with a useful route.",
                SourceTitle = "Fixture source", SourceUrl = "https://example.com/guide", Tags = "Online", IsMissable = true
            });
            var window = (AchievementGuideWindow)Activator.CreateInstance(typeof(AchievementGuideWindow),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object?[] { null, service, "Fixture", "fixture", rows, "ONE", new Func<MessageBoxResult>(() => unsavedChoice) }, null)!;
            try { action(window, service, rows); }
            finally
            {
                Click(window, "_cancelEdit");
                window.Close();
                SqliteConnection.ClearAllPools();
                Directory.Delete(path, true);
            }
        });
    }
}
