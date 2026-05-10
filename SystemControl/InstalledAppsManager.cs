using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.SystemControl
{
    /// <summary>
    /// Manages installed applications - scans, remembers, and launches apps.
    /// Can open ANY installed program on the PC.
    /// </summary>
    public class InstalledAppsManager
    {
        private static InstalledAppsManager? _instance;
        public static InstalledAppsManager Instance => _instance ??= new InstalledAppsManager();
        
        private Dictionary<string, InstalledApp> _apps = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _cacheFile;
        private DateTime _lastScan = DateTime.MinValue;
        private FileSystemWatcher? _startMenuWatcher;
        private FileSystemWatcher? _desktopWatcher;
        
        public event Action<string>? AppInstalled;
        public event Action<string>? AppRemoved;
        
        public int AppCount => _apps.Count;
        
        // COM interfaces for proper shortcut reading
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }
        
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
        
        private InstalledAppsManager()
        {
            _cacheFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AtlasAI", "installed_apps.json");
            
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            LoadCache();
        }
        
        public async Task InitializeAsync()
        {
            await ScanAllAppsAsync();
            SetupFileWatchers();
        }
        
        public async Task ScanAllAppsAsync()
        {
            await Task.Run(() =>
            {
                var newApps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
                
                // Priority order: Start Menu shortcuts have the most accurate paths
                ScanStartMenu(newApps);
                ScanDesktop(newApps);
                ScanUserInstalledApps(newApps);  // AppData apps like Spotify, Discord
                ScanRegistry(newApps);
                ScanProgramFolders(newApps);
                ScanWindowsApps(newApps);  // UWP/Store apps
                AddBuiltInApps(newApps);
                
                _apps = newApps;
                _lastScan = DateTime.Now;
                SaveCache();
                
                Debug.WriteLine($"[InstalledApps] Scanned {_apps.Count} applications");
            });
        }
        
        /// <summary>
        /// Scan user-installed apps in AppData (Spotify, Discord, Slack, etc.)
        /// </summary>
        private void ScanUserInstalledApps(Dictionary<string, InstalledApp> apps)
        {
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            // Common user-installed app patterns
            var userAppPaths = new[]
            {
                // Electron/Update.exe apps
                (Path.Combine(appDataLocal, "Discord"), "Discord", "Update.exe", "--processStart Discord.exe"),
                (Path.Combine(appDataLocal, "slack"), "Slack", "slack.exe", ""),
                (Path.Combine(appDataLocal, "Microsoft", "Teams"), "Microsoft Teams", "Update.exe", "--processStart Teams.exe"),
                (Path.Combine(appDataLocal, "GitHubDesktop"), "GitHub Desktop", "GitHubDesktop.exe", ""),
                (Path.Combine(appDataLocal, "Postman"), "Postman", "Postman.exe", ""),
                
                // Roaming apps
                (Path.Combine(appDataRoaming, "Spotify"), "Spotify", "Spotify.exe", ""),
                (Path.Combine(appDataRoaming, "Telegram Desktop"), "Telegram", "Telegram.exe", ""),
                (Path.Combine(appDataRoaming, "Zoom"), "Zoom", "bin\\Zoom.exe", ""),
                
                // Programs folder in LocalAppData
                (Path.Combine(appDataLocal, "Programs", "Microsoft VS Code"), "Visual Studio Code", "Code.exe", ""),
                (Path.Combine(appDataLocal, "Programs", "cursor"), "Cursor", "Cursor.exe", ""),
                (Path.Combine(appDataLocal, "Programs", "Notion"), "Notion", "Notion.exe", ""),
                (Path.Combine(appDataLocal, "Programs", "Figma"), "Figma", "Figma.exe", ""),
                (Path.Combine(appDataLocal, "Programs", "obsidian"), "Obsidian", "Obsidian.exe", ""),
            };
            
            foreach (var (basePath, name, exeName, args) in userAppPaths)
            {
                if (!Directory.Exists(basePath)) continue;
                
                var exePath = Path.Combine(basePath, exeName);
                if (File.Exists(exePath))
                {
                    var key = name.ToLower();
                    if (!apps.ContainsKey(key))
                    {
                        apps[key] = new InstalledApp
                        {
                            Name = name,
                            ExecutablePath = exePath,
                            LaunchArgs = args,
                            Source = "UserApp",
                            IsUWP = false,
                            LastSeen = DateTime.Now
                        };
                        
                        // Add common aliases
                        var aliases = GenerateAliases(name);
                        foreach (var alias in aliases)
                            if (!apps.ContainsKey(alias))
                                apps[alias] = apps[key];
                    }
                }
            }
            
            // Scan Programs folder for other apps
            var programsFolder = Path.Combine(appDataLocal, "Programs");
            if (Directory.Exists(programsFolder))
            {
                foreach (var dir in Directory.GetDirectories(programsFolder))
                {
                    try
                    {
                        var dirName = Path.GetFileName(dir);
                        var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                            .Where(e => !IsUninstallerExe(e))
                            .ToList();
                        
                        var mainExe = exes.FirstOrDefault(e =>
                            Path.GetFileNameWithoutExtension(e).Equals(dirName, StringComparison.OrdinalIgnoreCase)) ??
                            exes.FirstOrDefault();
                        
                        if (mainExe != null)
                            AddApp(apps, dirName, mainExe, "UserPrograms");
                    }
                    catch { }
                }
            }
        }
        
        /// <summary>
        /// Scan Windows Store / UWP apps
        /// </summary>
        private void ScanWindowsApps(Dictionary<string, InstalledApp> apps)
        {
            // Common UWP apps with their protocol handlers
            var uwpApps = new Dictionary<string, (string Protocol, string Name)>
            {
                { "calculator", ("calculator:", "Calculator") },
                { "photos", ("ms-photos:", "Photos") },
                { "camera", ("microsoft.windows.camera:", "Camera") },
                { "mail", ("mailto:", "Mail") },
                { "calendar", ("outlookcal:", "Calendar") },
                { "store", ("ms-windows-store:", "Microsoft Store") },
                { "xbox", ("xbox:", "Xbox") },
                { "groove", ("mswindowsmusic:", "Groove Music") },
                { "movies", ("mswindowsvideo:", "Movies & TV") },
                { "maps", ("bingmaps:", "Maps") },
                { "weather", ("bingweather:", "Weather") },
                { "news", ("bingnews:", "News") },
                { "alarms", ("ms-clock:", "Alarms & Clock") },
                { "clock", ("ms-clock:", "Alarms & Clock") },
                { "voice recorder", ("ms-voicerecorder:", "Voice Recorder") },
                { "sticky notes", ("ms-stickynotes:", "Sticky Notes") },
                { "snip & sketch", ("ms-screensketch:", "Snip & Sketch") },
                { "screen sketch", ("ms-screensketch:", "Snip & Sketch") },
                { "your phone", ("ms-phone:", "Your Phone") },
                { "phone link", ("ms-phone:", "Phone Link") },
                { "feedback hub", ("feedback-hub:", "Feedback Hub") },
                { "tips", ("ms-get-started:", "Tips") },
                { "whiteboard", ("ms-whiteboard-cmd:", "Whiteboard") },
            };
            
            foreach (var (key, (protocol, name)) in uwpApps)
            {
                if (!apps.ContainsKey(key))
                {
                    apps[key] = new InstalledApp
                    {
                        Name = name,
                        ExecutablePath = protocol,
                        Source = "UWP",
                        IsUWP = true,
                        LastSeen = DateTime.Now
                    };
                }
            }
        }
        
        private bool IsUninstallerExe(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLower();
            return name.Contains("unins") || name.Contains("uninst") || name.Contains("uninstall") ||
                   name.Contains("update") || name.StartsWith("au_") || name == "au";
        }

        
        private void ScanStartMenu(Dictionary<string, InstalledApp> apps)
        {
            var startMenuPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };
            
            foreach (var startMenu in startMenuPaths)
            {
                if (!Directory.Exists(startMenu)) continue;
                
                try
                {
                    foreach (var lnk in Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var name = Path.GetFileNameWithoutExtension(lnk);
                            var nameLower = name.ToLowerInvariant();
                            
                            // Skip uninstallers, updaters, and other non-app shortcuts
                            if (nameLower.Contains("uninstall") || nameLower.Contains("uninst") ||
                                nameLower.Contains("update") || nameLower.Contains("updater") ||
                                nameLower.Contains("remove") || nameLower.Contains("readme") ||
                                nameLower.Contains("help") || nameLower.Contains("manual") ||
                                nameLower.Contains("license") || nameLower.Contains("changelog") ||
                                nameLower.Contains("release notes") || nameLower.Contains("website") ||
                                nameLower.Contains("support") || nameLower.Contains("documentation"))
                                continue;
                            
                            var target = GetShortcutTarget(lnk);
                            if (string.IsNullOrEmpty(target)) continue;
                            
                            // Handle non-exe targets (URLs, protocols, etc.)
                            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                // Could be a protocol handler
                                if (target.Contains(":") && !target.Contains("\\"))
                                {
                                    AddApp(apps, name, target, "StartMenu");
                                    apps[name.ToLower()].IsUWP = true;
                                }
                                continue;
                            }
                            
                            if (!File.Exists(target)) continue;
                            
                            // Check the target exe name for uninstallers
                            var exeName = Path.GetFileNameWithoutExtension(target).ToLowerInvariant();
                            if (IsUninstallerExe(target))
                                continue;
                            
                            // Get launch args if stored
                            string? args = null;
                            if (_shortcutArgs.TryGetValue(lnk, out var storedArgs))
                                args = storedArgs;
                            
                            AddApp(apps, name, target, "StartMenu", args);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        
        private void ScanDesktop(Dictionary<string, InstalledApp> apps)
        {
            var desktopPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };
            
            foreach (var desktop in desktopPaths)
            {
                if (!Directory.Exists(desktop)) continue;
                
                try
                {
                    foreach (var lnk in Directory.GetFiles(desktop, "*.lnk"))
                    {
                        try
                        {
                            var target = GetShortcutTarget(lnk);
                            if (string.IsNullOrEmpty(target) || !File.Exists(target)) continue;
                            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            
                            var name = Path.GetFileNameWithoutExtension(lnk);
                            AddApp(apps, name, target, "Desktop");
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        
        private void ScanRegistry(Dictionary<string, InstalledApp> apps)
        {
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            
            foreach (var regPath in registryPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath);
                    if (key == null) continue;
                    
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;
                            
                            var displayName = subKey.GetValue("DisplayName") as string;
                            var installLocation = subKey.GetValue("InstallLocation") as string;
                            var displayIcon = subKey.GetValue("DisplayIcon") as string;
                            
                            if (string.IsNullOrEmpty(displayName)) continue;
                            
                            string? exePath = null;
                            
                            if (!string.IsNullOrEmpty(displayIcon) && displayIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                exePath = displayIcon.Split(',')[0].Trim('"');
                            }
                            else if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                var exes = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                                exePath = exes.FirstOrDefault(e => 
                                    Path.GetFileNameWithoutExtension(e).Contains(displayName.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
                                if (exePath == null && exes.Length > 0)
                                    exePath = exes[0];
                            }
                            
                            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                            {
                                AddApp(apps, displayName, exePath, "Registry");
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        
        private void ScanProgramFolders(Dictionary<string, InstalledApp> apps)
        {
            var programPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"D:\Program Files", @"D:\Games", @"D:\Steam", @"D:\SteamLibrary\steamapps\common",
                @"C:\Program Files (x86)\Steam\steamapps\common"
            };
            
            foreach (var programPath in programPaths.Distinct())
            {
                if (!Directory.Exists(programPath)) continue;
                
                try
                {
                    foreach (var dir in Directory.GetDirectories(programPath))
                    {
                        try
                        {
                            var dirName = Path.GetFileName(dir);
                            if (dirName.StartsWith("Windows") || dirName == "Common Files") continue;
                            
                            var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                                .Where(e => !Path.GetFileName(e).Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                                           !Path.GetFileName(e).Contains("update", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            
                            var mainExe = exes.FirstOrDefault(e => 
                                Path.GetFileNameWithoutExtension(e).Equals(dirName, StringComparison.OrdinalIgnoreCase));
                            
                            if (mainExe == null)
                                mainExe = exes.FirstOrDefault(e => 
                                    Path.GetFileNameWithoutExtension(e).Contains(dirName.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
                            
                            if (mainExe == null && exes.Count > 0)
                                mainExe = exes[0];
                            
                            if (mainExe != null)
                                AddApp(apps, dirName, mainExe, "ProgramFiles");
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        
        private void AddBuiltInApps(Dictionary<string, InstalledApp> apps)
        {
            // Common Windows apps and their paths/commands
            var builtIn = new Dictionary<string, (string Path, bool IsUWP)>
            {
                { "notepad", ("notepad.exe", false) },
                { "calculator", ("calc.exe", false) },
                { "calc", ("calc.exe", false) },
                { "paint", ("mspaint.exe", false) },
                { "wordpad", ("wordpad.exe", false) },
                { "snipping tool", ("snippingtool.exe", false) },
                { "file explorer", ("explorer.exe", false) },
                { "explorer", ("explorer.exe", false) },
                { "cmd", ("cmd.exe", false) },
                { "command prompt", ("cmd.exe", false) },
                { "powershell", ("powershell.exe", false) },
                { "terminal", ("wt.exe", false) },
                { "windows terminal", ("wt.exe", false) },
                { "task manager", ("taskmgr.exe", false) },
                { "control panel", ("control.exe", false) },
                { "settings", ("ms-settings:", true) },
                { "edge", ("msedge.exe", false) },
                { "microsoft edge", ("msedge.exe", false) },
            };
            
            foreach (var (name, (path, isUwp)) in builtIn)
            {
                if (!apps.ContainsKey(name))
                {
                    apps[name] = new InstalledApp
                    {
                        Name = name,
                        ExecutablePath = path,
                        Source = "BuiltIn",
                        IsUWP = isUwp,
                        LastSeen = DateTime.Now
                    };
                }
            }
            
            // Add common apps with known paths
            AddKnownApp(apps, "chrome", "Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
            AddKnownApp(apps, "firefox", "Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe");
            AddKnownApp(apps, "spotify", "Spotify", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"));
            AddKnownApp(apps, "discord", "Discord", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord", "Update.exe"));
            AddKnownApp(apps, "steam", "Steam", @"C:\Program Files (x86)\Steam\steam.exe");
            AddKnownApp(apps, "vscode", "Visual Studio Code", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"));
            AddKnownApp(apps, "visual studio code", "Visual Studio Code", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"));
            
            // Blender - check for launcher (4.0+) or regular exe
            AddBlenderApp(apps);
        }
        
        private void AddBlenderApp(Dictionary<string, InstalledApp> apps)
        {
            if (apps.ContainsKey("blender")) return;
            
            var blenderDirs = new[] { @"C:\Program Files\Blender Foundation", @"C:\Program Files (x86)\Blender Foundation" };
            foreach (var blenderDir in blenderDirs)
            {
                if (!Directory.Exists(blenderDir)) continue;
                try
                {
                    var versions = Directory.GetDirectories(blenderDir).OrderByDescending(v => v);
                    foreach (var ver in versions)
                    {
                        // Try blender-launcher.exe first (Blender 4.0+)
                        var launcher = Path.Combine(ver, "blender-launcher.exe");
                        if (File.Exists(launcher))
                        {
                            apps["blender"] = new InstalledApp { Name = "Blender", ExecutablePath = launcher, Source = "Known", IsUWP = false, LastSeen = DateTime.Now };
                            return;
                        }
                        // Fall back to blender.exe
                        var exe = Path.Combine(ver, "blender.exe");
                        if (File.Exists(exe))
                        {
                            apps["blender"] = new InstalledApp { Name = "Blender", ExecutablePath = exe, Source = "Known", IsUWP = false, LastSeen = DateTime.Now };
                            return;
                        }
                    }
                }
                catch { }
            }
        }
        
        private void AddKnownApp(Dictionary<string, InstalledApp> apps, string key, string name, string path)
        {
            if (!apps.ContainsKey(key) && File.Exists(path))
            {
                apps[key] = new InstalledApp { Name = name, ExecutablePath = path, Source = "Known", IsUWP = false, LastSeen = DateTime.Now };
            }
        }
        
        private void AddApp(Dictionary<string, InstalledApp> apps, string name, string exePath, string source, string? launchArgs = null)
        {
            var key = name.ToLower().Trim();
            if (apps.ContainsKey(key)) return;
            
            apps[key] = new InstalledApp
            {
                Name = name,
                ExecutablePath = exePath,
                LaunchArgs = launchArgs ?? "",
                Source = source,
                IsUWP = false,
                LastSeen = DateTime.Now
            };
            
            // Add aliases
            var aliases = GenerateAliases(name);
            foreach (var alias in aliases)
            {
                if (!apps.ContainsKey(alias))
                    apps[alias] = apps[key];
            }
            
            // Also add by exe name
            var exeName = Path.GetFileNameWithoutExtension(exePath).ToLower();
            if (!apps.ContainsKey(exeName) && exeName != key)
                apps[exeName] = apps[key];
        }
        
        private List<string> GenerateAliases(string name)
        {
            var aliases = new List<string>();
            var lower = name.ToLower();
            
            var cleaned = lower.Replace(" - shortcut", "").Replace(" shortcut", "")
                .Replace(" (x64)", "").Replace(" (x86)", "").Replace(" (64-bit)", "").Replace(" (32-bit)", "").Trim();
            
            if (cleaned != lower) aliases.Add(cleaned);
            
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1) aliases.Add(words[0]);
            
            // Common abbreviations
            if (cleaned.Contains("visual studio code")) aliases.Add("vscode");
            if (cleaned.Contains("visual studio") && !cleaned.Contains("code")) aliases.Add("vs");
            if (cleaned.Contains("google chrome")) { aliases.Add("chrome"); aliases.Add("browser"); }
            if (cleaned.Contains("mozilla firefox")) { aliases.Add("firefox"); }
            if (cleaned.Contains("microsoft edge")) { aliases.Add("edge"); }
            
            return aliases;
        }
        
        // Simple .lnk parser - try COM first, fall back to binary parsing
        private string? GetShortcutTarget(string lnkPath)
        {
            // Try COM-based approach first (more reliable)
            try
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(lnkPath, 0);
                
                var pathBuilder = new StringBuilder(260);
                var argsBuilder = new StringBuilder(1024);
                
                link.GetPath(pathBuilder, pathBuilder.Capacity, IntPtr.Zero, 0);
                link.GetArguments(argsBuilder, argsBuilder.Capacity);
                
                var target = pathBuilder.ToString();
                var args = argsBuilder.ToString();
                
                // Store arguments for special launchers
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    // Handle special cases where we need to store launch args
                    if (!string.IsNullOrEmpty(args))
                    {
                        _shortcutArgs[lnkPath] = args;
                    }
                    return target;
                }
            }
            catch { }
            
            // Fall back to binary parsing
            try
            {
                using var fs = new FileStream(lnkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                
                fs.Seek(0x14, SeekOrigin.Begin);
                var flags = br.ReadUInt32();
                
                fs.Seek(0x4C, SeekOrigin.Begin);
                
                if ((flags & 1) == 1)
                {
                    var idListSize = br.ReadUInt16();
                    fs.Seek(idListSize, SeekOrigin.Current);
                }
                
                var fileInfoStart = fs.Position;
                var fileInfoSize = br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                var localPathOffset = br.ReadUInt32();
                
                fs.Seek(fileInfoStart + localPathOffset, SeekOrigin.Begin);
                
                var pathBytes = new List<byte>();
                byte b;
                while ((b = br.ReadByte()) != 0 && pathBytes.Count < 260)
                    pathBytes.Add(b);
                
                return Encoding.Default.GetString(pathBytes.ToArray());
            }
            catch
            {
                return null;
            }
        }
        
        private Dictionary<string, string> _shortcutArgs = new();

        
        private void SetupFileWatchers()
        {
            try
            {
                var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                if (Directory.Exists(startMenu))
                {
                    _startMenuWatcher = new FileSystemWatcher(startMenu, "*.lnk")
                    {
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };
                    _startMenuWatcher.Created += OnShortcutCreated;
                }
                
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(desktop))
                {
                    _desktopWatcher = new FileSystemWatcher(desktop, "*.lnk") { EnableRaisingEvents = true };
                    _desktopWatcher.Created += OnShortcutCreated;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstalledApps] Watcher error: {ex.Message}");
            }
        }
        
        private async void OnShortcutCreated(object sender, FileSystemEventArgs e)
        {
            await Task.Delay(1000);
            try
            {
                var target = GetShortcutTarget(e.FullPath);
                if (!string.IsNullOrEmpty(target) && File.Exists(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(e.Name);
                    AddApp(_apps, name!, target, "NewInstall");
                    SaveCache();
                    AppInstalled?.Invoke(name!);
                }
            }
            catch { }
        }
        
        public InstalledApp? FindApp(string query)
        {
            var lower = query.ToLower().Trim();
            
            // 1. Direct exact match
            if (_apps.TryGetValue(lower, out var exact) && !IsUninstaller(exact))
                return exact;
            
            // 2. Try without common suffixes/prefixes
            var cleaned = lower
                .Replace("open ", "")
                .Replace("launch ", "")
                .Replace("start ", "")
                .Replace("run ", "")
                .Trim();
            
            if (_apps.TryGetValue(cleaned, out var cleanMatch) && !IsUninstaller(cleanMatch))
                return cleanMatch;
            
            // 3. Partial match - query contains key or key contains query
            var partialMatches = _apps
                .Where(a => !IsUninstaller(a.Value) && 
                           (a.Key.Contains(lower) || lower.Contains(a.Key) ||
                            a.Value.Name.ToLower().Contains(lower) || lower.Contains(a.Value.Name.ToLower())))
                .OrderBy(a => Math.Abs(a.Key.Length - lower.Length)) // Prefer closest length match
                .ToList();
            
            if (partialMatches.Any())
                return partialMatches.First().Value;
            
            // 4. Fuzzy match using Levenshtein distance
            var fuzzyMatches = _apps
                .Where(a => !IsUninstaller(a.Value))
                .Select(a => new { App = a.Value, Distance = LevenshteinDistance(a.Key, lower) })
                .Where(x => x.Distance <= 3) // Allow up to 3 character differences
                .OrderBy(x => x.Distance)
                .ToList();
            
            if (fuzzyMatches.Any())
                return fuzzyMatches.First().App;
            
            // 5. Word-based matching (any word matches)
            var queryWords = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var wordMatches = _apps
                .Where(a => !IsUninstaller(a.Value) &&
                           queryWords.Any(w => a.Key.Contains(w) || a.Value.Name.ToLower().Contains(w)))
                .OrderByDescending(a => queryWords.Count(w => a.Key.Contains(w) || a.Value.Name.ToLower().Contains(w)))
                .ToList();
            
            if (wordMatches.Any())
                return wordMatches.First().Value;
            
            return null;
        }
        
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;
            
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
        
        private bool IsUninstaller(InstalledApp app)
        {
            var nameLower = app.Name.ToLowerInvariant();
            var pathLower = app.ExecutablePath.ToLowerInvariant();
            
            return nameLower.Contains("uninstall") || nameLower.Contains("uninst") ||
                   nameLower.Contains("remove") || nameLower.Contains("setup") ||
                   pathLower.Contains("unins") || pathLower.Contains("uninst") ||
                   pathLower.Contains("uninstall");
        }
        
        public (bool Success, string Message) LaunchApp(string appName)
        {
            var app = FindApp(appName);
            if (app == null)
            {
                var builtIn = TryLaunchBuiltIn(appName);
                if (builtIn.Success) return builtIn;
                return (false, $"Couldn't find '{appName}'. Say \"scan my apps\" to discover installed programs.");
            }
            
            try
            {
                // UWP / Protocol apps
                if (app.IsUWP || app.ExecutablePath.StartsWith("ms-") || app.ExecutablePath.Contains(":"))
                {
                    Process.Start(new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true });
                    return (true, $"🚀 Launching {app.Name}.");
                }
                
                // Apps with special launch arguments (Discord, Teams, etc.)
                if (!string.IsNullOrEmpty(app.LaunchArgs))
                {
                    Process.Start(new ProcessStartInfo(app.ExecutablePath, app.LaunchArgs) { UseShellExecute = true });
                    return (true, $"🚀 Launching {app.Name}.");
                }
                
                // Standard exe launch
                Process.Start(new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true });
                return (true, $"🚀 Launching {app.Name}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstalledApps] Launch error for {app.Name}: {ex.Message}");
                
                // Try alternative launch methods
                try
                {
                    // Try with shell execute from working directory
                    var dir = Path.GetDirectoryName(app.ExecutablePath);
                    var psi = new ProcessStartInfo
                    {
                        FileName = app.ExecutablePath,
                        Arguments = app.LaunchArgs ?? "",
                        WorkingDirectory = dir ?? "",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return (true, $"🚀 Launching {app.Name}.");
                }
                catch
                {
                    return (false, $"❌ Couldn't launch {app.Name}: {ex.Message}");
                }
            }
        }
        
        private (bool Success, string Message) TryLaunchBuiltIn(string name)
        {
            var lower = name.ToLower();
            try
            {
                if (lower.Contains("notepad")) { Process.Start("notepad.exe"); return (true, "🚀 Launching Notepad."); }
                if (lower.Contains("calculator") || lower == "calc") { Process.Start("calc.exe"); return (true, "🚀 Launching Calculator."); }
                if (lower.Contains("paint")) { Process.Start("mspaint.exe"); return (true, "🚀 Launching Paint."); }
                if (lower.Contains("explorer")) { Process.Start("explorer.exe"); return (true, "🚀 Launching File Explorer."); }
                if (lower == "cmd" || lower.Contains("command prompt")) { Process.Start("cmd.exe"); return (true, "🚀 Launching Command Prompt."); }
                if (lower.Contains("powershell")) { Process.Start("powershell.exe"); return (true, "🚀 Launching PowerShell."); }
                if (lower.Contains("terminal")) { Process.Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true }); return (true, "🚀 Launching Terminal."); }
                if (lower.Contains("edge")) { Process.Start(new ProcessStartInfo("msedge.exe") { UseShellExecute = true }); return (true, "🚀 Launching Edge."); }
                if (lower.Contains("chrome")) { Process.Start(new ProcessStartInfo("chrome.exe") { UseShellExecute = true }); return (true, "🚀 Launching Chrome."); }
                if (lower.Contains("firefox")) { Process.Start(new ProcessStartInfo("firefox.exe") { UseShellExecute = true }); return (true, "🚀 Launching Firefox."); }
            }
            catch { }
            return (false, "");
        }
        
        public List<InstalledApp> GetAllApps() => _apps.Values.Distinct().ToList();
        
        public List<InstalledApp> SearchApps(string query)
        {
            var lower = query.ToLower();
            return _apps.Values.Where(a => a.Name.ToLower().Contains(lower)).Distinct().OrderBy(a => a.Name).ToList();
        }
        
        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFile))
                {
                    var json = File.ReadAllText(_cacheFile);
                    var cached = JsonSerializer.Deserialize<List<InstalledApp>>(json);
                    if (cached != null)
                        foreach (var app in cached)
                            _apps[app.Name.ToLower()] = app;
                }
            }
            catch { }
        }
        
        private void SaveCache()
        {
            try
            {
                var apps = _apps.Values.Distinct().ToList();
                var json = JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_cacheFile, json);
            }
            catch { }
        }
    }
}
