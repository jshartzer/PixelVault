using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string GetManualMetadataBadgeLabel(ManualMetadataItem item)
        {
            if (item == null) return "Manual";
            if (item.IntakeRuleMatched) return "Auto";
            if (item.TagSteam) return "Steam";
            if (item.TagPc) return "PC";
            if (item.TagPs5) return "PS5";
            if (item.TagXbox) return "Xbox";
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return CleanTag(item.CustomPlatformTag);
            return "Manual";
        }

        Brush GetManualMetadataBadgeBrush(string label)
        {
            if (string.Equals(label, "Auto", StringComparison.OrdinalIgnoreCase)) return Brush("#5CB88A");
            if (string.Equals(label, "Steam", StringComparison.OrdinalIgnoreCase)) return Brush("#69A7FF");
            if (string.Equals(label, "PC", StringComparison.OrdinalIgnoreCase)) return Brush("#7F8EA3");
            if (string.Equals(label, "PS5", StringComparison.OrdinalIgnoreCase)) return Brush("#4F83FF");
            if (string.Equals(label, "Xbox", StringComparison.OrdinalIgnoreCase)) return Brush("#66C47A");
            return Brush("#D0A15F");
        }

        static string GetSharedManualMetadataFieldText(IEnumerable<ManualMetadataItem> selection, Func<ManualMetadataItem, string> getter)
        {
            var values = selection.Select(getter).Select(v => (v ?? string.Empty).Trim()).Distinct(StringComparer.Ordinal).ToArray();
            return values.Length == 1 ? values[0] : string.Empty;
        }

        static bool? GetSharedManualMetadataFieldBool(IEnumerable<ManualMetadataItem> selection, Func<ManualMetadataItem, bool> getter)
        {
            var values = selection.Select(getter).Distinct().ToArray();
            return values.Length == 1 ? (bool?)values[0] : null;
        }

        string GetManualMetadataFilenameGuessSummary(IEnumerable<ManualMetadataItem> selection)
        {
            var guesses = selection.Select(item => FilenameGuessLabel(item == null ? string.Empty : item.FileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (guesses.Length == 0) return "Best Guess | No confident match";
            if (guesses.Length == 1) return "Best Guess | " + guesses[0];
            return "Best Guess | Mixed guesses";
        }

        static void ApplyConsolePlatformToManualMetadataItems(IEnumerable<ManualMetadataItem> selection, string platform)
        {
            foreach (var item in selection)
            {
                item.TagSteam = string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase);
                item.TagPc = string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase);
                item.TagPs5 = string.Equals(platform, "PS5", StringComparison.OrdinalIgnoreCase);
                item.TagXbox = string.Equals(platform, "Xbox", StringComparison.OrdinalIgnoreCase);
                item.TagOther = string.Equals(platform, "Other", StringComparison.OrdinalIgnoreCase);
                if (!item.TagOther) item.CustomPlatformTag = string.Empty;
                item.ForceTagMetadataWrite = true;
            }
        }

        List<string> GetManualMetadataRecentTitleLabelsList()
        {
            if (string.IsNullOrWhiteSpace(_manualMetadataRecentTitleLabelsSerialized)) return new List<string>();
            return _manualMetadataRecentTitleLabelsSerialized
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        void PushManualMetadataRecentTitleLabels(IEnumerable<string> labels)
        {
            var next = new List<string>();
            foreach (var segment in GetManualMetadataRecentTitleLabelsList())
            {
                if (!next.Contains(segment, StringComparer.OrdinalIgnoreCase)) next.Add(segment);
            }
            foreach (var raw in labels ?? Enumerable.Empty<string>())
            {
                var label = (raw ?? string.Empty).Trim().Replace("|", " ");
                if (string.IsNullOrWhiteSpace(label)) continue;
                next.RemoveAll(x => string.Equals(x, label, StringComparison.OrdinalIgnoreCase));
                next.Insert(0, label);
            }
            _manualMetadataRecentTitleLabelsSerialized = string.Join("|", next.Take(15));
            SaveSettings();
        }

        static void CopyManualMetadataItemFromAnother(ManualMetadataItem from, ManualMetadataItem to)
        {
            if (from == null || to == null) return;
            to.GameName = from.GameName ?? string.Empty;
            to.SteamAppId = from.SteamAppId ?? string.Empty;
            to.TagText = from.TagText ?? string.Empty;
            to.Comment = from.Comment ?? string.Empty;
            to.AddPhotographyTag = from.AddPhotographyTag;
            to.TagSteam = from.TagSteam;
            to.TagPc = from.TagPc;
            to.TagPs5 = from.TagPs5;
            to.TagXbox = from.TagXbox;
            to.TagOther = from.TagOther;
            to.CustomPlatformTag = from.CustomPlatformTag ?? string.Empty;
            to.CaptureTime = from.CaptureTime;
            to.UseCustomCaptureTime = from.UseCustomCaptureTime;
            to.ForceTagMetadataWrite = true;
        }

        UIElement BuildManualMetadataMultiPreviewStack(int count, bool useFlexiblePreview, double previewImageMaxHeight)
        {
            var multiH = useFlexiblePreview ? Math.Min(400, Math.Max(240, previewImageMaxHeight)) : 320;
            var grid = new Grid { Height = multiH };
            var art = new Grid { Width = 260, Height = 190, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var back = new Border { Width = 136, Height = 104, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(64, -44, 0, 0) };
            var mid = new Border { Width = 148, Height = 112, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, -20, 0, 0) };
            var front = new Border { Width = 160, Height = 120, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            var frontGrid = new Grid();
            frontGrid.Children.Add(new Border { Width = 78, Height = 78, Background = Brush("#161C20"), CornerRadius = new CornerRadius(39), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } });
            front.Child = frontGrid;
            art.Children.Add(back);
            art.Children.Add(mid);
            art.Children.Add(front);
            grid.Children.Add(art);
            return grid;
        }
    }
}
