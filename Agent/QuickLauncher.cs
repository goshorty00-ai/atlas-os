using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick Launcher - Find and launch anything on your PC.
    /// Indexes installed apps, recent files, and common locations.
    /// </summary>
    public class QuickLauncher
    {
        private static QuickLauncher? _instance;
        public static QuickLauncher Instance => _instance ??= new QuickLauncher();
        
        private readonly List<LaunchableItem> _items = new();
        private DateTime _lastIndexed;
        private bool _isIndexing;
        
        private QuickLauncher()
        {
            _ = IndexAsync();
        }
        
        /// <summary>
        /// Search for launchable items
        /// </summary>
        public List<LaunchableItem> Search(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _items.Take(maxResults).ToList();
            
            var lower = query.ToLowerInvariant();
            
            return _items
                .Where(i => i.Name.ToLower().Contains(lower) || 
                           i.Keywords.Any(k => k.Contains(lower)))
                .OrderByDescending(i => i.Name.ToLower().StartsWith(lower) ? 100 : 0)
                .ThenByDescending(i => i.LaunchCount)
                .Take(maxResults)
                .ToList();
        }
        
        /// <summary>
        /// Launch an item
        /// </summary>
        public async Task<string> LaunchAsync(string query)
        {
            var results = Search(query, 1);
            if (!results.Any())
                return $"❌ Nothing found matching '{query}'";
            
            var item = results.First();
            return await LaunchItemAsync(item);
        }
        
        /// <summary>
        /// Launch a specific item
        /// </summary>
        public async Task<string> LaunchItemAsync(LaunchableItem item)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = item.Path,
                    Arguments = item.Arguments ?? "",
                    UseShellExecute = true
                };
                
                Process.Start(psi);
                item.LaunchCount++;
                item.LastLaunched = DateTime.Now;
                
                return $"✓ Launched {item.Name}";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to launch {item.Name}: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Index all launchable items
        /// </summary>
        public async Task IndexAsync()
        {
            if (_isIndexing) return;
            _isIndexing = true;
            
            try
            {
                _items.Clear();
                
                await Task.Run(() =>
                {
                    // Index Start Menu
                    IndexStartMenu();
                    
                    // Index Desktop
                    IndexDesktop();
                    
                    // Index common apps
                    IndexCommonApps();
                    
                    // Index recent files
                    IndexRecentFiles();
                    
                    // Index control panel items
                    IndexControlPanel();
                    
                    // Index Windows settings
                    IndexWindowsSettings();
                });
                
                _lastIndexed = DateTime.Now;
                Debug.WriteLine($"[QuickLauncher] Indexed {_items.Count} items");
            }
            finally
            {
                _isIndexing = false;
            }
        }
        
        private void IndexStartMenu()
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
                    foreach (var file in Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (name.StartsWith("Uninstall") || name.Contains("Readme")) continue;
                        
                        _items.Add(new LaunchableItem
                        {
                            Name = name,
                            Path = file,
                            Type = LaunchableType.Application,
                            Keywords = new[] { name.ToLower() }
                        });
                    }
                }
                catch { }
            }
        }
        
        private void IndexDesktop()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!Directory.Exists(desktop)) return;
            
            try
            {
                foreach (var file in Directory.GetFiles(desktop))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".ini" || ext == ".tmp") continue;
                    
                    var name = Path.GetFileNameWithoutExtension(file);
                    _items.Add(new LaunchableItem
                    {
                        Name = name,
                        Path = file,
                        Type = ext == ".lnk" ? LaunchableType.Application : LaunchableType.File,
                        Keywords = new[] { name.ToLower(), "desktop" }
                    });
                }
            }
            catch { }
        }
        
        private void IndexCommonApps()
        {
            var commonApps = new Dictionary<string, string>
            {
                { "Calculator", "calc" },
                { "Notepad", "notepad" },
                { "Paint", "mspaint" },
                { "Command Prompt", "cmd" },
                { "PowerShell", "powershell" },
                { "Windows Terminal", "wt" },
                { "Task Manager", "taskmgr" },
                { "File Explorer", "explorer" },
                { "Control Panel", "control" },
                { "Device Manager", "devmgmt.msc" },
                { "Disk Management", "diskmgmt.msc" },
                { "Event Viewer", "eventvwr.msc" },
                { "Services", "services.msc" },
                { "Registry Editor", "regedit" },
                { "System Information", "msinfo32" },
                { "Resource Monitor", "resmon" },
                { "Performance Monitor", "perfmon" },
                { "Snipping Tool", "snippingtool" },
                { "Character Map", "charmap" },
                { "Magnifier", "magnify" },
                { "On-Screen Keyboard", "osk" },
                { "Remote Desktop", "mstsc" },
                { "WordPad", "wordpad" },
            };
            
            foreach (var (name, path) in commonApps)
            {
                _items.Add(new LaunchableItem
                {
                    Name = name,
                    Path = path,
                    Type = LaunchableType.SystemTool,
                    Keywords = new[] { name.ToLower() }
                });
            }
        }
        
        private void IndexRecentFiles()
        {
            var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (!Directory.Exists(recentPath)) return;
            
            try
            {
                var recentFiles = Directory.GetFiles(recentPath, "*.lnk")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(20);
                
                foreach (var file in recentFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    _items.Add(new LaunchableItem
                    {
                        Name = $"Recent: {name}",
                        Path = file,
                        Type = LaunchableType.RecentFile,
                        Keywords = new[] { name.ToLower(), "recent" }
                    });
                }
            }
            catch { }
        }
        
        private void IndexControlPanel()
        {
            var cplItems = new Dictionary<string, string>
            {
                { "Add/Remove Programs", "appwiz.cpl" },
                { "Display Settings", "desk.cpl" },
                { "Sound Settings", "mmsys.cpl" },
                { "Mouse Settings", "main.cpl" },
                { "Keyboard Settings", "main.cpl @1" },
                { "Network Connections", "ncpa.cpl" },
                { "Power Options", "powercfg.cpl" },
                { "Date and Time", "timedate.cpl" },
                { "Region Settings", "intl.cpl" },
                { "Internet Options", "inetcpl.cpl" },
                { "Firewall Settings", "firewall.cpl" },
                { "User Accounts", "nusrmgr.cpl" },
            };
            
            foreach (var (name, path) in cplItems)
            {
                _items.Add(new LaunchableItem
                {
                    Name = name,
                    Path = path,
                    Type = LaunchableType.ControlPanel,
                    Keywords = new[] { name.ToLower(), "settings", "control panel" }
                });
            }
        }
        
        private void IndexWindowsSettings()
        {
            var settings = new Dictionary<string, string>
            {
                { "Windows Settings", "ms-settings:" },
                { "Display Settings", "ms-settings:display" },
                { "Sound Settings", "ms-settings:sound" },
                { "Notifications", "ms-settings:notifications" },
                { "Power & Battery", "ms-settings:powersleep" },
                { "Storage", "ms-settings:storagesense" },
                { "Bluetooth", "ms-settings:bluetooth" },
                { "WiFi Settings", "ms-settings:network-wifi" },
                { "VPN Settings", "ms-settings:network-vpn" },
                { "Personalization", "ms-settings:personalization" },
                { "Background", "ms-settings:personalization-background" },
                { "Colors", "ms-settings:personalization-colors" },
                { "Lock Screen", "ms-settings:lockscreen" },
                { "Apps & Features", "ms-settings:appsfeatures" },
                { "Default Apps", "ms-settings:defaultapps" },
                { "Startup Apps", "ms-settings:startupapps" },
                { "Privacy Settings", "ms-settings:privacy" },
                { "Windows Update", "ms-settings:windowsupdate" },
                { "About PC", "ms-settings:about" },
            };
            
            foreach (var (name, path) in settings)
            {
                _items.Add(new LaunchableItem
                {
                    Name = name,
                    Path = path,
                    Type = LaunchableType.WindowsSetting,
                    Keywords = new[] { name.ToLower(), "settings" }
                });
            }
        }
        
        /// <summary>
        /// Get launcher stats
        /// </summary>
        public string GetStats()
        {
            return $"📊 Quick Launcher: {_items.Count} items indexed\n" +
                   $"Last indexed: {_lastIndexed:g}";
        }
    }
    
    public class LaunchableItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Arguments { get; set; }
        public LaunchableType Type { get; set; }
        public string[] Keywords { get; set; } = Array.Empty<string>();
        public int LaunchCount { get; set; }
        public DateTime? LastLaunched { get; set; }
    }
    
    public enum LaunchableType
    {
        Application,
        File,
        Folder,
        SystemTool,
        ControlPanel,
        WindowsSetting,
        RecentFile,
        Website
    }
}
