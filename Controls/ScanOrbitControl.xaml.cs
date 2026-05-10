using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AtlasAI.Controls
{
    /// <summary>
    /// Orbiting scan icons that rotate around the Atlas orb.
    /// Each icon triggers a real system scan and reports findings.
    /// Uses CompositionTarget.Rendering for smooth, frame-synced animation.
    /// </summary>
    public partial class ScanOrbitControl : UserControl
    {
        private double _currentAngle = 0;
        private double _targetAngle = 0;
        private bool _isScanning = false;
        private bool _isAnimating = false;
        private DateTime _lastFrameTime;
        
        // Rotation speed in degrees per second
        private const double RotationSpeed = 6.0; // 6 degrees/sec = 1 full rotation per minute
        
        // Event to communicate scan results to parent (ChatWindow)
        public event EventHandler<ScanResultEventArgs>? ScanCompleted;
        public event EventHandler<string>? ScanStarted;
        
        public ScanOrbitControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartOrbitAnimation();
        }
        
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopOrbit();
        }
        
        private void StartOrbitAnimation()
        {
            if (_isAnimating) return;
            
            _lastFrameTime = DateTime.Now;
            _isAnimating = true;
            CompositionTarget.Rendering += OnRendering;
        }
        
        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_isAnimating) return;
            
            var now = DateTime.Now;
            var deltaTime = (now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;
            
            // Cap delta to prevent jumps after lag
            deltaTime = Math.Min(deltaTime, 0.05);
            
            // Smooth continuous rotation
            _targetAngle += RotationSpeed * deltaTime;
            if (_targetAngle >= 360) _targetAngle -= 360;
            
            // Smooth interpolation to target (prevents judder)
            double smoothFactor = 1.0 - Math.Pow(0.001, deltaTime);
            _currentAngle = LerpAngle(_currentAngle, _targetAngle, smoothFactor);
            
            // Apply rotation
            OrbitRotation.Angle = _currentAngle;
            
            // Counter-rotate icons so they stay upright (single calculation)
            double counterAngle = -_currentAngle;
            HddCounterRotate.Angle = counterAngle;
            CpuCounterRotate.Angle = counterAngle;
            RamCounterRotate.Angle = counterAngle;
            NetworkCounterRotate.Angle = counterAngle;
            ProcessCounterRotate.Angle = counterAngle;
            RegistryCounterRotate.Angle = counterAngle;
            StartupCounterRotate.Angle = counterAngle;
            SecurityCounterRotate.Angle = counterAngle;
        }
        
        /// <summary>
        /// Lerp between angles handling the 0/360 wraparound
        /// </summary>
        private static double LerpAngle(double from, double to, double t)
        {
            double diff = to - from;
            
            // Handle wraparound
            if (diff > 180) diff -= 360;
            else if (diff < -180) diff += 360;
            
            return from + diff * t;
        }
        
        public void StopOrbit()
        {
            if (!_isAnimating) return;
            _isAnimating = false;
            CompositionTarget.Rendering -= OnRendering;
        }
        
        public void StartOrbit() => StartOrbitAnimation();

        #region Scan Click Handlers
        
        private async void HddScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("HDD", ScanHardDrivesAsync);
        }
        
        private async void CpuScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("CPU", ScanCpuAsync);
        }
        
        private async void RamScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("RAM", ScanMemoryAsync);
        }
        
        private async void NetworkScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("Network", ScanNetworkAsync);
        }
        
        private async void ProcessScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("Processes", ScanProcessesAsync);
        }
        
        private async void RegistryScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("Registry", ScanRegistryAsync);
        }
        
        private async void StartupScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("Startup", ScanStartupItemsAsync);
        }
        
        private async void SecurityScan_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isScanning) return;
            await RunScanAsync("Security", RunFullSecurityScanAsync);
        }
        
        private async Task RunScanAsync(string scanType, Func<Task<ScanResult>> scanFunc)
        {
            _isScanning = true;
            ScanStatusText.Text = $"SCANNING {scanType}...";
            ScanStarted?.Invoke(this, $"Starting {scanType} scan...");
            
            try
            {
                var result = await scanFunc();
                ScanStatusText.Text = $"{scanType} COMPLETE";
                ScanCompleted?.Invoke(this, new ScanResultEventArgs(scanType, result));
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = $"{scanType} ERROR";
                ScanCompleted?.Invoke(this, new ScanResultEventArgs(scanType, new ScanResult 
                { 
                    Success = false, 
                    Summary = $"Error: {ex.Message}",
                    Details = new List<string> { ex.ToString() }
                }));
            }
            finally
            {
                _isScanning = false;
                await Task.Delay(2000);
                ScanStatusText.Text = "";
            }
        }
        
        #endregion
        
        #region Real Scan Functions
        
        private async Task<ScanResult> ScanHardDrivesAsync()
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                long totalFree = 0, totalSize = 0;
                int driveCount = 0;
                
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    driveCount++;
                    totalFree += drive.AvailableFreeSpace;
                    totalSize += drive.TotalSize;
                    
                    var usedPercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
                    var status = usedPercent > 90 ? "⚠️ CRITICAL" : usedPercent > 75 ? "⚡ WARNING" : "✅ OK";
                    
                    details.Add($"{drive.Name} ({drive.DriveFormat}): {FormatSize(drive.AvailableFreeSpace)} free / {FormatSize(drive.TotalSize)} ({usedPercent:F1}% used) {status}");
                    
                    if (usedPercent > 90) result.IssuesFound++;
                    else if (usedPercent > 75) result.WarningsFound++;
                }
                
                result.Summary = $"Scanned {driveCount} drives. {FormatSize(totalFree)} free of {FormatSize(totalSize)} total.";
                result.Details = details;
                return result;
            });
        }
        
        private async Task<ScanResult> ScanCpuAsync()
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "Unknown";
                        var cores = obj["NumberOfCores"]?.ToString() ?? "?";
                        var threads = obj["NumberOfLogicalProcessors"]?.ToString() ?? "?";
                        var speed = obj["MaxClockSpeed"]?.ToString() ?? "?";
                        var load = obj["LoadPercentage"]?.ToString() ?? "0";
                        
                        details.Add($"CPU: {name}");
                        details.Add($"Cores: {cores} | Threads: {threads}");
                        details.Add($"Max Speed: {speed} MHz");
                        details.Add($"Current Load: {load}%");
                        
                        if (int.TryParse(load, out int loadVal) && loadVal > 80)
                        {
                            result.WarningsFound++;
                            details.Add("⚠️ High CPU usage detected");
                        }
                    }
                }
                catch (Exception ex)
                {
                    details.Add($"Error reading CPU info: {ex.Message}");
                }
                
                result.Summary = $"CPU scan complete. {result.WarningsFound} warnings.";
                result.Details = details;
                return result;
            });
        }
        
        private async Task<ScanResult> ScanMemoryAsync()
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                
                try
                {
                    var gcInfo = GC.GetGCMemoryInfo();
                    var totalRam = gcInfo.TotalAvailableMemoryBytes;
                    
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var totalVisible = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024;
                        var freePhysical = Convert.ToInt64(obj["FreePhysicalMemory"]) * 1024;
                        var usedPercent = (1 - (double)freePhysical / totalVisible) * 100;
                        
                        details.Add($"Total RAM: {FormatSize(totalVisible)}");
                        details.Add($"Free RAM: {FormatSize(freePhysical)}");
                        details.Add($"Used: {usedPercent:F1}%");
                        
                        if (usedPercent > 90)
                        {
                            result.IssuesFound++;
                            details.Add("⚠️ CRITICAL: Memory usage very high!");
                        }
                        else if (usedPercent > 75)
                        {
                            result.WarningsFound++;
                            details.Add("⚡ WARNING: Memory usage elevated");
                        }
                        else
                        {
                            details.Add("✅ Memory usage normal");
                        }
                    }
                    
                    // Top memory consumers
                    details.Add("\nTop Memory Consumers:");
                    var topProcs = Process.GetProcesses()
                        .OrderByDescending(p => p.WorkingSet64)
                        .Take(5);
                    foreach (var p in topProcs)
                    {
                        try { details.Add($"  • {p.ProcessName}: {FormatSize(p.WorkingSet64)}"); }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    details.Add($"Error: {ex.Message}");
                }
                
                result.Summary = $"Memory scan complete. {result.IssuesFound} issues, {result.WarningsFound} warnings.";
                result.Details = details;
                return result;
            });
        }

        private async Task<ScanResult> ScanNetworkAsync()
        {
            return await Task.Run(async () =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                
                try
                {
                    // Get network adapters
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var desc = obj["Description"]?.ToString() ?? "Unknown";
                        var ips = obj["IPAddress"] as string[];
                        var mac = obj["MACAddress"]?.ToString() ?? "N/A";
                        
                        details.Add($"Adapter: {desc}");
                        if (ips != null) details.Add($"  IP: {string.Join(", ", ips)}");
                        details.Add($"  MAC: {mac}");
                    }
                    
                    // Check listening ports
                    var portResult = await RunCommandAsync("netstat -an | findstr LISTENING");
                    var portCount = portResult.Split('\n').Count(l => l.Contains("LISTENING"));
                    details.Add($"\nListening Ports: {portCount}");
                    
                    // Check for suspicious ports
                    var suspiciousPorts = new[] { "4444", "5555", "6666", "31337", "12345" };
                    foreach (var port in suspiciousPorts)
                    {
                        if (portResult.Contains($":{port}"))
                        {
                            result.WarningsFound++;
                            details.Add($"⚠️ Suspicious port {port} is open!");
                        }
                    }
                    
                    if (result.WarningsFound == 0)
                        details.Add("✅ No suspicious ports detected");
                }
                catch (Exception ex)
                {
                    details.Add($"Error: {ex.Message}");
                }
                
                result.Summary = $"Network scan complete. {result.WarningsFound} warnings.";
                result.Details = details;
                return result;
            });
        }
        
        private async Task<ScanResult> ScanProcessesAsync()
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                
                var processes = Process.GetProcesses();
                details.Add($"Total Processes: {processes.Length}");
                
                // High CPU processes
                var highCpu = new List<string>();
                var highMem = new List<string>();
                
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.WorkingSet64 > 500 * 1024 * 1024) // > 500MB
                            highMem.Add($"{p.ProcessName} ({FormatSize(p.WorkingSet64)})");
                    }
                    catch { }
                }
                
                if (highMem.Any())
                {
                    details.Add("\n⚡ High Memory Processes:");
                    foreach (var pm in highMem.Take(10))
                        details.Add($"  • {pm}");
                    result.WarningsFound += highMem.Count;
                }
                
                // Check for known suspicious process names
                var suspicious = new[] { "cryptominer", "miner", "keylogger", "rat", "trojan" };
                foreach (var p in processes)
                {
                    try
                    {
                        var name = p.ProcessName.ToLower();
                        if (suspicious.Any(s => name.Contains(s)))
                        {
                            result.IssuesFound++;
                            details.Add($"⚠️ SUSPICIOUS: {p.ProcessName}");
                        }
                    }
                    catch { }
                }
                
                if (result.IssuesFound == 0 && result.WarningsFound == 0)
                    details.Add("✅ No suspicious processes detected");
                
                result.Summary = $"Scanned {processes.Length} processes. {result.IssuesFound} issues, {result.WarningsFound} warnings.";
                result.Details = details;
                return result;
            });
        }
        
        private async Task<ScanResult> ScanRegistryAsync()
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                
                try
                {
                    // Check Run keys
                    var runKeys = new[]
                    {
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
                    };
                    
                    int totalEntries = 0;
                    foreach (var keyPath in runKeys)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                        if (key != null)
                        {
                            var values = key.GetValueNames();
                            totalEntries += values.Length;
                            
                            foreach (var name in values)
                            {
                                var value = key.GetValue(name)?.ToString() ?? "";
                                // Check for suspicious paths
                                if (value.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
                                    value.Contains("appdata\\local\\temp", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.WarningsFound++;
                                    details.Add($"⚠️ Suspicious startup: {name}");
                                }
                            }
                        }
                        
                        using var userKey = Registry.CurrentUser.OpenSubKey(keyPath);
                        if (userKey != null)
                        {
                            totalEntries += userKey.GetValueNames().Length;
                        }
                    }
                    
                    details.Add($"Startup Registry Entries: {totalEntries}");
                    
                    // Check shell extensions
                    using var shellKey = Registry.ClassesRoot.OpenSubKey(@"*\shellex\ContextMenuHandlers");
                    if (shellKey != null)
                    {
                        details.Add($"Shell Extensions: {shellKey.GetSubKeyNames().Length}");
                    }
                    
                    if (result.WarningsFound == 0)
                        details.Add("✅ Registry looks clean");
                }
                catch (Exception ex)
                {
                    details.Add($"Error: {ex.Message}");
                }
                
                result.Summary = $"Registry scan complete. {result.WarningsFound} warnings.";
                result.Details = details;
                return result;
            });
        }
        
        private async Task<ScanResult> ScanStartupItemsAsync()
        {
            return await Task.Run(() =>
            {
                var result = new ScanResult { Success = true };
                var details = new List<string>();
                
                try
                {
                    // Registry startup items
                    var runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (runKey != null)
                    {
                        details.Add("User Startup Items:");
                        foreach (var name in runKey.GetValueNames())
                        {
                            var value = runKey.GetValue(name)?.ToString() ?? "";
                            details.Add($"  • {name}");
                        }
                    }
                    
                    // Startup folder
                    var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    if (Directory.Exists(startupFolder))
                    {
                        var files = Directory.GetFiles(startupFolder);
                        if (files.Any())
                        {
                            details.Add("\nStartup Folder Items:");
                            foreach (var file in files)
                                details.Add($"  • {Path.GetFileName(file)}");
                        }
                    }
                    
                    // Scheduled tasks that run at startup
                    details.Add("\nScheduled Tasks (Startup):");
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_StartupCommand");
                    int count = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        count++;
                        var name = obj["Name"]?.ToString() ?? "Unknown";
                        var location = obj["Location"]?.ToString() ?? "";
                        details.Add($"  • {name} ({location})");
                    }
                    
                    details.Add($"\nTotal startup items: {count}");
                    
                    if (count > 15)
                    {
                        result.WarningsFound++;
                        details.Add("⚠️ Many startup items may slow boot time");
                    }
                    else
                    {
                        details.Add("✅ Startup items look reasonable");
                    }
                }
                catch (Exception ex)
                {
                    details.Add($"Error: {ex.Message}");
                }
                
                result.Summary = $"Startup scan complete. {result.WarningsFound} warnings.";
                result.Details = details;
                return result;
            });
        }

        private async Task<ScanResult> RunFullSecurityScanAsync()
        {
            var result = new ScanResult { Success = true };
            var details = new List<string>();
            
            details.Add("═══ FULL SECURITY SCAN ═══\n");
            
            // Run all scans
            var hddResult = await ScanHardDrivesAsync();
            details.Add("【HDD】" + hddResult.Summary);
            result.IssuesFound += hddResult.IssuesFound;
            result.WarningsFound += hddResult.WarningsFound;
            
            var cpuResult = await ScanCpuAsync();
            details.Add("【CPU】" + cpuResult.Summary);
            result.IssuesFound += cpuResult.IssuesFound;
            result.WarningsFound += cpuResult.WarningsFound;
            
            var ramResult = await ScanMemoryAsync();
            details.Add("【RAM】" + ramResult.Summary);
            result.IssuesFound += ramResult.IssuesFound;
            result.WarningsFound += ramResult.WarningsFound;
            
            var netResult = await ScanNetworkAsync();
            details.Add("【NET】" + netResult.Summary);
            result.IssuesFound += netResult.IssuesFound;
            result.WarningsFound += netResult.WarningsFound;
            
            var procResult = await ScanProcessesAsync();
            details.Add("【PROC】" + procResult.Summary);
            result.IssuesFound += procResult.IssuesFound;
            result.WarningsFound += procResult.WarningsFound;
            
            var regResult = await ScanRegistryAsync();
            details.Add("【REG】" + regResult.Summary);
            result.IssuesFound += regResult.IssuesFound;
            result.WarningsFound += regResult.WarningsFound;
            
            var startupResult = await ScanStartupItemsAsync();
            details.Add("【STARTUP】" + startupResult.Summary);
            result.IssuesFound += startupResult.IssuesFound;
            result.WarningsFound += startupResult.WarningsFound;
            
            details.Add("\n═══════════════════════════");
            
            if (result.IssuesFound == 0 && result.WarningsFound == 0)
                details.Add("✅ System is healthy!");
            else if (result.IssuesFound > 0)
                details.Add($"⚠️ Found {result.IssuesFound} issues and {result.WarningsFound} warnings");
            else
                details.Add($"⚡ Found {result.WarningsFound} warnings");
            
            result.Summary = $"Full scan complete. {result.IssuesFound} issues, {result.WarningsFound} warnings.";
            result.Details = details;
            return result;
        }
        
        #endregion
        
        #region Helpers
        
        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1073741824) return $"{bytes / 1048576.0:F1} MB";
            return $"{bytes / 1073741824.0:F1} GB";
        }
        
        private static async Task<string> RunCommandAsync(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return "Failed to start process";
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
        
        #endregion
    }
    
    #region Event Args and Models
    
    public class ScanResultEventArgs : EventArgs
    {
        public string ScanType { get; }
        public ScanResult Result { get; }
        
        public ScanResultEventArgs(string scanType, ScanResult result)
        {
            ScanType = scanType;
            Result = result;
        }
    }
    
    public class ScanResult
    {
        public bool Success { get; set; }
        public string Summary { get; set; } = "";
        public List<string> Details { get; set; } = new();
        public int IssuesFound { get; set; }
        public int WarningsFound { get; set; }
    }
    
    #endregion
}
