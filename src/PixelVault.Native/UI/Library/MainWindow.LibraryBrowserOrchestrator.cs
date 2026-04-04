using System;
using System.Windows;

namespace PixelVaultNative
{
    // Library browser: nav/toolbar/pane wiring lives in LibraryBrowserShowOrchestration; render/layout in sibling partials.
    public sealed partial class MainWindow
    {
        internal void ShowLibraryBrowserCore(bool reuseMainWindow = false)
        {
            try
            {
                new LibraryBrowserShowOrchestration(this).Run(reuseMainWindow);
            }
            catch (Exception ex)
            {
                LogException("ShowLibraryBrowserCore", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
