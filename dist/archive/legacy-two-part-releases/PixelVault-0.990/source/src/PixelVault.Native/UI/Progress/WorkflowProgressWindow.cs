using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    /// <summary>
    /// Shared dark progress shell (title, meta line, bar, monospace log, footer button) used by import workflow, library scan, metadata apply, and cover refresh.
    /// </summary>
    public sealed class WorkflowProgressView
    {
        readonly List<string> _logLines = new List<string>();
        readonly int _maxLogLines;

        public WorkflowProgressView(
            Window window,
            TextBlock metaText,
            ProgressBar progressBar,
            TextBox logText,
            Button actionButton,
            int maxLogLines)
        {
            Window = window;
            MetaText = metaText;
            ProgressBar = progressBar;
            LogText = logText;
            ActionButton = actionButton;
            _maxLogLines = Math.Max(1, maxLogLines);
        }

        public Window Window { get; }
        public TextBlock MetaText { get; }
        public ProgressBar ProgressBar { get; }
        public TextBox LogText { get; }
        public Button ActionButton { get; }

        public void AppendLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || LogText == null) return;
            _logLines.Add(line);
            while (_logLines.Count > _maxLogLines) _logLines.RemoveAt(0);
            LogText.Text = string.Join(Environment.NewLine, _logLines.ToArray());
            LogText.ScrollToEnd();
        }
    }

    public static class WorkflowProgressWindow
    {
        public const int DefaultMaxLogLines = 200;
        public const int ScanStyleMaxLogLines = 180;

        /// <summary>
        /// Builds the standard progress window. When <paramref name="owner"/> is null or not visible, uses center-screen and no owner.
        /// </summary>
        public static WorkflowProgressView Create(
            Window owner,
            string windowTitle,
            string headingText,
            string initialMetaText,
            double progressMinimum,
            double progressMaximum,
            double progressValue,
            bool progressIndeterminate,
            Button actionButton,
            int maxLogLines = DefaultMaxLogLines)
        {
            var useOwner = owner != null && owner.IsVisible;
            var progressWindow = new Window
            {
                Title = windowTitle ?? string.Empty,
                Width = 900,
                Height = 580,
                MinWidth = 780,
                MinHeight = 520,
                Owner = useOwner ? owner : null,
                WindowStartupLocation = useOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = UiBrushHelper.FromHex("#0F1519")
            };

            var progressRoot = new Grid { Margin = new Thickness(18) };
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var progressTitle = new TextBlock
            {
                Text = headingText ?? string.Empty,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var progressMeta = new TextBlock
            {
                Text = initialMetaText ?? string.Empty,
                Foreground = UiBrushHelper.FromHex("#B7C6C0"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            var progressBar = new ProgressBar
            {
                Height = 18,
                Minimum = progressMinimum,
                Maximum = progressMaximum,
                Value = progressValue,
                IsIndeterminate = progressIndeterminate,
                Margin = new Thickness(0, 0, 0, 14)
            };
            var progressLog = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Background = UiBrushHelper.FromHex("#12191E"),
                Foreground = UiBrushHelper.FromHex("#F1E9DA"),
                BorderBrush = UiBrushHelper.FromHex("#2B3A44"),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Cascadia Mono")
            };

            actionButton.Margin = new Thickness(0);
            actionButton.HorizontalAlignment = HorizontalAlignment.Right;

            progressRoot.Children.Add(progressTitle);
            Grid.SetRow(progressMeta, 1);
            progressRoot.Children.Add(progressMeta);
            var centerPanel = new Grid();
            centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            centerPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            centerPanel.Children.Add(progressBar);
            var logBorder = new Border
            {
                Background = UiBrushHelper.FromHex("#12191E"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                BorderBrush = UiBrushHelper.FromHex("#26363F"),
                BorderThickness = new Thickness(1),
                Child = progressLog,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetRow(logBorder, 1);
            centerPanel.Children.Add(logBorder);
            Grid.SetRow(centerPanel, 2);
            progressRoot.Children.Add(centerPanel);
            Grid.SetRow(actionButton, 3);
            progressRoot.Children.Add(actionButton);
            progressWindow.Content = progressRoot;

            return new WorkflowProgressView(progressWindow, progressMeta, progressBar, progressLog, actionButton, maxLogLines);
        }
    }
}
