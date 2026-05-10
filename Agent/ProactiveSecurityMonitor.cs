using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Proactive Security Monitor - Watches for new installations, detects bloatware,
    /// and performs automatic health scans every 2 hours.
    /// </summary>
    public class ProactiveSecurityMonitor
    {
        private static ProactiveSecurityMonitor? _instance;
        public static ProactiveSecurityMonitor Instance => _instance ??= new ProactiveSecurityMonitor();
        
        private DispatcherTimer? _healthScanTimer;
        private FileSystemWatcher? _downloadsWatcher;
        private FileSystemWatcher? _programFilesWatcher;
        private ManagementEventWatcher? _processWatcher;
        
        private HashSet<string> _knownInstallers = new();
        private HashSet<string> _knownPrograms = new();
        private DateTime _lastHealthScan = DateTime.MinValue;
        
        // Settings
        public bool NotificationsEnabled { get; set; } = true;
        public bool AutoScanEnabled { get; set; } = true;
        public int ScanIntervalHours { get; set; } = 2;
        
        // Events
        public event EventHandler<InstallationAlert>? InstallationDetected;
        public event EventHandler<HealthReport>? HealthScanCompleted;
        public event EventHandler<string>? StatusChanged;
        
        // Known bloatware/PUP patterns
        private readonly string[] _bloatwarePatterns = new[]
        {
            "toolbar", "browser helper", "search protect", "optimizer", "cleaner",
            "driver update", "pc speed", "registry fix", "mcafee", "norton",
            "avast", "avg free", "opera", "bing bar", "ask toolbar", "babylon",
            "conduit", "delta search", "sweet page", "webssearches", "myway",
            "mindspark", "opencandy", "installcore", "softonic", "cnet installer"
        };
        
        private readonly string[] _trustedPublishers = new[]
        {
            "microsoft", "google", "mozilla", "adobe", "nvidia", "amd", "intel",
            "valve", "epic games", "steam", "discord", "spotify", "zoom"
        };
        
        // Polling-based backup monitor
        private DispatcherTimer? _pollingTimer;
        private HashSet<string> _knownDownloadFiles = new();
        private DateTime _lastPollTime = DateTime.Now;
        
        public void Start()
        {
            Debug.WriteLine("[SecurityMonitor] START called");
            LoadKnownPrograms();
            LoadKnownDownloadFiles();
            StartFileWatchers();
            StartPollingMonitor(); // Backup polling monitor
            StartProcessWatcher();
            StartHealthScanTimer();
            StatusChanged?.Invoke(this, "Security monitoring active");
            Debug.WriteLine("[SecurityMonitor] All monitors started");
        }
        
        private void LoadKnownDownloadFiles()
        {
            try
            {
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadsPath))
                {
                    foreach (var file in Directory.GetFiles(downloadsPath))
                    {
                        _knownDownloadFiles.Add(file.ToLower());
                    }
                    Debug.WriteLine($"[SecurityMonitor] Loaded {_knownDownloadFiles.Count} known download files");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityMonitor] LoadKnownDownloadFiles error: {ex.Message}");
            }
        }
        
        private void StartPollingMonitor()
        {
            _pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3) // Check every 3 seconds
            };
            _pollingTimer.Tick += PollForNewFiles;
            _pollingTimer.Start();
            Debug.WriteLine("[SecurityMonitor] Polling monitor started (3 second interval)");
        }
        
        private void PollForNewFiles(object? sender, EventArgs e)
        {
            if (!NotificationsEnabled) return;
            
            try
            {
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsPath)) return;
                
                var currentFiles = Directory.GetFiles(downloadsPath);
                foreach (var file in currentFiles)
                {
                    var fileLower = file.ToLower();
                    if (!_knownDownloadFiles.Contains(fileLower))
                    {
                        _knownDownloadFiles.Add(fileLower);
                        
                        var ext = Path.GetExtension(file).ToLower();
                        if (ext == ".exe" || ext == ".msi" || ext == ".msix" || ext == ".bat" || ext == ".cmd")
                        {
                            Debug.WriteLine($"[SecurityMonitor] POLLING detected new file: {file}");
                            Task.Run(async () => await AnalyzeInstallerAsync(file));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityMonitor] Polling error: {ex.Message}");
            }
        }
        
        public void Stop()
        {
            _pollingTimer?.Stop();
            _healthScanTimer?.Stop();
            _downloadsWatcher?.Dispose();
            _programFilesWatcher?.Dispose();
            _processWatcher?.Stop();
            StatusChanged?.Invoke(this, "Security monitoring stopped");
        }

        #region File Watchers
        
        private void StartFileWatchers()
        {
            try
            {
                // Watch Downloads folder for new installers
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Debug.WriteLine($"[SecurityMonitor] Downloads path: {downloadsPath}");
                if (Directory.Exists(downloadsPath))
                {
                    _downloadsWatcher = new FileSystemWatcher(downloadsPath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                        Filter = "*.*",
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _downloadsWatcher.Created += OnFileCreated;
                    _downloadsWatcher.Changed += OnFileCreated;
                    _downloadsWatcher.Renamed += (s, e) => OnFileCreated(s, e);
                    _downloadsWatcher.Error += (s, e) => Debug.WriteLine($"[SecurityMonitor] Watcher error: {e.GetException().Message}");
                    Debug.WriteLine($"[SecurityMonitor] Downloads watcher STARTED");
                }
                else
                {
                    Debug.WriteLine($"[SecurityMonitor] Downloads folder not found!");
                }
                
                // Watch Program Files for new installations
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                Debug.WriteLine($"[SecurityMonitor] Program Files path: {programFiles}");
                if (Directory.Exists(programFiles))
                {
                    _programFilesWatcher = new FileSystemWatcher(programFiles)
                    {
                        NotifyFilter = NotifyFilters.DirectoryName,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _programFilesWatcher.Created += OnProgramInstalled;
                    _programFilesWatcher.Error += (s, e) => Debug.WriteLine($"[SecurityMonitor] ProgramFiles watcher error: {e.GetException().Message}");
                    Debug.WriteLine($"[SecurityMonitor] Program Files watcher STARTED");
                }
                
                StatusChanged?.Invoke(this, "File watchers active");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityMonitor] StartFileWatchers EXCEPTION: {ex}");
                StatusChanged?.Invoke(this, $"File watcher error: {ex.Message}");
            }
        }
        
        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine($"[SecurityMonitor] FILE DETECTED: {e.FullPath} (Notifications: {NotificationsEnabled})");
            
            if (!NotificationsEnabled)
            {
                Debug.WriteLine($"[SecurityMonitor] Notifications disabled, skipping");
                return;
            }
            
            var ext = Path.GetExtension(e.FullPath).ToLower();
            Debug.WriteLine($"[SecurityMonitor] Extension: {ext}");
            if (ext == ".exe" || ext == ".msi" || ext == ".msix")
            {
                Debug.WriteLine($"[SecurityMonitor] ANALYZING installer: {e.FullPath}");
                Task.Run(async () => await AnalyzeInstallerAsync(e.FullPath));
            }
        }
        
        private void OnProgramInstalled(object sender, FileSystemEventArgs e)
        {
            if (!NotificationsEnabled) return;
            
            var folderName = Path.GetFileName(e.FullPath);
            if (!_knownPrograms.Contains(folderName.ToLower()))
            {
                Task.Run(async () => await CheckNewProgramAsync(e.FullPath, folderName));
            }
        }
        
        #endregion
        
        #region Process Watcher
        
        private void StartProcessWatcher()
        {
            try
            {
                var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _processWatcher = new ManagementEventWatcher(query);
                _processWatcher.EventArrived += OnProcessStarted;
                _processWatcher.Start();
            }
            catch
            {
                // Process watching requires admin - fail silently
            }
        }
        
        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            if (!NotificationsEnabled) return;
            
            try
            {
                var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
                var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
                
                // Check if it's an installer
                if (IsInstallerProcess(processName))
                {
                    Task.Run(async () => await MonitorInstallerProcessAsync(processName, processId));
                }
            }
            catch { }
        }
        
        private bool IsInstallerProcess(string name)
        {
            var lower = name.ToLower();
            return lower.Contains("setup") || lower.Contains("install") || 
                   lower.Contains("msiexec") || lower.EndsWith("_setup.exe") ||
                   lower.Contains("uninst");
        }
        
        #endregion
        
        #region Installation Analysis
        
        // Track files being analyzed to prevent duplicates
        private HashSet<string> _filesBeingAnalyzed = new();
        private object _analysisLock = new();
        
        private async Task AnalyzeInstallerAsync(string filePath)
        {
            // Prevent duplicate analysis
            lock (_analysisLock)
            {
                var key = filePath.ToLower();
                if (_filesBeingAnalyzed.Contains(key))
                {
                    Debug.WriteLine($"[SecurityMonitor] Already analyzing: {filePath}");
                    return;
                }
                _filesBeingAnalyzed.Add(key);
            }
            
            try
            {
                Debug.WriteLine($"[SecurityMonitor] AnalyzeInstallerAsync START: {filePath}");
                
                // Wait for file to be fully written
                await WaitForFileReady(filePath);
                
                var alert = new InstallationAlert
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    DetectedAt = DateTime.Now,
                    Type = InstallationType.Installer
                };
                
                await Task.Run(async () =>
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        alert.FileSize = fileInfo.Length;
                        
                        // Check digital signature
                        alert.IsSigned = CheckDigitalSignature(filePath, out string publisher);
                        alert.Publisher = publisher;
                        
                        // Check against bloatware patterns
                        var lowerName = alert.FileName.ToLower();
                        var lowerPublisher = publisher.ToLower();
                        
                        alert.IsTrusted = _trustedPublishers.Any(t => lowerPublisher.Contains(t));
                        alert.IsBloatware = _bloatwarePatterns.Any(b => lowerName.Contains(b) || lowerPublisher.Contains(b));
                        
                        // Online verification (VirusTotal)
                        try
                        {
                            var onlineResult = await OnlineSecurityVerifier.VerifyFileAsync(filePath);
                            if (onlineResult.VirusTotalChecked)
                            {
                                alert.OnlineVerified = true;
                                alert.VirusTotalMalicious = onlineResult.VirusTotalMalicious;
                                alert.VirusTotalClean = onlineResult.VirusTotalHarmless + onlineResult.VirusTotalUndetected;
                                
                                if (onlineResult.VirusTotalMalicious > 0)
                                {
                                    alert.RiskLevel = onlineResult.VirusTotalMalicious > 5 ? SecurityRiskLevel.High : SecurityRiskLevel.Medium;
                                    alert.Recommendation = onlineResult.Recommendation;
                                    Debug.WriteLine($"[SecurityMonitor] VirusTotal: {alert.VirusTotalMalicious} detections!");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SecurityMonitor] Online verification failed: {ex.Message}");
                        }
                        
                        // Determine risk level if not set by online check
                        if (alert.RiskLevel == SecurityRiskLevel.Unknown)
                        {
                            if (alert.IsBloatware)
                                alert.RiskLevel = SecurityRiskLevel.High;
                            else if (!alert.IsSigned)
                                alert.RiskLevel = SecurityRiskLevel.Medium;
                            else if (alert.IsTrusted)
                                alert.RiskLevel = SecurityRiskLevel.Low;
                            else
                                alert.RiskLevel = SecurityRiskLevel.Medium;
                        }
                        
                        // Generate recommendation if not set
                        if (string.IsNullOrEmpty(alert.Recommendation))
                            alert.Recommendation = GenerateRecommendation(alert);
                            
                        Debug.WriteLine($"[SecurityMonitor] Analysis complete: {alert.FileName}, Risk: {alert.RiskLevel}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SecurityMonitor] Analysis error: {ex.Message}");
                        alert.AnalysisError = ex.Message;
                        alert.RiskLevel = SecurityRiskLevel.Unknown;
                    }
                });
                
                Debug.WriteLine($"[SecurityMonitor] INVOKING InstallationDetected event");
                InstallationDetected?.Invoke(this, alert);
            }
            finally
            {
                // Remove from tracking after a delay to prevent re-triggering
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    lock (_analysisLock)
                    {
                        _filesBeingAnalyzed.Remove(filePath.ToLower());
                    }
                });
            }
        }
        
        private async Task WaitForFileReady(string filePath, int maxWaitMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < maxWaitMs)
            {
                try
                {
                    using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    return; // File is ready
                }
                catch (IOException)
                {
                    await Task.Delay(200);
                }
            }
        }
        
        private async Task CheckNewProgramAsync(string path, string name)
        {
            var alert = new InstallationAlert
            {
                FilePath = path,
                FileName = name,
                DetectedAt = DateTime.Now,
                Type = InstallationType.NewProgram
            };
            
            await Task.Run(() =>
            {
                // Check for bundled software indicators
                var exeFiles = Directory.GetFiles(path, "*.exe", SearchOption.AllDirectories).Take(20).ToList();
                alert.BundledApps = new List<string>();
                
                foreach (var exe in exeFiles)
                {
                    var exeName = Path.GetFileNameWithoutExtension(exe).ToLower();
                    if (_bloatwarePatterns.Any(b => exeName.Contains(b)))
                    {
                        alert.BundledApps.Add(Path.GetFileName(exe));
                        alert.IsBloatware = true;
                    }
                }
                
                alert.RiskLevel = alert.IsBloatware ? SecurityRiskLevel.High : SecurityRiskLevel.Low;
                alert.Recommendation = alert.IsBloatware 
                    ? $"⚠️ Detected {alert.BundledApps.Count} potentially unwanted programs bundled with this installation."
                    : "✅ No obvious bloatware detected.";
            });
            
            if (alert.IsBloatware || alert.BundledApps?.Any() == true)
            {
                InstallationDetected?.Invoke(this, alert);
            }
            
            _knownPrograms.Add(name.ToLower());
        }
        
        private async Task MonitorInstallerProcessAsync(string processName, int processId)
        {
            if (_knownInstallers.Contains(processName)) return;
            _knownInstallers.Add(processName);
            
            var alert = new InstallationAlert
            {
                FileName = processName,
                DetectedAt = DateTime.Now,
                Type = InstallationType.RunningInstaller,
                Recommendation = $"🔄 Installation in progress: {processName}"
            };
            
            InstallationDetected?.Invoke(this, alert);
            
            // Wait for process to finish and check what was installed
            await Task.Run(() =>
            {
                try
                {
                    var proc = Process.GetProcessById(processId);
                    proc.WaitForExit(300000); // 5 min timeout
                }
                catch { }
            });
            
            // Scan for new programs after installation
            await ScanForNewProgramsAsync();
            _knownInstallers.Remove(processName);
        }
        
        private bool CheckDigitalSignature(string filePath, out string publisher)
        {
            publisher = "Unknown";
            try
            {
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(filePath);
                publisher = cert.Subject;
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private string GenerateRecommendation(InstallationAlert alert)
        {
            if (alert.IsBloatware)
                return $"⚠️ HIGH RISK: This appears to be bloatware/PUP. Recommend NOT installing.";
            if (!alert.IsSigned)
                return $"⚡ CAUTION: This file is not digitally signed. Verify the source before installing.";
            if (alert.IsTrusted)
                return $"✅ SAFE: From trusted publisher ({alert.Publisher}).";
            return $"ℹ️ UNKNOWN: Signed by {alert.Publisher}. Verify this is what you intended to install.";
        }
        
        #endregion

        #region Health Scan Timer
        
        private void StartHealthScanTimer()
        {
            _healthScanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(ScanIntervalHours)
            };
            _healthScanTimer.Tick += async (s, e) =>
            {
                if (AutoScanEnabled)
                    await RunHealthScanAsync();
            };
            _healthScanTimer.Start();
        }
        
        public async Task<HealthReport> RunHealthScanAsync()
        {
            _lastHealthScan = DateTime.Now;
            StatusChanged?.Invoke(this, "Running health scan...");
            
            var report = new HealthReport { ScanTime = DateTime.Now };
            
            await Task.Run(() =>
            {
                // Disk space check
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var usedPercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
                    report.DiskUsage.Add(new DiskInfo
                    {
                        DriveName = drive.Name,
                        FreeSpace = drive.AvailableFreeSpace,
                        TotalSpace = drive.TotalSize,
                        UsedPercent = usedPercent,
                        Status = usedPercent > 90 ? "Critical" : usedPercent > 75 ? "Warning" : "OK"
                    });
                    if (usedPercent > 90) report.CriticalIssues++;
                    else if (usedPercent > 75) report.Warnings++;
                }
                
                // Memory check
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var total = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024;
                        var free = Convert.ToInt64(obj["FreePhysicalMemory"]) * 1024;
                        report.MemoryUsedPercent = (1 - (double)free / total) * 100;
                        report.MemoryFree = free;
                        report.MemoryTotal = total;
                        
                        if (report.MemoryUsedPercent > 90) report.CriticalIssues++;
                        else if (report.MemoryUsedPercent > 80) report.Warnings++;
                    }
                }
                catch { }
                
                // Process count
                report.ProcessCount = Process.GetProcesses().Length;
                var highMemProcs = Process.GetProcesses()
                    .Where(p => { try { return p.WorkingSet64 > 500_000_000; } catch { return false; } })
                    .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                report.HighMemoryProcesses = highMemProcs;
                if (highMemProcs.Count > 5) report.Warnings++;
                
                // Startup items
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    report.StartupItemCount = key?.GetValueNames().Length ?? 0;
                    if (report.StartupItemCount > 15) report.Warnings++;
                }
                catch { }
                
                // Generate summary
                report.OverallStatus = report.CriticalIssues > 0 ? "Critical" 
                    : report.Warnings > 0 ? "Warning" : "Healthy";
                report.Summary = GenerateHealthSummary(report);
            });
            
            HealthScanCompleted?.Invoke(this, report);
            StatusChanged?.Invoke(this, $"Health scan complete: {report.OverallStatus}");
            
            return report;
        }
        
        private string GenerateHealthSummary(HealthReport report)
        {
            var parts = new List<string>();
            
            // Disk summary
            var criticalDisks = report.DiskUsage.Where(d => d.Status == "Critical").ToList();
            var warningDisks = report.DiskUsage.Where(d => d.Status == "Warning").ToList();
            if (criticalDisks.Any())
                parts.Add($"⚠️ {criticalDisks.Count} drive(s) critically low on space");
            else if (warningDisks.Any())
                parts.Add($"⚡ {warningDisks.Count} drive(s) running low on space");
            else
                parts.Add("✅ Disk space OK");
            
            // Memory summary
            if (report.MemoryUsedPercent > 90)
                parts.Add($"⚠️ Memory critical ({report.MemoryUsedPercent:F0}% used)");
            else if (report.MemoryUsedPercent > 80)
                parts.Add($"⚡ Memory elevated ({report.MemoryUsedPercent:F0}% used)");
            else
                parts.Add($"✅ Memory OK ({report.MemoryUsedPercent:F0}% used)");
            
            // Process summary
            if (report.HighMemoryProcesses.Count > 0)
                parts.Add($"⚡ {report.HighMemoryProcesses.Count} high-memory processes");
            else
                parts.Add($"✅ {report.ProcessCount} processes running normally");
            
            return string.Join("\n", parts);
        }
        
        #endregion
        
        #region Helpers
        
        private void LoadKnownPrograms()
        {
            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                foreach (var dir in Directory.GetDirectories(programFiles))
                    _knownPrograms.Add(Path.GetFileName(dir).ToLower());
                
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                foreach (var dir in Directory.GetDirectories(programFilesX86))
                    _knownPrograms.Add(Path.GetFileName(dir).ToLower());
            }
            catch { }
        }
        
        private async Task ScanForNewProgramsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    foreach (var dir in Directory.GetDirectories(programFiles))
                    {
                        var name = Path.GetFileName(dir).ToLower();
                        if (!_knownPrograms.Contains(name))
                        {
                            _ = CheckNewProgramAsync(dir, name);
                        }
                    }
                }
                catch { }
            });
        }
        
        public void SetNotifications(bool enabled)
        {
            NotificationsEnabled = enabled;
            StatusChanged?.Invoke(this, enabled ? "Notifications enabled" : "Notifications disabled");
        }
        
        public void SetAutoScan(bool enabled, int intervalHours = 2)
        {
            AutoScanEnabled = enabled;
            ScanIntervalHours = intervalHours;
            
            if (_healthScanTimer != null)
            {
                _healthScanTimer.Interval = TimeSpan.FromHours(intervalHours);
                if (enabled) _healthScanTimer.Start();
                else _healthScanTimer.Stop();
            }
            
            StatusChanged?.Invoke(this, enabled 
                ? $"Auto-scan enabled (every {intervalHours} hours)" 
                : "Auto-scan disabled");
        }
        
        #endregion
    }
    
    #region Models
    
    public class InstallationAlert
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime DetectedAt { get; set; }
        public InstallationType Type { get; set; }
        public long FileSize { get; set; }
        public bool IsSigned { get; set; }
        public string Publisher { get; set; } = "";
        public bool IsTrusted { get; set; }
        public bool IsBloatware { get; set; }
        public SecurityRiskLevel RiskLevel { get; set; } = SecurityRiskLevel.Unknown;
        public string Recommendation { get; set; } = "";
        public string? AnalysisError { get; set; }
        public List<string>? BundledApps { get; set; }
        
        // Online verification results
        public bool OnlineVerified { get; set; }
        public int VirusTotalMalicious { get; set; }
        public int VirusTotalClean { get; set; }
    }
    
    public class HealthReport
    {
        public DateTime ScanTime { get; set; }
        public string OverallStatus { get; set; } = "Unknown";
        public string Summary { get; set; } = "";
        public int CriticalIssues { get; set; }
        public int Warnings { get; set; }
        public List<DiskInfo> DiskUsage { get; set; } = new();
        public double MemoryUsedPercent { get; set; }
        public long MemoryFree { get; set; }
        public long MemoryTotal { get; set; }
        public int ProcessCount { get; set; }
        public List<string> HighMemoryProcesses { get; set; } = new();
        public int StartupItemCount { get; set; }
    }
    
    public class DiskInfo
    {
        public string DriveName { get; set; } = "";
        public long FreeSpace { get; set; }
        public long TotalSpace { get; set; }
        public double UsedPercent { get; set; }
        public string Status { get; set; } = "";
    }
    
    public enum InstallationType { Installer, NewProgram, RunningInstaller }
    public enum SecurityRiskLevel { Low, Medium, High, Unknown }
    
    #endregion
}
