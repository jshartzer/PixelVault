using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    internal static class LibraryCommandPaletteWindow
    {
        sealed class PaletteEntry
        {
            internal PaletteEntry(string id, string title, string hint, string keywords, Action run)
            {
                Id = id;
                Title = title;
                Hint = hint;
                Keywords = keywords ?? string.Empty;
                Run = run;
            }

            internal string Id { get; }
            internal string Title { get; }
            internal string Hint { get; }
            internal string Keywords { get; }
            internal Action Run { get; }

            public override string ToString() => Title + " — " + Hint;
        }

        internal static void Show(Window owner, LibraryBrowserPaletteContext ctx, string initialSearch = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var handlers = LibraryCommandPaletteRegistry.BuildHandlerMap(ctx);
            var entries = new List<PaletteEntry>();
            foreach (var spec in LibraryCommandPaletteRegistry.All)
            {
                if (!handlers.TryGetValue(spec.Id, out var action) || action == null) continue;
                entries.Add(new PaletteEntry(spec.Id, spec.Title, spec.Hint, spec.Keywords, action));
            }

            if (entries.Count == 0) return;

            SolidColorBrush B(string hex) => UiBrushHelper.FromHex(hex);

            var win = new Window
            {
                Title = "Commands — PixelVault Library",
                Width = 560,
                Height = 440,
                MinHeight = 320,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = B(DesignTokens.PageBackground),
                ResizeMode = ResizeMode.NoResize
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var search = new TextBox
            {
                Padding = new Thickness(12, 10, 12, 10),
                Background = B(DesignTokens.InputBackground),
                Foreground = B(DesignTokens.TextOnInput),
                BorderBrush = B(DesignTokens.BorderDefault),
                BorderThickness = new Thickness(1),
                FontSize = 14,
                CaretBrush = Brushes.White
            };
            root.Children.Add(search);

            var list = new ListBox
            {
                Margin = new Thickness(0, 14, 0, 12),
                Background = B(DesignTokens.PanelElevated),
                Foreground = B(DesignTokens.TextShortcutBody),
                BorderBrush = B(DesignTokens.BorderDefault),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6)
            };
            KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Contained);
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            var footer = new TextBlock
            {
                Text = "Type to filter · Tab to list · ↑↓ · Enter run · Esc close",
                Foreground = B(DesignTokens.TextLabelMuted),
                FontSize = 12
            };
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            List<PaletteEntry> filtered = entries.ToList();

            void BindList()
            {
                list.ItemsSource = null;
                list.ItemsSource = filtered;
                if (filtered.Count > 0)
                {
                    list.SelectedIndex = 0;
                    list.ScrollIntoView(filtered[0]);
                }
            }

            bool Matches(PaletteEntry e, string ql)
            {
                if (string.IsNullOrEmpty(ql)) return true;
                if ((e.Title ?? string.Empty).ToLowerInvariant().Contains(ql)) return true;
                if ((e.Hint ?? string.Empty).ToLowerInvariant().Contains(ql)) return true;
                if ((e.Keywords ?? string.Empty).ToLowerInvariant().Contains(ql)) return true;
                return false;
            }

            void ApplyFilter()
            {
                var q = (search.Text ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(q))
                {
                    filtered = entries.ToList();
                }
                else
                {
                    var ql = q.ToLowerInvariant();
                    filtered = entries.Where(e => Matches(e, ql)).ToList();
                }
                BindList();
            }

            void RunSelected()
            {
                if (list.SelectedItem is not PaletteEntry pal) return;
                win.Close();
                pal.Run();
            }

            search.TextChanged += delegate { ApplyFilter(); };

            list.MouseDoubleClick += delegate { RunSelected(); };

            win.PreviewKeyDown += delegate(object _, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    win.Close();
                    return;
                }
                if (e.Key == Key.Tab && ReferenceEquals(Keyboard.FocusedElement, search) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    if (filtered.Count > 0)
                    {
                        e.Handled = true;
                        list.Focus();
                        list.SelectedIndex = 0;
                    }
                    return;
                }
                if (e.Key == Key.Down)
                {
                    if (filtered.Count == 0) return;
                    var i = list.SelectedIndex < 0 ? 0 : Math.Min(list.SelectedIndex + 1, filtered.Count - 1);
                    list.SelectedIndex = i;
                    list.ScrollIntoView(filtered[i]);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Up)
                {
                    if (filtered.Count == 0) return;
                    var i = list.SelectedIndex < 0 ? 0 : Math.Max(list.SelectedIndex - 1, 0);
                    list.SelectedIndex = i;
                    list.ScrollIntoView(filtered[i]);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    RunSelected();
                }
            };

            win.Content = root;
            win.Loaded += delegate
            {
                if (!string.IsNullOrWhiteSpace(initialSearch))
                    search.Text = initialSearch.Trim();
                ApplyFilter();
                search.Focus();
                if (!string.IsNullOrWhiteSpace(search.Text))
                    search.CaretIndex = search.Text.Length;
            };
            win.ShowDialog();
        }
    }
}
