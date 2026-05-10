using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AtlasAI.InAppAssistant.Models;

namespace AtlasAI.InAppAssistant.Services
{
    /// <summary>
    /// Captures context about the currently active Windows application
    /// </summary>
    public class WindowsContextService
    {
        private static readonly string[] BrowserProcesses = { "chrome", "firefox", "msedge", "opera", "brave", "vivaldi", "iexplore" };
        private static readonly string[] IDEProcesses = { "devenv", "code", "rider", "idea64", "pycharm64", "webstorm64", "notepad++", "sublime_text" };
        private static readonly string[] OfficeProcesses = { "winword", "excel", "powerpnt", "outlook", "onenote", "teams" };
        private static readonly string[] TerminalProcesses = { "cmd", "powershell", "pwsh", "windowsterminal", "conhost", "wt" };
        private static readonly string[] MediaProcesses = { "spotify", "vlc", "wmplayer", "groove", "itunes", "foobar2000" };

        public event EventHandler<ActiveAppContext>? ActiveAppChanged;

        private ActiveAppContext? _lastContext;
        private System.Timers.Timer? _pollTimer;

        /// <summary>
        /// Get the current active application context
        /// </summary>
        public ActiveAppContext GetActiveAppContext()
        {
            var context = new ActiveAppContext();

            try
            {
                var hwnd = GetForegroundWindow();
                context.WindowHandle = hwnd;

                // Get window title
                var titleBuilder = new StringBuilder(512);
                GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                context.WindowTitle = titleBuilder.ToString();

                // Get process info
                GetWindowThreadProcessId(hwnd, out uint processId);
                context.ProcessId = (int)processId;

                try
                {
                    var process = Process.GetProcessById(context.ProcessId);
                    context.ProcessName = process.ProcessName;
                    
                    try
                    {
                        context.ExecutablePath = process.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                        // Access denied for some system processes
                        context.ExecutablePath = "";
                    }
                }
                catch
                {
                    context.ProcessName = "Unknown";
                }

                // Categorize the app
                context.Category = CategorizeApp(context.ProcessName, context.WindowTitle);
                context.IsBrowser = context.Category == AppCategory.Browser;

                // Try to extract URL from browser title
                if (context.IsBrowser)
                {
                    ParseBrowserTitle(context);
                }

                context.CapturedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsContext] Error capturing context: {ex.Message}");
            }

            return context;
        }

        /// <summary>
        /// Start polling for active app changes
        /// </summary>
        public void StartPolling(int intervalMs = 500)
        {
            StopPolling();
            
            _pollTimer = new System.Timers.Timer(intervalMs);
            _pollTimer.Elapsed += (s, e) =>
            {
                var context = GetActiveAppContext();
                
                // Only fire event if app changed
                if (_lastContext == null || 
                    _lastContext.ProcessId != context.ProcessId ||
                    _lastContext.WindowTitle != context.WindowTitle)
                {
                    _lastContext = context;
                    ActiveAppChanged?.Invoke(this, context);
                }
            };
            _pollTimer.Start();
            Debug.WriteLine("[WindowsContext] Started polling for active app changes");
        }

        /// <summary>
        /// Stop polling
        /// </summary>
        public void StopPolling()
        {
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        /// <summary>
        /// Categorize an app based on process name and window title
        /// </summary>
        private AppCategory CategorizeApp(string processName, string windowTitle)
        {
            var lower = processName.ToLower();

            if (Array.Exists(BrowserProcesses, p => lower.Contains(p)))
                return AppCategory.Browser;

            if (lower == "explorer" && !windowTitle.Contains("File Explorer"))
                return AppCategory.System;

            if (lower == "explorer")
                return AppCategory.FileExplorer;

            if (Array.Exists(IDEProcesses, p => lower.Contains(p)))
                return AppCategory.IDE;

            if (Array.Exists(OfficeProcesses, p => lower.Contains(p)))
                return AppCategory.Office;

            if (Array.Exists(TerminalProcesses, p => lower.Contains(p)))
                return AppCategory.Terminal;

            if (Array.Exists(MediaProcesses, p => lower.Contains(p)))
                return AppCategory.MediaPlayer;

            // Check window title for additional hints
            var titleLower = windowTitle.ToLower();
            if (titleLower.Contains("notepad") || titleLower.Contains("editor") || titleLower.Contains(".txt"))
                return AppCategory.TextEditor;

            if (titleLower.Contains("discord") || titleLower.Contains("slack") || titleLower.Contains("teams"))
                return AppCategory.Communication;

            return AppCategory.Unknown;
        }

        /// <summary>
        /// Parse browser window title to extract URL/tab info
        /// Most browsers show: "Page Title - Browser Name" or "Page Title — Browser Name"
        /// </summary>
        private void ParseBrowserTitle(ActiveAppContext context)
        {
            var title = context.WindowTitle;
            
            // Common browser title separators
            var separators = new[] { " - Google Chrome", " - Mozilla Firefox", " - Microsoft Edge", 
                                     " - Opera", " - Brave", " - Vivaldi", " — ", " - " };

            foreach (var sep in separators)
            {
                var idx = title.LastIndexOf(sep, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    context.BrowserTabTitle = title.Substring(0, idx).Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(context.BrowserTabTitle))
                context.BrowserTabTitle = title;

            // Try to detect URL patterns in title (some sites show URL)
            if (context.BrowserTabTitle.Contains("://") || 
                context.BrowserTabTitle.StartsWith("www.") ||
                context.BrowserTabTitle.Contains(".com") ||
                context.BrowserTabTitle.Contains(".org"))
            {
                context.BrowserUrl = context.BrowserTabTitle;
            }
        }

        /// <summary>
        /// Check if a specific process is currently in foreground
        /// </summary>
        public bool IsProcessInForeground(string processName)
        {
            var context = GetActiveAppContext();
            return context.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get display name for an app category
        /// </summary>
        public static string GetCategoryDisplayName(AppCategory category)
        {
            return category switch
            {
                AppCategory.Browser => "🌐 Browser",
                AppCategory.FileExplorer => "📁 File Explorer",
                AppCategory.IDE => "💻 IDE",
                AppCategory.Office => "📄 Office",
                AppCategory.Terminal => "⌨️ Terminal",
                AppCategory.MediaPlayer => "🎵 Media Player",
                AppCategory.TextEditor => "📝 Text Editor",
                AppCategory.Communication => "💬 Communication",
                AppCategory.System => "⚙️ System",
                _ => "❓ Unknown"
            };
        }

        #region Win32 Imports
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        #endregion
    }
}
