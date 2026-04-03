using System;

namespace PixelVaultNative
{
    /// <summary>Orchestrates opening the Library browser; heavy UI lives in <see cref="MainWindow.ShowLibraryBrowserCore"/> (<c>MainWindow.LibraryBrowserOrchestrator.cs</c>).</summary>
    internal sealed class LibraryBrowserHost
    {
        readonly MainWindow _owner;
        readonly ILibrarySession _session;

        internal LibraryBrowserHost(MainWindow owner, ILibrarySession session)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal ILibrarySession Session => _session;

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
                _libraryBrowserHost = new LibraryBrowserHost(this, librarySession);
            _libraryBrowserHost.Show(reuseMainWindow);
        }
    }
}
