using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using AtlasAI.Core;

namespace AtlasAI.SystemControl
{
    public class ThreatRemover
    {
        // Protected Windows paths that require TrustedInstaller (beyond admin)
        private static readonly string[] ProtectedPaths = new[]
        {
            @"C:\Windows\System32\Tasks",
            @"C:\Windows\SysWOW64\Tasks",
            @"C:\Program Files\WindowsApps",
            @"C:\Windows\WinSxS",
            @"C:\Windows\servicing",
            @"C:\Windows\assembly",
            @"C:\$Recycle.Bin",
            @"C:\System Volume Information"
        };

        public static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool IsProtectedSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var normalizedPath = path.Replace("/", "\\").ToLowerInvariant();
            
            foreach (var protectedPath in ProtectedPaths)
            {
                if (normalizedPath.StartsWith(protectedPath.ToLowerInvariant()))
                    return true;
            }
            
            // Also check for Windows Store apps
            if (normalizedPath.Contains("windowsapps") || normalizedPath.Contains("systemapps"))
                return true;
                
            return false;
        }

        public async Task<RemovalResult> RemoveThreatAsync(UnifiedThreat threat)
        {
            try
            {
                Debug.WriteLine($"[ThreatRemover] Removing: {threat.Type} - {threat.Name} at {threat.Location}");
                
                // Check if this is a protected system path
                if (IsProtectedSystemPath(threat.Location))
                {
                    return new RemovalResult 
                    { 
                        Success = false, 
                        Message = "Protected Windows component - requires TrustedInstaller (safe to ignore)",
                        IsProtectedSystem = true
                    };
                }
                
                var result = threat.Type switch
                {
                    ThreatCategory.Process => await KillProcessAsync(threat),
                    ThreatCategory.File => await DeleteFileAsync(threat),
                    ThreatCategory.Startup => await RemoveStartupEntryAsync(threat),
                    ThreatCategory.Registry => await RemoveRegistryEntryAsync(threat),
                    ThreatCategory.Service => await DisableServiceAsync(threat),
                    _ => new RemovalResult { Success = false, Message = $"Removal not supported for type: {threat.Type}" }
                };
                
                Debug.WriteLine($"[ThreatRemover] Result: {result.Success} - {result.Message}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThreatRemover] Exception: {ex.Message}");
                return new RemovalResult { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        private async Task<RemovalResult> KillProcessAsync(UnifiedThreat threat)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var processName = threat.Name.Replace(".exe", "");
                    
                    // SAFETY GATE: Check before process kill
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.ProcessKillCritical,
                        OperationRisk.High,
                        $"Kill threat process: {threat.Name}",
                        new Dictionary<string, object>
                        {
                            ["processName"] = processName,
                            ["pid"] = threat.ProcessId ?? 0,
                            ["reason"] = "threat removal"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        return new RemovalResult { Success = false, Message = safetyCheck.Message };
                    }
                    
                    if (threat.ProcessId.HasValue)
                    {
                        var proc = Process.GetProcessById(threat.ProcessId.Value);
                        proc.Kill();
                        proc.WaitForExit(5000);
                        return new RemovalResult { Success = true, Message = $"Process {threat.Name} (PID {threat.ProcessId}) terminated" };
                    }
                    
                    // Try by name
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length == 0)
                        return new RemovalResult { Success = true, Message = "Process already terminated" };
                    
                    foreach (var p in processes)
                    {
                        try { p.Kill(); p.WaitForExit(3000); } catch { }
                    }
                    return new RemovalResult { Success = true, Message = $"Terminated {processes.Length} process(es)" };
                }
                catch (Exception ex)
                {
                    return new RemovalResult { Success = false, Message = $"Failed to kill process: {ex.Message}" };
                }
            });
        }

        private async Task<RemovalResult> DeleteFileAsync(UnifiedThreat threat)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var path = threat.Location;
                    if (!File.Exists(path))
                        return new RemovalResult { Success = true, Message = "File already removed" };
                    
                    // Check if protected path
                    if (IsProtectedSystemPath(path))
                    {
                        return new RemovalResult 
                        { 
                            Success = false, 
                            Message = "Protected Windows file - requires TrustedInstaller",
                            IsProtectedSystem = true
                        };
                    }
                    
                    // SAFETY GATE: Check before file delete
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.FileDelete,
                        OperationRisk.High,
                        $"Delete threat file: {Path.GetFileName(path)}",
                        new Dictionary<string, object>
                        {
                            ["path"] = path,
                            ["reason"] = "threat removal"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        return new RemovalResult { Success = false, Message = safetyCheck.Message };
                    }
                    
                    // Try to delete normally first
                    try
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        return new RemovalResult { Success = true, Message = $"Deleted: {Path.GetFileName(path)}" };
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Try using takeown and icacls for stubborn files
                        return await TryForceDeleteAsync(path);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Check if it's a system-protected file
                    if (IsProtectedSystemPath(threat.Location))
                        return new RemovalResult { Success = false, Message = "Protected Windows file - safe to ignore", IsProtectedSystem = true };
                    return new RemovalResult { Success = false, Message = "Access denied - file is in use or protected" };
                }
                catch (Exception ex)
                {
                    return new RemovalResult { Success = false, Message = $"Failed to delete: {ex.Message}" };
                }
            });
        }
        
        private async Task<RemovalResult> TryForceDeleteAsync(string path)
        {
            // SAFETY GATE: Force delete requires command execution
            var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                OperationType.SystemFileDelete,
                OperationRisk.Critical,
                $"Force delete file: {Path.GetFileName(path)}",
                new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["method"] = "takeown+icacls"
                });
            
            if (safetyCheck.Decision == SafetyDecision.Blocked)
            {
                return new RemovalResult { Success = false, Message = safetyCheck.Message };
            }
            
            try
            {
                // Try taking ownership first
                var takeownPsi = new ProcessStartInfo
                {
                    FileName = "takeown",
                    Arguments = $"/F \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (var proc = Process.Start(takeownPsi))
                {
                    proc?.WaitForExit(5000);
                }
                
                // Grant full control
                var icaclsPsi = new ProcessStartInfo
                {
                    FileName = "icacls",
                    Arguments = $"\"{path}\" /grant administrators:F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (var proc = Process.Start(icaclsPsi))
                {
                    proc?.WaitForExit(5000);
                }
                
                // Now try to delete
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                
                return new RemovalResult { Success = true, Message = $"Force deleted: {Path.GetFileName(path)}" };
            }
            catch
            {
                return new RemovalResult 
                { 
                    Success = false, 
                    Message = "File protected by Windows - may require manual removal or is a system component"
                };
            }
        }

        private async Task<RemovalResult> RemoveStartupEntryAsync(UnifiedThreat threat)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    // Parse location like "SOFTWARE\Microsoft\Windows\CurrentVersion\Run\EntryName"
                    var location = threat.Location;
                    var parts = location.Split('\\');
                    if (parts.Length < 2)
                        return new RemovalResult { Success = false, Message = "Invalid registry path" };
                    
                    var valueName = parts[^1];
                    var keyPath = string.Join('\\', parts[..^1]);
                    
                    // SAFETY GATE: Check before startup entry removal
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.StartupEntryChange,
                        OperationRisk.High,
                        $"Remove startup entry: {valueName}",
                        new Dictionary<string, object>
                        {
                            ["entryName"] = valueName,
                            ["registryPath"] = keyPath,
                            ["action"] = "remove"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        return new RemovalResult { Success = false, Message = safetyCheck.Message };
                    }
                    
                    // Try HKCU first, then HKLM
                    bool removed = false;
                    
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
                        if (key?.GetValue(valueName) != null)
                        {
                            key.DeleteValue(valueName);
                            removed = true;
                        }
                    }
                    catch { }
                    
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                        if (key?.GetValue(valueName) != null)
                        {
                            key.DeleteValue(valueName);
                            removed = true;
                        }
                    }
                    catch { }
                    
                    return removed 
                        ? new RemovalResult { Success = true, Message = $"Removed startup entry: {valueName}" }
                        : new RemovalResult { Success = false, Message = "Could not remove startup entry" };
                }
                catch (Exception ex)
                {
                    return new RemovalResult { Success = false, Message = $"Failed: {ex.Message}" };
                }
            });
        }

        private async Task<RemovalResult> RemoveRegistryEntryAsync(UnifiedThreat threat)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var location = threat.Location;
                    
                    // Parse "HKEY_LOCAL_MACHINE\path\valuename" or similar
                    RegistryKey? hive = null;
                    string keyPath;
                    
                    if (location.StartsWith("HKEY_LOCAL_MACHINE\\"))
                    {
                        hive = Registry.LocalMachine;
                        keyPath = location.Substring("HKEY_LOCAL_MACHINE\\".Length);
                    }
                    else if (location.StartsWith("HKEY_CURRENT_USER\\"))
                    {
                        hive = Registry.CurrentUser;
                        keyPath = location.Substring("HKEY_CURRENT_USER\\".Length);
                    }
                    else
                    {
                        // Try both
                        keyPath = location;
                    }
                    
                    var parts = keyPath.Split('\\');
                    var valueName = parts[^1];
                    var subKeyPath = string.Join('\\', parts[..^1]);
                    
                    // SAFETY GATE: Check before registry delete
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.RegistryDelete,
                        OperationRisk.High,
                        $"Remove registry entry: {valueName}",
                        new Dictionary<string, object>
                        {
                            ["registryPath"] = location,
                            ["valueName"] = valueName,
                            ["reason"] = "threat removal"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        return new RemovalResult { Success = false, Message = safetyCheck.Message };
                    }
                    
                    if (hive != null)
                    {
                        using var key = hive.OpenSubKey(subKeyPath, true);
                        if (key?.GetValue(valueName) != null)
                        {
                            key.DeleteValue(valueName);
                            return new RemovalResult { Success = true, Message = $"Removed registry value: {valueName}" };
                        }
                    }
                    else
                    {
                        // Try both hives
                        foreach (var h in new[] { Registry.LocalMachine, Registry.CurrentUser })
                        {
                            try
                            {
                                using var key = h.OpenSubKey(subKeyPath, true);
                                if (key?.GetValue(valueName) != null)
                                {
                                    key.DeleteValue(valueName);
                                    return new RemovalResult { Success = true, Message = $"Removed registry value: {valueName}" };
                                }
                            }
                            catch { }
                        }
                    }
                    
                    return new RemovalResult { Success = false, Message = "Registry entry not found or access denied" };
                }
                catch (Exception ex)
                {
                    return new RemovalResult { Success = false, Message = $"Failed: {ex.Message}" };
                }
            });
        }

        private async Task<RemovalResult> DisableServiceAsync(UnifiedThreat threat)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    // Extract service name from location
                    var serviceName = Path.GetFileNameWithoutExtension(threat.Location);
                    
                    // SAFETY GATE: Check before service change
                    var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                        OperationType.ServiceChange,
                        OperationRisk.High,
                        $"Disable service: {serviceName}",
                        new Dictionary<string, object>
                        {
                            ["serviceName"] = serviceName,
                            ["action"] = "disable"
                        });
                    
                    if (safetyCheck.Decision == SafetyDecision.Blocked)
                    {
                        return new RemovalResult { Success = false, Message = safetyCheck.Message };
                    }
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"config \"{serviceName}\" start= disabled",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(10000);
                    
                    // Also try to stop it
                    psi.Arguments = $"stop \"{serviceName}\"";
                    using var proc2 = Process.Start(psi);
                    proc2?.WaitForExit(10000);
                    
                    return new RemovalResult { Success = true, Message = $"Service {serviceName} disabled" };
                }
                catch (Exception ex)
                {
                    return new RemovalResult { Success = false, Message = $"Failed: {ex.Message}" };
                }
            });
        }
    }

    public class RemovalResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool IsProtectedSystem { get; set; } = false;
    }
}
