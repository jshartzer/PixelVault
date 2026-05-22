using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PixelVaultNative
{
    static class PixelVaultWindowChromeStyler
    {
        const int DwmwaUseImmersiveDarkMode = 20;
        const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;
        const int DwmwaBorderColor = 34;
        const int DwmwaCaptionColor = 35;
        const int DwmwaTextColor = 36;

        static bool installed;

        public static void Install(Application app)
        {
            if (app == null || installed) return;
            installed = true;
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
                {
                    Apply(sender as Window);
                }));
            app.Activated += delegate
            {
                foreach (Window window in app.Windows) Apply(window);
            };
        }

        public static void Apply(Window window)
        {
            if (window == null) return;
            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.BeginInvoke(new Action(delegate { Apply(window); }), DispatcherPriority.Loaded);
                return;
            }
            try
            {
                var helper = new WindowInteropHelper(window);
                var hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero) return;

                var dark = 1;
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20h1, ref dark, sizeof(int));

                var caption = ColorRef(0x0F, 0x15, 0x19);
                var border = ColorRef(0x24, 0x35, 0x3F);
                var text = ColorRef(0xD7, 0xE2, 0xEA);
                _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
            }
            catch
            {
                // Older Windows builds ignore unsupported DWM attributes; native chrome remains usable.
            }
        }

        static int ColorRef(byte r, byte g, byte b)
        {
            return r | (g << 8) | (b << 16);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
