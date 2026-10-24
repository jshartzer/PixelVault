using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        CaptureGroupStore CaptureGroups => new CaptureGroupStore(Path.Combine(dataRoot, "capture-groups.sqlite"));

        void MoveCaptureGroupFile(string root, string before, string after)
        {
            try { CaptureGroups.MoveFile(root, before, after); }
            catch (Exception ex) { LogException("MoveCaptureGroupFile", ex); }
        }

        static string CaptureGroupGameId(LibraryBrowserFolderView folder) =>
            folder == null || folder.IsTimelineProjection || folder.IsSessionProjection || folder.PendingGameAssignment
                ? null : !string.IsNullOrWhiteSpace(folder.GameId) ? folder.GameId : folder.PrimaryFolder?.GameId;

        static bool IsRobloxCaptureGroupGame(LibraryBrowserFolderView folder) =>
            string.Equals(folder?.Name?.Trim(), RobloxCaptureNaming.GameTitle, StringComparison.OrdinalIgnoreCase);

        void RefreshCaptureGroupToggle(LibraryBrowserWorkingSet ws, bool grouped, bool available)
        {
            var toggle = ws.Panes?.CaptureGroupsToggle;
            if (toggle == null) return;
            toggle.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            toggle.IsChecked = grouped;
        }

        void ToggleCaptureGroupView(LibraryBrowserWorkingSet ws, Action redraw)
        {
            var game = CaptureGroupGameId(ws.Current);
            if (string.IsNullOrWhiteSpace(game)) return;
            try
            {
                CaptureGroups.SetGrouped(libraryRoot, game, ws.Panes.CaptureGroupsToggle.IsChecked == true);
                ws.PreserveDetailScrollOnNextRender = true;
                ws.PreservedDetailScrollOffset = 0;
                ws.DetailSelectionAnchorIndex = -1;
                redraw();
            }
            catch (Exception ex)
            {
                ws.Panes.CaptureGroupsToggle.IsChecked = ws.Panes.CaptureGroupsToggle.IsChecked != true;
                ShowLibraryBrowserToast(ws, "Could not save the group view: " + ex.Message);
            }
        }

        void AttachCaptureGroupMenu(Border tile, LibraryBrowserWorkingSet ws, LibraryBrowserFolderView folder,
            string file, Action redraw)
        {
            var game = CaptureGroupGameId(folder);
            if (string.IsNullOrWhiteSpace(game) || tile.ContextMenu == null) return;
            var menu = new MenuItem { Header = "Add to Group…" };
            var remove = new MenuItem { Header = "Remove from Group" };
            tile.ContextMenu.Items.Insert(0, menu);
            tile.ContextMenu.Items.Insert(1, remove);
            tile.ContextMenu.Items.Insert(2, new Separator());
            var root = libraryRoot;
            var roblox = IsRobloxCaptureGroupGame(folder);
            List<string> SelectedFiles() => ws.SelectedDetailFiles.Contains(file)
                ? ws.DetailFilesDisplayOrder.Where(ws.SelectedDetailFiles.Contains).ToList() : new List<string> { file };

            void Assign(string name)
            {
                try
                {
                    CaptureGroups.Assign(root, game, SelectedFiles(), name);
                    if (!string.IsNullOrEmpty(name) && CaptureGroups.Load(root, game).Grouped == null)
                        CaptureGroups.SetGrouped(root, game, true);
                    ws.DetailSelectionAnchorIndex = -1;
                    redraw();
                    ShowLibraryBrowserToast(ws, string.IsNullOrEmpty(name) ? "Removed from group" : "Added to " + name);
                }
                catch (Exception ex) { ShowLibraryBrowserToast(ws, "Could not update the group: " + ex.Message); }
            }

            tile.ContextMenu.Opened += delegate
            {
                menu.Items.Clear();
                try
                {
                    var snapshot = CaptureGroups.Load(root, game);
                    var names = snapshot.Names.Concat((folder.FilePaths ?? Array.Empty<string>()).Select(path => snapshot.Resolve(path, roblox)))
                        .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
                    foreach (var name in names)
                    {
                        var item = new MenuItem { Header = new TextBlock { Text = name } };
                        item.Click += delegate { Assign(name); };
                        menu.Items.Add(item);
                    }
                    if (names.Count > 0) menu.Items.Add(new Separator());
                    var create = new MenuItem { Header = "Create New Group…" };
                    create.Click += delegate
                    {
                        var name = PromptCaptureGroupName(Window.GetWindow(tile) ?? this);
                        if (name != null) Assign(name);
                    };
                    menu.Items.Add(create);
                    remove.IsEnabled = SelectedFiles().Any(path => !string.IsNullOrEmpty(snapshot.Resolve(path, roblox)));
                }
                catch (Exception ex)
                {
                    menu.Items.Add(new MenuItem { Header = "Could not load groups", IsEnabled = false, ToolTip = ex.Message });
                    remove.IsEnabled = false;
                }
            };
            remove.Click += delegate { Assign(string.Empty); };
        }

        string PromptCaptureGroupName(Window owner)
        {
            var dialog = new Window
            {
                Title = "Create New Group", Owner = owner, Width = 420, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#182129"), Foreground = Brushes.White, ShowInTaskbar = false
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "Group name", Margin = new Thickness(0, 0, 0, 8) });
            var input = new TextBox { MaxLength = 200, Padding = new Thickness(8), FontSize = 14 };
            panel.Children.Add(input);
            var error = new TextBlock { Foreground = Brush("#FFB9A5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            var create = new Button { Content = "Create and Add", IsDefault = true, Padding = new Thickness(12, 6, 12, 6) };
            string result = null;
            create.Click += delegate
            {
                try { result = CaptureGroupStore.ValidateName(input.Text); dialog.DialogResult = true; }
                catch (ArgumentException ex) { error.Text = ex.Message; }
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(create);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += delegate { input.Focus(); };
            return dialog.ShowDialog() == true ? result : null;
        }
    }
}
