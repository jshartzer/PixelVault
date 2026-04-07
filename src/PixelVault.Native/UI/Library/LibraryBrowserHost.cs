using System;
using System.Windows;

namespace PixelVaultNative
{
    /// <summary>
    /// Phase E host: owns opening the Library browser (try/catch, logging) and exposes <see cref="ILibrarySession"/>.
    /// Orchestration body: <see cref="MainWindow.LibraryBrowserShowOrchestration"/> with <see cref="ILibraryBrowserShell"/> (<see cref="MainWindow.LibraryBrowserShellBridge"/>).
    /// </summary>
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
            try
            {
                new MainWindow.LibraryBrowserShowOrchestration(new MainWindow.LibraryBrowserShellBridge(_owner)).Run(reuseMainWindow);
            }
            catch (Exception ex)
            {
                _owner.LogException("LibraryBrowserHost.Show", ex);
                _owner.TryLibraryToast(ex.Message, MessageBoxImage.Error);
            }
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
