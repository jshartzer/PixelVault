using System.Windows.Media;

namespace PixelVaultNative
{
    /// <summary>
    /// Shared WPF brush helpers (Phase B1). Keeps hex parsing in one place for MainWindow and extracted UI types.
    /// </summary>
    static class UiBrushHelper
    {
        public static SolidColorBrush FromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
