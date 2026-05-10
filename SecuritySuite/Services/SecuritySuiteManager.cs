using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AtlasAI.SecuritySuite.Models;
using AtlasAI.SystemControl;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// Main coordinator for the Security Suite - ties all services together
    /// </summary>
    public class SecuritySuiteManager : IDisposable
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AtlasAI", "SecuritySuite");
        
        private static readonly string ReportsPath = Path.Combine(DataDir, "reports.json");
        private static readonly string ActivityPath = Path.Combine(DataDir, "activity.json");
        
        public ScanEngine ScanEngine { get; }
        public DefinitionsManager DefinitionsManager { get; }
        public QuarantineManager QuarantineManager { get; }
        public UpdateScheduler Scheduler { get; }
        public HostsFileWatcher HostsWatcher { get; }
        public StartupWatcher StartupWatcher { get; }
        public ScheduledTaskWatcher TaskWatcher { get; }
        
        private List<ScanReport> _scanHistory = new();
        private List<RecentActivity> _recentActivity = new();
        
        public event Action<DashboardStatus>? DashboardUpdated;
        public event Action<RecentActivity>? ActivityAdded;
        
        public SecuritySuiteManager()
        {
            Directory.CreateDirectory(DataDir);
            
            ScanEngine = new ScanEngine();
            DefinitionsManager = new DefinitionsManager();
            QuarantineManager = new QuarantineManager();
            Scheduler = new UpdateScheduler(DefinitionsManager, ScanEngine);
            HostsWatcher = HostsFileWatcher.Instance;
            StartupWatcher = StartupWatcher.Instance;
            TaskWatcher = ScheduledTaskWatcher.Instance;
            
            // Wire up events
            ScanEngine.JobUpdated += OnScanJobUpdated;
            ScanEngine.FindingDetected += OnFindingDetected;
            DefinitionsManager.StatusChanged += msg => AddActivity("🔄", "Definitions Update", msg);
            Scheduler.StatusChanged += msg => AddActivity("⏰", "Scheduler", msg);
            HostsWatcher.StatusChanged += msg => AddActivity("📁", "Hosts Watcher", msg);
            StartupWatcher.StatusChanged += msg => AddActivity("🚀", "Startup Watcher", msg);
            TaskWatcher.StatusChanged += msg => AddActivity("📋", "Task Watcher", msg);
            
            // STEP 30: Initialize chat integration
            SecurityChatIntegration.Initialize(this);
            
            LoadData();
            Scheduler.Start();
            
            // Start the system watchers
            HostsWatcher.Start();
            StartupWatcher.Start();
            TaskWatcher.Start();
        }
        
        private void LoadData()
        {
            try
            {
                if (File.Exists(ReportsPath))
                {
                    var json = File.ReadAllText(ReportsPath);
                    _scanHistory = JsonSerializer.Deserialize<List<ScanReport>>(json) ?? new();
                }
                if (File.Exists(ActivityPath))
                {
                    var json = File.ReadAllText(ActivityPath);
                    _recentActivity = JsonSerializer.Deserialize<List<RecentActivity>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuiteManager] Error loading persisted data: {ex.Message}");
            }
        }
        
        private void SaveData()
        {
            try
            {
                var reportsJson = JsonSerializer.Serialize(_scanHistory.Take(100).ToList(), 
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ReportsPath, reportsJson);
                
                var activityJson = JsonSerializer.Serialize(_recentActivity.Take(200).ToList(),
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ActivityPath, activityJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecuritySuiteManager] Error saving persisted data: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current dashboard status
        /// </summary>
        public DashboardStatus GetDashboardStatus()
        {
            return new DashboardStatus
            {
                ProtectionScore = CalculateProtectionScore(),
                Definitions = DefinitionsManager.GetCurrentInfo(),
                LastScan = _scanHistory.FirstOrDefault(),
                QuarantinedItemsCount = QuarantineManager.ActiveCount,
                ActiveThreatsCount = GetActiveThreatsCount(),
                RecentActivities = _recentActivity.Take(10).ToList()
            };
        }
        
        /// <summary>
        /// Start a scan
        /// </summary>
        public async Task<ScanJob> StartScanAsync(ScanType type, List<string>? customPaths = null)
        {
            AddActivity("🔍", $"{type} Scan Started", "Scanning your system for threats...");
            var job = await ScanEngine.StartScanAsync(type, customPaths);
            
            // Save report
            var report = new ScanReport
            {
                Type = type,
                StartTime = job.StartedAt,
                EndTime = job.CompletedAt ?? DateTime.Now,
                FilesScanned = job.FilesScanned,
                ThreatsFound = job.ThreatsFound,
                Findings = job.Findings,
                WasCancelled = job.Status == ScanStatus.Cancelled,
                ErrorMessage = job.ErrorMessage
            };
            _scanHistory.Insert(0, report);
            SaveData();
            
            var statusMsg = job.Status == ScanStatus.Completed
                ? $"Found {job.ThreatsFound} threats in {job.FilesScanned:N0} files"
                : job.Status == ScanStatus.Cancelled ? "Scan was cancelled" : $"Error: {job.ErrorMessage}";
            AddActivity("✅", $"{type} Scan Complete", statusMsg);
            
            DashboardUpdated?.Invoke(GetDashboardStatus());
            return job;
        }
        
        /// <summary>
        /// Cancel current scan
        /// </summary>
        public void CancelScan()
        {
            ScanEngine.CancelScan();
            AddActivity("⚠️", "Scan Cancelled", "User cancelled the scan");
        }

        /// <summary>
        /// Remove a threat (quarantine or delete)
        /// </summary>
        public async Task<(bool Success, string Message)> RemoveThreatAsync(SecurityFinding finding, bool quarantine = true)
        {
            try
            {
                if (quarantine && finding.CanQuarantine)
                {
                    var result = await QuarantineManager.QuarantineFileAsync(finding.FilePath, finding);
                    if (result.Success)
                        AddActivity("🔒", "Threat Quarantined", $"{finding.Title} moved to quarantine");
                    return result;
                }
                else if (finding.CanDelete)
                {
                    // Use existing ThreatRemover
                    var threat = new UnifiedThreat
                    {
                        Type = ConvertCategory(finding.Category),
                        Name = finding.Title,
                        Location = finding.FilePath,
                        CanRemove = true
                    };
                    
                    var remover = new ThreatRemover();
                    var result = await remover.RemoveThreatAsync(threat);
                    
                    if (result.Success)
                        AddActivity("🗑️", "Threat Removed", $"{finding.Title} deleted");
                    
                    return (result.Success, result.Message);
                }
                
                return (false, "Cannot remove this threat");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Remove all threats from last scan
        /// </summary>
        public async Task<(int Removed, int Failed, string Summary)> RemoveAllThreatsAsync(List<SecurityFinding> findings)
        {
            int removed = 0, failed = 0;
            var messages = new List<string>();
            
            foreach (var finding in findings.Where(f => f.CanDelete || f.CanQuarantine))
            {
                var result = await RemoveThreatAsync(finding, quarantine: true);
                if (result.Success)
                    removed++;
                else
                {
                    failed++;
                    messages.Add($"• {finding.Title}: {result.Message}");
                }
            }
            
            var summary = removed > 0
                ? $"✅ Removed {removed} threat(s)"
                : "No threats were removed";
            
            if (failed > 0)
                summary += $"\n⚠️ {failed} failed:\n{string.Join("\n", messages.Take(5))}";
            
            AddActivity("🛡️", "Threat Removal", $"Removed {removed}, Failed {failed}");
            DashboardUpdated?.Invoke(GetDashboardStatus());
            
            return (removed, failed, summary);
        }
        
        /// <summary>
        /// Check for definition updates
        /// </summary>
        public async Task<(bool Available, string Message)> CheckForUpdatesAsync()
        {
            AddActivity("🔄", "Update Check", "Checking for definition updates...");
            var (available, manifest) = await DefinitionsManager.CheckForUpdatesAsync();
            
            if (available && manifest != null)
            {
                AddActivity("📦", "Update Available", $"Version {manifest.Version} is available");
                return (true, $"Update available: v{manifest.Version} ({manifest.SignatureCount:N0} signatures)");
            }
            
            return (false, "Definitions are up to date");
        }
        
        /// <summary>
        /// Apply definition update
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateDefinitionsAsync()
        {
            var (available, manifest) = await DefinitionsManager.CheckForUpdatesAsync();
            
            if (!available || manifest == null)
                return (false, "No update available");
            
            AddActivity("⬇️", "Downloading Update", $"Downloading v{manifest.Version}...");
            var success = await DefinitionsManager.UpdateAsync(manifest);
            
            if (success)
            {
                AddActivity("✅", "Update Complete", $"Now running v{manifest.Version}");
                DashboardUpdated?.Invoke(GetDashboardStatus());
                return (true, $"Updated to v{manifest.Version}");
            }
            
            return (false, "Update failed - check logs for details");
        }
        
        /// <summary>
        /// Get scan history
        /// </summary>
        public List<ScanReport> GetScanHistory(int count = 20)
        {
            return _scanHistory.Take(count).ToList();
        }
        
        /// <summary>
        /// Get recent activity
        /// </summary>
        public List<RecentActivity> GetRecentActivity(int count = 20)
        {
            return _recentActivity.Take(count).ToList();
        }
        
        #region Private Helpers
        
        private ProtectionScore CalculateProtectionScore()
        {
            var score = new ProtectionScore { Score = 100 };
            
            // Check definitions age
            var defsInfo = DefinitionsManager.GetCurrentInfo();
            var defsAge = DateTime.Now - defsInfo.LastUpdated;
            if (defsAge.TotalDays > 7)
            {
                score.Score -= 20;
                score.Issues.Add("Definitions are outdated");
                score.Recommendations.Add("Update your security definitions");
            }
            else if (defsAge.TotalDays > 3)
            {
                score.Score -= 10;
                score.Issues.Add("Definitions are getting old");
            }
            
            // Check last scan
            var lastScan = _scanHistory.FirstOrDefault();
            if (lastScan == null)
            {
                score.Score -= 15;
                score.Issues.Add("No scans performed yet");
                score.Recommendations.Add("Run a Quick Scan to check your system");
            }
            else if ((DateTime.Now - lastScan.EndTime).TotalDays > 7)
            {
                score.Score -= 10;
                score.Issues.Add("No recent scans");
                score.Recommendations.Add("Run a scan to ensure protection");
            }
            
            // Check quarantine
            if (QuarantineManager.ActiveCount > 0)
            {
                score.Score -= 5;
                score.Issues.Add($"{QuarantineManager.ActiveCount} items in quarantine");
            }
            
            // Check for unresolved threats
            var unresolvedThreats = GetActiveThreatsCount();
            if (unresolvedThreats > 0)
            {
                score.Score -= 25;
                score.Issues.Add($"{unresolvedThreats} unresolved threats");
                score.Recommendations.Add("Remove detected threats immediately");
            }
            
            // Determine status
            score.Status = score.Score switch
            {
                >= 80 => ProtectionStatus.Protected,
                >= 50 => ProtectionStatus.AtRisk,
                _ => ProtectionStatus.Critical
            };
            
            return score;
        }
        
        private int GetActiveThreatsCount()
        {
            var lastScan = _scanHistory.FirstOrDefault();
            if (lastScan == null) return 0;
            
            // Count threats that haven't been resolved
            return lastScan.ThreatsFound - lastScan.ThreatsRemoved - lastScan.ThreatsQuarantined;
        }
        
        private void AddActivity(string icon, string title, string description, string? action = null)
        {
            var activity = new RecentActivity
            {
                Icon = icon,
                Title = title,
                Description = description,
                ActionTaken = action
            };
            
            _recentActivity.Insert(0, activity);
            if (_recentActivity.Count > 200)
                _recentActivity.RemoveRange(200, _recentActivity.Count - 200);
            
            SaveData();
            ActivityAdded?.Invoke(activity);
        }
        
        private void OnScanJobUpdated(ScanJob job)
        {
            // Could update UI here
        }
        
        private void OnFindingDetected(SecurityFinding finding)
        {
            AddActivity("⚠️", "Threat Detected", finding.Title);
        }
        
        private SystemControl.ThreatCategory ConvertCategory(Models.ThreatCategory cat) => cat switch
        {
            Models.ThreatCategory.Malware => SystemControl.ThreatCategory.File,
            Models.ThreatCategory.StartupItem => SystemControl.ThreatCategory.Startup,
            Models.ThreatCategory.Registry => SystemControl.ThreatCategory.Registry,
            Models.ThreatCategory.Service => SystemControl.ThreatCategory.Service,
            Models.ThreatCategory.BrowserHijacker => SystemControl.ThreatCategory.BrowserExtension,
            _ => SystemControl.ThreatCategory.File
        };
        
        #endregion
        
        public void Dispose()
        {
            HostsWatcher.Dispose();
            StartupWatcher.Dispose();
            TaskWatcher.Dispose();
            Scheduler.Dispose();
        }
    }
}
