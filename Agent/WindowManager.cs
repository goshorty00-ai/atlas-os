using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Window management - control windows with voice commands.
    /// </summary>
    public static class WindowManager
    {
        #region Win32 APIs
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
        
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
        
        private const int SW_MINIMIZE = 6;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        
        #endregion
        
        /// <summary>
        /// Get all visible windows
        /// </summary>
        public static List<WindowInfo> GetAllWindows()
        {
            var windows = new List<WindowInfo>();
            
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                
                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, 256);
                var titleStr = title.ToString();
                
                if (string.IsNullOrWhiteSpace(titleStr)) return true;
                
                GetWindowThreadProcessId(hWnd, out uint pid);
                string processName = "";
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                }
                catch { }
                
                GetWindowRect(hWnd, out RECT rect);
                
                windows.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = titleStr,
                    ProcessName = processName,
                    ProcessId = (int)pid,
                    Bounds = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
                });
                
                return true;
            }, IntPtr.Zero);
            
            return windows;
        }
        
        /// <summary>
        /// Find a window by name (fuzzy match)
        /// </summary>
        public static WindowInfo? FindWindow(string query)
        {
            var windows = GetAllWindows();
            var lower = query.ToLowerInvariant();
            
            // Exact title match
            var exact = windows.FirstOrDefault(w => w.Title.Equals(query, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            
            // Title contains
            var contains = windows.FirstOrDefault(w => w.Title.ToLower().Contains(lower));
            if (contains != null) return contains;
            
            // Process name match
            var procMatch = windows.FirstOrDefault(w => w.ProcessName.ToLower().Contains(lower));
            if (procMatch != null) return procMatch;
            
            return null;
        }
        
        /// <summary>
        /// Focus a window
        /// </summary>
        public static async Task<string> FocusWindowAsync(string query)
        {
            var window = FindWindow(query);
            if (window == null)
                return $"❌ Window not found: {query}";
            
            ShowWindow(window.Handle, SW_RESTORE);
            SetForegroundWindow(window.Handle);
            
            return $"✓ Focused: {window.Title}";
        }
        
        /// <summary>
        /// Minimize a window
        /// </summary>
        public static async Task<string> MinimizeWindowAsync(string query)
        {
            if (query.ToLower() == "all" || query.ToLower() == "everything")
            {
                var windows = GetAllWindows();
                foreach (var w in windows)
                    ShowWindow(w.Handle, SW_MINIMIZE);
                return $"✓ Minimized {windows.Count} windows";
            }
            
            var window = FindWindow(query);
            if (window == null)
                return $"❌ Window not found: {query}";
            
            ShowWindow(window.Handle, SW_MINIMIZE);
            return $"✓ Minimized: {window.Title}";
        }
        
        /// <summary>
        /// Maximize a window
        /// </summary>
        public static async Task<string> MaximizeWindowAsync(string query)
        {
            var window = FindWindow(query);
            if (window == null)
                return $"❌ Window not found: {query}";
            
            ShowWindow(window.Handle, SW_MAXIMIZE);
            return $"✓ Maximized: {window.Title}";
        }
        
        /// <summary>
        /// Restore a window
        /// </summary>
        public static async Task<string> RestoreWindowAsync(string query)
        {
            var window = FindWindow(query);
            if (window == null)
                return $"❌ Window not found: {query}";
            
            ShowWindow(window.Handle, SW_RESTORE);
            return $"✓ Restored: {window.Title}";
        }
        
        /// <summary>
        /// Move window to a position
        /// </summary>
        public static async Task<string> MoveWindowAsync(string query, WindowPosition position)
        {
            var window = FindWindow(query);
            if (window == null)
                return $"❌ Window not found: {query}";
            
            // Get monitor info
            var monitor = MonitorFromWindow(window.Handle, MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref monitorInfo);
            
            var workArea = monitorInfo.rcWork;
            var screenWidth = workArea.Right - workArea.Left;
            var screenHeight = workArea.Bottom - workArea.Top;
            
            int x, y, w, h;
            
            switch (position)
            {
                case WindowPosition.Left:
                    x = workArea.Left;
                    y = workArea.Top;
                    w = screenWidth / 2;
                    h = screenHeight;
                    break;
                case WindowPosition.Right:
                    x = workArea.Left + screenWidth / 2;
                    y = workArea.Top;
                    w = screenWidth / 2;
                    h = screenHeight;
                    break;
                case WindowPosition.Top:
                    x = workArea.Left;
                    y = workArea.Top;
                    w = screenWidth;
                    h = screenHeight / 2;
                    break;
                case WindowPosition.Bottom:
                    x = workArea.Left;
                    y = workArea.Top + screenHeight / 2;
                    w = screenWidth;
                    h = screenHeight / 2;
                    break;
                case WindowPosition.TopLeft:
                    x = workArea.Left;
                    y = workArea.Top;
                    w = screenWidth / 2;
                    h = screenHeight / 2;
                    break;
                case WindowPosition.TopRight:
                    x = workArea.Left + screenWidth / 2;
                    y = workArea.Top;
                    w = screenWidth / 2;
                    h = screenHeight / 2;
                    break;
                case WindowPosition.BottomLeft:
                    x = workArea.Left;
                    y = workArea.Top + screenHeight / 2;
                    w = screenWidth / 2;
                    h = screenHeight / 2;
                    break;
                case WindowPosition.BottomRight:
                    x = workArea.Left + screenWidth / 2;
                    y = workArea.Top + screenHeight / 2;
                    w = screenWidth / 2;
                    h = screenHeight / 2;
                    break;
                case WindowPosition.Center:
                    w = screenWidth * 2 / 3;
                    h = screenHeight * 2 / 3;
                    x = workArea.Left + (screenWidth - w) / 2;
                    y = workArea.Top + (screenHeight - h) / 2;
                    break;
                case WindowPosition.Fullscreen:
                    ShowWindow(window.Handle, SW_MAXIMIZE);
                    return $"✓ Maximized: {window.Title}";
                default:
                    return "❌ Unknown position";
            }
            
            ShowWindow(window.Handle, SW_RESTORE);
            MoveWindow(window.Handle, x, y, w, h, true);
            
            return $"✓ Moved {window.Title} to {position}";
        }
        
        /// <summary>
        /// Snap two windows side by side
        /// </summary>
        public static async Task<string> SnapWindowsAsync(string left, string right)
        {
            var leftWindow = FindWindow(left);
            var rightWindow = FindWindow(right);
            
            if (leftWindow == null)
                return $"❌ Window not found: {left}";
            if (rightWindow == null)
                return $"❌ Window not found: {right}";
            
            await MoveWindowAsync(left, WindowPosition.Left);
            await MoveWindowAsync(right, WindowPosition.Right);
            
            return $"✓ Snapped {leftWindow.Title} and {rightWindow.Title} side by side";
        }
        
        /// <summary>
        /// List all windows
        /// </summary>
        public static string ListWindows()
        {
            var windows = GetAllWindows();
            if (!windows.Any())
                return "No windows found";
            
            var sb = new StringBuilder();
            sb.AppendLine($"📋 Open Windows ({windows.Count}):\n");
            
            foreach (var w in windows.Take(15))
            {
                var title = w.Title.Length > 40 ? w.Title.Substring(0, 37) + "..." : w.Title;
                sb.AppendLine($"• {title} ({w.ProcessName})");
            }
            
            if (windows.Count > 15)
                sb.AppendLine($"... and {windows.Count - 15} more");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Parse position from text
        /// </summary>
        public static WindowPosition? ParsePosition(string text)
        {
            var lower = text.ToLowerInvariant();
            
            if (lower.Contains("left") && lower.Contains("top")) return WindowPosition.TopLeft;
            if (lower.Contains("right") && lower.Contains("top")) return WindowPosition.TopRight;
            if (lower.Contains("left") && lower.Contains("bottom")) return WindowPosition.BottomLeft;
            if (lower.Contains("right") && lower.Contains("bottom")) return WindowPosition.BottomRight;
            if (lower.Contains("left")) return WindowPosition.Left;
            if (lower.Contains("right")) return WindowPosition.Right;
            if (lower.Contains("top")) return WindowPosition.Top;
            if (lower.Contains("bottom")) return WindowPosition.Bottom;
            if (lower.Contains("center") || lower.Contains("middle")) return WindowPosition.Center;
            if (lower.Contains("full") || lower.Contains("max")) return WindowPosition.Fullscreen;
            
            return null;
        }
    }
    
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public int ProcessId { get; set; }
        public System.Drawing.Rectangle Bounds { get; set; }
    }
    
    public enum WindowPosition
    {
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Center,
        Fullscreen
    }
}
