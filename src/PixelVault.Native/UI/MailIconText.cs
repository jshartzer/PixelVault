using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    // Interpret only known game icon tags, never arbitrary markup or paths.
    static class MailIconText
    {
        static readonly Regex Token = new Regex(@"<icon\s+name=\s*(faceicon_(?:bad|cry|death|good|love|please|smile|sweat|think|whale)|gaiji_tutorial_prio_(?:etc|quality|quantity|speed|bridgelink))\s*>", RegexOptions.Compiled);
        static readonly Dictionary<string, BitmapImage> Images = new Dictionary<string, BitmapImage>();
        public static bool HasIcons(string text) => Token.IsMatch(text ?? "");
        public static string Plain(string text) => Token.Replace(text ?? "", m => "[" + m.Groups[1].Value.Replace("faceicon_", "").Replace("gaiji_tutorial_prio_", "") + "]");
        public static void Fill(InlineCollection inlines, string text, double size)
        {
            text ??= ""; int start = 0;
            foreach (Match match in Token.Matches(text))
            {
                inlines.Add(new Run(text.Substring(start, match.Index - start)));
                var name = match.Groups[1].Value;
                if (!Images.TryGetValue(name, out var bitmap))
                {
                    using var stream = typeof(MailIconText).Assembly.GetManifestResourceStream("PixelVault.MailIcons." + name + ".png");
                    if (stream != null)
                    {
                        bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Images[name] = bitmap;
                    }
                }
                if (bitmap == null || SystemParameters.HighContrast) inlines.Add(new Run(Plain(match.Value)));
                else
                {
                    var image = new Image { Source = bitmap, Width = size * 1.3, Height = size * 1.3, ToolTip = Plain(match.Value), Margin = new Thickness(1,0,1,0) };
                    System.Windows.Automation.AutomationProperties.SetName(image, Plain(match.Value));
                    inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
                }
                start = match.Index + match.Length;
            }
            inlines.Add(new Run(text.Substring(start)));
        }
        public static TextBlock Apply(TextBlock block, string text, double iconScale = 1)
        {
            if (HasIcons(text)) { block.Text = ""; Fill(block.Inlines, text, block.FontSize * iconScale); }
            return block;
        }
    }
}
