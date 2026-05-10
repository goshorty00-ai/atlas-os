using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Enhanced app control - smarter process management with fuzzy matching.
    /// </summary>
    public static class EnhancedAppControl
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;
        
        // Extended app aliases with paths
        private static readonly Dictionary<string, AppInfo> AppDatabase = new(StringComparer.OrdinalIgnoreCase)
        {
            // Special Folders (handled separately in OpenAppAsync)
            { "documents", new AppInfo("shell:documents", "Documents", new[] { "explorer" }, true) },
            { "downloads", new AppInfo("shell:downloads", "Downloads", new[] { "explorer" }, true) },
            { "desktop", new AppInfo("shell:desktop", "Desktop", new[] { "explorer" }, true) },
            { "pictures", new AppInfo("shell:pictures", "Pictures", new[] { "explorer" }, true) },
            { "music", new AppInfo("shell:music", "Music", new[] { "explorer" }, true) },
            { "videos", new AppInfo("shell:videos", "Videos", new[] { "explorer" }, true) },
            { "my documents", new AppInfo("shell:documents", "Documents", new[] { "explorer" }, true) },
            { "my downloads", new AppInfo("shell:downloads", "Downloads", new[] { "explorer" }, true) },
            { "my pictures", new AppInfo("shell:pictures", "Pictures", new[] { "explorer" }, true) },
            { "my music", new AppInfo("shell:music", "Music", new[] { "explorer" }, true) },
            { "my videos", new AppInfo("shell:videos", "Videos", new[] { "explorer" }, true) },
            { "recent", new AppInfo("shell:recent", "Recent Files", new[] { "explorer" }, true) },
            { "recycle bin", new AppInfo("shell:RecycleBinFolder", "Recycle Bin", new[] { "explorer" }, true) },
            { "trash", new AppInfo("shell:RecycleBinFolder", "Recycle Bin", new[] { "explorer" }, true) },
            { "appdata", new AppInfo("shell:appdata", "AppData", new[] { "explorer" }, true) },
            { "startup", new AppInfo("shell:startup", "Startup Folder", new[] { "explorer" }, true) },
            { "fonts", new AppInfo("shell:fonts", "Fonts", new[] { "explorer" }, true) },
            { "programs", new AppInfo("shell:programs", "Programs", new[] { "explorer" }, true) },
            { "user folder", new AppInfo("shell:profile", "User Folder", new[] { "explorer" }, true) },
            { "home", new AppInfo("shell:profile", "User Folder", new[] { "explorer" }, true) },
            
            // Browsers
            { "chrome", new AppInfo("chrome", "Google Chrome", new[] { "chrome" }) },
            { "firefox", new AppInfo("firefox", "Mozilla Firefox", new[] { "firefox" }) },
            { "edge", new AppInfo("msedge", "Microsoft Edge", new[] { "msedge" }) },
            { "brave", new AppInfo("brave", "Brave Browser", new[] { "brave" }) },
            { "opera", new AppInfo("opera", "Opera", new[] { "opera" }) },
            
            // Communication
            { "discord", new AppInfo("discord", "Discord", new[] { "Discord" }) },
            { "slack", new AppInfo("slack", "Slack", new[] { "slack" }) },
            { "teams", new AppInfo("ms-teams:", "Microsoft Teams", new[] { "Teams", "ms-teams" }, true) },
            { "zoom", new AppInfo("zoom", "Zoom", new[] { "Zoom" }) },
            { "skype", new AppInfo("skype:", "Skype", new[] { "Skype" }, true) },
            { "telegram", new AppInfo("telegram", "Telegram", new[] { "Telegram" }) },
            { "whatsapp", new AppInfo("whatsapp:", "WhatsApp", new[] { "WhatsApp" }, true) },
            
            // Media
            { "spotify", new AppInfo("spotify", "Spotify", new[] { "Spotify" }) },
            { "vlc", new AppInfo("vlc", "VLC Media Player", new[] { "vlc" }) },
            { "itunes", new AppInfo("itunes", "iTunes", new[] { "iTunes" }) },
            
            // Dev tools
            { "vscode", new AppInfo("code", "Visual Studio Code", new[] { "Code" }) },
            { "code", new AppInfo("code", "Visual Studio Code", new[] { "Code" }) },
            { "vs", new AppInfo("devenv", "Visual Studio", new[] { "devenv" }) },
            { "visual studio", new AppInfo("devenv", "Visual Studio", new[] { "devenv" }) },
            { "rider", new AppInfo("rider64", "JetBrains Rider", new[] { "rider64" }) },
            { "pycharm", new AppInfo("pycharm64", "PyCharm", new[] { "pycharm64" }) },
            { "intellij", new AppInfo("idea64", "IntelliJ IDEA", new[] { "idea64" }) },
            { "sublime", new AppInfo("sublime_text", "Sublime Text", new[] { "sublime_text" }) },
            { "notepad++", new AppInfo("notepad++", "Notepad++", new[] { "notepad++" }) },
            
            // Terminal
            { "terminal", new AppInfo("wt", "Windows Terminal", new[] { "WindowsTerminal" }) },
            { "cmd", new AppInfo("cmd", "Command Prompt", new[] { "cmd" }) },
            { "powershell", new AppInfo("powershell", "PowerShell", new[] { "powershell" }) },
            { "git bash", new AppInfo("git-bash", "Git Bash", new[] { "bash", "mintty" }) },
            
            // Office
            { "word", new AppInfo("winword", "Microsoft Word", new[] { "WINWORD" }) },
            { "excel", new AppInfo("excel", "Microsoft Excel", new[] { "EXCEL" }) },
            { "powerpoint", new AppInfo("powerpnt", "PowerPoint", new[] { "POWERPNT" }) },
            { "outlook", new AppInfo("outlook", "Microsoft Outlook", new[] { "OUTLOOK" }) },
            { "onenote", new AppInfo("onenote", "OneNote", new[] { "ONENOTE" }) },
            
            // System
            { "explorer", new AppInfo("explorer", "File Explorer", new[] { "explorer" }) },
            { "files", new AppInfo("explorer", "File Explorer", new[] { "explorer" }) },
            { "settings", new AppInfo("ms-settings:", "Settings", new[] { "SystemSettings" }, true) },
            { "control panel", new AppInfo("control", "Control Panel", new[] { "control" }) },
            { "task manager", new AppInfo("taskmgr", "Task Manager", new[] { "Taskmgr" }) },
            { "device manager", new AppInfo("devmgmt.msc", "Device Manager", new[] { "mmc" }) },
            
            // Utils
            { "calculator", new AppInfo("calc", "Calculator", new[] { "Calculator", "calc" }) },
            { "calc", new AppInfo("calc", "Calculator", new[] { "Calculator", "calc" }) },
            { "notepad", new AppInfo("notepad", "Notepad", new[] { "notepad" }) },
            { "paint", new AppInfo("mspaint", "Paint", new[] { "mspaint" }) },
            { "snipping tool", new AppInfo("snippingtool", "Snipping Tool", new[] { "SnippingTool" }) },
            { "snip", new AppInfo("snippingtool", "Snipping Tool", new[] { "SnippingTool" }) },
            
            // Games
            { "steam", new AppInfo("steam", "Steam", new[] { "steam", "steamwebhelper" }) },
            { "epic", new AppInfo("EpicGamesLauncher", "Epic Games", new[] { "EpicGamesLauncher" }) },
            { "battle.net", new AppInfo("Battle.net", "Battle.net", new[] { "Battle.net" }) },
            { "origin", new AppInfo("Origin", "EA Origin", new[] { "Origin" }) },
            
            // Download Managers
            { "jdownloader", new AppInfo("JDownloader2", "JDownloader 2", new[] { "JDownloader2" }) },
            { "jdownloader 2", new AppInfo("JDownloader2", "JDownloader 2", new[] { "JDownloader2" }) },
            { "jdownloader2", new AppInfo("JDownloader2", "JDownloader 2", new[] { "JDownloader2" }) },
            
            // Creative
            { "photoshop", new AppInfo("Photoshop", "Adobe Photoshop", new[] { "Photoshop" }) },
            { "premiere", new AppInfo("Adobe Premiere Pro", "Premiere Pro", new[] { "Adobe Premiere Pro" }) },
            { "after effects", new AppInfo("AfterFX", "After Effects", new[] { "AfterFX" }) },
            { "blender", new AppInfo("blender", "Blender", new[] { "blender" }) },
            { "obs", new AppInfo("obs64", "OBS Studio", new[] { "obs64" }) },
        };
        
        /// <summary>
        /// Open an app with fuzzy matching
        /// </summary>
        public static bool CanResolveOpenTarget(string query)
        {
            return TryResolveOpenTarget(query, out _, out _);
        }

        public static async Task<string> OpenAppAsync(string query)
        {
            var normalizedQuery = NormalizeOpenQuery(query);
            var lower = normalizedQuery.ToLowerInvariant();

            if (TryResolveOpenTarget(normalizedQuery, out var resolvedPath, out var resolvedDisplayName) &&
                !string.IsNullOrWhiteSpace(resolvedPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(resolvedPath) { UseShellExecute = true });
                    return $"✓ Opened {resolvedDisplayName}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AppControl] Direct open error for {normalizedQuery}: {ex.Message}");
                }
            }
            
            // Direct match in our database
            if (AppDatabase.TryGetValue(lower, out var app))
            {
                return await LaunchAppAsync(app);
            }
            
            // Fuzzy match in our database
            var bestMatch = AppDatabase
                .Where(kv => kv.Key.Contains(lower) || lower.Contains(kv.Key) || 
                             kv.Value.DisplayName.ToLower().Contains(lower))
                .OrderBy(kv => LevenshteinDistance(kv.Key, lower))
                .FirstOrDefault();
            
            if (bestMatch.Value != null)
            {
                return await LaunchAppAsync(bestMatch.Value);
            }
            
            // Try InstalledAppsManager for dynamically discovered apps
            try
            {
                var installedApp = SystemControl.InstalledAppsManager.Instance.FindApp(normalizedQuery);
                if (installedApp != null)
                {
                    var result = SystemControl.InstalledAppsManager.Instance.LaunchApp(normalizedQuery);
                    if (result.Success)
                        return $"✓ Opened {installedApp.Name}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppControl] InstalledAppsManager error: {ex.Message}");
            }
            
            // Try direct launch as last resort
            try
            {
                Process.Start(new ProcessStartInfo(normalizedQuery) { UseShellExecute = true });
                return $"✓ Opened {normalizedQuery}";
            }
            catch
            {
                return $"❌ Couldn't find or open '{normalizedQuery}'. Try saying \"scan my apps\" to discover installed programs.";
            }
        }

        private static bool TryResolveOpenTarget(string query, out string? resolvedPath, out string displayName)
        {
            resolvedPath = null;
            displayName = string.Empty;

            var normalizedQuery = NormalizeOpenQuery(query);
            if (string.IsNullOrWhiteSpace(normalizedQuery))
                return false;

            if (Directory.Exists(normalizedQuery) || File.Exists(normalizedQuery))
            {
                resolvedPath = normalizedQuery;
                displayName = Path.GetFileName(normalizedQuery.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = normalizedQuery;
                return true;
            }

            if (TryResolveKnownFolderPath(normalizedQuery, out var knownFolderPath, out var knownFolderName))
            {
                resolvedPath = knownFolderPath;
                displayName = knownFolderName;
                return true;
            }

            if (TryFindInCommonUserLocations(normalizedQuery, out var foundPath))
            {
                resolvedPath = foundPath;
                displayName = Path.GetFileName(foundPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = foundPath;
                return true;
            }

            return false;
        }

        private static string NormalizeOpenQuery(string query)
        {
            var normalized = (query ?? string.Empty).Trim().Trim('"');

            foreach (var prefix in new[] { "the ", "my " })
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length).Trim();
                    break;
                }
            }

            foreach (var suffix in new[] { " folder", " directory", " file" })
            {
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length).Trim();
                    break;
                }
            }

            return normalized;
        }

        private static bool TryResolveKnownFolderPath(string query, out string resolvedPath, out string displayName)
        {
            resolvedPath = string.Empty;
            displayName = string.Empty;

            var normalized = NormalizeOpenQuery(query).ToLowerInvariant();
            var folderMap = new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
            {
                ["documents"] = Environment.SpecialFolder.MyDocuments,
                ["downloads"] = Environment.SpecialFolder.UserProfile,
                ["desktop"] = Environment.SpecialFolder.DesktopDirectory,
                ["pictures"] = Environment.SpecialFolder.MyPictures,
                ["music"] = Environment.SpecialFolder.MyMusic,
                ["videos"] = Environment.SpecialFolder.MyVideos,
                ["recent"] = Environment.SpecialFolder.Recent,
                ["appdata"] = Environment.SpecialFolder.ApplicationData,
                ["startup"] = Environment.SpecialFolder.Startup,
                ["programs"] = Environment.SpecialFolder.Programs,
                ["home"] = Environment.SpecialFolder.UserProfile,
                ["user folder"] = Environment.SpecialFolder.UserProfile,
            };

            if (folderMap.TryGetValue(normalized, out var specialFolder))
            {
                var path = Environment.GetFolderPath(specialFolder);
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    resolvedPath = path;
                    displayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = normalized;
                    return true;
                }
            }

            if (string.Equals(normalized, "downloads", StringComparison.OrdinalIgnoreCase))
            {
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadsPath))
                {
                    resolvedPath = downloadsPath;
                    displayName = "Downloads";
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindInCommonUserLocations(string query, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            var normalized = NormalizeOpenQuery(query);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var targetLower = normalized.ToLowerInvariant();
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            foreach (var root in roots)
            {
                if (TryFindFileSystemEntry(root, targetLower, maxDepth: 4, out resolvedPath))
                    return true;
            }

            return false;
        }

        private static bool TryFindFileSystemEntry(string root, string targetLower, int maxDepth, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));
            var visited = 0;
            const int maxVisitedEntries = 4000;

            while (queue.Count > 0 && visited < maxVisitedEntries)
            {
                var (currentPath, depth) = queue.Dequeue();
                visited++;

                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(currentPath))
                    {
                        visited++;
                        var entryName = Path.GetFileName(entry);
                        if (string.IsNullOrWhiteSpace(entryName))
                            continue;

                        var entryNameLower = entryName.ToLowerInvariant();
                        var entryBaseNameLower = Path.GetFileNameWithoutExtension(entryName).ToLowerInvariant();
                        if (entryNameLower == targetLower || entryBaseNameLower == targetLower)
                        {
                            resolvedPath = entry;
                            return true;
                        }

                        if (depth < maxDepth && Directory.Exists(entry))
                            queue.Enqueue((entry, depth + 1));

                        if (visited >= maxVisitedEntries)
                            break;
                    }
                }
                catch
                {
                }
            }

            return false;
        }
        
        /// <summary>
        /// Close/kill an app with fuzzy matching
        /// </summary>
        public static async Task<string> CloseAppAsync(string query)
        {
            var lower = query.ToLowerInvariant().Trim();
            
            // Get process names to kill
            string[] processNames;
            
            if (AppDatabase.TryGetValue(lower, out var app))
            {
                processNames = app.ProcessNames;
            }
            else
            {
                // Fuzzy match
                var bestMatch = AppDatabase
                    .Where(kv => kv.Key.Contains(lower) || lower.Contains(kv.Key))
                    .FirstOrDefault();
                
                if (bestMatch.Value != null)
                {
                    processNames = bestMatch.Value.ProcessNames;
                }
                else
                {
                    processNames = new[] { query };
                }
            }
            
            int killed = 0;
            foreach (var name in processNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
                    foreach (var proc in procs)
                    {
                        try
                        {
                            proc.Kill();
                            killed++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            
            return killed > 0 ? $"✓ Closed {query}" : $"❌ {query} wasn't running";
        }
        
        /// <summary>
        /// Focus an already running app
        /// </summary>
        public static async Task<string> FocusAppAsync(string query)
        {
            var lower = query.ToLowerInvariant().Trim();
            string[] processNames;
            
            if (AppDatabase.TryGetValue(lower, out var app))
            {
                processNames = app.ProcessNames;
            }
            else
            {
                // Fuzzy match against known apps, including full display names.
                var bestMatch = AppDatabase
                    .Where(kv => kv.Key.Contains(lower) || lower.Contains(kv.Key) ||
                                 kv.Value.DisplayName.ToLower().Contains(lower))
                    .OrderBy(kv => LevenshteinDistance(kv.Key, lower))
                    .FirstOrDefault();

                if (bestMatch.Value != null)
                    processNames = bestMatch.Value.ProcessNames;
                else
                    processNames = new[] { query };
            }
            
            foreach (var name in processNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
                    var proc = procs.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                    
                    if (proc != null)
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                        return $"✓ Focused {query}";
                    }
                }
                catch { }
            }
            
            return $"❌ {query} isn't running";
        }
        
        /// <summary>
        /// Minimize an app
        /// </summary>
        public static async Task<string> MinimizeAppAsync(string query)
        {
            var lower = query.ToLowerInvariant().Trim();
            string[] processNames;
            
            if (AppDatabase.TryGetValue(lower, out var app))
            {
                processNames = app.ProcessNames;
            }
            else
            {
                processNames = new[] { query };
            }
            
            foreach (var name in processNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
                    foreach (var proc in procs.Where(p => p.MainWindowHandle != IntPtr.Zero))
                    {
                        ShowWindow(proc.MainWindowHandle, SW_MINIMIZE);
                    }
                    if (procs.Any())
                        return $"✓ Minimized {query}";
                }
                catch { }
            }
            
            return $"❌ {query} isn't running";
        }
        
        /// <summary>
        /// Get currently running apps
        /// </summary>
        public static List<string> GetRunningApps()
        {
            return Process.GetProcesses()
                .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                .Select(p => p.MainWindowTitle)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }
        
        /// <summary>
        /// Get the current foreground app
        /// </summary>
        public static string? GetForegroundApp()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                GetWindowThreadProcessId(hwnd, out uint pid);
                var proc = Process.GetProcessById((int)pid);
                return proc.MainWindowTitle;
            }
            catch
            {
                return null;
            }
        }
        
        private static async Task<string> LaunchAppAsync(AppInfo app)
        {
            try
            {
                // Handle shell: protocol for special folders
                if (app.Command.StartsWith("shell:"))
                {
                    var psi = new ProcessStartInfo("explorer.exe", app.Command) { UseShellExecute = true };
                    Process.Start(psi);
                    return $"✓ Opened {app.DisplayName}";
                }
                
                // Handle ms- and other URI protocols
                if (app.IsProtocol || app.Command.Contains(":"))
                {
                    var psi = new ProcessStartInfo(app.Command) { UseShellExecute = true };
                    Process.Start(psi);
                    return $"✓ Opened {app.DisplayName}";
                }
                
                // Try to find the actual executable path for common apps
                var actualPath = FindAppExecutable(app.Command, app.DisplayName);
                if (!string.IsNullOrEmpty(actualPath))
                {
                    var psi = new ProcessStartInfo(actualPath) { UseShellExecute = true };
                    Process.Start(psi);
                    return $"✓ Opened {app.DisplayName}";
                }
                
                // Try direct launch (works for apps in PATH)
                var startInfo = new ProcessStartInfo(app.Command) { UseShellExecute = true };
                Process.Start(startInfo);
                return $"✓ Opened {app.DisplayName}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppControl] Launch error for {app.DisplayName}: {ex.Message}");
                
                // Try InstalledAppsManager as fallback
                try
                {
                    var result = SystemControl.InstalledAppsManager.Instance.LaunchApp(app.DisplayName);
                    if (result.Success)
                        return result.Message;
                }
                catch { }
                
                return $"❌ Couldn't open {app.DisplayName}";
            }
        }
        
        /// <summary>
        /// Find the actual executable path for common apps
        /// </summary>
        private static string? FindAppExecutable(string command, string displayName)
        {
            var lower = command.ToLowerInvariant();
            
            // Spotify - check multiple locations
            if (lower == "spotify")
            {
                var paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe"),
                    @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_*\Spotify.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Spotify", "Spotify.exe"),
                };
                
                foreach (var path in paths)
                {
                    if (path.Contains("*"))
                    {
                        // Handle wildcard for Windows Store apps
                        var dir = Path.GetDirectoryName(path);
                        var pattern = Path.GetFileName(path.Replace("*", ""));
                        if (dir != null && Directory.Exists(Path.GetDirectoryName(dir)))
                        {
                            try
                            {
                                var parentDir = Path.GetDirectoryName(dir);
                                var searchPattern = Path.GetFileName(dir);
                                if (parentDir != null)
                                {
                                    var matches = Directory.GetDirectories(parentDir, searchPattern.Replace("*", "*"));
                                    foreach (var match in matches)
                                    {
                                        var exe = Path.Combine(match, "Spotify.exe");
                                        if (File.Exists(exe)) return exe;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    else if (File.Exists(path))
                    {
                        return path;
                    }
                }
                
                // Try Spotify URI protocol as last resort
                return "spotify:";
            }
            
            // Discord - special handling
            if (lower == "discord")
            {
                var discordPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
                if (Directory.Exists(discordPath))
                {
                    var updateExe = Path.Combine(discordPath, "Update.exe");
                    if (File.Exists(updateExe))
                    {
                        // Discord uses Update.exe --processStart Discord.exe
                        return updateExe;
                    }
                }
            }
            
            // VS Code
            if (lower == "code")
            {
                var paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
                    @"C:\Program Files\Microsoft VS Code\Code.exe",
                    @"C:\Program Files (x86)\Microsoft VS Code\Code.exe",
                };
                foreach (var path in paths)
                    if (File.Exists(path)) return path;
            }
            
            // Chrome
            if (lower == "chrome")
            {
                var paths = new[]
                {
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
                };
                foreach (var path in paths)
                    if (File.Exists(path)) return path;
            }
            
            // Firefox
            if (lower == "firefox")
            {
                var paths = new[]
                {
                    @"C:\Program Files\Mozilla Firefox\firefox.exe",
                    @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
                };
                foreach (var path in paths)
                    if (File.Exists(path)) return path;
            }
            
            // Steam
            if (lower == "steam")
            {
                var paths = new[]
                {
                    @"C:\Program Files (x86)\Steam\steam.exe",
                    @"C:\Program Files\Steam\steam.exe",
                    @"D:\Steam\steam.exe",
                };
                foreach (var path in paths)
                    if (File.Exists(path)) return path;
            }
            
            // OBS
            if (lower == "obs64" || lower == "obs")
            {
                var paths = new[]
                {
                    @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
                    @"C:\Program Files (x86)\obs-studio\bin\64bit\obs64.exe",
                };
                foreach (var path in paths)
                    if (File.Exists(path)) return path;
            }
            
            // Blender - check for both blender.exe and blender-launcher.exe (newer versions)
            if (lower == "blender")
            {
                var blenderDir = @"C:\Program Files\Blender Foundation";
                if (Directory.Exists(blenderDir))
                {
                    try
                    {
                        // Get all version folders, sorted descending (newest first)
                        var versions = Directory.GetDirectories(blenderDir)
                            .OrderByDescending(v => v);
                        
                        foreach (var ver in versions)
                        {
                            // Try blender-launcher.exe first (Blender 4.0+)
                            var launcher = Path.Combine(ver, "blender-launcher.exe");
                            if (File.Exists(launcher)) return launcher;
                            
                            // Fall back to blender.exe (older versions)
                            var exe = Path.Combine(ver, "blender.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                    catch { }
                }
                
                // Also check common alternative locations
                var altPaths = new[]
                {
                    @"C:\Program Files (x86)\Blender Foundation",
                    @"D:\Blender",
                    @"D:\Program Files\Blender Foundation",
                };
                foreach (var altDir in altPaths)
                {
                    if (!Directory.Exists(altDir)) continue;
                    try
                    {
                        var versions = Directory.GetDirectories(altDir).OrderByDescending(v => v);
                        foreach (var ver in versions)
                        {
                            var launcher = Path.Combine(ver, "blender-launcher.exe");
                            if (File.Exists(launcher)) return launcher;
                            var exe = Path.Combine(ver, "blender.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                    catch { }
                }
            }
            
            // JDownloader
            if (lower == "jdownloader2" || lower == "jdownloader" || lower.Contains("jdownloader"))
            {
                var paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JDownloader 2", "JDownloader2.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "JDownloader 2.0", "JDownloader2.exe"),
                    @"C:\JDownloader 2.0\JDownloader2.exe",
                    @"C:\Program Files\JDownloader\JDownloader2.exe",
                    @"C:\Program Files (x86)\JDownloader\JDownloader2.exe",
                };
                foreach (var path in paths)
                    if (File.Exists(path)) return path;
            }
            
            return null;
        }
        
        private static int LevenshteinDistance(string s1, string s2)
        {
            int[,] d = new int[s1.Length + 1, s2.Length + 1];
            for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) d[0, j] = j;
            
            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[s1.Length, s2.Length];
        }
    }
    
    public class AppInfo
    {
        public string Command { get; }
        public string DisplayName { get; }
        public string[] ProcessNames { get; }
        public bool IsProtocol { get; }
        
        public AppInfo(string command, string displayName, string[] processNames, bool isProtocol = false)
        {
            Command = command;
            DisplayName = displayName;
            ProcessNames = processNames;
            IsProtocol = isProtocol;
        }
    }
}
