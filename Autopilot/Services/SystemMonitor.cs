using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Autopilot.Models;

namespace AtlasAI.Autopilot.Services
{
    /// <summary>
    /// Monitors system state and generates proactive suggestions
    /// </summary>
    public class SystemMonitor
    {
        private readonly AutopilotEngine _engine;
        private Timer? _monitorTimer;
        private bool _isRunning;
        private readonly List<SystemObservation> _observations = new();
        
        public event EventHandler<SystemObservation>? ObservationMade;
        public event EventHandler<AutopilotSuggestion>? SuggestionGenerated;
        
        public IReadOnlyList<SystemObservation> Observations => _observations.AsReadOnly();
        
        public SystemMonitor(AutopilotEngine engine)
        {
            _engine = engine;
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _monitorTimer = new Timer(CheckSystem, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
            Debug.WriteLine("[SystemMonitor] Started");
        }
        
        public void Stop()
        {
            _isRunning = false;
            _monitorTimer?.Dispose();
            _monitorTimer = null;
            Debug.WriteLine("[SystemMonitor] Stopped");
        }
        
        private void CheckSystem(object? state)
        {
            if (!_isRunning) return;
            
            try
            {
                CheckDiskSpace();
                CheckMemoryUsage();
                CheckDownloadsFolder();
                CheckTempFiles();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] Error: {ex.Message}");
            }
        }
        
        private void CheckDiskSpace()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                
                foreach (var drive in drives)
                {
                    var freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
                    
                    if (freePercent < 10)
                    {
                        var obs = new SystemObservation
                        {
                            Type = ObservationType.DiskSpace,
                            Description = $"Drive {drive.Name} is low on space ({freePercent:F1}% free)",
                            RequiresAttention = true,
                            Data = { ["Drive"] = drive.Name, ["FreePercent"] = freePercent }
                        };
                        
                        RecordObservation(obs);
                        
                        _engine.GenerateSuggestion(
                            "Low Disk Space",
                            $"Drive {drive.Name} has only {freePercent:F1}% free space",
                            "Low disk space can cause system slowdowns and prevent file operations",
                            SuggestionType.Maintenance,
                            SuggestionPriority.High,
                            "Clean up temporary files and old downloads"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] Error checking disk space: {ex.Message}");
            }
        }

        private void CheckMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / 1024 / 1024;
                
                if (memoryMB > 500)
                {
                    var obs = new SystemObservation
                    {
                        Type = ObservationType.MemoryUsage,
                        Description = $"Atlas AI is using {memoryMB}MB of memory",
                        RequiresAttention = memoryMB > 1000,
                        Data = { ["MemoryMB"] = memoryMB }
                    };
                    
                    RecordObservation(obs);
                    
                    if (memoryMB > 1000)
                    {
                        _engine.GenerateSuggestion(
                            "High Memory Usage",
                            $"Atlas AI is using {memoryMB}MB of memory",
                            "High memory usage may indicate a memory leak or too many cached items",
                            SuggestionType.Optimization,
                            SuggestionPriority.Medium,
                            "Consider restarting the application"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] Error checking memory usage: {ex.Message}");
            }
        }
        
        private void CheckDownloadsFolder()
        {
            try
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads)) return;
                
                var files = Directory.GetFiles(downloads);
                var oldFiles = files.Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)).ToList();
                
                if (oldFiles.Count > 20)
                {
                    var obs = new SystemObservation
                    {
                        Type = ObservationType.SystemHealth,
                        Description = $"Downloads folder has {oldFiles.Count} files older than 30 days",
                        RequiresAttention = false,
                        Data = { ["OldFileCount"] = oldFiles.Count }
                    };
                    
                    RecordObservation(obs);
                    
                    _engine.GenerateSuggestion(
                        "Clean Up Downloads",
                        $"Your Downloads folder has {oldFiles.Count} files older than 30 days",
                        "Old downloads can take up disk space and make it harder to find recent files",
                        SuggestionType.Maintenance,
                        SuggestionPriority.Low,
                        "Review and delete old downloads"
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] Error checking downloads folder: {ex.Message}");
            }
        }

        private void CheckTempFiles()
        {
            try
            {
                var tempPath = Path.GetTempPath();
                var tempFiles = Directory.GetFiles(tempPath, "*", SearchOption.TopDirectoryOnly);
                var oldTempFiles = tempFiles.Where(f => 
                {
                    try { return File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7); }
                    catch { return false; }
                }).ToList();
                
                var totalSizeMB = oldTempFiles.Sum(f => 
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0; }
                }) / 1024 / 1024;
                
                if (totalSizeMB > 500)
                {
                    _engine.GenerateSuggestion(
                        "Clean Temp Files",
                        $"Temporary files are using {totalSizeMB}MB of disk space",
                        "Old temporary files can be safely deleted to free up space",
                        SuggestionType.Maintenance,
                        SuggestionPriority.Low,
                        "Clean up temporary files"
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] Error checking temp files: {ex.Message}");
            }
        }
        
        private void RecordObservation(SystemObservation observation)
        {
            _observations.Add(observation);
            
            // Keep only recent observations
            if (_observations.Count > 100)
            {
                _observations.RemoveRange(0, _observations.Count - 100);
            }
            
            ObservationMade?.Invoke(this, observation);
        }
        
        /// <summary>
        /// Get observations requiring attention
        /// </summary>
        public List<SystemObservation> GetAttentionRequired()
        {
            return _observations.Where(o => o.RequiresAttention).ToList();
        }
        
        /// <summary>
        /// Get recent observations
        /// </summary>
        public List<SystemObservation> GetRecent(int count = 20)
        {
            return _observations.OrderByDescending(o => o.Timestamp).Take(count).ToList();
        }
    }
}
