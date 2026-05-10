using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.SystemControl
{
    public enum SystemIssueType
    {
        Performance,
        Storage,
        Services,
        Registry,
        Network,
        Security,
        Updates,
        Hardware,
        Software
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class SystemIssue
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public SystemIssueType Type { get; set; }
        public IssueSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Recommendation { get; set; }
        public bool CanAutoFix { get; set; }
        public string AutoFixAction { get; set; }
        public string Location { get; set; } = "";
        public string Details { get; set; } = "";
        public string FixAction { get; set; } = "";
        public DateTime DetectedAt { get; set; } = DateTime.Now;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class SystemScanResult
    {
        public DateTime ScanTime { get; set; }
        public TimeSpan ScanDuration { get; set; }
        public List<SystemIssue> Issues { get; set; } = new();
        public Dictionary<string, object> SystemInfo { get; set; } = new();
        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == IssueSeverity.Critical);
        public int ErrorIssues => Issues.Count(i => i.Severity == IssueSeverity.Error);
        public int WarningIssues => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    }

    public class WindowsSystemScanner
    {
        private readonly List<Func<Task<List<SystemIssue>>>> _scanners;

        public event Action<string> ScanProgress;
        public event Action<SystemIssue> IssueDetected;

        public WindowsSystemScanner()
        {
            _scanners = new List<Func<Task<List<SystemIssue>>>>
            {
                ScanPerformanceIssues,
                ScanStorageIssues,
                ScanServiceIssues,
                ScanRegistryIssues,
                ScanNetworkIssues,
                ScanSecurityIssues,
                ScanUpdateIssues,
                ScanHardwareIssues,
                ScanSoftwareIssues
            };
        }

        public async Task<SystemScanResult> PerformFullScanAsync()
        {
            var startTime = DateTime.Now;
            var result = new SystemScanResult
            {
                ScanTime = startTime
            };

            ScanProgress?.Invoke("Starting comprehensive system scan...");

            // Collect basic system information
            await CollectSystemInfo(result);

            // Run all scanners
            var scanTasks = new List<Task<List<SystemIssue>>>();
            
            foreach (var scanner in _scanners)
            {
                scanTasks.Add(scanner());
            }

            var scanResults = await Task.WhenAll(scanTasks);
            
            foreach (var issues in scanResults)
            {
                result.Issues.AddRange(issues);
                foreach (var issue in issues)
                {
                    IssueDetected?.Invoke(issue);
                }
            }

            result.ScanDuration = DateTime.Now - startTime;
            ScanProgress?.Invoke($"Scan completed. Found {result.TotalIssues} issues.");

            return result;
        }

        private async Task CollectSystemInfo(SystemScanResult result)
        {
            ScanProgress?.Invoke("Collecting system information...");

            try
            {
                // Basic system info
                result.SystemInfo["OS"] = Environment.OSVersion.ToString();
                result.SystemInfo["MachineName"] = Environment.MachineName;
                result.SystemInfo["UserName"] = Environment.UserName;
                result.SystemInfo["ProcessorCount"] = Environment.ProcessorCount;
                result.SystemInfo["WorkingSet"] = Environment.WorkingSet;
                result.SystemInfo["SystemDirectory"] = Environment.SystemDirectory;

                // Memory info
                var memoryInfo = await GetMemoryInfoAsync();
                result.SystemInfo["TotalMemoryGB"] = memoryInfo.TotalMemoryGB;
                result.SystemInfo["AvailableMemoryGB"] = memoryInfo.AvailableMemoryGB;
                result.SystemInfo["MemoryUsagePercent"] = memoryInfo.UsagePercent;

                // Disk info
                var diskInfo = GetDiskInfo();
                result.SystemInfo["Drives"] = diskInfo;

                // CPU info
                var cpuInfo = await GetCpuInfoAsync();
                result.SystemInfo["CPUUsage"] = cpuInfo.UsagePercent;
                result.SystemInfo["CPUName"] = cpuInfo.Name;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error collecting system info: {ex.Message}");
            }
        }

        private async Task<List<SystemIssue>> ScanPerformanceIssues()
        {
            ScanProgress?.Invoke("Scanning performance issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // CPU Usage
                var cpuUsage = await GetCpuUsageAsync();
                if (cpuUsage > 90)
                {
                    issues.Add(new SystemIssue
                    {
                        Type = SystemIssueType.Performance,
                        Severity = IssueSeverity.Warning,
                        Title = "High CPU Usage",
                        Description = $"CPU usage is at {cpuUsage:F1}%",
                        Recommendation = "Close unnecessary applications or restart high-usage processes",
                        CanAutoFix = true,
                        AutoFixAction = "kill_high_cpu_processes"
                    });
                }

                // Memory Usage
                var memInfo = await GetMemoryInfoAsync();
                if (memInfo.UsagePercent > 85)
                {
                    issues.Add(new SystemIssue
                    {
                        Type = SystemIssueType.Performance,
                        Severity = memInfo.UsagePercent > 95 ? IssueSeverity.Critical : IssueSeverity.Warning,
                        Title = "High Memory Usage",
                        Description = $"Memory usage is at {memInfo.UsagePercent:F1}%",
                        Recommendation = "Close memory-intensive applications or add more RAM",
                        CanAutoFix = true,
                        AutoFixAction = "clear_memory_cache"
                    });
                }

                // Check for memory leaks
                var processes = Process.GetProcesses()
                    .Where(p => p.WorkingSet64 > 1024 * 1024 * 1024) // > 1GB
                    .OrderByDescending(p => p.WorkingSet64)
                    .Take(5);

                foreach (var process in processes)
                {
                    try
                    {
                        issues.Add(new SystemIssue
                        {
                            Type = SystemIssueType.Performance,
                            Severity = IssueSeverity.Info,
                            Title = "High Memory Process",
                            Description = $"{process.ProcessName} is using {process.WorkingSet64 / (1024 * 1024)} MB",
                            Recommendation = "Monitor this process for memory leaks",
                            Metadata = { ["ProcessId"] = process.Id, ["ProcessName"] = process.ProcessName }
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Performance,
                    Severity = IssueSeverity.Error,
                    Title = "Performance Scan Error",
                    Description = $"Could not complete performance scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanStorageIssues()
        {
            ScanProgress?.Invoke("Scanning storage issues...");
            var issues = new List<SystemIssue>();

            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady);

                foreach (var drive in drives)
                {
                    var freeSpacePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
                    var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);

                    if (freeSpacePercent < 10)
                    {
                        issues.Add(new SystemIssue
                        {
                            Type = SystemIssueType.Storage,
                            Severity = freeSpacePercent < 5 ? IssueSeverity.Critical : IssueSeverity.Warning,
                            Title = $"Low Disk Space on {drive.Name}",
                            Description = $"Only {freeSpaceGB:F1} GB ({freeSpacePercent:F1}%) free space remaining",
                            Recommendation = "Delete unnecessary files or move data to another drive",
                            CanAutoFix = true,
                            AutoFixAction = "cleanup_disk",
                            Metadata = { ["DriveName"] = drive.Name, ["FreeSpaceGB"] = freeSpaceGB }
                        });
                    }
                }

                // Check for large files
                await CheckForLargeFilesAsync(issues);

                // Check temp directories
                await CheckTempDirectoriesAsync(issues);
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Storage,
                    Severity = IssueSeverity.Error,
                    Title = "Storage Scan Error",
                    Description = $"Could not complete storage scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanServiceIssues()
        {
            ScanProgress?.Invoke("Scanning Windows services...");
            var issues = new List<SystemIssue>();

            try
            {
                var criticalServices = new[]
                {
                    "Themes", "AudioSrv", "BITS", "CryptSvc", "Dhcp", "Dnscache",
                    "EventLog", "LanmanServer", "LanmanWorkstation", "RpcSs",
                    "Schedule", "SENS", "SharedAccess", "ShellHWDetection",
                    "Spooler", "TrkWks", "W32Time", "Winmgmt", "WSearch"
                };

                var services = ServiceController.GetServices();

                foreach (var serviceName in criticalServices)
                {
                    var service = services.FirstOrDefault(s => 
                        s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

                    if (service != null)
                    {
                        if (service.Status != ServiceControllerStatus.Running)
                        {
                            issues.Add(new SystemIssue
                            {
                                Type = SystemIssueType.Services,
                                Severity = IssueSeverity.Warning,
                                Title = $"Critical Service Not Running",
                                Description = $"Service '{service.DisplayName}' is {service.Status}",
                                Recommendation = "Start the service if it should be running",
                                CanAutoFix = true,
                                AutoFixAction = "start_service",
                                Metadata = { ["ServiceName"] = service.ServiceName }
                            });
                        }
                    }
                }

                // Check for services in error state
                var errorServices = services.Where(s => 
                    s.Status == ServiceControllerStatus.Stopped && 
                    s.StartType == ServiceStartMode.Automatic);

                foreach (var service in errorServices.Take(10))
                {
                    issues.Add(new SystemIssue
                    {
                        Type = SystemIssueType.Services,
                        Severity = IssueSeverity.Info,
                        Title = "Automatic Service Stopped",
                        Description = $"Service '{service.DisplayName}' is set to automatic but is stopped",
                        Recommendation = "Check if this service should be running",
                        Metadata = { ["ServiceName"] = service.ServiceName }
                    });
                }
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Services,
                    Severity = IssueSeverity.Error,
                    Title = "Service Scan Error",
                    Description = $"Could not complete service scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanRegistryIssues()
        {
            ScanProgress?.Invoke("Scanning registry issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // Check startup programs
                await CheckStartupProgramsAsync(issues);

                // Check for invalid file associations
                await CheckFileAssociationsAsync(issues);

                // Check for orphaned registry entries
                await CheckOrphanedEntriesAsync(issues);
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Registry,
                    Severity = IssueSeverity.Error,
                    Title = "Registry Scan Error",
                    Description = $"Could not complete registry scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanNetworkIssues()
        {
            ScanProgress?.Invoke("Scanning network issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // Check internet connectivity
                var hasInternet = await CheckInternetConnectivityAsync();
                if (!hasInternet)
                {
                    issues.Add(new SystemIssue
                    {
                        Type = SystemIssueType.Network,
                        Severity = IssueSeverity.Warning,
                        Title = "No Internet Connection",
                        Description = "Cannot reach external websites",
                        Recommendation = "Check network adapter and router settings",
                        CanAutoFix = true,
                        AutoFixAction = "reset_network_adapter"
                    });
                }

                // Check DNS resolution
                var dnsWorking = await CheckDnsResolutionAsync();
                if (!dnsWorking)
                {
                    issues.Add(new SystemIssue
                    {
                        Type = SystemIssueType.Network,
                        Severity = IssueSeverity.Warning,
                        Title = "DNS Resolution Issues",
                        Description = "Cannot resolve domain names",
                        Recommendation = "Try flushing DNS cache or changing DNS servers",
                        CanAutoFix = true,
                        AutoFixAction = "flush_dns"
                    });
                }
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Network,
                    Severity = IssueSeverity.Error,
                    Title = "Network Scan Error",
                    Description = $"Could not complete network scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanSecurityIssues()
        {
            ScanProgress?.Invoke("Scanning security issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // Check Windows Defender status
                await CheckWindowsDefenderAsync(issues);

                // Check firewall status
                await CheckFirewallStatusAsync(issues);

                // Check for suspicious processes
                await CheckSuspiciousProcessesAsync(issues);

                // Check user account control
                await CheckUacStatusAsync(issues);
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Security,
                    Severity = IssueSeverity.Error,
                    Title = "Security Scan Error",
                    Description = $"Could not complete security scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanUpdateIssues()
        {
            ScanProgress?.Invoke("Scanning Windows Update issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // Check Windows Update service
                var updateService = ServiceController.GetServices()
                    .FirstOrDefault(s => s.ServiceName == "wuauserv");

                if (updateService?.Status != ServiceControllerStatus.Running)
                {
                    issues.Add(new SystemIssue
                    {
                        Type = SystemIssueType.Updates,
                        Severity = IssueSeverity.Warning,
                        Title = "Windows Update Service Not Running",
                        Description = "Windows Update service is not active",
                        Recommendation = "Start Windows Update service to receive security updates",
                        CanAutoFix = true,
                        AutoFixAction = "start_windows_update"
                    });
                }

                // Check for pending updates (simplified check)
                await CheckPendingUpdatesAsync(issues);
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Updates,
                    Severity = IssueSeverity.Error,
                    Title = "Update Scan Error",
                    Description = $"Could not complete update scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanHardwareIssues()
        {
            ScanProgress?.Invoke("Scanning hardware issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // Check device manager for errors
                await CheckDeviceManagerAsync(issues);

                // Check system temperature (if available)
                await CheckSystemTemperatureAsync(issues);

                // Check hard drive health
                await CheckHardDriveHealthAsync(issues);
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Hardware,
                    Severity = IssueSeverity.Error,
                    Title = "Hardware Scan Error",
                    Description = $"Could not complete hardware scan: {ex.Message}"
                });
            }

            return issues;
        }

        private async Task<List<SystemIssue>> ScanSoftwareIssues()
        {
            ScanProgress?.Invoke("Scanning software issues...");
            var issues = new List<SystemIssue>();

            try
            {
                // Check for corrupted system files
                await CheckSystemFileIntegrityAsync(issues);

                // Check installed programs for issues
                await CheckInstalledProgramsAsync(issues);

                // Check for duplicate files
                await CheckDuplicateFilesAsync(issues);
            }
            catch (Exception ex)
            {
                issues.Add(new SystemIssue
                {
                    Type = SystemIssueType.Software,
                    Severity = IssueSeverity.Error,
                    Title = "Software Scan Error",
                    Description = $"Could not complete software scan: {ex.Message}"
                });
            }

            return issues;
        }

        // Helper methods for detailed checks
        private async Task<double> GetCpuUsageAsync()
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "cpu get loadpercentage /value",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("LoadPercentage="))
                {
                    var value = line.Split('=')[1].Trim();
                    if (double.TryParse(value, out var usage))
                        return usage;
                }
            }

            return 0;
        }

        private async Task<(double TotalMemoryGB, double AvailableMemoryGB, double UsagePercent)> GetMemoryInfoAsync()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, AvailablePhysicalMemory FROM Win32_OperatingSystem");
                using var results = searcher.Get();
                
                foreach (ManagementObject result in results)
                {
                    var totalKB = Convert.ToDouble(result["TotalVisibleMemorySize"]);
                    var availableKB = Convert.ToDouble(result["AvailablePhysicalMemory"]);
                    
                    var totalGB = totalKB / (1024 * 1024);
                    var availableGB = availableKB / (1024 * 1024);
                    var usagePercent = ((totalKB - availableKB) / totalKB) * 100;
                    
                    return (totalGB, availableGB, usagePercent);
                }
            }
            catch { }

            return (0, 0, 0);
        }

        private async Task<(double UsagePercent, string Name)> GetCpuInfoAsync()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, LoadPercentage FROM Win32_Processor");
                using var results = searcher.Get();
                
                foreach (ManagementObject result in results)
                {
                    var name = result["Name"]?.ToString() ?? "Unknown";
                    var usage = Convert.ToDouble(result["LoadPercentage"] ?? 0);
                    return (usage, name);
                }
            }
            catch { }

            return (0, "Unknown");
        }

        private List<object> GetDiskInfo()
        {
            var drives = new List<object>();
            
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                drives.Add(new
                {
                    Name = drive.Name,
                    TotalSizeGB = drive.TotalSize / (1024.0 * 1024 * 1024),
                    FreeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024),
                    UsagePercent = (1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100,
                    DriveType = drive.DriveType.ToString()
                });
            }

            return drives;
        }

        // Placeholder methods for detailed scans (implement as needed)
        private async Task CheckForLargeFilesAsync(List<SystemIssue> issues) { }
        private async Task CheckTempDirectoriesAsync(List<SystemIssue> issues) { }
        private async Task CheckStartupProgramsAsync(List<SystemIssue> issues) { }
        private async Task CheckFileAssociationsAsync(List<SystemIssue> issues) { }
        private async Task CheckOrphanedEntriesAsync(List<SystemIssue> issues) { }
        private async Task<bool> CheckInternetConnectivityAsync() { return true; }
        private async Task<bool> CheckDnsResolutionAsync() { return true; }
        private async Task CheckWindowsDefenderAsync(List<SystemIssue> issues) { }
        private async Task CheckFirewallStatusAsync(List<SystemIssue> issues) { }
        private async Task CheckSuspiciousProcessesAsync(List<SystemIssue> issues) { }
        private async Task CheckUacStatusAsync(List<SystemIssue> issues) { }
        private async Task CheckPendingUpdatesAsync(List<SystemIssue> issues) { }
        private async Task CheckDeviceManagerAsync(List<SystemIssue> issues) { }
        private async Task CheckSystemTemperatureAsync(List<SystemIssue> issues) { }
        private async Task CheckHardDriveHealthAsync(List<SystemIssue> issues) { }
        private async Task CheckSystemFileIntegrityAsync(List<SystemIssue> issues) { }
        private async Task CheckInstalledProgramsAsync(List<SystemIssue> issues) { }
        private async Task CheckDuplicateFilesAsync(List<SystemIssue> issues) { }
    }
}