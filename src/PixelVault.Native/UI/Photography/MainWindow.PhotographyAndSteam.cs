using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        List<string> GetTaggedImagesCached(string root, bool forceRefresh, params string[] tagCandidates)
        {
            var index = LoadLibraryMetadataIndexViaSessionWhenActive(root, false);
            if (!forceRefresh)
            {
                var stamp = BuildPhotographyGalleryCacheStampFromIndex(index);
                var cached = LoadTaggedImageCache(root, stamp);
                if (cached != null)
                {
                    Log("Photography gallery cache hit.");
                    return cached;
                }
            }

            List<string> fresh;
            if (forceRefresh)
            {
                Log("Refreshing photography gallery cache (full library Exif scan).");
                fresh = FindTaggedImages(root, tagCandidates);
            }
            else
            {
                Log("Rebuilding photography gallery from metadata index.");
                fresh = FindTaggedImagesFromMetadataIndex(index, tagCandidates);
                if (fresh.Count == 0 && index.Count == 0)
                {
                    Log("Photography gallery: metadata index empty; scanning library with Exif (first-time cost).");
                    fresh = FindTaggedImages(root, tagCandidates);
                }
            }

            var stampForSave = BuildPhotographyGalleryCacheStampFromIndex(LoadLibraryMetadataIndexViaSessionWhenActive(root, false));
            SaveTaggedImageCache(root, stampForSave, fresh);
            return fresh;
        }

        /// <summary>Cache invalidation tied to persisted metadata index content (not a full media-tree walk).</summary>
        static string BuildPhotographyGalleryCacheStampFromIndex(Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (index == null || index.Count == 0) return "idx|0|0";
            unchecked
            {
                long h = index.Count;
                foreach (var kv in index.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var e = kv.Value;
                    if (e == null) continue;
                    h = h * 397 ^ (e.FilePath ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (e.Stamp ?? string.Empty).GetHashCode(StringComparison.Ordinal);
                    h = h * 397 ^ (e.TagText ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ e.CaptureUtcTicks;
                    h = h * 397 ^ (e.Starred ? 1 : 0);
                }
                return "idx|" + index.Count + "|" + h;
            }
        }

        static bool MetadataIndexTagTextMatchesCandidates(string tagText, List<string> candidates)
        {
            if (candidates == null || candidates.Count == 0 || string.IsNullOrWhiteSpace(tagText)) return false;
            foreach (var part in tagText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var piece = part.Trim();
                if (piece.Length == 0) continue;
                foreach (var want in candidates)
                {
                    if (string.Equals(piece, want, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>Uses <see cref="LibraryMetadataIndexEntry.TagText"/> (comma-separated embedded tags) — avoids enumerating every media file and batch Exif reads when the index is populated.</summary>
        List<string> FindTaggedImagesFromMetadataIndex(Dictionary<string, LibraryMetadataIndexEntry> index, params string[] tagCandidates)
        {
            var wanted = tagCandidates
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (wanted.Count == 0 || index == null || index.Count == 0) return new List<string>();
            var result = new List<string>();
            foreach (var kv in index)
            {
                var path = kv.Key;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsMedia(path)) continue;
                var entry = kv.Value;
                var tagText = entry == null ? string.Empty : entry.TagText ?? string.Empty;
                if (!MetadataIndexTagTextMatchesCandidates(tagText, wanted)) continue;
                result.Add(path);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        string TaggedImageCachePath(string root)
        {
            return Path.Combine(cacheRoot, "photography-gallery-" + SafeCacheName(root) + ".cache");
        }

        List<string> LoadTaggedImageCache(string root, string stamp)
        {
            var path = TaggedImageCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(lines[1], stamp, StringComparison.Ordinal)) return null;
            return lines.Skip(2).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        void SaveTaggedImageCache(string root, string stamp, List<string> files)
        {
            var path = TaggedImageCachePath(root);
            var lines = new List<string>();
            lines.Add(root);
            lines.Add(stamp);
            lines.AddRange(files.Distinct(StringComparer.OrdinalIgnoreCase));
            File.WriteAllLines(path, lines.ToArray());
        }

        List<string> FindTaggedImages(string root, params string[] tagCandidates)
        {
            var tags = tagCandidates.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tags.Count == 0) return new List<string>();
            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(IsMedia)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tagMap = ReadEmbeddedKeywordTagsBatch(files);
            return files
                .Where(file => tagMap.ContainsKey(file) && tagMap[file].Any(tag => tags.Any(candidate => string.Equals(tag, candidate, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        string BuildPhotographyGalleryCaption(string file)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(file));
            var dt = GetLibraryDate(file);
            var datePart = dt > DateTime.MinValue && dt.Year > 1 ? FormatFriendlyTimestamp(dt) : string.Empty;
            if (string.IsNullOrEmpty(folder)) return datePart ?? string.Empty;
            return string.IsNullOrEmpty(datePart) ? folder : folder + "  ·  " + datePart;
        }

        List<PhotographyGalleryEntry> BuildPhotographyGalleryEntries(IEnumerable<string> files)
        {
            var index = LoadLibraryMetadataIndexViaSessionWhenActive(libraryWorkspace.LibraryRoot, false);
            return (files ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(GetLibraryDate)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(f =>
                {
                    var starred = false;
                    LibraryMetadataIndexEntry row;
                    if (index != null && index.TryGetValue(f, out row) && row != null) starred = row.Starred;
                    return new PhotographyGalleryEntry
                    {
                        FullPath = f,
                        Title = Path.GetFileName(f) ?? f,
                        Caption = BuildPhotographyGalleryCaption(f),
                        Starred = starred
                    };
                })
                .ToList();
        }

        void TogglePhotographyGalleryEntryStarred(PhotographyGalleryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullPath) || !File.Exists(entry.FullPath)) return;
            var root = libraryWorkspace.LibraryRoot;
            if (string.IsNullOrWhiteSpace(root)) return;
            var fullPath = entry.FullPath;
            var dispatcher = Dispatcher;
            Task.Run(delegate
            {
                bool ok = false;
                bool newStarred = false;
                Exception caught = null;
                try
                {
                    var index = LoadLibraryMetadataIndexViaSessionWhenActive(root, true);
                    LibraryMetadataIndexEntry row;
                    if (!index.TryGetValue(fullPath, out row) || row == null) return;
                    newStarred = !row.Starred;
                    ApplyEmbeddedXmpStarRating(fullPath, newStarred);
                    row.Starred = newStarred;
                    row.Stamp = BuildLibraryMetadataStamp(fullPath);
                    SaveLibraryMetadataIndexViaSessionWhenActive(root, index);
                    ok = true;
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                dispatcher.BeginInvoke(new Action(delegate
                {
                    if (caught != null) LogException("TogglePhotographyGalleryEntryStarred", caught);
                    else if (ok) entry.Starred = newStarred;
                }));
            });
        }

        void ShowPhotographyGallery(Window owner)
        {
            try
            {
                EnsureDir(libraryWorkspace.LibraryRoot, "Library folder");
                var libraryRoot = libraryWorkspace.LibraryRoot;
                PhotographyGalleryHost host = null;
                host = new PhotographyGalleryHost
                {
                    LibraryRoot = libraryRoot,
                    AppVersion = AppVersion,
                    GamePhotographyTag = GamePhotographyTag,
                    LoadTaggedImagePaths = force => GetTaggedImagesCached(libraryRoot, force, GamePhotographyTag, "Photography"),
                    BuildEntries = BuildPhotographyGalleryEntries,
                    TogglePhotoStarred = TogglePhotographyGalleryEntryStarred,
                    PrepareExifOnBackgroundThread = EnsureExifTool,
                    SetAppStatus = delegate(string text) { if (status != null) status.Text = text; },
                    LogError = LogException,
                    NotifyUser = (msg, icon) => TryLibraryToast(msg, icon),
                    OpenLibraryFolder = delegate { OpenFolder(libraryRoot); },
                    OpenImageWithShell = OpenWithShell,
                    QueueImageLoad = delegate(Image img, string path, int w, Action<BitmapImage> onDone)
                    {
                        QueueImageLoad(img, path, w, onDone, false, null);
                    },
                    OpenContainingFolderForFile = delegate(string path)
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(dir)) OpenFolder(dir);
                    },
                    OpenMetadataEditorForFile = OpenStandaloneLibraryMetadataEditor,
                    ToggleGamePhotographyTagForFile = delegate(string path)
                    {
                        ToggleLibraryFileGamePhotographyTagByPath(path);
                        host.RefreshTaggedGallery?.Invoke();
                    },
                    CopyFilePathToClipboard = delegate(string path)
                    {
                        try
                        {
                            Clipboard.SetText(path);
                        }
                        catch (Exception ex)
                        {
                            LogException("Photography gallery copy path", ex);
                        }
                    },
                    GetFileHasGamePhotographyTag = LibraryFileIndexHasGamePhotographyTag
                };
                var win = new PhotographyGalleryWindow(host, owner ?? this);
                _activePhotographyGalleryWindow = win;
                win.Closed += delegate
                {
                    if (ReferenceEquals(_activePhotographyGalleryWindow, win)) _activePhotographyGalleryWindow = null;
                };
                host.RefreshTaggedGallery = delegate
                {
                    win.Dispatcher.BeginInvoke(new Action(win.RequestGalleryReload));
                };
                win.Show();
                if (status != null) status.Text = "Loading photography gallery…";
            }
            catch (Exception ex)
            {
                LogException("ShowPhotographyGallery", ex);
                TryLibraryToast(ex.Message, MessageBoxImage.Error);
            }
        }

        void NotifyPhotographyGalleryAfterLibraryFilesRemoved()
        {
            var w = _activePhotographyGalleryWindow;
            if (w == null) return;
            _ = w.Dispatcher.BeginInvoke(new Action(() => w.RequestGalleryForceReload()));
        }

        Tuple<string, string> ShowSteamAppMatchWindow(Window owner, string query, List<Tuple<string, string>> matches)
        {
            var candidates = (matches ?? new List<Tuple<string, string>>()).Where(match => match != null && !string.IsNullOrWhiteSpace(match.Item1) && !string.IsNullOrWhiteSpace(match.Item2)).Take(24).ToList();
            if (candidates.Count == 0) return null;

            Tuple<string, string> selected = null;
            var wanted = NormalizeTitle(query);
            var pickerWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Steam Matches",
                Width = 760,
                Height = 720,
                MinWidth = 680,
                MinHeight = 560,
                Owner = owner ?? this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock { Text = "Choose the Steam match", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            headerStack.Children.Add(new TextBlock { Text = "Results for \"" + query + "\". Pick the right game and PixelVault will save its AppID before import.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            header.Child = headerStack;
            root.Children.Add(header);

            var list = new ListBox
            {
                Background = Brush("#12191E"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Padding = new Thickness(12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            var selectedIndex = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var match = candidates[i];
                var isExact = NormalizeTitle(match.Item2) == wanted;
                if (isExact) selectedIndex = i;
                var item = new ListBoxItem { Tag = match, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var border = new Border
                {
                    Background = isExact ? Brush("#183A30") : Brush("#1A2329"),
                    BorderBrush = isExact ? Brush("#3FAE7C") : Brush("#243139"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 12, 14, 12)
                };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = match.Item2, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(new TextBlock { Text = "Steam AppID " + match.Item1 + (isExact ? " | exact title match" : string.Empty), Foreground = isExact ? Brush("#BEE8D3") : Brush("#9FB0BA"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
                border.Child = stack;
                item.Content = border;
                list.Items.Add(item);
            }

            var buttons = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cancelButton = Btn("Cancel", null, "#334249", Brushes.White);
            cancelButton.Margin = new Thickness(0);
            var selectButton = Btn("Use Match", null, "#275D47", Brushes.White);
            selectButton.Margin = new Thickness(12, 0, 0, 0);
            selectButton.IsEnabled = candidates.Count > 0;
            buttons.Children.Add(cancelButton);
            Grid.SetColumn(selectButton, 1);
            buttons.Children.Add(selectButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Action<bool> closeWindow = delegate(bool accept)
            {
                if (accept)
                {
                    var selectedItem = list.SelectedItem as ListBoxItem;
                    if (selectedItem == null || !(selectedItem.Tag is Tuple<string, string>)) return;
                    selected = (Tuple<string, string>)selectedItem.Tag;
                    pickerWindow.DialogResult = true;
                }
                else
                {
                    pickerWindow.DialogResult = false;
                }
                pickerWindow.Close();
            };

            list.SelectionChanged += delegate
            {
                selectButton.IsEnabled = list.SelectedItem is ListBoxItem;
            };
            list.MouseDoubleClick += delegate
            {
                if (list.SelectedItem is ListBoxItem) closeWindow(true);
            };
            cancelButton.Click += delegate { closeWindow(false); };
            selectButton.Click += delegate { closeWindow(true); };
            if (list.Items.Count > 0) list.SelectedIndex = selectedIndex;

            pickerWindow.Content = root;
            return pickerWindow.ShowDialog() == true ? selected : null;
        }
    }
}
