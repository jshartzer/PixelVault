using System;

namespace PixelVaultNative
{
    /// <summary>Orchestrates opening the Library browser; heavy UI lives in <see cref="MainWindow.ShowLibraryBrowserCore"/>.</summary>
    internal sealed class LibraryBrowserHost
    {
        readonly MainWindow _owner;

        internal LibraryBrowserHost(MainWindow owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal void Show(bool reuseMainWindow)
        {
            _owner.ShowLibraryBrowserCore(reuseMainWindow);
        }
    }

    public sealed partial class MainWindow
    {
        LibraryBrowserHost _libraryBrowserHost;

        void ShowLibraryBrowser(bool reuseMainWindow = false)
        {
            if (_libraryBrowserHost == null)
                _libraryBrowserHost = new LibraryBrowserHost(this);
            _libraryBrowserHost.Show(reuseMainWindow);
        }
    }
}
