using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.ITManagement
{
    /// <summary>
    /// Real-time system health monitoring with proactive alerts
    /// </summary>
    public class SystemHealthMonitor : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramCounter;
        private readonly Timer _monitorTimer;
        private readonly List<HealthAlert> _activeAlerts = new();
        private readonly List<HealthSnapshot> _history = new();
        private const int MAX_HISTORY = 1000;
        
        private SystemHealth _currentHealth = new();
        private bool _isMonitoring;
        
        // Alert thresholds (configurable)
        public int CpuWarningThreshold { get; set; } = 80;
        public int CpuCriticalThreshold { get; set; } = 95;
        public int RamWarningThreshold { get; set; } = 80;
        public int RamCriticalThreshold { get; set; } = 95;
        public int DiskWarningThreshold { get; set; } = 85;
        public int DiskCriticalThreshold { get; set; } = 95;
        
        public event Action<SystemHealth>? OnHealthUpdated;
        public event Action<HealthAlert>? OnAlertTriggered;
        public event Action<HealthAlert>? OnAlertCleared;
        
        public SystemHealth CurrentHealth => _currentHealth;
        public IReadOnlyList<HealthAlert> ActiveAlerts => _activeAlerts;
        
        public SystemHealthMonitor()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            
            // Initialize counters (first read is always 0)
            _cpuCounter.NextValue();
            _ramCounter.NextValue();
            
            _monitorTimer = new Timer(MonitorCallback, null, Timeout.Infinite, Timeout.Infinite);
        }
        
        public void StartMonitoring(int intervalMs = 2000)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _monitorTimer.Change(0, intervalMs);
            Debug.WriteLine("[SystemHealth] Monitoring started");
        }
        
        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Debug.WriteLine("[SystemHealth] Monitoring stopped");
        }
        
        private void MonitorCallback(object? state)
        {
            try
            {
                var health = CollectHealthData();
                _currentHealth = health;
                
                // Store in history
                lock (_history)
                {
                    _history.Add(new HealthSnapshot
                    {
                        Timestamp = DateTime.Now,
                        Health = health
                    });
                    
                    while (_history.Count > MAX_HISTORY)
                        _history.RemoveAt(0);
                }
                
                // Check for alerts
                CheckAlerts(health);
                
                OnHealthUpdated?.Invoke(health);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemHealth] Monitor error: {ex.Message}");
            }
        }
        
        public SystemHealth CollectHealthData()
        {
            var health = new SystemHealth
            {
                Timestamp = DateTime.Now
            };
            
            // Get CPU usage - use WMI for more reliable reading
            try
            {
                using var cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject obj in cpuSearcher.Get())
                {
                    health.CpuUsage = Convert.ToInt32(obj["LoadPercentage"] ?? 0);
                    break;
                }
            }
            catch 
            {
                // Fallback to performance counter
                health.CpuUsage = (int)_cpuCounter.NextValue();
            }
            
            // Get RAM usage from performance counter
            try
            {
                health.RamUsage = (int)_ramCounter.NextValue();
            }
            catch { }
            
            // Get RAM details from WMI
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var totalMem = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024; // MB
                    var freeMem = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024; // MB
                    health.TotalRamMB = (int)totalMem;
                    health.AvailableRamMB = (int)freeMem;
                    health.UsedRamMB = (int)(totalMem - freeMem);
                    
                    // Calculate RAM percentage if counter failed
                    if (health.RamUsage == 0 && totalMem > 0)
                    {
                        health.RamUsage = (int)((totalMem - freeMem) * 100 / totalMem);
                    }
                }
            }
            catch { }
            
            // Get disk info
            health.Drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => new DriveHealth
                {
                    Name = d.Name,
                    Label = d.VolumeLabel,
                    TotalGB = (int)(d.TotalSize / 1073741824),
                    FreeGB = (int)(d.AvailableFreeSpace / 1073741824),
                    UsedGB = (int)((d.TotalSize - d.AvailableFreeSpace) / 1073741824),
                    UsagePercent = (int)(100 - (d.AvailableFreeSpace * 100.0 / d.TotalSize))
                }).ToList();
            
            // Get network status
            health.NetworkAdapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && 
                           n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => new NetworkHealth
                {
                    Name = n.Name,
                    Description = n.Description,
                    Speed = n.Speed / 1000000, // Mbps
                    IsConnected = n.OperationalStatus == OperationalStatus.Up,
                    BytesSent = n.GetIPStatistics().BytesSent,
                    BytesReceived = n.GetIPStatistics().BytesReceived
                }).ToList();
            
            // Get top processes
            health.TopProcesses = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                .Take(10)
                .Select(p => {
                    try
                    {
                        return new ProcessInfo
                        {
                            Name = p.ProcessName,
                            Id = p.Id,
                            MemoryMB = (int)(p.WorkingSet64 / 1048576),
                            Threads = p.Threads.Count
                        };
                    }
                    catch { return null; }
                })
                .Where(p => p != null)
                .Cast<ProcessInfo>()
                .ToList();
            
            // System uptime
            try
            {
                using var uptime = new PerformanceCounter("System", "System Up Time");
                uptime.NextValue();
                health.UptimeHours = (int)(uptime.NextValue() / 3600);
            }
            catch { }
            
            return health;
        }
        
        private void CheckAlerts(SystemHealth health)
        {
            // CPU alerts
            CheckThreshold("CPU", health.CpuUsage, CpuWarningThreshold, CpuCriticalThreshold, 
                $"CPU usage at {health.CpuUsage}%");
            
            // RAM alerts
            CheckThreshold("RAM", health.RamUsage, RamWarningThreshold, RamCriticalThreshold,
                $"RAM usage at {health.RamUsage}% ({health.UsedRamMB}MB / {health.TotalRamMB}MB)");
            
            // Disk alerts
            foreach (var drive in health.Drives)
            {
                CheckThreshold($"Disk_{drive.Name}", drive.UsagePercent, DiskWarningThreshold, DiskCriticalThreshold,
                    $"Drive {drive.Name} at {drive.UsagePercent}% ({drive.FreeGB}GB free)");
            }
        }
        
        private void CheckThreshold(string alertId, int value, int warning, int critical, string message)
        {
            var existingAlert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            AlertSeverity? newSeverity = null;
            
            if (value >= critical)
                newSeverity = AlertSeverity.Critical;
            else if (value >= warning)
                newSeverity = AlertSeverity.Warning;
            
            if (newSeverity.HasValue)
            {
                if (existingAlert == null)
                {
                    var alert = new HealthAlert
                    {
                        Id = alertId,
                        Severity = newSeverity.Value,
                        Message = message,
                        TriggeredAt = DateTime.Now,
                        Value = value
                    };
                    _activeAlerts.Add(alert);
                    OnAlertTriggered?.Invoke(alert);
                    Debug.WriteLine($"[SystemHealth] Alert: {alert.Severity} - {message}");
                }
                else if (existingAlert.Severity != newSeverity.Value)
                {
                    existingAlert.Severity = newSeverity.Value;
                    existingAlert.Message = message;
                    existingAlert.Value = value;
                }
            }
            else if (existingAlert != null)
            {
                _activeAlerts.Remove(existingAlert);
                existingAlert.ClearedAt = DateTime.Now;
                OnAlertCleared?.Invoke(existingAlert);
                Debug.WriteLine($"[SystemHealth] Alert cleared: {alertId}");
            }
        }
        
        public List<HealthSnapshot> GetHistory(int minutes = 30)
        {
            var cutoff = DateTime.Now.AddMinutes(-minutes);
            lock (_history)
            {
                return _history.Where(h => h.Timestamp >= cutoff).ToList();
            }
        }
        
        public string GetHealthSummary()
        {
            // Always collect fresh data for summary
            var h = CollectHealthData();
            _currentHealth = h; // Update current health too
            
            var status = _activeAlerts.Any(a => a.Severity == AlertSeverity.Critical) ? "🔴 Critical" :
                        _activeAlerts.Any(a => a.Severity == AlertSeverity.Warning) ? "🟡 Warning" : "🟢 Healthy";
            
            return $"""
                System Status: {status}
                CPU: {h.CpuUsage}% | RAM: {h.RamUsage}% ({h.UsedRamMB}MB / {h.TotalRamMB}MB)
                Drives: {string.Join(", ", h.Drives.Select(d => $"{d.Name} {d.UsagePercent}%"))}
                Uptime: {h.UptimeHours} hours
                Active Alerts: {_activeAlerts.Count}
                """;
        }
        
        public void Dispose()
        {
            StopMonitoring();
            _monitorTimer.Dispose();
            _cpuCounter.Dispose();
            _ramCounter.Dispose();
        }
    }

    
    #region Data Models
    
    public class SystemHealth
    {
        public DateTime Timestamp { get; set; }
        public int CpuUsage { get; set; }
        public int RamUsage { get; set; }
        public int TotalRamMB { get; set; }
        public int UsedRamMB { get; set; }
        public int AvailableRamMB { get; set; }
        public int UptimeHours { get; set; }
        public List<DriveHealth> Drives { get; set; } = new();
        public List<NetworkHealth> NetworkAdapters { get; set; } = new();
        public List<ProcessInfo> TopProcesses { get; set; } = new();
    }
    
    public class DriveHealth
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public int TotalGB { get; set; }
        public int FreeGB { get; set; }
        public int UsedGB { get; set; }
        public int UsagePercent { get; set; }
    }
    
    public class NetworkHealth
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public long Speed { get; set; } // Mbps
        public bool IsConnected { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
    }
    
    public class ProcessInfo
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
        public int MemoryMB { get; set; }
        public int Threads { get; set; }
    }
    
    public class HealthSnapshot
    {
        public DateTime Timestamp { get; set; }
        public SystemHealth Health { get; set; } = new();
    }
    
    public class HealthAlert
    {
        public string Id { get; set; } = "";
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public DateTime TriggeredAt { get; set; }
        public DateTime? ClearedAt { get; set; }
        public int Value { get; set; }
    }
    
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }
    
    #endregion
}
