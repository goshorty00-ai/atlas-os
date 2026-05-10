using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.SystemControl
{
    public class InstalledApp
    {
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Version { get; set; } = "";
        public string InstallDate { get; set; } = "";
        public string InstallLocation { get; set; } = "";
        public string UninstallString { get; set; } = "";
        public string QuietUninstallString { get; set; } = "";
        public long EstimatedSize { get; set; }
        public string SizeDisplay => EstimatedSize > 0 ? $"{EstimatedSize / 1024.0:F1} MB" : "Unknown";
        public bool IsSystemComponent { get; set; }
        public string RegistryKey { get; set; } = "";
        
        // Properties for app launching
        public string ExecutablePath { get; set; } = "";
        public string LaunchArgs { get; set; } = "";  // Arguments needed to launch (e.g., Discord's --processStart)
        public string Source { get; set; } = "";
        public bool IsUWP { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class UninstallResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> LeftoverPaths { get; set; } = new();
        public List<string> LeftoverRegistry { get; set; } = new();
    }

    public static class AppUninstaller
    {
        public static event Action<string>? ProgressChanged;
        public static event Action<int>? ProgressPercentChanged;

        public static async Task<List<InstalledApp>> GetInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var apps = new List<InstalledApp>();
                var registryPaths = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var basePath in registryPaths)
                {
                    foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                    {
                        try
                        {
                            using var key = hive.OpenSubKey(basePath);
                            if (key == null) continue;

                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using var subKey = key.OpenSubKey(subKeyName);
                                    if (subKey == null) continue;

                                    var displayName = subKey.GetValue("DisplayName")?.ToString();
                                    if (string.IsNullOrEmpty(displayName)) continue;

                                    var uninstallString = subKey.GetValue("UninstallString")?.ToString() ?? "";
                                    if (string.IsNullOrEmpty(uninstallString)) continue;

                                    var app = new InstalledApp
                                    {
                                        Name = displayName,
                                        Publisher = subKey.GetValue("Publisher")?.ToString() ?? "",
                                        Version = subKey.GetValue("DisplayVersion")?.ToString() ?? "",
                                        InstallDate = subKey.GetValue("InstallDate")?.ToString() ?? "",
                                        InstallLocation = subKey.GetValue("InstallLocation")?.ToString() ?? "",
                                        UninstallString = uninstallString,
                                        QuietUninstallString = subKey.GetValue("QuietUninstallString")?.ToString() ?? "",
                                        EstimatedSize = Convert.ToInt64(subKey.GetValue("EstimatedSize") ?? 0),
                                        IsSystemComponent = (subKey.GetValue("SystemComponent")?.ToString() ?? "0") == "1",
                                        RegistryKey = $"{(hive == Registry.LocalMachine ? "HKLM" : "HKCU")}\\{basePath}\\{subKeyName}"
                                    };

                                    // Skip duplicates
                                    if (!apps.Any(a => a.Name == app.Name && a.Version == app.Version))
                                        apps.Add(app);
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                return apps.OrderBy(a => a.Name).ToList();
            });
        }

        public static async Task<UninstallResult> UninstallAppAsync(InstalledApp app, bool cleanLeftovers = true)
        {
            var result = new UninstallResult();

            try
            {
                // SAFETY GATE: Check before uninstall
                var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                    AtlasAI.Core.OperationType.Uninstall,
                    AtlasAI.Core.OperationRisk.High,
                    $"Uninstall application: {app.Name}",
                    new Dictionary<string, object>
                    {
                        ["appName"] = app.Name,
                        ["publisher"] = app.Publisher,
                        ["uninstallString"] = app.UninstallString
                    });
                
                if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
                {
                    result.Success = false;
                    result.Message = safetyCheck.Message;
                    ProgressChanged?.Invoke("🛡️ Uninstall blocked by Safety Mode");
                    return result;
                }
                
                ProgressChanged?.Invoke($"Uninstalling {app.Name}...");
                ProgressPercentChanged?.Invoke(10);

                // Try quiet uninstall first
                var uninstallCmd = !string.IsNullOrEmpty(app.QuietUninstallString) 
                    ? app.QuietUninstallString 
                    : app.UninstallString;

                // Parse and execute uninstall command
                var success = await ExecuteUninstallAsync(uninstallCmd, app.Name);
                
                ProgressPercentChanged?.Invoke(50);

                if (cleanLeftovers)
                {
                    ProgressChanged?.Invoke("Scanning for leftover files...");
                    result.LeftoverPaths = await FindLeftoverFilesAsync(app);
                    ProgressPercentChanged?.Invoke(70);

                    ProgressChanged?.Invoke("Scanning for leftover registry entries...");
                    result.LeftoverRegistry = await FindLeftoverRegistryAsync(app);
                    ProgressPercentChanged?.Invoke(90);
                }

                result.Success = success;
                result.Message = success 
                    ? $"Successfully uninstalled {app.Name}" 
                    : $"Uninstall may have failed for {app.Name}";

                ProgressPercentChanged?.Invoke(100);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
            }

            return result;
        }

        private static async Task<bool> ExecuteUninstallAsync(string uninstallString, string appName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string fileName, arguments;

                    ProgressChanged?.Invoke($"Preparing to uninstall {appName}...");
                    Debug.WriteLine($"Original uninstall string: {uninstallString}");

                    // Handle MsiExec - DON'T use silent mode, let user see the uninstaller
                    if (uninstallString.Contains("MsiExec", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = "msiexec.exe";
                        var productCode = ExtractMsiProductCode(uninstallString);
                        if (string.IsNullOrEmpty(productCode))
                        {
                            // Use the original arguments but remove /I and add /X for uninstall
                            arguments = uninstallString.Replace("MsiExec.exe", "", StringComparison.OrdinalIgnoreCase)
                                                       .Replace("msiexec.exe", "", StringComparison.OrdinalIgnoreCase)
                                                       .Replace("/I", "/X", StringComparison.OrdinalIgnoreCase)
                                                       .Trim();
                        }
                        else
                        {
                            // Interactive uninstall - user can see and confirm
                            arguments = $"/x {productCode}";
                        }
                    }
                    // Handle quoted paths
                    else if (uninstallString.StartsWith("\""))
                    {
                        var endQuote = uninstallString.IndexOf('"', 1);
                        if (endQuote > 1)
                        {
                            fileName = uninstallString.Substring(1, endQuote - 1);
                            arguments = uninstallString.Length > endQuote + 1 
                                ? uninstallString.Substring(endQuote + 1).Trim() 
                                : "";
                        }
                        else
                        {
                            fileName = uninstallString.Trim('"');
                            arguments = "";
                        }
                    }
                    else
                    {
                        // Check if it's a path with spaces but no quotes
                        if (File.Exists(uninstallString))
                        {
                            fileName = uninstallString;
                            arguments = "";
                        }
                        else
                        {
                            // Try to find the executable
                            var parts = uninstallString.Split(new[] { ' ' }, 2);
                            fileName = parts[0];
                            arguments = parts.Length > 1 ? parts[1] : "";
                        }
                    }

                    ProgressChanged?.Invoke($"Launching: {Path.GetFileName(fileName)}");
                    Debug.WriteLine($"Uninstall command: {fileName} {arguments}");

                    // Check if file exists (skip for msiexec)
                    if (!fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) && 
                        !File.Exists(fileName))
                    {
                        ProgressChanged?.Invoke($"Uninstaller not found: {fileName}");
                        Debug.WriteLine($"Uninstaller file not found: {fileName}");
                        return false;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true,
                        Verb = "runas" // Request admin elevation
                    };

                    ProgressChanged?.Invoke($"Starting uninstaller (admin required)...");
                    ProgressPercentChanged?.Invoke(30);

                    Process? process = null;
                    try
                    {
                        process = Process.Start(psi);
                    }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                    {
                        ProgressChanged?.Invoke("Cancelled - admin permission denied");
                        return false;
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        ProgressChanged?.Invoke($"Failed: {ex.Message}");
                        Debug.WriteLine($"Win32 error: {ex.NativeErrorCode} - {ex.Message}");
                        return false;
                    }

                    if (process == null)
                    {
                        ProgressChanged?.Invoke("Failed to start uninstaller");
                        return false;
                    }

                    ProgressChanged?.Invoke($"Uninstaller running - follow prompts in the window...");
                    ProgressPercentChanged?.Invoke(50);

                    // Wait for process (10 minutes max for interactive uninstallers)
                    bool exited = process.WaitForExit(600000);
                    
                    if (!exited)
                    {
                        ProgressChanged?.Invoke("Uninstaller still running...");
                        return true; // Assume it's working
                    }

                    var exitCode = process.ExitCode;
                    Debug.WriteLine($"Uninstall exit code: {exitCode}");

                    // Success codes
                    if (exitCode == 0 || exitCode == 3010 || exitCode == 1641 || exitCode == 1605)
                    {
                        ProgressChanged?.Invoke($"Uninstall completed!");
                        return true;
                    }
                    
                    // Error codes
                    var errorMsg = exitCode switch
                    {
                        1602 => "User cancelled",
                        1603 => "Fatal error",
                        1618 => "Another install in progress",
                        1619 => "Package not found",
                        _ => $"Exit code: {exitCode}"
                    };
                    
                    ProgressChanged?.Invoke($"Result: {errorMsg}");
                    
                    // If exit code is non-zero but not a known error, it might still have worked
                    // Check if the app is still installed
                    return exitCode == 0;
                }
                catch (Exception ex)
                {
                    ProgressChanged?.Invoke($"Error: {ex.Message}");
                    Debug.WriteLine($"Uninstall exception: {ex}");
                    return false;
                }
            });
        }

        private static string ExtractMsiProductCode(string uninstallString)
        {
            var match = System.Text.RegularExpressions.Regex.Match(uninstallString, @"\{[A-F0-9-]+\}", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : "";
        }

        private static async Task<List<string>> FindLeftoverFilesAsync(InstalledApp app)
        {
            return await Task.Run(() =>
            {
                var leftovers = new List<string>();
                var searchPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
                };

                var appNameParts = app.Name.Split(' ').Where(p => p.Length > 3).Take(2).ToArray();
                var publisherParts = app.Publisher.Split(' ').Where(p => p.Length > 3).Take(1).ToArray();

                foreach (var basePath in searchPaths)
                {
                    if (!Directory.Exists(basePath)) continue;

                    try
                    {
                        foreach (var dir in Directory.GetDirectories(basePath))
                        {
                            var dirName = Path.GetFileName(dir).ToLower();
                            
                            if (appNameParts.Any(p => dirName.Contains(p.ToLower())) ||
                                publisherParts.Any(p => dirName.Contains(p.ToLower())))
                            {
                                leftovers.Add(dir);
                            }
                        }
                    }
                    catch { }
                }

                // Check install location
                if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
                    leftovers.Add(app.InstallLocation);

                return leftovers.Distinct().ToList();
            });
        }

        private static async Task<List<string>> FindLeftoverRegistryAsync(InstalledApp app)
        {
            return await Task.Run(() =>
            {
                var leftovers = new List<string>();
                var searchPaths = new[]
                {
                    @"SOFTWARE",
                    @"SOFTWARE\WOW6432Node"
                };

                var appNameParts = app.Name.Split(' ').Where(p => p.Length > 3).Take(2).ToArray();

                foreach (var basePath in searchPaths)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(basePath);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            if (appNameParts.Any(p => subKeyName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                            {
                                leftovers.Add($"HKLM\\{basePath}\\{subKeyName}");
                            }
                        }
                    }
                    catch { }
                }

                return leftovers;
            });
        }

        public static async Task<int> CleanLeftoversAsync(List<string> paths, List<string> registryKeys)
        {
            // SAFETY GATE: Check with SafetyKernel before proceeding
            var safetyCheck = await AtlasAI.Core.SafetyKernel.Instance.CheckAndBlockAsync(
                AtlasAI.Core.OperationType.CleanupLeftovers,
                AtlasAI.Core.OperationRisk.Critical,
                $"Clean {paths.Count} paths and {registryKeys.Count} registry keys",
                new Dictionary<string, object>
                {
                    ["pathCount"] = paths.Count,
                    ["registryKeyCount"] = registryKeys.Count,
                    ["paths"] = string.Join("; ", paths.Take(5)),
                    ["registryKeys"] = string.Join("; ", registryKeys.Take(5))
                });

            if (safetyCheck.Decision == AtlasAI.Core.SafetyDecision.Blocked)
            {
                System.Diagnostics.Debug.WriteLine($"[AppUninstaller] CleanLeftovers blocked: {safetyCheck.Reason}");
                ProgressChanged?.Invoke("🛡️ Cleanup blocked by Safety Mode");
                return 0;
            }

            return await Task.Run(() =>
            {
                int cleaned = 0;

                // Delete folders
                foreach (var path in paths)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                            cleaned++;
                        }
                        else if (File.Exists(path))
                        {
                            File.Delete(path);
                            cleaned++;
                        }
                    }
                    catch { }
                }

                // Delete registry keys
                foreach (var regKey in registryKeys)
                {
                    try
                    {
                        var parts = regKey.Split('\\', 2);
                        var hive = parts[0] == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                        hive.DeleteSubKeyTree(parts[1], false);
                        cleaned++;
                    }
                    catch { }
                }

                return cleaned;
            });
        }
    }
}
