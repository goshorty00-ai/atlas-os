using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.ITManagement
{
    /// <summary>
    /// AI-powered proactive issue detection - finds problems before they become critical
    /// </summary>
    public class ProactiveIssueDetector : IDisposable
    {
        private readonly SystemHealthMonitor _healthMonitor;
        private readonly Timer _analysisTimer;
        private readonly List<DetectedIssue> _activeIssues = new();
        private readonly List<DetectedIssue> _issueHistory = new();
        private readonly Dictionary<string, DateTime> _issueCooldowns = new();
        
        private const int COOLDOWN_MINUTES = 30; // Don't re-alert same issue within this time
        
        public event Action<DetectedIssue>? OnIssueDetected;
        public event Action<DetectedIssue>? OnIssueResolved;
        public event Action<string>? OnRecommendation;
        
        public IReadOnlyList<DetectedIssue> ActiveIssues => _activeIssues;
        public bool IsAnalyzing { get; private set; }
        
        public ProactiveIssueDetector(SystemHealthMonitor healthMonitor)
        {
            _healthMonitor = healthMonitor;
            _analysisTimer = new Timer(AnalysisCallback, null, Timeout.Infinite, Timeout.Infinite);
        }
        
        public void StartAnalysis(int intervalMs = 60000) // Default: every minute
        {
            if (IsAnalyzing) return;
            IsAnalyzing = true;
            _analysisTimer.Change(0, intervalMs);
            Debug.WriteLine("[ProactiveDetector] Analysis started");
        }
        
        public void StopAnalysis()
        {
            IsAnalyzing = false;
            _analysisTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Debug.WriteLine("[ProactiveDetector] Analysis stopped");
        }
        
        private async void AnalysisCallback(object? state)
        {
            try
            {
                await RunFullAnalysisAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProactiveDetector] Analysis error: {ex.Message}");
            }
        }
        
        public async Task<List<DetectedIssue>> RunFullAnalysisAsync()
        {
            var newIssues = new List<DetectedIssue>();
            
            // Run all detection checks
            var tasks = new List<Task<DetectedIssue?>>
            {
                CheckDiskSpaceAsync(),
                CheckMemoryLeaksAsync(),
                CheckHighCpuProcessesAsync(),
                CheckDiskHealthAsync(),
                CheckStartupBloatAsync(),
                CheckLargeFilesAsync(),
                CheckOldTempFilesAsync(),
                CheckBrowserCacheAsync(),
                CheckWindowsUpdatesAsync(),
                CheckSecurityAsync()
            };
            
            var results = await Task.WhenAll(tasks);
            
            foreach (var issue in results.Where(i => i != null))
            {
                if (ShouldReportIssue(issue!))
                {
                    newIssues.Add(issue!);
                    _activeIssues.Add(issue!);
                    _issueHistory.Add(issue!);
                    _issueCooldowns[issue!.Id] = DateTime.Now;
                    OnIssueDetected?.Invoke(issue!);
                }
            }
            
            // Check for resolved issues
            var resolvedIds = new List<string>();
            foreach (var active in _activeIssues.ToList())
            {
                if (!results.Any(r => r?.Id == active.Id))
                {
                    active.ResolvedAt = DateTime.Now;
                    resolvedIds.Add(active.Id);
                    OnIssueResolved?.Invoke(active);
                }
            }
            _activeIssues.RemoveAll(i => resolvedIds.Contains(i.Id));
            
            return newIssues;
        }
        
        private bool ShouldReportIssue(DetectedIssue issue)
        {
            // Check cooldown
            if (_issueCooldowns.TryGetValue(issue.Id, out var lastReport))
            {
                if (DateTime.Now - lastReport < TimeSpan.FromMinutes(COOLDOWN_MINUTES))
                    return false;
            }
            
            // Check if already active
            if (_activeIssues.Any(i => i.Id == issue.Id))
                return false;
            
            return true;
        }
        
        #region Detection Checks
        
        private async Task<DetectedIssue?> CheckDiskSpaceAsync()
        {
            return await Task.Run(() =>
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    var freePercent = (drive.AvailableFreeSpace * 100.0) / drive.TotalSize;
                    var freeGB = drive.AvailableFreeSpace / 1073741824.0;
                    
                    if (freePercent < 10 || freeGB < 10)
                    {
                        return new DetectedIssue
                        {
                            Id = $"disk_space_{drive.Name}",
                            Title = $"Low Disk Space on {drive.Name}",
                            Description = $"Drive {drive.Name} has only {freeGB:F1} GB ({freePercent:F1}%) free space remaining.",
                            Severity = freePercent < 5 ? IssueSeverity.Critical : IssueSeverity.Warning,
                            Category = IssueCategory.Storage,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Run 'Clean Temporary Files' to free up space",
                                "Empty the Recycle Bin",
                                "Uninstall unused applications",
                                "Move large files to external storage"
                            },
                            AutoFixAvailable = true,
                            AutoFixScriptId = "cleanup_temp"
                        };
                    }
                }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckMemoryLeaksAsync()
        {
            return await Task.Run(() =>
            {
                var health = _healthMonitor.CurrentHealth;
                if (health.RamUsage > 90)
                {
                    // Find memory hogs
                    var topProcesses = health.TopProcesses.Take(5)
                        .Select(p => $"{p.Name} ({p.MemoryMB} MB)")
                        .ToList();
                    
                    return new DetectedIssue
                    {
                        Id = "high_memory_usage",
                        Title = "High Memory Usage Detected",
                        Description = $"System memory usage is at {health.RamUsage}%. Top consumers: {string.Join(", ", topProcesses)}",
                        Severity = health.RamUsage > 95 ? IssueSeverity.Critical : IssueSeverity.Warning,
                        Category = IssueCategory.Performance,
                        DetectedAt = DateTime.Now,
                        Recommendations = new List<string>
                        {
                            "Close unused applications",
                            "Restart memory-heavy applications",
                            "Consider adding more RAM",
                            "Check for memory leaks in running applications"
                        }
                    };
                }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckHighCpuProcessesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses()
                        .Where(p => {
                            try { return p.TotalProcessorTime.TotalMinutes > 10; }
                            catch { return false; }
                        })
                        .OrderByDescending(p => {
                            try { return p.TotalProcessorTime; }
                            catch { return TimeSpan.Zero; }
                        })
                        .Take(3)
                        .ToList();
                    
                    if (_healthMonitor.CurrentHealth.CpuUsage > 80)
                    {
                        var processNames = processes.Select(p => p.ProcessName).ToList();
                        
                        return new DetectedIssue
                        {
                            Id = "high_cpu_usage",
                            Title = "Sustained High CPU Usage",
                            Description = $"CPU has been running at {_healthMonitor.CurrentHealth.CpuUsage}%. Possible culprits: {string.Join(", ", processNames)}",
                            Severity = IssueSeverity.Warning,
                            Category = IssueCategory.Performance,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Check Task Manager for runaway processes",
                                "Scan for malware",
                                "Update drivers",
                                "Check for Windows Update running in background"
                            }
                        };
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckDiskHealthAsync()
        {
            // Check SMART status via WMI
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT * FROM Win32_DiskDrive");
                    
                    foreach (var disk in searcher.Get())
                    {
                        var status = disk["Status"]?.ToString();
                        if (status != null && status != "OK")
                        {
                            return new DetectedIssue
                            {
                                Id = $"disk_health_{disk["DeviceID"]}",
                                Title = "Disk Health Warning",
                                Description = $"Disk {disk["Model"]} is reporting status: {status}",
                                Severity = IssueSeverity.Critical,
                                Category = IssueCategory.Hardware,
                                DetectedAt = DateTime.Now,
                                Recommendations = new List<string>
                                {
                                    "⚠️ BACKUP YOUR DATA IMMEDIATELY",
                                    "Run disk diagnostics",
                                    "Consider replacing the drive",
                                    "Check manufacturer warranty"
                                }
                            };
                        }
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckStartupBloatAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    int startupCount = 0;
                    
                    // Count registry startup items
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        startupCount += key?.GetValueNames().Length ?? 0;
                    }
                    
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        startupCount += key?.GetValueNames().Length ?? 0;
                    }
                    
                    // Count startup folder items
                    var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    if (Directory.Exists(startupFolder))
                        startupCount += Directory.GetFiles(startupFolder, "*.lnk").Length;
                    
                    if (startupCount > 15)
                    {
                        return new DetectedIssue
                        {
                            Id = "startup_bloat",
                            Title = "Too Many Startup Programs",
                            Description = $"You have {startupCount} programs starting with Windows, which may slow down boot time.",
                            Severity = IssueSeverity.Info,
                            Category = IssueCategory.Performance,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Review startup programs in Task Manager",
                                "Disable unnecessary startup items",
                                "Use 'Optimize Startup Programs' script"
                            },
                            AutoFixAvailable = true,
                            AutoFixScriptId = "optimize_startup"
                        };
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckLargeFilesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
                    if (!Directory.Exists(downloads)) return null;
                    
                    var largeFiles = Directory.GetFiles(downloads, "*", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .Where(f => f.Length > 500 * 1024 * 1024) // > 500MB
                        .OrderByDescending(f => f.Length)
                        .Take(5)
                        .ToList();
                    
                    if (largeFiles.Count > 0)
                    {
                        var totalSize = largeFiles.Sum(f => f.Length) / 1073741824.0;
                        var fileList = string.Join(", ", largeFiles.Select(f => $"{f.Name} ({f.Length / 1048576}MB)"));
                        
                        return new DetectedIssue
                        {
                            Id = "large_downloads",
                            Title = "Large Files in Downloads",
                            Description = $"Found {largeFiles.Count} large files ({totalSize:F1} GB total) in Downloads: {fileList}",
                            Severity = IssueSeverity.Info,
                            Category = IssueCategory.Storage,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Review and delete unneeded downloads",
                                "Move important files to appropriate folders",
                                "Consider archiving old files"
                            }
                        };
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckOldTempFilesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var tempPath = Path.GetTempPath();
                    var cutoff = DateTime.Now.AddDays(-7);
                    
                    long totalSize = 0;
                    int fileCount = 0;
                    
                    foreach (var file in Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.LastWriteTime < cutoff)
                            {
                                totalSize += info.Length;
                                fileCount++;
                            }
                        }
                        catch { }
                    }
                    
                    var sizeMB = totalSize / 1048576.0;
                    if (sizeMB > 500) // More than 500MB of old temp files
                    {
                        return new DetectedIssue
                        {
                            Id = "old_temp_files",
                            Title = "Old Temporary Files Accumulating",
                            Description = $"Found {fileCount} old temporary files ({sizeMB:F0} MB) that can be safely deleted.",
                            Severity = IssueSeverity.Info,
                            Category = IssueCategory.Storage,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Run 'Clean Temporary Files' to free up space"
                            },
                            AutoFixAvailable = true,
                            AutoFixScriptId = "cleanup_temp"
                        };
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckBrowserCacheAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    long totalCache = 0;
                    
                    var cachePaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Google", "Chrome", "User Data", "Default", "Cache"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Microsoft", "Edge", "User Data", "Default", "Cache")
                    };
                    
                    foreach (var cachePath in cachePaths)
                    {
                        if (Directory.Exists(cachePath))
                        {
                            try
                            {
                                totalCache += Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories)
                                    .Sum(f => new FileInfo(f).Length);
                            }
                            catch { }
                        }
                    }
                    
                    var cacheGB = totalCache / 1073741824.0;
                    if (cacheGB > 2) // More than 2GB of browser cache
                    {
                        return new DetectedIssue
                        {
                            Id = "browser_cache",
                            Title = "Large Browser Cache",
                            Description = $"Browser caches are using {cacheGB:F1} GB of disk space.",
                            Severity = IssueSeverity.Info,
                            Category = IssueCategory.Storage,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Clear browser cache from browser settings",
                                "Run 'Clean Temporary Files' script"
                            },
                            AutoFixAvailable = true,
                            AutoFixScriptId = "cleanup_temp"
                        };
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckWindowsUpdatesAsync()
        {
            // Check if updates haven't been installed in a while
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
                    
                    if (key != null)
                    {
                        var lastSuccess = key.GetValue("LastSuccessTime")?.ToString();
                        if (DateTime.TryParse(lastSuccess, out var lastUpdate))
                        {
                            var daysSinceUpdate = (DateTime.Now - lastUpdate).TotalDays;
                            if (daysSinceUpdate > 30)
                            {
                                return new DetectedIssue
                                {
                                    Id = "windows_updates_overdue",
                                    Title = "Windows Updates Overdue",
                                    Description = $"Windows hasn't been updated in {daysSinceUpdate:F0} days.",
                                    Severity = IssueSeverity.Warning,
                                    Category = IssueCategory.Security,
                                    DetectedAt = DateTime.Now,
                                    Recommendations = new List<string>
                                    {
                                        "Check for Windows Updates",
                                        "Enable automatic updates",
                                        "Run 'Check for Updates' script"
                                    },
                                    AutoFixAvailable = true,
                                    AutoFixScriptId = "check_updates"
                                };
                            }
                        }
                    }
                }
                catch { }
                return null;
            });
        }
        
        private async Task<DetectedIssue?> CheckSecurityAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Check Windows Defender status
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        @"root\SecurityCenter2", "SELECT * FROM AntiVirusProduct");
                    
                    bool hasAntivirus = false;
                    foreach (var av in searcher.Get())
                    {
                        hasAntivirus = true;
                        var state = Convert.ToUInt32(av["productState"]);
                        var enabled = ((state >> 12) & 0xF) == 0;
                        var upToDate = ((state >> 4) & 0xF) == 0;
                        
                        if (!enabled || !upToDate)
                        {
                            return new DetectedIssue
                            {
                                Id = "antivirus_issue",
                                Title = "Antivirus Protection Issue",
                                Description = $"Antivirus ({av["displayName"]}) is {(enabled ? "enabled" : "DISABLED")} and {(upToDate ? "up to date" : "OUT OF DATE")}.",
                                Severity = IssueSeverity.Critical,
                                Category = IssueCategory.Security,
                                DetectedAt = DateTime.Now,
                                Recommendations = new List<string>
                                {
                                    "Enable real-time protection",
                                    "Update virus definitions",
                                    "Run a full system scan"
                                }
                            };
                        }
                    }
                    
                    if (!hasAntivirus)
                    {
                        return new DetectedIssue
                        {
                            Id = "no_antivirus",
                            Title = "No Antivirus Detected",
                            Description = "No antivirus software was detected on this system.",
                            Severity = IssueSeverity.Critical,
                            Category = IssueCategory.Security,
                            DetectedAt = DateTime.Now,
                            Recommendations = new List<string>
                            {
                                "Enable Windows Defender",
                                "Install antivirus software"
                            }
                        };
                    }
                }
                catch { }
                return null;
            });
        }
        
        #endregion
        
        public string GetIssuesSummary()
        {
            if (_activeIssues.Count == 0)
                return "✅ No issues detected. Your system is healthy!";
            
            var critical = _activeIssues.Count(i => i.Severity == IssueSeverity.Critical);
            var warnings = _activeIssues.Count(i => i.Severity == IssueSeverity.Warning);
            var info = _activeIssues.Count(i => i.Severity == IssueSeverity.Info);
            
            var summary = $"""
                System Issues Detected:
                🔴 Critical: {critical} | 🟡 Warning: {warnings} | 🔵 Info: {info}
                
                {string.Join("\n", _activeIssues.Select(i => $"[{i.Severity}] {i.Title}"))}
                """;
            
            return summary;
        }
        
        public void Dispose()
        {
            StopAnalysis();
            _analysisTimer.Dispose();
        }
    }

    
    #region Data Models
    
    public class DetectedIssue
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public IssueSeverity Severity { get; set; }
        public IssueCategory Category { get; set; }
        public DateTime DetectedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public List<string> Recommendations { get; set; } = new();
        public bool AutoFixAvailable { get; set; }
        public string? AutoFixScriptId { get; set; }
        
        public bool IsResolved => ResolvedAt.HasValue;
        public TimeSpan Duration => (ResolvedAt ?? DateTime.Now) - DetectedAt;
    }
    
    public enum IssueSeverity
    {
        Info,
        Warning,
        Critical
    }
    
    public enum IssueCategory
    {
        Performance,
        Storage,
        Security,
        Network,
        Hardware,
        Software
    }
    
    #endregion
}
