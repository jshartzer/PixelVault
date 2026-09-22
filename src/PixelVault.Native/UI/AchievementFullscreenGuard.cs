using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PixelVaultNative
{
    internal static class AchievementFullscreenGuard
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        internal static IDisposable Watch(Action changed) => new Watcher(changed);
        sealed class Watcher : IDisposable
        {
            readonly WinEventProc callback;
            readonly IntPtr foregroundHook, locationHook;
            public Watcher(Action changed)
            {
                callback = (_, ev, hwnd, obj, child, thread, time) =>
                {
                    if (ev == 3 || hwnd == GetForegroundWindow()) changed();
                };
                foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
                locationHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, 0, 0, 2);
            }
            public void Dispose()
            {
                if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
                if (locationHook != IntPtr.Zero) UnhookWinEvent(locationHook);
                GC.KeepAlive(callback);
            }
        }
        internal static bool CoversMonitor(Rect window, Rect monitor) => window.Left <= monitor.Left && window.Top <= monitor.Top
            && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        internal static bool Suppress()
        {
            // Respect fullscreen Direct3D/presentation, session lock and Windows quiet periods too.
            if (SHQueryUserNotificationState(out var state) != 0 || state != 5) return true;
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return true;
            if (foreground == GetDesktopWindow() || foreground == GetShellWindow()) return false;
            var name = new StringBuilder(256);
            GetClassName(foreground, name, name.Capacity);
            if (name.ToString() == "WorkerW" || name.ToString() == "Progman") return false;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetWindowRect(foreground, out var rect) || !GetMonitorInfo(MonitorFromWindow(foreground, 2), ref info)) return true;
            return CoversMonitor(rect, info.Monitor);
        }
    }
}
