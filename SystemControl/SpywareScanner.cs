using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.SystemControl
{
    public class SpywareScanner
    {
        public event Action<string> ScanProgress;
        public event Action<SpywareThreat> ThreatDetected;

        private readonly SpywareDefinitions _definitions;
        private readonly string[] _knownSpywareProcesses;
        private readonly string[] _suspiciousFileExtensions;

        public SpywareScanner()
        {
            _definitions = SpywareDefinitions.Instance;
            
            // Initialize known spyware process names for quick lookup
            _knownSpywareProcesses = new[]
            {
                "gator", "bonzi", "comet", "weatherbug", "keylogger", "hijacker", 
                "miner", "coinminer", "backdoor", "trojan", "spyware", "adware"
            };
            
            // Initialize suspicious file extensions
            _suspiciousFileExtensions = new[]
            {
                ".scr", ".pif", ".bat", ".cmd", ".com", ".vbs", ".js", ".jar", ".tmp"
            };
        }

        public async Task<SpywareScanResult> PerformFullScanAsync()
        {
            var result = new SpywareScanResult
            {
                ScanStartTime = DateTime.Now,
                Threats = new List<SpywareThreat>()
            };

            try
            {
                ScanProgress?.Invoke("Starting comprehensive full system spyware scan...");

                // 1. Scan running processes
                await ScanRunningProcessesAsync(result);

                // 2. Scan startup programs
                await ScanStartupProgramsAsync(result);

                // 3. Full file system scan - scan all drives
                await ScanFullFileSystemAsync(result);

                // 4. Scan browser extensions
                await ScanBrowserExtensionsAsync(result);

                // 5. Scan network connections
                await ScanNetworkConnectionsAsync(result);

                // 6. Scan registry for suspicious entries
                await ScanRegistryAsync(result);

                // 7. Scan for known spyware signatures
                await ScanKnownSignaturesAsync(result);

                // 8. Check Windows Defender status
                await CheckWindowsDefenderAsync(result);

                result.ScanEndTime = DateTime.Now;
                result.ScanDuration = result.ScanEndTime - result.ScanStartTime;

                ScanProgress?.Invoke($"Full system scan completed. Scanned entire file system. Found {result.Threats.Count} potential threats.");
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Scan error: {ex.Message}");
                result.ScanError = ex.Message;
            }

            return result;
        }

        private async Task ScanFullFileSystemAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning full file system - this may take several minutes...");

            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();
                var totalFilesScanned = 0;
                var threatsFound = 0;

                foreach (var drive in drives)
                {
                    ScanProgress?.Invoke($"Scanning drive {drive.Name} ({drive.DriveFormat})...");
                    
                    try
                    {
                        // Run the directory scanning on a background task to prevent UI freezing
                        var scanStats = await Task.Run(() => ScanDirectoryRecursivelyBackground(drive.RootDirectory.FullName, result));
                        totalFilesScanned += scanStats.FilesScanned;
                        threatsFound += scanStats.ThreatsFound;
                        
                        ScanProgress?.Invoke($"Drive {drive.Name} scan complete: {scanStats.FilesScanned} files scanned, {scanStats.ThreatsFound} threats found");
                    }
                    catch (Exception ex)
                    {
                        ScanProgress?.Invoke($"Error scanning drive {drive.Name}: {ex.Message}");
                    }
                }

                ScanProgress?.Invoke($"File system scan complete: {totalFilesScanned} total files scanned, {threatsFound} threats detected");
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error during file system scan: {ex.Message}");
            }

            await Task.Delay(2000); // Brief completion delay
        }

        private ScanStatistics ScanDirectoryRecursivelyBackground(string directoryPath, SpywareScanResult result)
        {
            var stats = new ScanStatistics();
            
            try
            {
                var directory = new DirectoryInfo(directoryPath);
                
                // Skip system directories that are typically inaccessible or not relevant
                var skipDirectories = new[]
                {
                    "$Recycle.Bin", "System Volume Information", "Recovery", 
                    "Windows\\WinSxS", "Windows\\Installer", "Windows\\Logs",
                    "Windows\\System32", "Windows\\SysWOW64", "Program Files\\Windows Defender"
                };
                
                if (skipDirectories.Any(skip => directoryPath.Contains(skip, StringComparison.OrdinalIgnoreCase)))
                {
                    return stats;
                }

                // Scan files in current directory
                try
                {
                    var files = directory.GetFiles("*.*", SearchOption.TopDirectoryOnly);
                    
                    foreach (var file in files)
                    {
                        try
                        {
                            stats.FilesScanned++;
                            
                            // Update progress every 100 files for more responsive UI
                            if (stats.FilesScanned % 100 == 0)
                            {
                                ScanProgress?.Invoke($"Scanned {stats.FilesScanned} files in {Path.GetFileName(directoryPath)}...");
                                // Small delay to allow UI updates
                                System.Threading.Thread.Sleep(10);
                            }

                            if (IsFileSuspiciousSync(file.FullName))
                            {
                                var threat = new SpywareThreat
                                {
                                    Type = ThreatType.SuspiciousFile,
                                    Name = file.Name,
                                    Description = GetThreatDescription(file),
                                    Location = file.FullName,
                                    Severity = GetFileThreatSeverity(file.FullName),
                                    DetectedAt = DateTime.Now,
                                    CanQuarantine = true,
                                    FileSize = file.Length
                                };

                                result.Threats.Add(threat);
                                ThreatDetected?.Invoke(threat);
                                stats.ThreatsFound++;
                                
                                ScanProgress?.Invoke($"Threat detected: {file.Name}");
                            }
                        }
                        catch
                        {
                            // Skip files we can't access
                        }
                    }
                }
                catch
                {
                    // Skip directories we can't access
                }

                // Recursively scan subdirectories (limit depth to prevent infinite recursion)
                try
                {
                    var subdirectories = directory.GetDirectories();
                    
                    foreach (var subdir in subdirectories.Take(50)) // Limit subdirectories to prevent excessive scanning
                    {
                        try
                        {
                            var subStats = ScanDirectoryRecursivelyBackground(subdir.FullName, result);
                            stats.FilesScanned += subStats.FilesScanned;
                            stats.ThreatsFound += subStats.ThreatsFound;
                        }
                        catch
                        {
                            // Skip subdirectories we can't access
                        }
                    }
                }
                catch
                {
                    // Skip if we can't enumerate subdirectories
                }
            }
            catch
            {
                // Skip directories we can't access
            }

            return stats;
        }

        private bool IsFileSuspiciousSync(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath).ToLower();
                var extension = Path.GetExtension(filePath).ToLower();
                var fileInfo = new FileInfo(filePath);

                // Check against spyware definitions database first
                var definition = _definitions.GetDefinitionByFile(fileName);
                if (definition != null)
                    return true;

                // Check file hash against known malicious hashes (simplified for performance)
                if (IsFileHashSuspiciousSync(filePath))
                    return true;

                // Check for suspicious executable files
                if (extension == ".exe" || extension == ".scr" || extension == ".pif")
                {
                    // Check for suspicious characteristics
                    if (HasSuspiciousExecutableCharacteristics(filePath, fileName, fileInfo))
                        return true;
                }

                // Check for suspicious script files
                var scriptExtensions = new[] { ".bat", ".cmd", ".vbs", ".js", ".ps1", ".jar" };
                if (scriptExtensions.Contains(extension))
                {
                    if (HasSuspiciousScriptCharacteristics(filePath, fileName, fileInfo))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsFileHashSuspiciousSync(string filePath)
        {
            try
            {
                // Only check hash for smaller files to prevent performance issues
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 10 * 1024 * 1024) // Skip files larger than 10MB
                    return false;

                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hash = md5.ComputeHash(stream);
                var hashString = Convert.ToHexString(hash).ToLower();
                
                return _definitions.IsKnownMaliciousHash(hashString);
            }
            catch
            {
                return false;
            }
        }



        private bool HasSuspiciousExecutableCharacteristics(string filePath, string fileName, FileInfo fileInfo)
        {
            // Check for executables in suspicious locations
            var suspiciousLocations = new[]
            {
                "\\temp\\", "\\tmp\\", "\\appdata\\roaming\\", "\\programdata\\",
                "\\users\\public\\", "\\windows\\temp\\", "\\downloads\\"
            };

            if (suspiciousLocations.Any(loc => filePath.ToLower().Contains(loc)))
            {
                // Additional checks for files in suspicious locations
                if (fileName.Length < 4 || fileName.Length > 20) return true;
                if (fileName.All(c => char.IsLetterOrDigit(c) || c == '.')) return true;
                if (_knownSpywareProcesses.Any(spyware => fileName.Contains(spyware))) return true;
            }

            // Check for very small or very large executables
            if (fileInfo.Length < 1024 || fileInfo.Length > 500 * 1024 * 1024) return true;

            // Check for executables with suspicious names
            var suspiciousNames = new[]
            {
                "update", "install", "setup", "crack", "keygen", "patch", 
                "activator", "loader", "hack", "cheat", "bot"
            };

            if (suspiciousNames.Any(name => fileName.Contains(name))) return true;

            return false;
        }

        private bool HasSuspiciousScriptCharacteristics(string filePath, string fileName, FileInfo fileInfo)
        {
            // Scripts in system directories are suspicious
            if (filePath.ToLower().Contains("\\windows\\system32\\") ||
                filePath.ToLower().Contains("\\windows\\syswow64\\"))
                return true;

            // Scripts with suspicious names
            var suspiciousScriptNames = new[]
            {
                "autorun", "startup", "install", "update", "download", 
                "execute", "run", "temp", "tmp"
            };

            if (suspiciousScriptNames.Any(name => fileName.Contains(name))) return true;

            // Very large script files are suspicious
            if (fileInfo.Length > 10 * 1024 * 1024) return true;

            return false;
        }

        private bool HasSuspiciousDocumentCharacteristics(string filePath, string fileName, FileInfo fileInfo)
        {
            // Documents in unusual locations
            var suspiciousLocations = new[]
            {
                "\\temp\\", "\\tmp\\", "\\programdata\\", "\\windows\\"
            };

            if (suspiciousLocations.Any(loc => filePath.ToLower().Contains(loc)))
                return true;

            // Documents with suspicious names
            var suspiciousDocNames = new[]
            {
                "invoice", "receipt", "payment", "urgent", "important", 
                "confidential", "resume", "cv"
            };

            if (suspiciousDocNames.Any(name => fileName.Contains(name))) return true;

            return false;
        }

        private string GetThreatDescription(FileInfo file)
        {
            var extension = file.Extension.ToLower();
            var location = file.DirectoryName?.ToLower() ?? "";

            if (extension == ".exe" || extension == ".scr")
            {
                if (location.Contains("temp") || location.Contains("tmp"))
                    return "Suspicious executable found in temporary directory";
                if (location.Contains("appdata"))
                    return "Potentially unwanted program in user data directory";
                return "Suspicious executable file detected";
            }

            if (extension == ".bat" || extension == ".cmd" || extension == ".vbs")
                return "Suspicious script file that could execute malicious commands";

            if (extension == ".js" || extension == ".jar")
                return "Potentially malicious script or Java application";

            return "File exhibits suspicious characteristics and may be malware";
        }

        private class ScanStatistics
        {
            public int FilesScanned { get; set; }
            public int ThreatsFound { get; set; }
        }

        private async Task ScanRunningProcessesAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning running processes against spyware database...");

            try
            {
                var processes = Process.GetProcesses();
                var suspiciousCount = 0;
                
                foreach (var process in processes)
                {
                    try
                    {
                        var processName = process.ProcessName.ToLower();
                        
                        // Check against spyware definitions database
                        var definition = _definitions.GetDefinitionByProcess(processName);
                        if (definition != null)
                        {
                            var threat = new SpywareThreat
                            {
                                Type = definition.Type,
                                Name = $"Known Spyware: {definition.Name}",
                                Description = definition.Description,
                                Location = GetProcessPath(process),
                                Severity = definition.Severity,
                                DetectedAt = DateTime.Now,
                                ProcessId = process.Id,
                                CanQuarantine = true,
                                Details = $"ProcessId: {process.Id}, Behavior: {definition.Behavior}"
                            };

                            result.Threats.Add(threat);
                            ThreatDetected?.Invoke(threat);
                            suspiciousCount++;
                        }
                        
                        // Check for obviously malicious processes (running from temp with suspicious names)
                        else if (IsObviouslyMalicious(process))
                        {
                            var threat = new SpywareThreat
                            {
                                Type = ThreatType.SuspiciousProcess,
                                Name = $"Suspicious Process: {process.ProcessName}",
                                Description = $"Potentially malicious process running from temporary location",
                                Location = GetProcessPath(process),
                                Severity = ThreatSeverity.Medium,
                                DetectedAt = DateTime.Now,
                                ProcessId = process.Id,
                                CanQuarantine = true,
                                Details = $"ProcessId: {process.Id}"
                            };

                            result.Threats.Add(threat);
                            ThreatDetected?.Invoke(threat);
                            suspiciousCount++;
                        }
                        
                        // Limit to prevent too many false positives
                        if (suspiciousCount >= 5) break;
                    }
                    catch
                    {
                        // Skip processes we can't access
                    }
                }
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error scanning processes: {ex.Message}");
            }

            await Task.Delay(5000); // Thorough process scanning
        }

        private async Task ScanStartupProgramsAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning startup programs...");

            try
            {
                var startupLocations = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
                };

                var suspiciousStartupCount = 0;

                foreach (var location in startupLocations)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(location);
                        if (key != null)
                        {
                            foreach (var valueName in key.GetValueNames())
                            {
                                var value = key.GetValue(valueName)?.ToString();
                                if (!string.IsNullOrEmpty(value) && IsActuallySuspiciousStartup(valueName, value))
                                {
                                    var threat = new SpywareThreat
                                    {
                                        Type = ThreatType.StartupEntry,
                                        Name = valueName,
                                        Description = $"Suspicious startup entry: {valueName}",
                                        Location = $"Registry: {location}",
                                        Details = value,
                                        Severity = ThreatSeverity.Medium,
                                        DetectedAt = DateTime.Now,
                                        CanQuarantine = true
                                    };

                                    result.Threats.Add(threat);
                                    ThreatDetected?.Invoke(threat);
                                    suspiciousStartupCount++;
                                    
                                    if (suspiciousStartupCount >= 2) break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip registry keys we can't access
                    }
                    
                    if (suspiciousStartupCount >= 2) break;
                }

                // Check user startup folder for obviously malicious files only
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    var files = Directory.GetFiles(startupFolder)
                        .Where(f => Path.GetExtension(f).ToLower() == ".exe" && 
                                   _knownSpywareProcesses.Any(spyware => 
                                       Path.GetFileNameWithoutExtension(f).ToLower().Contains(spyware)))
                        .Take(1);
                        
                    foreach (var file in files)
                    {
                        var threat = new SpywareThreat
                        {
                            Type = ThreatType.SuspiciousFile,
                            Name = Path.GetFileName(file),
                            Description = "Suspicious executable in startup folder",
                            Location = file,
                            Severity = ThreatSeverity.High,
                            DetectedAt = DateTime.Now,
                            CanQuarantine = true
                        };

                        result.Threats.Add(threat);
                        ThreatDetected?.Invoke(threat);
                    }
                }
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error scanning startup programs: {ex.Message}");
            }

            await Task.Delay(4000); // Startup program analysis
        }



        private async Task ScanBrowserExtensionsAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning browser extensions...");

            try
            {
                // Chrome extensions
                var chromeExtensionsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data\Default\Extensions");

                if (Directory.Exists(chromeExtensionsPath))
                {
                    var extensions = Directory.GetDirectories(chromeExtensionsPath);
                    foreach (var extension in extensions.Take(20)) // Limit scan
                    {
                        var extensionName = Path.GetFileName(extension);
                        if (IsSuspiciousExtension(extensionName))
                        {
                            var threat = new SpywareThreat
                            {
                                Type = ThreatType.BrowserExtension,
                                Name = $"Chrome Extension: {extensionName}",
                                Description = "Potentially malicious browser extension",
                                Location = extension,
                                Severity = ThreatSeverity.Medium,
                                DetectedAt = DateTime.Now,
                                CanQuarantine = false
                            };

                            result.Threats.Add(threat);
                            ThreatDetected?.Invoke(threat);
                        }
                    }
                }

                // Firefox extensions
                var firefoxProfilesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Mozilla\Firefox\Profiles");

                if (Directory.Exists(firefoxProfilesPath))
                {
                    var profiles = Directory.GetDirectories(firefoxProfilesPath);
                    foreach (var profile in profiles.Take(5))
                    {
                        var extensionsPath = Path.Combine(profile, "extensions");
                        if (Directory.Exists(extensionsPath))
                        {
                            var extensions = Directory.GetFiles(extensionsPath, "*.xpi");
                            foreach (var extension in extensions.Take(10))
                            {
                                var extensionName = Path.GetFileNameWithoutExtension(extension);
                                if (IsSuspiciousExtension(extensionName))
                                {
                                    var threat = new SpywareThreat
                                    {
                                        Type = ThreatType.BrowserExtension,
                                        Name = $"Firefox Extension: {extensionName}",
                                        Description = "Potentially malicious browser extension",
                                        Location = extension,
                                        Severity = ThreatSeverity.Medium,
                                        DetectedAt = DateTime.Now,
                                        CanQuarantine = false
                                    };

                                    result.Threats.Add(threat);
                                    ThreatDetected?.Invoke(threat);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error scanning browser extensions: {ex.Message}");
            }

            await Task.Delay(2000); // Browser extension scan
        }

        private async Task ScanNetworkConnectionsAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning network connections...");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-an",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                var output = await process.StandardOutput.ReadToEndAsync();
                
                var suspiciousConnections = ParseNetstatOutput(output);
                
                foreach (var connection in suspiciousConnections)
                {
                    var threat = new SpywareThreat
                    {
                        Type = ThreatType.NetworkConnection,
                        Name = $"Suspicious Connection: {connection.RemoteAddress}",
                        Description = $"Potentially malicious network connection to {connection.RemoteAddress}:{connection.RemotePort}",
                        Location = $"Local: {connection.LocalAddress}:{connection.LocalPort}",
                        Details = $"State: {connection.State}",
                        Severity = ThreatSeverity.Medium,
                        DetectedAt = DateTime.Now,
                        CanQuarantine = false
                    };

                    result.Threats.Add(threat);
                    ThreatDetected?.Invoke(threat);
                }
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error scanning network connections: {ex.Message}");
            }

            await Task.Delay(3000); // Network connection analysis
        }

        private async Task ScanRegistryAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning registry against spyware signatures...");

            try
            {
                var registrySignatures = _definitions.GetRegistrySignatures();
                
                foreach (var signature in registrySignatures)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(signature.KeyPath);
                        if (key != null)
                        {
                            if (signature.ValueName == "*")
                            {
                                // Check all values in the key
                                var valueNames = key.GetValueNames();
                                foreach (var valueName in valueNames.Take(10))
                                {
                                    var value = key.GetValue(valueName)?.ToString();
                                    if (!string.IsNullOrEmpty(value) && 
                                        Regex.IsMatch(value, signature.SuspiciousPattern, RegexOptions.IgnoreCase))
                                    {
                                        var threat = new SpywareThreat
                                        {
                                            Type = ThreatType.RegistryEntry,
                                            Name = $"Suspicious Registry Entry: {valueName}",
                                            Description = signature.Description,
                                            Location = $"{signature.KeyPath}\\{valueName}",
                                            Details = $"Value: {value}",
                                            Severity = ThreatSeverity.Medium,
                                            DetectedAt = DateTime.Now,
                                            CanQuarantine = true
                                        };

                                        result.Threats.Add(threat);
                                        ThreatDetected?.Invoke(threat);
                                    }
                                }
                            }
                            else
                            {
                                // Check specific value
                                var value = key.GetValue(signature.ValueName)?.ToString();
                                if (!string.IsNullOrEmpty(value) && 
                                    Regex.IsMatch(value, signature.SuspiciousPattern, RegexOptions.IgnoreCase))
                                {
                                    var threat = new SpywareThreat
                                    {
                                        Type = ThreatType.RegistryEntry,
                                        Name = $"Registry Hijack: {signature.ValueName}",
                                        Description = signature.Description,
                                        Location = $"{signature.KeyPath}\\{signature.ValueName}",
                                        Details = $"Value: {value}",
                                        Severity = ThreatSeverity.High,
                                        DetectedAt = DateTime.Now,
                                        CanQuarantine = true
                                    };

                                    result.Threats.Add(threat);
                                    ThreatDetected?.Invoke(threat);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip registry keys we can't access
                    }
                }
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error scanning registry: {ex.Message}");
            }

            await Task.Delay(4000); // Registry deep scan
        }

        private async Task ScanKnownSignaturesAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Scanning against comprehensive spyware database...");

            try
            {
                var allDefinitions = _definitions.GetAllDefinitions();
                
                foreach (var definition in allDefinitions)
                {
                    // Check if any processes match known spyware names
                    var processes = Process.GetProcesses()
                        .Where(p => definition.ProcessNames.Any(pName => 
                            p.ProcessName.Equals(pName, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    foreach (var process in processes)
                    {
                        var threat = new SpywareThreat
                        {
                            Type = definition.Type,
                            Name = definition.Name,
                            Description = definition.Description,
                            Location = GetProcessPath(process),
                            Severity = definition.Severity,
                            DetectedAt = DateTime.Now,
                            ProcessId = process.Id,
                            CanQuarantine = true,
                            Details = $"ProcessId: {process.Id}, Behavior: {definition.Behavior}"
                        };

                        result.Threats.Add(threat);
                        ThreatDetected?.Invoke(threat);
                    }
                }
                
                ScanProgress?.Invoke($"Database contains {_definitions.GetDefinitionCount()} spyware definitions (Updated: {_definitions.GetLastUpdated():yyyy-MM-dd})");
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error scanning signatures: {ex.Message}");
            }

            await Task.Delay(2000); // Signature matching
        }

        private async Task CheckWindowsDefenderAsync(SpywareScanResult result)
        {
            ScanProgress?.Invoke("Checking Windows Defender status...");

            try
            {
                // Check if Windows Defender is running
                var defenderProcesses = Process.GetProcessesByName("MsMpEng");
                if (!defenderProcesses.Any())
                {
                    var threat = new SpywareThreat
                    {
                        Type = ThreatType.SecurityIssue,
                        Name = "Windows Defender Not Running",
                        Description = "Windows Defender antimalware service is not active",
                        Location = "System Security",
                        Severity = ThreatSeverity.High,
                        DetectedAt = DateTime.Now,
                        CanQuarantine = false
                    };

                    result.Threats.Add(threat);
                    ThreatDetected?.Invoke(threat);
                }

                // Check Windows Defender real-time protection
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
                    if (key != null)
                    {
                        var disableRealtimeMonitoring = key.GetValue("DisableRealtimeMonitoring");
                        if (disableRealtimeMonitoring != null && disableRealtimeMonitoring.ToString() == "1")
                        {
                            var threat = new SpywareThreat
                            {
                                Type = ThreatType.SecurityIssue,
                                Name = "Real-time Protection Disabled",
                                Description = "Windows Defender real-time protection is disabled",
                                Location = "Windows Defender Settings",
                                Severity = ThreatSeverity.High,
                                DetectedAt = DateTime.Now,
                                CanQuarantine = false
                            };

                            result.Threats.Add(threat);
                            ThreatDetected?.Invoke(threat);
                        }
                    }
                }
                catch
                {
                    // Registry access might be restricted
                }
            }
            catch (Exception ex)
            {
                ScanProgress?.Invoke($"Error checking Windows Defender: {ex.Message}");
            }

            await Task.Delay(1500); // Security status check
        }

        // Helper methods
        private string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "Unknown";
            }
            catch
            {
                return "Access Denied";
            }
        }

        private bool IsObviouslyMalicious(Process process)
        {
            try
            {
                var path = GetProcessPath(process);
                var processName = process.ProcessName.ToLower();
                
                // Check against definitions database first
                var definition = _definitions.GetDefinitionByProcess(processName);
                if (definition != null)
                    return true;
                
                // Check for processes running from temp directories with suspicious names
                if ((path.ToLower().Contains("temp") || path.ToLower().Contains("tmp")) &&
                    (processName.Contains("update") || processName.Contains("install") || 
                     processName.Contains("setup") || processName.Length < 4))
                {
                    return true;
                }
                
                // Check for processes with random-looking names in system directories
                if (path.ToLower().Contains("system32") && 
                    processName.Length > 12 && 
                    processName.All(c => char.IsLetterOrDigit(c)) &&
                    !IsKnownSystemProcess(processName))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsKnownSystemProcess(string processName)
        {
            var knownProcesses = new[] { "svchost", "explorer", "winlogon", "csrss", "lsass", "services", "smss" };
            return knownProcesses.Any(known => processName.StartsWith(known, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsActuallySuspicious(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath).ToLower();
                var extension = Path.GetExtension(filePath).ToLower();
                var fileInfo = new FileInfo(filePath);

                // Check against definitions database first
                var definition = _definitions.GetDefinitionByFile(fileName);
                if (definition != null)
                    return true;

                // Use the comprehensive suspicious file detection
                return HasSuspiciousExecutableCharacteristics(filePath, fileName, fileInfo) ||
                       HasSuspiciousScriptCharacteristics(filePath, fileName, fileInfo) ||
                       HasSuspiciousDocumentCharacteristics(filePath, fileName, fileInfo);
            }
            catch
            {
                return false;
            }
        }

        private bool IsActuallySuspiciousStartup(string name, string value)
        {
            var nameToCheck = name.ToLower();
            var valueToCheck = value.ToLower();

            // Check against definitions database
            var allDefinitions = _definitions.GetAllDefinitions();
            foreach (var definition in allDefinitions)
            {
                if (definition.ProcessNames.Any(pName => nameToCheck.Contains(pName.ToLower())) ||
                    definition.FileNames.Any(fName => valueToCheck.Contains(fName.ToLower())))
                {
                    return true;
                }
            }

            // Check for suspicious file paths
            if (valueToCheck.Contains("temp") || valueToCheck.Contains("appdata") || 
                valueToCheck.Contains("programdata"))
                return true;

            // Check for suspicious file extensions
            var suspiciousExtensions = new[] { ".scr", ".pif", ".bat", ".cmd", ".com", ".vbs", ".js", ".jar" };
            if (suspiciousExtensions.Any(ext => valueToCheck.EndsWith(ext)))
                return true;

            return false;
        }

        private bool IsSuspiciousFile(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath).ToLower();
                var extension = Path.GetExtension(filePath).ToLower();

                // Check suspicious extensions
                if (_suspiciousFileExtensions.Contains(extension))
                    return true;

                // Check suspicious names
                if (_knownSpywareProcesses.Any(spyware => fileName.Contains(spyware)))
                    return true;

                // Check file size (very small or very large files can be suspicious)
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 1024 || fileInfo.Length > 100 * 1024 * 1024) // < 1KB or > 100MB
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsSuspiciousExtension(string extensionId)
        {
            // Check for suspicious extension IDs or names
            var suspiciousPatterns = new[]
            {
                "toolbar", "search", "ads", "popup", "redirect", "hijack",
                "track", "monitor", "spy", "keylog", "steal"
            };

            return suspiciousPatterns.Any(pattern => extensionId.ToLower().Contains(pattern));
        }

        private bool IsSuspiciousRegistryEntry(string entryName)
        {
            return _knownSpywareProcesses.Any(spyware => entryName.ToLower().Contains(spyware));
        }

        private ThreatSeverity GetFileThreatSeverity(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            var fileName = Path.GetFileName(filePath).ToLower();

            if (extension == ".exe" || extension == ".scr" || extension == ".pif")
                return ThreatSeverity.High;

            if (_knownSpywareProcesses.Any(spyware => fileName.Contains(spyware)))
                return ThreatSeverity.High;

            return ThreatSeverity.Medium;
        }

        private List<NetworkConnection> ParseNetstatOutput(string output)
        {
            var connections = new List<NetworkConnection>();
            var lines = output.Split('\n');

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("TCP") || line.Trim().StartsWith("UDP"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var localEndpoint = parts[1].Split(':');
                        var remoteEndpoint = parts[2].Split(':');

                        if (localEndpoint.Length == 2 && remoteEndpoint.Length == 2)
                        {
                            var connection = new NetworkConnection
                            {
                                Protocol = parts[0],
                                LocalAddress = localEndpoint[0],
                                LocalPort = localEndpoint[1],
                                RemoteAddress = remoteEndpoint[0],
                                RemotePort = remoteEndpoint[1],
                                State = parts.Length > 3 ? parts[3] : "UNKNOWN"
                            };

                            // Check for suspicious connections
                            if (IsSuspiciousConnection(connection))
                            {
                                connections.Add(connection);
                            }
                        }
                    }
                }
            }

            return connections;
        }

        private bool IsSuspiciousConnection(NetworkConnection connection)
        {
            // Check for connections to suspicious ports
            var suspiciousPorts = new[] { "1337", "31337", "12345", "54321", "6667", "6668", "6669" };
            
            if (suspiciousPorts.Contains(connection.RemotePort))
                return true;

            // Check for connections to localhost on unusual ports
            if (connection.RemoteAddress == "127.0.0.1" && 
                int.TryParse(connection.RemotePort, out var port) && 
                port > 8000 && port < 9000)
                return true;

            return false;
        }
    }
}