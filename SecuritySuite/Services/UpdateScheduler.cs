using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.SecuritySuite.Models;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// Schedules automatic definition updates and scans
    /// </summary>
    public class UpdateScheduler : IDisposable
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AtlasAI", "SecuritySuite", "scheduler.json");
        
        private readonly DefinitionsManager _definitionsManager;
        private readonly ScanEngine _scanEngine;
        private Timer? _updateTimer;
        private Timer? _scanTimer;
        private SchedulerSettings _settings;
        private bool _disposed;
        
        public event Action<string>? StatusChanged;
        public event Action<bool>? UpdateCheckCompleted;
        public event Action<ScanJob>? ScheduledScanCompleted;
        
        public SchedulerSettings Settings => _settings;
        public bool IsRunning => _updateTimer != null;
        
        public UpdateScheduler(DefinitionsManager definitionsManager, ScanEngine scanEngine)
        {
            _definitionsManager = definitionsManager;
            _scanEngine = scanEngine;
            _settings = LoadSettings();
        }
        
        private SchedulerSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<SchedulerSettings>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateScheduler] Error loading settings: {ex.Message}");
            }
            return new SchedulerSettings();
        }
        
        public void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateScheduler] Error saving settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Start the scheduler
        /// </summary>
        public void Start()
        {
            if (_disposed) return;
            
            Stop();
            
            if (_settings.AutoUpdateEnabled)
            {
                var interval = TimeSpan.FromHours(_settings.UpdateCheckIntervalHours);
                _updateTimer = new Timer(OnUpdateTimerTick, null, TimeSpan.FromMinutes(5), interval);
                StatusChanged?.Invoke($"Update scheduler started (every {_settings.UpdateCheckIntervalHours}h)");
            }
            
            if (_settings.AutoScanEnabled)
            {
                // Calculate time until next scheduled scan
                var nextScan = GetNextScheduledScanTime();
                var delay = nextScan - DateTime.Now;
                if (delay < TimeSpan.Zero) delay = TimeSpan.FromDays(7) + delay;
                
                _scanTimer = new Timer(OnScanTimerTick, null, delay, TimeSpan.FromDays(7));
                StatusChanged?.Invoke($"Scan scheduled for {nextScan:g}");
            }
        }
        
        /// <summary>
        /// Stop the scheduler
        /// </summary>
        public void Stop()
        {
            _updateTimer?.Dispose();
            _updateTimer = null;
            _scanTimer?.Dispose();
            _scanTimer = null;
        }
        
        /// <summary>
        /// Update settings and restart scheduler
        /// </summary>
        public void UpdateSettings(SchedulerSettings newSettings)
        {
            _settings = newSettings;
            SaveSettings();
            Start();
        }
        
        private async void OnUpdateTimerTick(object? state)
        {
            if (_disposed) return;
            
            // Check quiet hours
            if (IsInQuietHours())
            {
                StatusChanged?.Invoke("Skipping update check (quiet hours)");
                return;
            }
            
            try
            {
                StatusChanged?.Invoke("Checking for updates...");
                var (available, manifest) = await _definitionsManager.CheckForUpdatesAsync();
                
                if (available && manifest != null)
                {
                    StatusChanged?.Invoke($"Update available: v{manifest.Version}");
                    
                    // Auto-apply update
                    var success = await _definitionsManager.UpdateAsync(manifest);
                    UpdateCheckCompleted?.Invoke(success);
                }
                else
                {
                    StatusChanged?.Invoke("Definitions are up to date");
                    UpdateCheckCompleted?.Invoke(true);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Update check failed: {ex.Message}");
                UpdateCheckCompleted?.Invoke(false);
            }
        }
        
        private async void OnScanTimerTick(object? state)
        {
            if (_disposed) return;
            
            // Check quiet hours
            if (IsInQuietHours())
            {
                StatusChanged?.Invoke("Skipping scheduled scan (quiet hours)");
                return;
            }
            
            try
            {
                StatusChanged?.Invoke($"Starting scheduled {_settings.AutoScanType} scan...");
                var job = await _scanEngine.StartScanAsync(_settings.AutoScanType);
                ScheduledScanCompleted?.Invoke(job);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Scheduled scan failed: {ex.Message}");
            }
        }
        
        private bool IsInQuietHours()
        {
            if (!_settings.QuietHoursEnabled) return false;
            
            var now = DateTime.Now.TimeOfDay;
            var start = _settings.QuietHoursStart;
            var end = _settings.QuietHoursEnd;
            
            if (start < end)
            {
                // Normal range (e.g., 22:00 to 07:00 next day)
                return now >= start || now < end;
            }
            else
            {
                // Overnight range
                return now >= start && now < end;
            }
        }
        
        private DateTime GetNextScheduledScanTime()
        {
            var now = DateTime.Now;
            var scanTime = _settings.AutoScanTime;
            var scanDay = _settings.AutoScanDay;
            
            // Find next occurrence of the scheduled day/time
            var daysUntilScan = ((int)scanDay - (int)now.DayOfWeek + 7) % 7;
            var nextScan = now.Date.AddDays(daysUntilScan).Add(scanTime);
            
            // If it's today but already passed, schedule for next week
            if (nextScan <= now)
                nextScan = nextScan.AddDays(7);
            
            return nextScan;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
