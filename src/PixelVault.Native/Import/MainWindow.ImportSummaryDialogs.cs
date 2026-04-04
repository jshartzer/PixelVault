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
        List<string> BuildImportSummaryLines(string workflowLabel, bool usedReview, RenameStepResult renameResult, DeleteStepResult deleteResult, MetadataStepResult metadataResult, MoveStepResult moveResult, SortStepResult sortResult, int manualItemsLeft, bool manualItemsLeftAreUploadSkips = false)
        {
            var lines = new List<string>();
            lines.Add("Workflow: " + workflowLabel + (usedReview ? " with review window." : "."));
            lines.Add("Rename summary: renamed " + (renameResult == null ? 0 : renameResult.Renamed) + ", skipped " + (renameResult == null ? 0 : renameResult.Skipped) + ".");
            if (deleteResult != null && (usedReview || deleteResult.Deleted > 0 || deleteResult.Skipped > 0))
            {
                lines.Add("Delete summary: deleted " + deleteResult.Deleted + ", skipped " + deleteResult.Skipped + ".");
            }
            lines.Add("Metadata summary: updated " + (metadataResult == null ? 0 : metadataResult.Updated) + ", skipped " + (metadataResult == null ? 0 : metadataResult.Skipped) + ".");
            lines.Add("Move summary: moved " + (moveResult == null ? 0 : moveResult.Moved) + ", skipped " + (moveResult == null ? 0 : moveResult.Skipped) + ", renamed-on-conflict " + (moveResult == null ? 0 : moveResult.RenamedOnConflict) + ".");
            if (sortResult == null)
            {
                lines.Add("Sort summary: skipped because no files were imported into the destination root.");
            }
            else
            {
                lines.Add("Sort summary: sorted " + sortResult.Sorted + ", folders created " + sortResult.FoldersCreated + ", renamed-on-conflict " + sortResult.RenamedOnConflict + ".");
            }
            if (manualItemsLeftAreUploadSkips)
            {
                if (manualItemsLeft > 0)
                {
                    lines.Add("Upload folder: " + manualItemsLeft + " file(s) were not selected for this import and remain in the upload folder.");
                }
                else
                {
                    lines.Add("Upload folder: every listed file was included in this import (none left unselected).");
                }
            }
            else if (manualItemsLeft > 0)
            {
                lines.Add("Manual Intake queue: left " + manualItemsLeft + " unmatched image(s) untouched.");
            }
            else
            {
                lines.Add("Manual Intake queue: no unmatched image(s) waiting.");
            }
            return lines;
        }

        void ShowImportSummaryWindow(string title, string meta, IEnumerable<string> lines)
        {
            var owner = ResolveStatusWindowOwner();
            var summaryWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " " + title,
                Width = 900,
                Height = 580,
                MinWidth = 780,
                MinHeight = 520,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };
            if (owner != null) summaryWindow.Owner = owner;

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var summaryTitle = new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var summaryMeta = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(meta) ? "Import work completed." : meta,
                Foreground = Brush("#B7C6C0"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            var summaryLog = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Background = Brush("#12191E"),
                Foreground = Brush("#F1E9DA"),
                BorderBrush = Brush("#2B3A44"),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Cascadia Mono"),
                Text = string.Join(Environment.NewLine, (lines ?? Enumerable.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray())
            };
            var closeButton = Btn("Close", null, "#334249", Brushes.White);
            closeButton.Margin = new Thickness(0);
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.Click += delegate { summaryWindow.Close(); };

            root.Children.Add(summaryTitle);
            Grid.SetRow(summaryMeta, 1);
            root.Children.Add(summaryMeta);

            var logBorder = new Border
            {
                Background = Brush("#12191E"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                BorderBrush = Brush("#26363F"),
                BorderThickness = new Thickness(1),
                Child = summaryLog
            };
            Grid.SetRow(logBorder, 2);
            root.Children.Add(logBorder);

            Grid.SetRow(closeButton, 3);
            root.Children.Add(closeButton);

            summaryWindow.Content = root;
            summaryWindow.ShowDialog();
        }
    }
}
