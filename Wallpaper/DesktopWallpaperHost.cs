using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AtlasAI.Wallpaper
{
    internal static class DesktopWallpaperHost
    {
        private const int WM_SPAWN_WORKERW = 0x052C;
        private const uint SMTO_NORMAL = 0x0000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static bool TryAttachToDesktop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            var progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                _ = SendMessageTimeout(progman, WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
            }

            var workerw = FindDesktopWorkerW();
            if (workerw == IntPtr.Zero) return false;

            _ = SetParent(hwnd, workerw);
            return true;
        }

        private static IntPtr FindDesktopWorkerW()
        {
            IntPtr foundWorkerW = IntPtr.Zero;

            EnumWindows((topHandle, _) =>
            {
                var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView == IntPtr.Zero) return true;

                var next = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
                if (next != IntPtr.Zero)
                {
                    foundWorkerW = next;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (foundWorkerW != IntPtr.Zero) return foundWorkerW;

            var progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                var shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView != IntPtr.Zero) return progman;
            }

            EnumWindows((hWnd, _) =>
            {
                var cls = new StringBuilder(256);
                GetClassName(hWnd, cls, cls.Capacity);
                if (string.Equals(cls.ToString(), "WorkerW", StringComparison.Ordinal))
                {
                    foundWorkerW = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            return foundWorkerW;
        }
    }
}
