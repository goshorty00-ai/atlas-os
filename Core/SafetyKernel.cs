using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Core
{
    /// <summary>
    /// Operation types that can be blocked by the Safety Kernel
    /// </summary>
    public enum OperationType
    {
        CommandExecution,
        RegistryWrite,
        RegistryDelete,
        Uninstall,
        CleanupLeftovers,
        StartupEntryChange,
        ServiceChange,
        ScheduledTaskChange,
        SystemFileDelete,
        ProcessKillCritical,
        FileDelete,
        FolderDelete
    }

    /// <summary>
    /// Risk level of an operation
    /// </summary>
    public enum OperationRisk
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Decision result from the Safety Kernel
    /// </summary>
    public enum SafetyDecision
    {
        Allowed,
        Blocked
    }

    /// <summary>
    /// Result of a safety check
    /// </summary>
    public class SafetyCheckResult
    {
        public SafetyDecision Decision { get; set; }
        public string Message { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Log entry for attempted actions
    /// </summary>
    public class ActionAttemptLog
    {
        public DateTime Timestamp { get; set; }
        public OperationType Type { get; set; }
        public OperationRisk Risk { get; set; }
        public string Description { get; set; } = "";
        public SafetyDecision Decision { get; set; }
        public string Reason { get; set; } = "";
        public Dictionary<string, object> Payload { get; set; } = new();
    }

    /// <summary>
    /// Central Safety Kernel - all dangerous operations must pass through this gate
    /// </summary>
    public class SafetyKernel
    {
        private static SafetyKernel? _instance;
        private static readonly object _lock = new object();

        private readonly string _logDirectory;
        private readonly string _settingsPath;
        
        // Safety settings
        private bool _dangerousActionsEnabled = false;
        private bool _allowRegistryCleanup = false; // Extra gate for registry cleanup
        
        // FAIL-CLOSED: Blocked command tokens - these are ALWAYS blocked regardless of settings
        private static readonly string[] BlockedCommandTokens = new[]
        {
            "reg ",           // Registry manipulation
            "bcdedit",        // Boot configuration
            "diskpart",       // Disk partitioning
            "format ",        // Drive formatting
            "del /f",         // Force file deletion
            "rd /s",          // Recursive directory removal
            "rmdir /s",       // Recursive directory removal
            "takeown",        // Ownership takeover
            "icacls",         // Permission modification
            "schtasks /delete", // Scheduled task deletion
            "sc delete",      // Service deletion
            "sc stop",        // Service stop (can break Windows)
            "net stop",       // Service stop via net
            "wmic",           // WMI command line (powerful)
            "cipher /w",      // Secure wipe
            "attrib -s -h",   // Remove system/hidden attributes
        };
        
        // FAIL-CLOSED: Safe registry namespace - only these paths are allowed for registry operations
        private static readonly string[] SafeRegistryNamespaces = new[]
        {
            @"HKCU\Software\AtlasAI",
            @"HKEY_CURRENT_USER\Software\AtlasAI",
            @"Software\AtlasAI",
        };
        
        // Critical processes that should NEVER be killed
        private static readonly string[] CriticalProcesses = new[]
        {
            "system", "csrss", "winlogon", "services", "lsass", "svchost",
            "smss", "wininit", "dwm", "explorer", "sihost", "fontdrvhost",
            "spoolsv", "searchindexer", "securityhealthservice", "msiexec"
        };
        
        public static SafetyKernel Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SafetyKernel();
                        }
                    }
                }
                return _instance;
            }
        }

        private SafetyKernel()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            
            _logDirectory = Path.Combine(appDataPath, "logs");
            _settingsPath = Path.Combine(appDataPath, "safety_settings.json");
            
            Directory.CreateDirectory(_logDirectory);
            LoadSettings();
        }

        /// <summary>
        /// Check if dangerous actions are enabled
        /// </summary>
        public bool DangerousActionsEnabled => _dangerousActionsEnabled;

        /// <summary>
        /// Check if registry cleanup is allowed (requires both DangerousActions AND this flag)
        /// </summary>
        public bool AllowRegistryCleanup => _allowRegistryCleanup;

        /// <summary>
        /// Enable or disable dangerous actions (requires explicit call)
        /// </summary>
        public void SetDangerousActionsEnabled(bool enabled)
        {
            _dangerousActionsEnabled = enabled;
            SaveSettings();
            System.Diagnostics.Debug.WriteLine($"[SafetyKernel] DangerousActionsEnabled set to: {enabled}");
        }

        /// <summary>
        /// Enable or disable registry cleanup (requires explicit call)
        /// </summary>
        public void SetAllowRegistryCleanup(bool enabled)
        {
            _allowRegistryCleanup = enabled;
            SaveSettings();
            System.Diagnostics.Debug.WriteLine($"[SafetyKernel] AllowRegistryCleanup set to: {enabled}");
        }

        /// <summary>
        /// Main safety gate - check if an operation should be allowed
        /// FAIL-CLOSED: Any exception defaults to BLOCKED
        /// </summary>
        public async Task<SafetyCheckResult> CheckAndBlockAsync(
            OperationType type,
            OperationRisk risk,
            string description,
            Dictionary<string, object>? payload = null)
        {
            payload ??= new Dictionary<string, object>();

            try
            {
                // FAIL-CLOSED: Enforce required payload fields for destructive operations
                var payloadCheck = ValidateRequiredPayload(type, payload);
                if (payloadCheck != null)
                {
                    await LogAttemptAsync(type, OperationRisk.Critical, description, SafetyDecision.Blocked, payloadCheck.Reason, payload);
                    return payloadCheck;
                }
                
                // FAIL-CLOSED: Check for blocked command tokens FIRST (always blocked)
                if (type == OperationType.CommandExecution)
                {
                    var command = description.ToLowerInvariant();
                    if (payload.TryGetValue("command", out var cmdObj))
                        command = cmdObj?.ToString()?.ToLowerInvariant() ?? command;
                    if (payload.TryGetValue("script", out var scriptObj))
                        command = scriptObj?.ToString()?.ToLowerInvariant() ?? command;
                    
                    foreach (var token in BlockedCommandTokens)
                    {
                        if (command.Contains(token.ToLowerInvariant()))
                        {
                            var result = new SafetyCheckResult
                            {
                                Decision = SafetyDecision.Blocked,
                                Message = $"🛡️ BLOCKED: Command contains dangerous token '{token}'",
                                Reason = $"Fail-closed: '{token}' is unconditionally blocked"
                            };
                            await LogAttemptAsync(type, OperationRisk.Critical, description, SafetyDecision.Blocked, result.Reason, payload);
                            return result;
                        }
                    }
                }
                
                // FAIL-CLOSED: Registry operations must be in safe namespace
                if (type == OperationType.RegistryWrite || type == OperationType.RegistryDelete)
                {
                    var registryPath = "";
                    if (payload.TryGetValue("registryPath", out var pathObj))
                        registryPath = pathObj?.ToString() ?? "";
                    if (payload.TryGetValue("path", out var path2Obj))
                        registryPath = path2Obj?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(registryPath))
                    {
                        var isInSafeNamespace = SafeRegistryNamespaces.Any(ns => 
                            registryPath.StartsWith(ns, StringComparison.OrdinalIgnoreCase));
                        
                        if (!isInSafeNamespace)
                        {
                            var result = new SafetyCheckResult
                            {
                                Decision = SafetyDecision.Blocked,
                                Message = "🛡️ BLOCKED: Registry path outside safe namespace",
                                Reason = $"Fail-closed: Only HKCU\\Software\\AtlasAI is allowed, got: {registryPath}"
                            };
                            await LogAttemptAsync(type, OperationRisk.Critical, description, SafetyDecision.Blocked, result.Reason, payload);
                            return result;
                        }
                    }
                }
                
                // FAIL-CLOSED: Check for critical process termination
                if (type == OperationType.ProcessKillCritical)
                {
                    var processName = "";
                    if (payload.TryGetValue("processName", out var procObj))
                        processName = procObj?.ToString()?.ToLowerInvariant() ?? "";
                    if (payload.TryGetValue("name", out var nameObj))
                        processName = nameObj?.ToString()?.ToLowerInvariant() ?? "";
                    
                    if (CriticalProcesses.Any(cp => processName.Contains(cp)))
                    {
                        var result = new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = $"🛡️ BLOCKED: Cannot terminate critical system process '{processName}'",
                            Reason = "Fail-closed: Critical Windows process protection"
                        };
                        await LogAttemptAsync(type, OperationRisk.Critical, description, SafetyDecision.Blocked, result.Reason, payload);
                        return result;
                    }
                }

                // CRITICAL: Registry cleanup is ALWAYS blocked unless both flags are enabled
                if (type == OperationType.RegistryDelete || type == OperationType.CleanupLeftovers)
                {
                    if (!_dangerousActionsEnabled || !_allowRegistryCleanup)
                    {
                        var result = new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ Action blocked by Safety Mode: Registry operations are disabled",
                            Reason = "Registry cleanup requires both DangerousActions AND AllowRegistryCleanup to be enabled"
                        };
                        
                        await LogAttemptAsync(type, risk, description, SafetyDecision.Blocked, result.Reason, payload);
                        return result;
                    }
                }

                // Check if dangerous actions are enabled
                if (!_dangerousActionsEnabled)
                {
                    if (type == OperationType.CommandExecution && IsSafeCommandAllowed(payload))
                    {
                        var allow = new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Allowed,
                            Message = "Operation allowed",
                            Reason = "Allowed by safe command allowlist"
                        };
                        await LogAttemptAsync(type, OperationRisk.Low, description, SafetyDecision.Allowed, allow.Reason, payload);
                        return allow;
                    }

                    var result = new SafetyCheckResult
                    {
                        Decision = SafetyDecision.Blocked,
                        Message = $"🛡️ Action blocked by Safety Mode: {GetOperationName(type)}",
                        Reason = "DangerousActionsEnabled is set to false"
                    };
                    
                    await LogAttemptAsync(type, risk, description, SafetyDecision.Blocked, result.Reason, payload);
                    return result;
                }

                // If we get here, dangerous actions are enabled - allow the operation
                var allowedResult = new SafetyCheckResult
                {
                    Decision = SafetyDecision.Allowed,
                    Message = "Operation allowed",
                    Reason = "DangerousActionsEnabled is true"
                };
                
                await LogAttemptAsync(type, risk, description, SafetyDecision.Allowed, allowedResult.Reason, payload);
                return allowedResult;
            }
            catch (Exception ex)
            {
                // FAIL-CLOSED: Any exception means BLOCKED
                System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Exception in CheckAndBlockAsync: {ex.Message}");
                var failedResult = new SafetyCheckResult
                {
                    Decision = SafetyDecision.Blocked,
                    Message = "🛡️ Action blocked due to safety check error",
                    Reason = $"Fail-closed: Exception during safety check: {ex.Message}"
                };
                
                try
                {
                    await LogAttemptAsync(type, risk, description, SafetyDecision.Blocked, failedResult.Reason, payload);
                }
                catch { /* Logging failure should not prevent blocking */ }
                
                return failedResult;
            }
        }

        private static bool IsSafeCommandAllowed(Dictionary<string, object> payload)
        {
            try
            {
                var cmd = "";
                if (payload.TryGetValue("command", out var c)) cmd = c?.ToString() ?? "";
                if (payload.TryGetValue("script", out var s)) cmd = s?.ToString() ?? cmd;
                cmd = (cmd ?? "").Trim();
                if (string.IsNullOrWhiteSpace(cmd)) return false;

                var lower = cmd.ToLowerInvariant();
                if (lower.StartsWith("dotnet build", StringComparison.Ordinal) || lower.StartsWith("dotnet test", StringComparison.Ordinal))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Check if a command contains blocked tokens (for external validation)
        /// </summary>
        public bool ContainsBlockedToken(string command, out string? blockedToken)
        {
            var cmdLower = command.ToLowerInvariant();
            foreach (var token in BlockedCommandTokens)
            {
                if (cmdLower.Contains(token.ToLowerInvariant()))
                {
                    blockedToken = token;
                    return true;
                }
            }
            blockedToken = null;
            return false;
        }
        
        /// <summary>
        /// Check if a registry path is in the safe namespace
        /// </summary>
        public bool IsRegistryPathSafe(string registryPath)
        {
            return SafeRegistryNamespaces.Any(ns => 
                registryPath.StartsWith(ns, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Check if a process name is critical (should not be killed)
        /// </summary>
        public bool IsCriticalProcess(string processName)
        {
            var nameLower = processName.ToLowerInvariant();
            return CriticalProcesses.Any(cp => nameLower.Contains(cp));
        }
        
        /// <summary>
        /// FAIL-CLOSED: Validate that required payload fields are present for destructive operations
        /// Returns null if valid, or a blocked result if missing required fields
        /// </summary>
        private SafetyCheckResult? ValidateRequiredPayload(OperationType type, Dictionary<string, object> payload)
        {
            switch (type)
            {
                case OperationType.RegistryWrite:
                case OperationType.RegistryDelete:
                    if (!payload.ContainsKey("registryPath") || string.IsNullOrEmpty(payload["registryPath"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Registry operation missing required payload",
                            Reason = "Fail-closed: registryPath is required for registry operations"
                        };
                    }
                    break;
                    
                case OperationType.FileDelete:
                case OperationType.FolderDelete:
                case OperationType.SystemFileDelete:
                    if (!payload.ContainsKey("path") || string.IsNullOrEmpty(payload["path"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Delete operation missing required payload",
                            Reason = "Fail-closed: path is required for delete operations"
                        };
                    }
                    break;
                    
                case OperationType.ProcessKillCritical:
                    if (!payload.ContainsKey("processName") || string.IsNullOrEmpty(payload["processName"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Process kill missing required payload",
                            Reason = "Fail-closed: processName is required for process termination"
                        };
                    }
                    // Also check if it's a critical process
                    var procName = payload["processName"]?.ToString() ?? "";
                    if (IsCriticalProcess(procName))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = $"🛡️ BLOCKED: Cannot terminate critical system process '{procName}'",
                            Reason = "Fail-closed: Critical Windows process protection"
                        };
                    }
                    break;
                    
                case OperationType.Uninstall:
                    if (!payload.ContainsKey("appName") || string.IsNullOrEmpty(payload["appName"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Uninstall operation missing required payload",
                            Reason = "Fail-closed: appName is required for uninstall operations"
                        };
                    }
                    break;
                    
                case OperationType.ServiceChange:
                    if (!payload.ContainsKey("serviceName") || string.IsNullOrEmpty(payload["serviceName"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Service operation missing required payload",
                            Reason = "Fail-closed: serviceName is required for service operations"
                        };
                    }
                    break;
                    
                case OperationType.StartupEntryChange:
                    if (!payload.ContainsKey("entryName") || string.IsNullOrEmpty(payload["entryName"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Startup entry operation missing required payload",
                            Reason = "Fail-closed: entryName is required for startup entry operations"
                        };
                    }
                    break;
                    
                case OperationType.ScheduledTaskChange:
                    if (!payload.ContainsKey("taskName") || string.IsNullOrEmpty(payload["taskName"]?.ToString()))
                    {
                        return new SafetyCheckResult
                        {
                            Decision = SafetyDecision.Blocked,
                            Message = "🛡️ BLOCKED: Scheduled task operation missing required payload",
                            Reason = "Fail-closed: taskName is required for scheduled task operations"
                        };
                    }
                    break;
            }
            
            return null; // Payload is valid
        }

        /// <summary>
        /// Log an attempted action to the append-only log file
        /// </summary>
        private async Task LogAttemptAsync(
            OperationType type,
            OperationRisk risk,
            string description,
            SafetyDecision decision,
            string reason,
            Dictionary<string, object> payload)
        {
            try
            {
                var logEntry = new ActionAttemptLog
                {
                    Timestamp = DateTime.Now,
                    Type = type,
                    Risk = risk,
                    Description = description,
                    Decision = decision,
                    Reason = reason,
                    Payload = payload
                };

                var logFileName = $"actions-{DateTime.Now:yyyy-MM-dd}.jsonl";
                var logFilePath = Path.Combine(_logDirectory, logFileName);

                var jsonLine = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions 
                { 
                    WriteIndented = false 
                });

                // Append to log file (one JSON object per line)
                await File.AppendAllTextAsync(logFilePath, jsonLine + Environment.NewLine);
                
                System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Logged: {type} - {decision} - {description}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Failed to log action: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a human-readable name for an operation type
        /// </summary>
        private string GetOperationName(OperationType type)
        {
            return type switch
            {
                OperationType.CommandExecution => "Command execution",
                OperationType.RegistryWrite => "Registry write",
                OperationType.RegistryDelete => "Registry delete",
                OperationType.Uninstall => "Software uninstall",
                OperationType.CleanupLeftovers => "Cleanup leftovers",
                OperationType.StartupEntryChange => "Startup entry change",
                OperationType.ServiceChange => "Service change",
                OperationType.ScheduledTaskChange => "Scheduled task change",
                OperationType.SystemFileDelete => "System file delete",
                OperationType.ProcessKillCritical => "Critical process termination",
                OperationType.FileDelete => "File delete",
                OperationType.FolderDelete => "Folder delete",
                _ => "Unknown operation"
            };
        }

        /// <summary>
        /// Load settings from disk
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("dangerousActionsEnabled", out var dangerousElement))
                    {
                        _dangerousActionsEnabled = dangerousElement.GetBoolean();
                    }
                    
                    if (doc.RootElement.TryGetProperty("allowRegistryCleanup", out var registryElement))
                    {
                        _allowRegistryCleanup = registryElement.GetBoolean();
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Settings loaded: DangerousActions={_dangerousActionsEnabled}, RegistryCleanup={_allowRegistryCleanup}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[SafetyKernel] No settings file found, using defaults (all disabled)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Failed to load settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Save settings to disk
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                var settings = new
                {
                    dangerousActionsEnabled = _dangerousActionsEnabled,
                    allowRegistryCleanup = _allowRegistryCleanup,
                    lastModified = DateTime.Now
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                File.WriteAllText(_settingsPath, json);
                System.Diagnostics.Debug.WriteLine("[SafetyKernel] Settings saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Get recent action logs for display
        /// </summary>
        public List<ActionAttemptLog> GetRecentLogs(int count = 50)
        {
            var logs = new List<ActionAttemptLog>();
            
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "actions-*.jsonl");
                Array.Sort(logFiles);
                Array.Reverse(logFiles); // Most recent first

                foreach (var logFile in logFiles)
                {
                    try
                    {
                        var lines = File.ReadAllLines(logFile);
                        Array.Reverse(lines); // Most recent first within file

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            try
                            {
                                var log = JsonSerializer.Deserialize<ActionAttemptLog>(line);
                                if (log != null)
                                {
                                    logs.Add(log);
                                    if (logs.Count >= count) return logs;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SafetyKernel] Failed to read logs: {ex.Message}");
            }

            return logs;
        }
        
        /// <summary>
        /// Get the log directory path
        /// </summary>
        public string LogDirectory => _logDirectory;
    }
    
    /// <summary>
    /// Result of a single self-test operation
    /// </summary>
    public class SelfTestResult
    {
        public string TestName { get; set; } = "";
        public OperationType OperationType { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; } = "";
        public bool WasBlocked { get; set; }
        public bool WasLogged { get; set; }
    }
    
    /// <summary>
    /// Safety self-test runner - validates that all dangerous operations are properly blocked
    /// </summary>
    public class SafetySelfTest
    {
        private readonly SafetyKernel _kernel;
        private readonly List<SelfTestResult> _results = new();
        
        public SafetySelfTest()
        {
            _kernel = SafetyKernel.Instance;
        }
        
        /// <summary>
        /// Run all safety self-tests
        /// </summary>
        public async Task<List<SelfTestResult>> RunAllTestsAsync()
        {
            _results.Clear();
            
            // Get log count before tests
            var logsBefore = _kernel.GetRecentLogs(1000).Count;
            
            // Test 1: Command execution should be blocked
            await TestOperationBlockedAsync(
                "CommandExecution - Basic",
                OperationType.CommandExecution,
                "Test command: echo hello",
                new Dictionary<string, object> { ["command"] = "echo hello" });
            
            // Test 2: Blocked command tokens should be rejected
            await TestBlockedTokenAsync("reg delete", "reg delete HKCU\\Test");
            await TestBlockedTokenAsync("bcdedit", "bcdedit /set testsigning on");
            await TestBlockedTokenAsync("diskpart", "diskpart /s script.txt");
            await TestBlockedTokenAsync("format", "format c: /q");
            await TestBlockedTokenAsync("del /f", "del /f /q C:\\test.txt");
            await TestBlockedTokenAsync("rd /s", "rd /s /q C:\\test");
            await TestBlockedTokenAsync("schtasks /delete", "schtasks /delete /tn test");
            await TestBlockedTokenAsync("sc delete", "sc delete testservice");
            
            // Test 3: Registry operations should be blocked
            await TestOperationBlockedAsync(
                "RegistryWrite - Outside safe namespace",
                OperationType.RegistryWrite,
                "Write to HKCU\\Software\\Test",
                new Dictionary<string, object> { ["registryPath"] = "HKCU\\Software\\Test" });
            
            await TestOperationBlockedAsync(
                "RegistryDelete - Outside safe namespace",
                OperationType.RegistryDelete,
                "Delete HKCU\\Software\\Test",
                new Dictionary<string, object> { ["registryPath"] = "HKCU\\Software\\Test" });
            
            // Test 4: Cleanup leftovers should be blocked
            await TestOperationBlockedAsync(
                "CleanupLeftovers",
                OperationType.CleanupLeftovers,
                "Clean 5 paths and 3 registry keys",
                new Dictionary<string, object> { ["pathCount"] = 5, ["registryKeyCount"] = 3 });
            
            // Test 5: Startup entry changes should be blocked
            await TestOperationBlockedAsync(
                "StartupEntryChange",
                OperationType.StartupEntryChange,
                "Modify startup entry: TestApp",
                new Dictionary<string, object> { ["entryName"] = "TestApp" });
            
            // Test 6: Service changes should be blocked
            await TestOperationBlockedAsync(
                "ServiceChange",
                OperationType.ServiceChange,
                "Stop service: TestService",
                new Dictionary<string, object> { ["serviceName"] = "TestService" });
            
            // Test 7: Scheduled task changes should be blocked
            await TestOperationBlockedAsync(
                "ScheduledTaskChange",
                OperationType.ScheduledTaskChange,
                "Delete task: TestTask",
                new Dictionary<string, object> { ["taskName"] = "TestTask" });
            
            // Test 8: System file delete should be blocked
            await TestOperationBlockedAsync(
                "SystemFileDelete",
                OperationType.SystemFileDelete,
                "Delete C:\\Windows\\test.dll",
                new Dictionary<string, object> { ["path"] = "C:\\Windows\\test.dll" });
            
            // Test 9: Critical process kill should be blocked
            await TestOperationBlockedAsync(
                "ProcessKillCritical - csrss",
                OperationType.ProcessKillCritical,
                "Kill process: csrss",
                new Dictionary<string, object> { ["processName"] = "csrss" });
            
            await TestOperationBlockedAsync(
                "ProcessKillCritical - lsass",
                OperationType.ProcessKillCritical,
                "Kill process: lsass",
                new Dictionary<string, object> { ["processName"] = "lsass" });
            
            // Test 10: File delete should be blocked
            await TestOperationBlockedAsync(
                "FileDelete",
                OperationType.FileDelete,
                "Delete file: test.txt",
                new Dictionary<string, object> { ["path"] = "test.txt" });
            
            // Test 11: Folder delete should be blocked
            await TestOperationBlockedAsync(
                "FolderDelete",
                OperationType.FolderDelete,
                "Delete folder: TestFolder",
                new Dictionary<string, object> { ["path"] = "TestFolder" });
            
            // Test 12: Uninstall should be blocked
            await TestOperationBlockedAsync(
                "Uninstall",
                OperationType.Uninstall,
                "Uninstall: TestApp",
                new Dictionary<string, object> { ["appName"] = "TestApp" });
            
            // Test 13: Missing payload tests (fail-closed validation)
            await TestMissingPayloadAsync(
                "RegistryWrite - Missing registryPath",
                OperationType.RegistryWrite,
                "Write to registry without path",
                new Dictionary<string, object> { ["valueName"] = "test" }); // Missing registryPath
            
            await TestMissingPayloadAsync(
                "FileDelete - Missing path",
                OperationType.FileDelete,
                "Delete file without path",
                new Dictionary<string, object> { ["reason"] = "test" }); // Missing path
            
            await TestMissingPayloadAsync(
                "ProcessKillCritical - Missing processName",
                OperationType.ProcessKillCritical,
                "Kill process without name",
                new Dictionary<string, object> { ["pid"] = 1234 }); // Missing processName
            
            await TestMissingPayloadAsync(
                "Uninstall - Missing appName",
                OperationType.Uninstall,
                "Uninstall without app name",
                new Dictionary<string, object> { ["reason"] = "test" }); // Missing appName
            
            // Verify logs were written
            var logsAfter = _kernel.GetRecentLogs(1000).Count;
            var newLogs = logsAfter - logsBefore;
            
            _results.Add(new SelfTestResult
            {
                TestName = "Log Verification",
                OperationType = OperationType.CommandExecution,
                Passed = newLogs >= _results.Count - 1, // -1 because this result isn't logged yet
                Message = $"Expected at least {_results.Count - 1} new log entries, found {newLogs}",
                WasBlocked = true,
                WasLogged = newLogs > 0
            });
            
            return _results;
        }
        
        private async Task TestOperationBlockedAsync(
            string testName,
            OperationType type,
            string description,
            Dictionary<string, object> payload)
        {
            var result = await _kernel.CheckAndBlockAsync(type, OperationRisk.High, description, payload);
            
            _results.Add(new SelfTestResult
            {
                TestName = testName,
                OperationType = type,
                Passed = result.Decision == SafetyDecision.Blocked,
                Message = result.Decision == SafetyDecision.Blocked 
                    ? $"✅ Correctly blocked: {result.Reason}"
                    : $"❌ FAILED: Operation was allowed when it should be blocked!",
                WasBlocked = result.Decision == SafetyDecision.Blocked,
                WasLogged = true // Assume logged if no exception
            });
        }
        
        private async Task TestBlockedTokenAsync(string tokenName, string command)
        {
            var result = await _kernel.CheckAndBlockAsync(
                OperationType.CommandExecution,
                OperationRisk.Critical,
                $"Test blocked token: {tokenName}",
                new Dictionary<string, object> { ["command"] = command });
            
            _results.Add(new SelfTestResult
            {
                TestName = $"BlockedToken - {tokenName}",
                OperationType = OperationType.CommandExecution,
                Passed = result.Decision == SafetyDecision.Blocked && result.Reason.Contains("Fail-closed"),
                Message = result.Decision == SafetyDecision.Blocked 
                    ? $"✅ Token '{tokenName}' correctly blocked"
                    : $"❌ FAILED: Token '{tokenName}' was not blocked!",
                WasBlocked = result.Decision == SafetyDecision.Blocked,
                WasLogged = true
            });
        }
        
        private async Task TestMissingPayloadAsync(
            string testName,
            OperationType type,
            string description,
            Dictionary<string, object> incompletePayload)
        {
            var result = await _kernel.CheckAndBlockAsync(type, OperationRisk.High, description, incompletePayload);
            
            _results.Add(new SelfTestResult
            {
                TestName = testName,
                OperationType = type,
                Passed = result.Decision == SafetyDecision.Blocked && result.Reason.Contains("Fail-closed"),
                Message = result.Decision == SafetyDecision.Blocked 
                    ? $"✅ Correctly blocked due to missing payload: {result.Reason}"
                    : $"❌ FAILED: Operation with missing payload was allowed!",
                WasBlocked = result.Decision == SafetyDecision.Blocked,
                WasLogged = true
            });
        }
        
        /// <summary>
        /// Get a summary of test results
        /// </summary>
        public string GetSummary()
        {
            var passed = _results.Count(r => r.Passed);
            var failed = _results.Count(r => !r.Passed);
            
            var summary = $"Safety Self-Test Results\n";
            summary += $"========================\n\n";
            summary += $"Total Tests: {_results.Count}\n";
            summary += $"Passed: {passed} ✅\n";
            summary += $"Failed: {failed} ❌\n\n";
            
            if (failed > 0)
            {
                summary += "FAILED TESTS:\n";
                foreach (var result in _results.Where(r => !r.Passed))
                {
                    summary += $"  • {result.TestName}: {result.Message}\n";
                }
                summary += "\n";
            }
            
            summary += "All Tests:\n";
            foreach (var result in _results)
            {
                var status = result.Passed ? "✅" : "❌";
                summary += $"  {status} {result.TestName}\n";
            }
            
            return summary;
        }
    }
}
