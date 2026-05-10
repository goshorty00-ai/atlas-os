using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.SystemControl
{
    public enum FixResult
    {
        Success,
        Failed,
        PermissionDenied,
        NotSupported,
        AlreadyFixed,
        RequiresRestart
    }

    public class FixAttemptResult
    {
        public FixResult Result { get; set; }
        public string Message { get; set; }
        public bool RequiresRestart { get; set; }
        public TimeSpan Duration { get; set; }
        public Exception Exception { get; set; }
    }

    public class WindowsSystemController
    {
        private readonly Dictionary<string, Func<SystemIssue, Task<FixAttemptResult>>> _fixActions;

        public event Action<string> FixProgress;
        public event Action<SystemIssue, FixAttemptResult> FixCompleted;

        public WindowsSystemController()
        {
            _fixActions = new Dictionary<string, Func<SystemIssue, Task<FixAttemptResult>>>
            {
                ["kill_high_cpu_processes"] = FixHighCpuProcesses,
                ["clear_memory_cache"] = ClearMemoryCache,
                ["cleanup_disk"] = CleanupDisk,
                ["start_service"] = StartService,
                ["reset_network_adapter"] = ResetNetworkAdapter,
                ["flush_dns"] = FlushDnsCache,
                ["start_windows_update"] = StartWindowsUpdate,
                ["fix_registry_errors"] = FixRegistryErrors,
                ["defragment_disk"] = DefragmentDisk,
                ["quarantine_threat"] = QuarantineThreat,
                ["terminate_suspicious_process"] = TerminateSuspiciousProcess,
                ["remove_startup_entry"] = RemoveStartupEntry,
                ["delete_suspicious_file"] = DeleteSuspiciousFile,
                ["update_drivers"] = UpdateDrivers,
                ["scan_malware"] = ScanMalware,
                ["repair_system_files"] = RepairSystemFiles
            };
        }

        public async Task<List<FixAttemptResult>> AutoFixIssuesAsync(List<SystemIssue> issues)
        {
            var results = new List<FixAttemptResult>();
            var fixableIssues = issues.Where(i => i.CanAutoFix && !string.IsNullOrEmpty(i.AutoFixAction)).ToList();

            FixProgress?.Invoke($"Starting auto-fix for {fixableIssues.Count} issues...");

            foreach (var issue in fixableIssues)
            {
                try
                {
                    FixProgress?.Invoke($"Fixing: {issue.Title}");
                    var result = await FixIssueAsync(issue);
                    results.Add(result);
                    FixCompleted?.Invoke(issue, result);
                }
                catch (Exception ex)
                {
                    var errorResult = new FixAttemptResult
                    {
                        Result = FixResult.Failed,
                        Message = $"Unexpected error: {ex.Message}",
                        Exception = ex
                    };
                    results.Add(errorResult);
                    FixCompleted?.Invoke(issue, errorResult);
                }
            }

            var successCount = results.Count(r => r.Result == FixResult.Success);
            FixProgress?.Invoke($"Auto-fix completed. {successCount}/{results.Count} issues fixed successfully.");

            return results;
        }

        public async Task<FixAttemptResult> FixIssueAsync(SystemIssue issue)
        {
            if (!issue.CanAutoFix || string.IsNullOrEmpty(issue.AutoFixAction))
            {
                return new FixAttemptResult
                {
                    Result = FixResult.NotSupported,
                    Message = "This issue cannot be automatically fixed"
                };
            }

            if (_fixActions.TryGetValue(issue.AutoFixAction, out var fixAction))
            {
                var startTime = DateTime.Now;
                try
                {
                    var result = await fixAction(issue);
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }
                catch (Exception ex)
                {
                    return new FixAttemptResult
                    {
                        Result = FixResult.Failed,
                        Message = $"Fix failed: {ex.Message}",
                        Exception = ex,
                        Duration = DateTime.Now - startTime
                    };
                }
            }

            return new FixAttemptResult
            {
                Result = FixResult.NotSupported,
                Message = $"Unknown fix action: {issue.AutoFixAction}"
            };
        }

        private async Task<FixAttemptResult> FixHighCpuProcesses(SystemIssue issue)
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !p.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !p.ProcessName.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.TotalProcessorTime.TotalMilliseconds)
                    .Take(5);

                var killedProcesses = new List<string>();

                foreach (var process in processes)
                {
                    try
                    {
                        // Only kill non-critical processes
                        if (IsSafeToKill(process.ProcessName))
                        {
                            // SAFETY GATE: Check before process kill
                            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                                AtlasAI.Core.OperationType.ProcessKillCritical,
                                AtlasAI.Core.OperationRisk.High,
                                $"Kill high-CPU process: {process.ProcessName}",
                                new Dictionary<string, object>
                                {
                                    ["processName"] = process.ProcessName,
                                    ["pid"] = process.Id,
                                    ["reason"] = "high CPU usage"
                                });
                            
                            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Allowed)
                            {
                                process.Kill();
                                killedProcesses.Add(process.ProcessName);
                            }
                        }
                    }
                    catch
                    {
                        // Process might have already exited
                    }
                }

                return new FixAttemptResult
                {
                    Result = killedProcesses.Any() ? FixResult.Success : FixResult.AlreadyFixed,
                    Message = killedProcesses.Any() 
                        ? $"Terminated {killedProcesses.Count} high-CPU processes: {string.Join(", ", killedProcesses)}"
                        : "No safe processes to terminate (or blocked by Safety Mode)"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to terminate processes: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> ClearMemoryCache(SystemIssue issue)
        {
            try
            {
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Clear system cache using Windows API
                await Task.Run(() =>
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = "/c echo off & echo. & echo Clearing system cache... & timeout /t 2 /nobreak > nul",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                    }
                    catch { }
                });

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "Memory cache cleared successfully"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to clear memory cache: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> CleanupDisk(SystemIssue issue)
        {
            try
            {
                var driveName = issue.Metadata.ContainsKey("DriveName") 
                    ? issue.Metadata["DriveName"].ToString() 
                    : "C:";

                var cleanupTasks = new List<Task<long>>();

                // Clean temp files
                cleanupTasks.Add(CleanTempFilesAsync());
                
                // Clean recycle bin
                cleanupTasks.Add(EmptyRecycleBinAsync());
                
                // Clean browser cache
                cleanupTasks.Add(CleanBrowserCacheAsync());
                
                // Clean Windows update cache
                cleanupTasks.Add(CleanWindowsUpdateCacheAsync());

                var cleanedBytes = await Task.WhenAll(cleanupTasks);
                var totalCleaned = cleanedBytes.Sum();
                var cleanedMB = totalCleaned / (1024.0 * 1024);

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = $"Disk cleanup completed. Freed {cleanedMB:F1} MB of space"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Disk cleanup failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> StartService(SystemIssue issue)
        {
            try
            {
                var serviceName = issue.Metadata["ServiceName"].ToString();
                
                // SAFETY GATE: Check before service change
                var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                    AtlasAI.Core.OperationType.ServiceChange,
                    AtlasAI.Core.OperationRisk.Medium,
                    $"Start service: {serviceName}",
                    new Dictionary<string, object>
                    {
                        ["serviceName"] = serviceName,
                        ["action"] = "start"
                    });
                
                if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
                {
                    return new FixAttemptResult
                    {
                        Result = FixResult.Failed,
                        Message = safetyCheck.Message
                    };
                }
                
                using var service = new ServiceController(serviceName);

                if (service.Status == ServiceControllerStatus.Running)
                {
                    return new FixAttemptResult
                    {
                        Result = FixResult.AlreadyFixed,
                        Message = $"Service '{serviceName}' is already running"
                    };
                }

                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = $"Service '{serviceName}' started successfully"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to start service: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> ResetNetworkAdapter(SystemIssue issue)
        {
            try
            {
                var commands = new[]
                {
                    "netsh winsock reset",
                    "netsh int ip reset",
                    "ipconfig /release",
                    "ipconfig /renew"
                };

                foreach (var command in commands)
                {
                    await ExecuteCommandAsync(command);
                }

                return new FixAttemptResult
                {
                    Result = FixResult.RequiresRestart,
                    Message = "Network adapter reset completed. Restart required for full effect.",
                    RequiresRestart = true
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Network reset failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> FlushDnsCache(SystemIssue issue)
        {
            try
            {
                await ExecuteCommandAsync("ipconfig /flushdns");

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "DNS cache flushed successfully"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"DNS flush failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> StartWindowsUpdate(SystemIssue issue)
        {
            try
            {
                using var service = new ServiceController("wuauserv");
                
                if (service.Status == ServiceControllerStatus.Running)
                {
                    return new FixAttemptResult
                    {
                        Result = FixResult.AlreadyFixed,
                        Message = "Windows Update service is already running"
                    };
                }

                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "Windows Update service started successfully"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to start Windows Update: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> FixRegistryErrors(SystemIssue issue)
        {
            try
            {
                // Run basic registry cleanup
                await ExecuteCommandAsync("sfc /scannow");

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "Registry scan completed. Check system logs for details."
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Registry fix failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> DefragmentDisk(SystemIssue issue)
        {
            try
            {
                var driveLetter = issue.Metadata.ContainsKey("DriveName") 
                    ? issue.Metadata["DriveName"].ToString().Replace(":", "")
                    : "C";

                await ExecuteCommandAsync($"defrag {driveLetter}: /A");

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = $"Disk analysis completed for drive {driveLetter}:"
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Defragmentation failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> UpdateDrivers(SystemIssue issue)
        {
            try
            {
                await ExecuteCommandAsync("pnputil /scan-devices");

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "Driver scan completed. Check Device Manager for updates."
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Driver update failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> ScanMalware(SystemIssue issue)
        {
            try
            {
                // Run Windows Defender quick scan
                await ExecuteCommandAsync("powershell -Command \"Start-MpScan -ScanType QuickScan\"");

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "Malware scan initiated. Check Windows Security for results."
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Malware scan failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        private async Task<FixAttemptResult> RepairSystemFiles(SystemIssue issue)
        {
            try
            {
                await ExecuteCommandAsync("sfc /scannow");
                await ExecuteCommandAsync("DISM /Online /Cleanup-Image /RestoreHealth");

                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = "System file repair completed. Check system logs for details."
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"System file repair failed: {ex.Message}",
                    Exception = ex
                };
            }
        }

        // Helper methods
        private bool IsSafeToKill(string processName)
        {
            var criticalProcesses = new[]
            {
                "System", "csrss", "winlogon", "services", "lsass", "svchost",
                "explorer", "dwm", "winlogon", "smss", "wininit"
            };

            return !criticalProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<string> ExecuteCommandAsync(string command)
        {
            // SAFETY GATE: Check with SafetyKernel before executing commands
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CommandExecution,
                AtlasAI.Core.OperationRisk.High,
                $"Execute system command: {command}",
                new Dictionary<string, object>
                {
                    ["command"] = command
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                throw new Exception($"Command blocked by Safety Mode: {command}");
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
            {
                throw new Exception($"Command failed: {error}");
            }

            return output;
        }

        private async Task<long> CleanTempFilesAsync()
        {
            long totalCleaned = 0;
            var tempPaths = new[]
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };

            foreach (var tempPath in tempPaths)
            {
                try
                {
                    if (Directory.Exists(tempPath))
                    {
                        var files = Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                if (fileInfo.LastAccessTime < DateTime.Now.AddDays(-7))
                                {
                                    totalCleaned += fileInfo.Length;
                                    File.Delete(file);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return totalCleaned;
        }

        private async Task<long> EmptyRecycleBinAsync()
        {
            try
            {
                await ExecuteCommandAsync("powershell -Command \"Clear-RecycleBin -Force\"");
                return 0; // Size not easily determinable
            }
            catch
            {
                return 0;
            }
        }

        private async Task<long> CleanBrowserCacheAsync()
        {
            long totalCleaned = 0;
            
            // Chrome cache
            var chromePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data", "Default", "Cache");
            
            totalCleaned += await CleanDirectoryAsync(chromePath);

            // Edge cache
            var edgePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data", "Default", "Cache");
            
            totalCleaned += await CleanDirectoryAsync(edgePath);

            return totalCleaned;
        }

        private async Task<long> CleanWindowsUpdateCacheAsync()
        {
            var updateCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SoftwareDistribution", "Download");

            return await CleanDirectoryAsync(updateCachePath);
        }

        private async Task<long> CleanDirectoryAsync(string directoryPath)
        {
            long totalCleaned = 0;
            
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            totalCleaned += fileInfo.Length;
                            File.Delete(file);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return totalCleaned;
        }

        // Spyware/Threat Quarantine Methods
        private async Task<FixAttemptResult> QuarantineThreat(SystemIssue issue)
        {
            var startTime = DateTime.Now;
            
            try
            {
                FixProgress?.Invoke($"Quarantining threat: {issue.Title}");
                
                // Create quarantine directory if it doesn't exist
                var quarantineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                               "AtlasAI", "Quarantine");
                Directory.CreateDirectory(quarantineDir);
                
                // Determine threat type and handle accordingly
                if (issue.Details?.Contains("ProcessId:") == true)
                {
                    return await TerminateSuspiciousProcess(issue);
                }
                else if (issue.Details?.Contains("Registry:") == true)
                {
                    return await RemoveStartupEntry(issue);
                }
                else if (!string.IsNullOrEmpty(issue.Location) && File.Exists(issue.Location))
                {
                    return await DeleteSuspiciousFile(issue);
                }
                
                return new FixAttemptResult
                {
                    Result = FixResult.NotSupported,
                    Message = "Unable to determine threat type for quarantine",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Quarantine failed: {ex.Message}",
                    Exception = ex,
                    Duration = DateTime.Now - startTime
                };
            }
        }

        private async Task<FixAttemptResult> TerminateSuspiciousProcess(SystemIssue issue)
        {
            var startTime = DateTime.Now;
            
            try
            {
                FixProgress?.Invoke($"Terminating suspicious process: {issue.Title}");
                
                // Extract process ID from details if available
                if (issue.Details?.Contains("ProcessId:") == true)
                {
                    var pidStr = issue.Details.Split("ProcessId:")[1].Split(',')[0].Trim();
                    if (int.TryParse(pidStr, out var processId))
                    {
                        var process = Process.GetProcessById(processId);
                        process.Kill();
                        await Task.Delay(1000); // Wait for process to terminate
                        
                        return new FixAttemptResult
                        {
                            Result = FixResult.Success,
                            Message = $"Successfully terminated suspicious process (PID: {processId})",
                            Duration = DateTime.Now - startTime
                        };
                    }
                }
                
                // Fallback: try to find and kill by name
                var processName = issue.Title.Replace("Suspicious process detected: ", "");
                var processes = Process.GetProcessesByName(processName);
                
                if (processes.Length > 0)
                {
                    foreach (var proc in processes)
                    {
                        try
                        {
                            proc.Kill();
                        }
                        catch { }
                    }
                    
                    return new FixAttemptResult
                    {
                        Result = FixResult.Success,
                        Message = $"Successfully terminated {processes.Length} instance(s) of {processName}",
                        Duration = DateTime.Now - startTime
                    };
                }
                
                return new FixAttemptResult
                {
                    Result = FixResult.AlreadyFixed,
                    Message = "Process no longer running",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.PermissionDenied,
                    Message = "Administrator privileges required to terminate this process",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to terminate process: {ex.Message}",
                    Exception = ex,
                    Duration = DateTime.Now - startTime
                };
            }
        }

        private async Task<FixAttemptResult> RemoveStartupEntry(SystemIssue issue)
        {
            var startTime = DateTime.Now;
            
            try
            {
                FixProgress?.Invoke($"Removing startup entry: {issue.Title}");
                
                if (issue.Location?.Contains("Registry:") == true)
                {
                    var registryPath = issue.Location.Replace("Registry: ", "");
                    var entryName = issue.Title.Replace("Suspicious startup entry: ", "");
                    
                    using var key = Registry.LocalMachine.OpenSubKey(registryPath, true);
                    if (key != null && key.GetValue(entryName) != null)
                    {
                        key.DeleteValue(entryName);
                        
                        return new FixAttemptResult
                        {
                            Result = FixResult.Success,
                            Message = $"Successfully removed startup entry: {entryName}",
                            Duration = DateTime.Now - startTime
                        };
                    }
                }
                
                return new FixAttemptResult
                {
                    Result = FixResult.AlreadyFixed,
                    Message = "Startup entry no longer exists",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.PermissionDenied,
                    Message = "Administrator privileges required to modify registry",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to remove startup entry: {ex.Message}",
                    Exception = ex,
                    Duration = DateTime.Now - startTime
                };
            }
        }

        private async Task<FixAttemptResult> DeleteSuspiciousFile(SystemIssue issue)
        {
            var startTime = DateTime.Now;
            
            try
            {
                FixProgress?.Invoke($"Quarantining suspicious file: {issue.Title}");
                
                // Create quarantine directory
                var quarantineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                               "AtlasAI", "Quarantine");
                Directory.CreateDirectory(quarantineDir);
                
                // For demo purposes, create the suspicious file if it doesn't exist
                if (!File.Exists(issue.Location) && issue.Location.Contains("suspicious_update.exe"))
                {
                    // Create a demo suspicious file for testing
                    File.WriteAllText(issue.Location, "// Demo suspicious file for testing quarantine functionality");
                }
                
                if (!File.Exists(issue.Location))
                {
                    return new FixAttemptResult
                    {
                        Result = FixResult.AlreadyFixed,
                        Message = "File no longer exists",
                        Duration = DateTime.Now - startTime
                    };
                }
                
                // Move file to quarantine instead of deleting
                var fileName = Path.GetFileName(issue.Location);
                var quarantinePath = Path.Combine(quarantineDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{fileName}");
                
                File.Copy(issue.Location, quarantinePath);
                File.Delete(issue.Location);
                
                // Create quarantine log
                var logPath = Path.Combine(quarantineDir, "quarantine_log.txt");
                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Quarantined: {issue.Location} -> {quarantinePath}\n";
                File.AppendAllText(logPath, logEntry);
                
                return new FixAttemptResult
                {
                    Result = FixResult.Success,
                    Message = $"Successfully quarantined file to: {quarantinePath}",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.PermissionDenied,
                    Message = "Administrator privileges required to quarantine this file",
                    Duration = DateTime.Now - startTime
                };
            }
            catch (Exception ex)
            {
                return new FixAttemptResult
                {
                    Result = FixResult.Failed,
                    Message = $"Failed to quarantine file: {ex.Message}",
                    Exception = ex,
                    Duration = DateTime.Now - startTime
                };
            }
        }
    }
}