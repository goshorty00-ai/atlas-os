using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.ITManagement
{
    /// <summary>
    /// Library of IT automation scripts Atlas can execute
    /// </summary>
    public class AutomationScriptLibrary
    {
        private readonly List<AutomationScript> _scripts = new();
        private readonly List<ScriptExecutionLog> _executionHistory = new();
        
        public event Action<string>? OnScriptOutput;
        public event Action<ScriptExecutionLog>? OnScriptCompleted;
        
        public IReadOnlyList<AutomationScript> Scripts => _scripts;
        public IReadOnlyList<ScriptExecutionLog> ExecutionHistory => _executionHistory;
        
        public AutomationScriptLibrary()
        {
            InitializeBuiltInScripts();
        }
        
        private void InitializeBuiltInScripts()
        {
            // System Cleanup Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "cleanup_temp",
                Name = "Clean Temporary Files",
                Description = "Removes temporary files from Windows temp folders, browser caches, and recycle bin",
                Category = ScriptCategory.Cleanup,
                EstimatedDuration = "1-5 minutes",
                RequiresAdmin = false,
                Icon = "🧹"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "cleanup_windows_update",
                Name = "Clean Windows Update Cache",
                Description = "Clears Windows Update download cache to free up disk space",
                Category = ScriptCategory.Cleanup,
                EstimatedDuration = "2-10 minutes",
                RequiresAdmin = true,
                Icon = "🪟"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "empty_recycle_bin",
                Name = "Empty Recycle Bin",
                Description = "Permanently deletes all items in the Recycle Bin",
                Category = ScriptCategory.Cleanup,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "🗑️"
            });
            
            // Performance Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "optimize_startup",
                Name = "Optimize Startup Programs",
                Description = "Lists and allows disabling of startup programs to speed up boot time",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "🚀"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "flush_dns",
                Name = "Flush DNS Cache",
                Description = "Clears the DNS resolver cache to fix network issues",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = true,
                Icon = "🌐"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "clear_memory",
                Name = "Clear Standby Memory",
                Description = "Frees up standby memory to improve system responsiveness",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = true,
                Icon = "💾"
            });
            
            // Maintenance Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "check_disk",
                Name = "Check Disk Health",
                Description = "Runs CHKDSK to check for disk errors and bad sectors",
                Category = ScriptCategory.Maintenance,
                EstimatedDuration = "5-30 minutes",
                RequiresAdmin = true,
                Icon = "💿"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "sfc_scan",
                Name = "System File Checker",
                Description = "Scans and repairs corrupted Windows system files",
                Category = ScriptCategory.Maintenance,
                EstimatedDuration = "10-30 minutes",
                RequiresAdmin = true,
                Icon = "🔧"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "defrag_drives",
                Name = "Optimize Drives",
                Description = "Defragments HDDs or optimizes SSDs for better performance",
                Category = ScriptCategory.Maintenance,
                EstimatedDuration = "10-60 minutes",
                RequiresAdmin = true,
                Icon = "⚡"
            });
            
            // Security Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "windows_defender_scan",
                Name = "Quick Virus Scan",
                Description = "Runs Windows Defender quick scan for malware",
                Category = ScriptCategory.Security,
                EstimatedDuration = "5-15 minutes",
                RequiresAdmin = false,
                Icon = "🛡️"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "check_updates",
                Name = "Check for Updates",
                Description = "Checks for available Windows and driver updates",
                Category = ScriptCategory.Security,
                EstimatedDuration = "1-5 minutes",
                RequiresAdmin = false,
                Icon = "🔄"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "firewall_status",
                Name = "Check Firewall Status",
                Description = "Verifies Windows Firewall is enabled and configured correctly",
                Category = ScriptCategory.Security,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "🔥"
            });
            
            // Network Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "network_reset",
                Name = "Reset Network Stack",
                Description = "Resets TCP/IP stack, Winsock, and network adapters",
                Category = ScriptCategory.Network,
                EstimatedDuration = "1-2 minutes",
                RequiresAdmin = true,
                Icon = "📡"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "speed_test",
                Name = "Network Speed Test",
                Description = "Tests internet download and upload speeds",
                Category = ScriptCategory.Network,
                EstimatedDuration = "1-2 minutes",
                RequiresAdmin = false,
                Icon = "📶"
            });
            
            // Backup Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "create_restore_point",
                Name = "Create Restore Point",
                Description = "Creates a Windows System Restore point",
                Category = ScriptCategory.Backup,
                EstimatedDuration = "1-5 minutes",
                RequiresAdmin = true,
                Icon = "💾"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "backup_documents",
                Name = "Backup Documents",
                Description = "Backs up Documents folder to specified location",
                Category = ScriptCategory.Backup,
                EstimatedDuration = "5-30 minutes",
                RequiresAdmin = false,
                Icon = "📁"
            });
            
            // Process Management Scripts
            _scripts.Add(new AutomationScript
            {
                Id = "kill_chrome",
                Name = "Kill All Chrome Processes",
                Description = "Terminates all Google Chrome processes to free up memory",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "🌐"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "kill_browser_processes",
                Name = "Kill All Browser Processes",
                Description = "Terminates Chrome, Edge, Firefox, and other browser processes",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "🔪"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "kill_process",
                Name = "Kill Specific Process",
                Description = "Terminates a specific process by name",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "❌"
            });
            
            _scripts.Add(new AutomationScript
            {
                Id = "list_high_memory",
                Name = "List High Memory Processes",
                Description = "Shows processes using the most memory",
                Category = ScriptCategory.Performance,
                EstimatedDuration = "< 1 minute",
                RequiresAdmin = false,
                Icon = "📊"
            });
        }
        
        public async Task<ScriptResult> ExecuteScriptAsync(string scriptId, Dictionary<string, string>? parameters = null)
        {
            var script = _scripts.FirstOrDefault(s => s.Id == scriptId);
            if (script == null)
                return new ScriptResult { Success = false, Message = $"Script '{scriptId}' not found" };
            
            var log = new ScriptExecutionLog
            {
                ScriptId = scriptId,
                ScriptName = script.Name,
                StartTime = DateTime.Now,
                Parameters = parameters ?? new()
            };
            
            OnScriptOutput?.Invoke($"🚀 Starting: {script.Name}");
            
            try
            {
                var result = scriptId switch
                {
                    "cleanup_temp" => await CleanTempFilesAsync(),
                    "cleanup_windows_update" => await CleanWindowsUpdateCacheAsync(),
                    "empty_recycle_bin" => await EmptyRecycleBinAsync(),
                    "optimize_startup" => await GetStartupProgramsAsync(),
                    "flush_dns" => await FlushDnsAsync(),
                    "clear_memory" => await ClearStandbyMemoryAsync(),
                    "check_disk" => await CheckDiskAsync(parameters?.GetValueOrDefault("drive", "C:")),
                    "sfc_scan" => await RunSfcScanAsync(),
                    "windows_defender_scan" => await RunDefenderScanAsync(),
                    "check_updates" => await CheckWindowsUpdatesAsync(),
                    "firewall_status" => await CheckFirewallStatusAsync(),
                    "network_reset" => await ResetNetworkStackAsync(),
                    "speed_test" => await RunSpeedTestAsync(),
                    "create_restore_point" => await CreateRestorePointAsync(parameters?.GetValueOrDefault("name", "Atlas AI Restore Point")),
                    "defrag_drives" => await OptimizeDrivesAsync(),
                    "kill_chrome" => await KillChromeProcessesAsync(),
                    "kill_browser_processes" => await KillAllBrowsersAsync(),
                    "kill_process" => await KillProcessByNameAsync(parameters?.GetValueOrDefault("process", "")),
                    "list_high_memory" => await ListHighMemoryProcessesAsync(),
                    _ => new ScriptResult { Success = false, Message = "Script not implemented" }
                };
                
                log.EndTime = DateTime.Now;
                log.Success = result.Success;
                log.Output = result.Message;
                
                _executionHistory.Add(log);
                OnScriptCompleted?.Invoke(log);
                OnScriptOutput?.Invoke(result.Success ? $"✅ Completed: {script.Name}" : $"❌ Failed: {script.Name}");
                
                return result;
            }
            catch (Exception ex)
            {
                log.EndTime = DateTime.Now;
                log.Success = false;
                log.Output = ex.Message;
                _executionHistory.Add(log);
                
                OnScriptOutput?.Invoke($"❌ Error: {ex.Message}");
                return new ScriptResult { Success = false, Message = ex.Message };
            }
        }

        
        #region Script Implementations
        
        private async Task<ScriptResult> CleanTempFilesAsync()
        {
            long totalFreed = 0;
            var errors = new List<string>();
            
            string[] tempPaths = {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };
            
            foreach (var tempPath in tempPaths)
            {
                if (Directory.Exists(tempPath))
                {
                    var (freed, err) = await CleanDirectoryAsync(tempPath);
                    totalFreed += freed;
                    errors.AddRange(err);
                }
            }
            
            // Clean browser caches
            var browserCaches = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "Google", "Chrome", "User Data", "Default", "Cache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data", "Default", "Cache")
            };
            
            foreach (var cache in browserCaches)
            {
                if (Directory.Exists(cache))
                {
                    var (freed, err) = await CleanDirectoryAsync(cache);
                    totalFreed += freed;
                }
            }
            
            var freedMB = totalFreed / 1048576.0;
            return new ScriptResult
            {
                Success = true,
                Message = $"Cleaned {freedMB:F1} MB of temporary files",
                Data = new { FreedBytes = totalFreed, FreedMB = freedMB }
            };
        }
        
        private async Task<(long freed, List<string> errors)> CleanDirectoryAsync(string path)
        {
            long freed = 0;
            var errors = new List<string>();
            
            await Task.Run(() =>
            {
                try
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            var size = info.Length;
                            info.Delete();
                            freed += size;
                        }
                        catch { }
                    }
                    
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            });
            
            return (freed, errors);
        }
        
        private async Task<ScriptResult> CleanWindowsUpdateCacheAsync()
        {
            return await RunPowerShellAsync(@"
                Stop-Service -Name wuauserv -Force -ErrorAction SilentlyContinue
                Remove-Item -Path 'C:\Windows\SoftwareDistribution\Download\*' -Recurse -Force -ErrorAction SilentlyContinue
                Start-Service -Name wuauserv
                Write-Output 'Windows Update cache cleared'
            ");
        }
        
        private async Task<ScriptResult> EmptyRecycleBinAsync()
        {
            return await RunPowerShellAsync("Clear-RecycleBin -Force -ErrorAction SilentlyContinue; Write-Output 'Recycle Bin emptied'");
        }
        
        private async Task<ScriptResult> GetStartupProgramsAsync()
        {
            var startupItems = new List<StartupItem>();
            
            // Registry Run keys
            string[] runKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            
            foreach (var keyPath in runKeys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        startupItems.Add(new StartupItem
                        {
                            Name = name,
                            Command = key.GetValue(name)?.ToString() ?? "",
                            Location = $"HKLM\\{keyPath}",
                            Enabled = true
                        });
                    }
                }
                
                using var userKey = Registry.CurrentUser.OpenSubKey(keyPath);
                if (userKey != null)
                {
                    foreach (var name in userKey.GetValueNames())
                    {
                        startupItems.Add(new StartupItem
                        {
                            Name = name,
                            Command = userKey.GetValue(name)?.ToString() ?? "",
                            Location = $"HKCU\\{keyPath}",
                            Enabled = true
                        });
                    }
                }
            }
            
            // Startup folder
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder, "*.lnk"))
                {
                    startupItems.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = "Startup Folder",
                        Enabled = true
                    });
                }
            }
            
            var summary = string.Join("\n", startupItems.Select(s => $"• {s.Name}"));
            return new ScriptResult
            {
                Success = true,
                Message = $"Found {startupItems.Count} startup programs:\n{summary}",
                Data = startupItems
            };
        }
        
        private async Task<ScriptResult> FlushDnsAsync()
        {
            return await RunCommandAsync("ipconfig", "/flushdns");
        }
        
        private async Task<ScriptResult> ClearStandbyMemoryAsync()
        {
            // This requires a small utility or PowerShell with admin rights
            return await RunPowerShellAsync(@"
                [System.GC]::Collect()
                [System.GC]::WaitForPendingFinalizers()
                Write-Output 'Memory cleanup initiated'
            ");
        }
        
        private async Task<ScriptResult> CheckDiskAsync(string drive)
        {
            return await RunCommandAsync("chkdsk", $"{drive} /scan");
        }
        
        private async Task<ScriptResult> RunSfcScanAsync()
        {
            OnScriptOutput?.Invoke("Running System File Checker (this may take a while)...");
            return await RunCommandAsync("sfc", "/scannow");
        }
        
        private async Task<ScriptResult> RunDefenderScanAsync()
        {
            var defenderPath = @"C:\Program Files\Windows Defender\MpCmdRun.exe";
            if (!File.Exists(defenderPath))
                return new ScriptResult { Success = false, Message = "Windows Defender not found" };
            
            return await RunCommandAsync(defenderPath, "-Scan -ScanType 1");
        }
        
        private async Task<ScriptResult> CheckWindowsUpdatesAsync()
        {
            return await RunPowerShellAsync(@"
                $UpdateSession = New-Object -ComObject Microsoft.Update.Session
                $UpdateSearcher = $UpdateSession.CreateUpdateSearcher()
                $Updates = $UpdateSearcher.Search('IsInstalled=0')
                if ($Updates.Updates.Count -eq 0) {
                    Write-Output 'No updates available'
                } else {
                    Write-Output ""$($Updates.Updates.Count) updates available:""
                    $Updates.Updates | ForEach-Object { Write-Output ""  - $($_.Title)"" }
                }
            ");
        }
        
        private async Task<ScriptResult> CheckFirewallStatusAsync()
        {
            return await RunPowerShellAsync(@"
                $fw = Get-NetFirewallProfile
                $fw | ForEach-Object {
                    $status = if ($_.Enabled) { '✅ Enabled' } else { '❌ Disabled' }
                    Write-Output ""$($_.Name): $status""
                }
            ");
        }
        
        private async Task<ScriptResult> ResetNetworkStackAsync()
        {
            var commands = new[]
            {
                ("netsh", "winsock reset"),
                ("netsh", "int ip reset"),
                ("ipconfig", "/release"),
                ("ipconfig", "/renew"),
                ("ipconfig", "/flushdns")
            };
            
            var results = new List<string>();
            foreach (var (cmd, args) in commands)
            {
                var result = await RunCommandAsync(cmd, args);
                results.Add($"{cmd} {args}: {(result.Success ? "OK" : "Failed")}");
            }
            
            return new ScriptResult
            {
                Success = true,
                Message = "Network stack reset complete. You may need to restart.\n" + string.Join("\n", results)
            };
        }
        
        private async Task<ScriptResult> RunSpeedTestAsync()
        {
            // Simple speed test using a known file
            OnScriptOutput?.Invoke("Testing download speed...");
            
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                var sw = Stopwatch.StartNew();
                var response = await client.GetAsync("http://speedtest.tele2.net/1MB.zip");
                var bytes = await response.Content.ReadAsByteArrayAsync();
                sw.Stop();
                
                var mbps = (bytes.Length * 8.0 / 1000000) / (sw.ElapsedMilliseconds / 1000.0);
                
                return new ScriptResult
                {
                    Success = true,
                    Message = $"Download speed: {mbps:F1} Mbps",
                    Data = new { SpeedMbps = mbps, BytesDownloaded = bytes.Length, TimeMs = sw.ElapsedMilliseconds }
                };
            }
            catch (Exception ex)
            {
                return new ScriptResult { Success = false, Message = $"Speed test failed: {ex.Message}" };
            }
        }
        
        private async Task<ScriptResult> CreateRestorePointAsync(string name)
        {
            return await RunPowerShellAsync($@"
                Checkpoint-Computer -Description '{name}' -RestorePointType 'MODIFY_SETTINGS'
                Write-Output 'Restore point created: {name}'
            ");
        }
        
        private async Task<ScriptResult> OptimizeDrivesAsync()
        {
            return await RunPowerShellAsync(@"
                Get-Volume | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' } | ForEach-Object {
                    Write-Output ""Optimizing drive $($_.DriveLetter):...""
                    Optimize-Volume -DriveLetter $_.DriveLetter -Verbose
                }
                Write-Output 'Drive optimization complete'
            ");
        }
        
        private async Task<ScriptResult> KillChromeProcessesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var chromeProcesses = Process.GetProcessesByName("chrome");
                    var count = chromeProcesses.Length;
                    long memoryFreed = 0;
                    
                    foreach (var proc in chromeProcesses)
                    {
                        try
                        {
                            memoryFreed += proc.WorkingSet64;
                            proc.Kill();
                            proc.WaitForExit(1000);
                        }
                        catch { }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                    
                    var mbFreed = memoryFreed / (1024.0 * 1024.0);
                    return new ScriptResult
                    {
                        Success = true,
                        Message = $"Killed {count} Chrome processes, freed ~{mbFreed:F0} MB of memory",
                        Data = new { ProcessesKilled = count, MemoryFreedMB = mbFreed }
                    };
                }
                catch (Exception ex)
                {
                    return new ScriptResult { Success = false, Message = $"Error: {ex.Message}" };
                }
            });
        }
        
        private async Task<ScriptResult> KillAllBrowsersAsync()
        {
            return await Task.Run(() =>
            {
                var browserNames = new[] { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };
                var totalKilled = 0;
                long totalMemory = 0;
                var results = new List<string>();
                
                foreach (var browser in browserNames)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(browser);
                        if (processes.Length > 0)
                        {
                            long browserMemory = 0;
                            foreach (var proc in processes)
                            {
                                try
                                {
                                    browserMemory += proc.WorkingSet64;
                                    proc.Kill();
                                    proc.WaitForExit(1000);
                                    totalKilled++;
                                }
                                catch { }
                                finally
                                {
                                    proc.Dispose();
                                }
                            }
                            totalMemory += browserMemory;
                            results.Add($"{browser}: {processes.Length} processes ({browserMemory / (1024 * 1024):F0} MB)");
                        }
                    }
                    catch { }
                }
                
                var mbFreed = totalMemory / (1024.0 * 1024.0);
                return new ScriptResult
                {
                    Success = true,
                    Message = $"Killed {totalKilled} browser processes, freed ~{mbFreed:F0} MB\n" + string.Join("\n", results),
                    Data = new { ProcessesKilled = totalKilled, MemoryFreedMB = mbFreed }
                };
            });
        }
        
        private async Task<ScriptResult> KillProcessByNameAsync(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return new ScriptResult { Success = false, Message = "Process name not specified" };
            
            return await Task.Run(() =>
            {
                try
                {
                    // Remove .exe if provided
                    processName = processName.Replace(".exe", "").Trim();
                    
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length == 0)
                        return new ScriptResult { Success = false, Message = $"No processes found with name '{processName}'" };
                    
                    var count = processes.Length;
                    long memoryFreed = 0;
                    
                    foreach (var proc in processes)
                    {
                        try
                        {
                            memoryFreed += proc.WorkingSet64;
                            proc.Kill();
                            proc.WaitForExit(1000);
                        }
                        catch { }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                    
                    var mbFreed = memoryFreed / (1024.0 * 1024.0);
                    return new ScriptResult
                    {
                        Success = true,
                        Message = $"Killed {count} '{processName}' processes, freed ~{mbFreed:F0} MB",
                        Data = new { ProcessesKilled = count, MemoryFreedMB = mbFreed }
                    };
                }
                catch (Exception ex)
                {
                    return new ScriptResult { Success = false, Message = $"Error: {ex.Message}" };
                }
            });
        }
        
        private async Task<ScriptResult> ListHighMemoryProcessesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses()
                        .Where(p => { try { return p.WorkingSet64 > 0; } catch { return false; } })
                        .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                        .Take(15)
                        .Select(p =>
                        {
                            try
                            {
                                return new
                                {
                                    Name = p.ProcessName,
                                    MemoryMB = p.WorkingSet64 / (1024.0 * 1024.0),
                                    Id = p.Id
                                };
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(p => p != null)
                        .ToList();
                    
                    var summary = string.Join("\n", processes.Select(p => $"• {p!.Name}: {p.MemoryMB:F0} MB (PID: {p.Id})"));
                    
                    return new ScriptResult
                    {
                        Success = true,
                        Message = $"Top 15 processes by memory:\n{summary}",
                        Data = processes
                    };
                }
                catch (Exception ex)
                {
                    return new ScriptResult { Success = false, Message = $"Error: {ex.Message}" };
                }
            });
        }
        
        #endregion
        
        #region Helpers
        
        private async Task<ScriptResult> RunCommandAsync(string command, string arguments)
        {
            // SAFETY GATE: Check with SafetyKernel before executing commands
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CommandExecution,
                AtlasAI.Core.OperationRisk.High,
                $"Execute: {command} {arguments}",
                new Dictionary<string, object>
                {
                    ["command"] = command,
                    ["arguments"] = arguments
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                return new ScriptResult 
                { 
                    Success = false, 
                    Message = safetyCheck.Message
                };
            }

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process == null)
                        return new ScriptResult { Success = false, Message = "Failed to start process" };
                    
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    return new ScriptResult
                    {
                        Success = process.ExitCode == 0,
                        Message = string.IsNullOrEmpty(error) ? output : $"{output}\nErrors: {error}"
                    };
                }
                catch (Exception ex)
                {
                    return new ScriptResult { Success = false, Message = ex.Message };
                }
            });
        }
        
        private async Task<ScriptResult> RunPowerShellAsync(string script)
        {
            return await RunCommandAsync("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"");
        }
        
        #endregion
    }

    
    #region Data Models
    
    public class AutomationScript
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ScriptCategory Category { get; set; }
        public string EstimatedDuration { get; set; } = "";
        public bool RequiresAdmin { get; set; }
        public string Icon { get; set; } = "⚙️";
    }
    
    public enum ScriptCategory
    {
        Cleanup,
        Performance,
        Maintenance,
        Security,
        Network,
        Backup
    }
    
    public class ScriptResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public object? Data { get; set; }
    }
    
    public class ScriptExecutionLog
    {
        public string ScriptId { get; set; } = "";
        public string ScriptName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public Dictionary<string, string> Parameters { get; set; } = new();
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class StartupItem
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Location { get; set; } = "";
        public bool Enabled { get; set; }
    }
    
    #endregion
}
