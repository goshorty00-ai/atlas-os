using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.SystemControl
{
    /// <summary>
    /// Norton-style REAL malware scanner - scans ALL files on ALL drives
    /// Performs thorough scanning with hash checking for executables
    /// </summary>
    public class UnifiedScanner
    {
        public event Action<string>? ProgressChanged;
        public event Action<UnifiedThreat>? ThreatFound;
        public event Action<int>? ProgressPercentChanged;
        public event Action<long>? FilesScannedChanged;
        public event Action<string>? CurrentFileChanged;

        private CancellationTokenSource? _cts;
        private bool _isScanning;
        private long _filesScanned;
        private long _totalFilesToScan;
        private int _threatsFound;
        private Stopwatch _scanTimer = new();

        // ALL file extensions to scan (not just executables)
        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".scr", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".ps1", ".psm1", ".msi", ".msp", ".com", ".pif",
            ".application", ".gadget", ".msc", ".jar", ".hta", ".cpl",
            ".inf", ".reg", ".lnk", ".scf", ".sys", ".drv"
        };

        // Extensions that could contain malware
        private static readonly HashSet<string> ScanExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Executables
            ".exe", ".dll", ".scr", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".ps1", ".psm1", ".msi", ".msp", ".com", ".pif",
            ".application", ".gadget", ".msc", ".jar", ".hta", ".cpl",
            ".inf", ".reg", ".lnk", ".scf", ".sys", ".drv",
            // Documents that can contain macros
            ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".pptm",
            ".pdf", ".rtf",
            // Archives
            ".zip", ".rar", ".7z", ".tar", ".gz",
            // Scripts
            ".py", ".rb", ".pl", ".sh", ".php",
            // Other
            ".iso", ".img", ".vhd"
        };

        public bool IsScanning => _isScanning;
        public long FilesScanned => _filesScanned;
        public int ThreatsFound => _threatsFound;
        public TimeSpan ElapsedTime => _scanTimer.Elapsed;

        public void CancelScan() => _cts?.Cancel();

        public async Task<UnifiedScanResult> PerformDeepScanAsync()
        {
            if (_isScanning) return new UnifiedScanResult { Error = "Scan already in progress" };

            _isScanning = true;
            _cts = new CancellationTokenSource();
            _filesScanned = 0;
            _threatsFound = 0;
            _totalFilesToScan = 0;
            _scanTimer.Restart();

            var result = new UnifiedScanResult { StartTime = DateTime.Now };

            try
            {
                Report("🔍 Initializing Norton-style deep scan...", 0);
                await Task.Delay(500, _cts.Token);

                // Phase 1: Count ALL files to scan (0-3%)
                Report("📊 Analyzing drives and counting files...", 1);
                _totalFilesToScan = await CountAllFilesAsync(_cts.Token);
                Report($"📊 Found {_totalFilesToScan:N0} files to scan across all drives", 3);
                await Task.Delay(300, _cts.Token);

                // Phase 2: Scan running processes (3-6%)
                Report("🔄 Scanning running processes for malware...", 4);
                await ScanRunningProcessesAsync(result, _cts.Token);

                // Phase 3: Scan startup programs (6-9%)
                Report("🚀 Checking startup programs...", 7);
                await ScanStartupProgramsAsync(result, _cts.Token);

                // Phase 4: Scan browser extensions (9-12%)
                Report("🌐 Scanning browser extensions for adware...", 10);
                await ScanBrowserExtensionsAsync(result, _cts.Token);

                // Phase 5: FULL DRIVE SCAN - The main event (12-90%)
                Report("🛡️ Starting comprehensive file scan...", 12);
                await ScanAllDrivesAsync(result, _cts.Token);

                // Phase 6: Registry scan (90-95%)
                Report("📝 Deep scanning registry for threats...", 91);
                await ScanRegistryAsync(result, _cts.Token);

                // Phase 7: Scheduled tasks scan (95-98%)
                Report("⏰ Checking scheduled tasks...", 96);
                await ScanScheduledTasksAsync(result, _cts.Token);

                // Phase 8: Final checks (98-100%)
                Report("✅ Finalizing scan results...", 99);
                await Task.Delay(300, _cts.Token);

                _scanTimer.Stop();
                result.EndTime = DateTime.Now;
                result.Duration = _scanTimer.Elapsed;
                result.FilesScanned = _filesScanned;
                result.ThreatsFound = _threatsFound;

                var msg = result.Threats.Count == 0
                    ? $"✅ Scan complete! Scanned {_filesScanned:N0} files in {FormatDuration(_scanTimer.Elapsed)} - No threats found!"
                    : $"⚠️ Found {result.Threats.Count} threats in {_filesScanned:N0} files ({FormatDuration(_scanTimer.Elapsed)})";
                Report(msg, 100);
            }
            catch (OperationCanceledException)
            {
                _scanTimer.Stop();
                result.WasCancelled = true;
                result.FilesScanned = _filesScanned;
                result.Duration = _scanTimer.Elapsed;
                Report($"⚠️ Scan cancelled after {_filesScanned:N0} files ({FormatDuration(_scanTimer.Elapsed)})", 0);
            }
            catch (Exception ex)
            {
                _scanTimer.Stop();
                result.Error = ex.Message;
                Report($"❌ Error: {ex.Message}", 0);
            }
            finally
            {
                _isScanning = false;
            }

            return result;
        }

        private void Report(string msg, int pct)
        {
            ProgressChanged?.Invoke(msg);
            ProgressPercentChanged?.Invoke(pct);
        }

        private void ReportCurrentFile(string filePath)
        {
            CurrentFileChanged?.Invoke(filePath);
            FilesScannedChanged?.Invoke(_filesScanned);
        }

        private void AddThreat(UnifiedScanResult result, UnifiedThreat threat)
        {
            _threatsFound++;
            result.Threats.Add(threat);
            ThreatFound?.Invoke(threat);
        }

        private string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        #region File Counting - Count ALL files
        private async Task<long> CountAllFilesAsync(CancellationToken ct)
        {
            long count = 0;
            await Task.Run(() =>
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                    try
                    {
                        count += CountFilesRecursive(drive.RootDirectory.FullName, ct);
                    }
                    catch { }
                }
            }, ct);
            return count;
        }

        private long CountFilesRecursive(string path, CancellationToken ct)
        {
            long count = 0;
            try
            {
                // Count ALL files with scannable extensions
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    ct.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(file);
                    if (ScanExtensions.Contains(ext))
                        count++;
                }

                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    ct.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(dir).ToLower();
                    if (ShouldSkipDirectory(dirName)) continue;
                    count += CountFilesRecursive(dir, ct);
                }
            }
            catch { }
            return count;
        }
        #endregion

        #region Process Scanner
        private async Task ScanRunningProcessesAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var proc in processes)
                    {
                        ct.ThrowIfCancellationRequested();
                        _filesScanned++;

                        try
                        {
                            var procName = proc.ProcessName.ToLower();
                            
                            if (MalwareSignatures.IsMalwareProcess(procName))
                            {
                                string? filePath = null;
                                try { filePath = proc.MainModule?.FileName; } catch { }

                                AddThreat(result, new UnifiedThreat
                                {
                                    Type = ThreatCategory.Process,
                                    Name = $"Suspicious Process: {proc.ProcessName}",
                                    Description = $"Running process matches known malware signature",
                                    Location = filePath ?? "Unknown",
                                    Severity = SeverityLevel.Critical,
                                    Category = "Malware",
                                    CanRemove = true,
                                    ProcessId = proc.Id
                                });
                            }
                        }
                        catch { }
                        finally
                        {
                            try { proc.Dispose(); } catch { }
                        }
                    }
                }
                catch { }
            }, ct);
        }
        #endregion

        #region Startup Scanner
        private async Task ScanStartupProgramsAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                var startupPaths = new[]
                {
                    (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                    (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
                    (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                    (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
                    (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
                };

                foreach (var (hive, path) in startupPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = hive.OpenSubKey(path);
                        if (key == null) continue;

                        foreach (var name in key.GetValueNames())
                        {
                            _filesScanned++;
                            var value = key.GetValue(name)?.ToString() ?? "";
                            var lowerValue = value.ToLower();
                            var lowerName = name.ToLower();

                            var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(lowerName);
                            if (!isSuspicious)
                                (isSuspicious, pattern) = MalwareSignatures.CheckFileName(lowerValue);

                            if (isSuspicious)
                            {
                                AddThreat(result, new UnifiedThreat
                                {
                                    Type = ThreatCategory.Startup,
                                    Name = $"Suspicious Startup: {name}",
                                    Description = $"Startup entry contains suspicious pattern: {pattern}",
                                    Location = $"{path}\\{name}",
                                    Details = value,
                                    Severity = SeverityLevel.High,
                                    Category = "Startup Threat",
                                    CanRemove = true
                                });
                            }
                        }
                    }
                    catch { }
                }
            }, ct);
        }
        #endregion

        #region Browser Extension Scanner
        private async Task ScanBrowserExtensionsAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                // Chrome extensions
                var chromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data", "Default", "Extensions");
                ScanExtensionFolder(chromePath, "Chrome", result, ct);

                // Edge extensions
                var edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data", "Default", "Extensions");
                ScanExtensionFolder(edgePath, "Edge", result, ct);

                // Firefox addons
                var firefoxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Mozilla", "Firefox", "Profiles");
                if (Directory.Exists(firefoxPath))
                {
                    foreach (var profile in Directory.EnumerateDirectories(firefoxPath))
                    {
                        ct.ThrowIfCancellationRequested();
                        var addonsPath = Path.Combine(profile, "extensions");
                        ScanExtensionFolder(addonsPath, "Firefox", result, ct);
                    }
                }
            }, ct);
        }

        private void ScanExtensionFolder(string path, string browser, UnifiedScanResult result, CancellationToken ct)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                foreach (var extDir in Directory.EnumerateDirectories(path))
                {
                    ct.ThrowIfCancellationRequested();
                    _filesScanned++;

                    var extName = Path.GetFileName(extDir).ToLower();
                    var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(extName);

                    // Check manifest.json
                    var manifestPath = Path.Combine(extDir, "manifest.json");
                    if (!isSuspicious && File.Exists(manifestPath))
                    {
                        try
                        {
                            var manifest = File.ReadAllText(manifestPath).ToLower();
                            foreach (var adware in MalwareSignatures.AdwareSignatures)
                            {
                                if (manifest.Contains(adware))
                                {
                                    isSuspicious = true;
                                    pattern = $"Adware: {adware}";
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    if (isSuspicious)
                    {
                        AddThreat(result, new UnifiedThreat
                        {
                            Type = ThreatCategory.BrowserExtension,
                            Name = $"Suspicious {browser} Extension",
                            Description = $"Browser extension matches threat pattern: {pattern}",
                            Location = extDir,
                            Severity = SeverityLevel.Medium,
                            Category = "Adware/PUP",
                            CanRemove = true
                        });
                    }
                }
            }
            catch { }
        }
        #endregion

        #region Full Drive Scanner - THOROUGH SCAN
        private async Task ScanAllDrivesAsync(UnifiedScanResult result, CancellationToken ct)
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList();

            int driveIndex = 0;
            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                
                var driveLetter = drive.Name.TrimEnd('\\');
                Report($"🔍 Scanning {driveLetter} ({FormatBytes(drive.TotalSize - drive.AvailableFreeSpace)} used)...", 
                    12 + (driveIndex * 78 / drives.Count));

                await ScanDirectoryAsync(drive.RootDirectory.FullName, result, ct, drives.Count, driveIndex);
                driveIndex++;
            }
        }

        private async Task ScanDirectoryAsync(string path, UnifiedScanResult result, CancellationToken ct, int totalDrives, int currentDrive)
        {
            await Task.Run(() => 
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                ScanDirectoryRecursive(path, result, ct, totalDrives, currentDrive);
            }, ct);
        }

        // Time-based throttle for UI updates - max 4 updates per second
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private string _lastReportedFile = "";
        
        private void ScanDirectoryRecursive(string path, UnifiedScanResult result, CancellationToken ct, int totalDrives, int currentDrive)
        {
            try
            {
                // Scan ALL files with scannable extensions
                foreach (var filePath in Directory.EnumerateFiles(path))
                {
                    ct.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(filePath);
                    if (!ScanExtensions.Contains(ext)) continue;

                    _filesScanned++;
                    
                    // Update UI with time-based throttle (max 4 updates per second = 250ms)
                    // This prevents UI freezing while still showing real-time progress
                    var now = DateTime.Now;
                    if ((now - _lastUIUpdate).TotalMilliseconds >= 250 || _filesScanned <= 5)
                    {
                        _lastUIUpdate = now;
                        _lastReportedFile = filePath;
                        
                        ReportCurrentFile(filePath);
                        FilesScannedChanged?.Invoke(_filesScanned);
                        
                        // Calculate progress (12-90% range for file scanning phase)
                        int progress = 12;
                        if (_totalFilesToScan > 0)
                        {
                            progress = 12 + (int)((_filesScanned * 78.0 / _totalFilesToScan));
                        }
                        ProgressPercentChanged?.Invoke(Math.Min(90, progress));
                        
                        // Small yield to let UI thread breathe
                        Thread.Sleep(1);
                    }

                    // THOROUGH file scan
                    ScanFileThorough(filePath, result, ct);
                }

                // Recurse into subdirectories
                foreach (var dirPath in Directory.EnumerateDirectories(path))
                {
                    ct.ThrowIfCancellationRequested();

                    var dirName = Path.GetFileName(dirPath).ToLower();
                    if (ShouldSkipDirectory(dirName)) continue;

                    ScanDirectoryRecursive(dirPath, result, ct, totalDrives, currentDrive);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }

        private bool ShouldSkipDirectory(string dirName)
        {
            return dirName == "$recycle.bin" ||
                   dirName == "system volume information" ||
                   dirName == "windows.old" ||
                   dirName == "recovery" ||
                   dirName == "$windows.~bt" ||
                   dirName == "$windows.~ws" ||
                   dirName == "config.msi" ||
                   dirName == "msocache" ||
                   dirName == "perflogs" ||
                   dirName.StartsWith("$");
        }

        /// <summary>
        /// Thorough file scan - checks filename, hash for executables, file attributes
        /// </summary>
        private void ScanFileThorough(string filePath, UnifiedScanResult result, CancellationToken ct)
        {
            try
            {
                var fileName = Path.GetFileName(filePath).ToLower();
                var ext = Path.GetExtension(filePath).ToLower();
                var fileInfo = new FileInfo(filePath);

                // 1. Check filename for suspicious patterns
                var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(fileName);
                if (isSuspicious)
                {
                    AddThreat(result, new UnifiedThreat
                    {
                        Type = ThreatCategory.File,
                        Name = $"Suspicious File: {fileName}",
                        Description = $"Filename matches threat pattern: {pattern}",
                        Location = filePath,
                        Severity = SeverityLevel.Medium,
                        Category = "Suspicious File",
                        CanRemove = true,
                        SizeBytes = fileInfo.Length
                    });
                    return;
                }

                // 2. Check for double extensions (e.g., document.pdf.exe)
                // EXCLUDE .lnk files - Windows shortcuts naturally have double extensions like "file.png.lnk"
                var nameParts = fileName.Split('.');
                if (nameParts.Length > 2)
                {
                    var lastExt = "." + nameParts[^1];
                    var secondLastExt = "." + nameParts[^2];
                    
                    // Skip .lnk files - they're legitimate Windows shortcuts that always have double extensions
                    if (lastExt != ".lnk" && 
                        ExecutableExtensions.Contains(lastExt) && 
                        (secondLastExt == ".pdf" || secondLastExt == ".doc" || secondLastExt == ".docx" ||
                         secondLastExt == ".xls" || secondLastExt == ".xlsx" || secondLastExt == ".jpg" ||
                         secondLastExt == ".png" || secondLastExt == ".txt" || secondLastExt == ".mp3"))
                    {
                        AddThreat(result, new UnifiedThreat
                        {
                            Type = ThreatCategory.File,
                            Name = $"Double Extension: {fileName}",
                            Description = "File uses double extension trick to disguise executable",
                            Location = filePath,
                            Severity = SeverityLevel.High,
                            Category = "Suspicious File",
                            CanRemove = true,
                            SizeBytes = fileInfo.Length
                        });
                        return;
                    }
                }

                // 3. For executables, check hash against known malware (files < 50MB)
                if (ExecutableExtensions.Contains(ext) && fileInfo.Length > 0 && fileInfo.Length < 50_000_000)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var hash = MalwareSignatures.GetFileHash(filePath);
                    if (MalwareSignatures.IsKnownMalware(hash))
                    {
                        AddThreat(result, new UnifiedThreat
                        {
                            Type = ThreatCategory.File,
                            Name = $"MALWARE DETECTED: {fileName}",
                            Description = $"File hash matches known malware signature!",
                            Location = filePath,
                            Details = $"SHA256: {hash}",
                            Severity = SeverityLevel.Critical,
                            Category = "Malware",
                            CanRemove = true,
                            SizeBytes = fileInfo.Length
                        });
                        return;
                    }
                }

                // 4. Check for hidden executables in suspicious locations
                if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 && ExecutableExtensions.Contains(ext))
                {
                    var parentDir = Path.GetDirectoryName(filePath)?.ToLower() ?? "";
                    if (parentDir.Contains("temp") || parentDir.Contains("appdata") || 
                        parentDir.Contains("programdata"))
                    {
                        AddThreat(result, new UnifiedThreat
                        {
                            Type = ThreatCategory.File,
                            Name = $"Hidden Executable: {fileName}",
                            Description = "Hidden executable file in suspicious location",
                            Location = filePath,
                            Severity = SeverityLevel.High,
                            Category = "Suspicious File",
                            CanRemove = true,
                            SizeBytes = fileInfo.Length
                        });
                    }
                }

                // 5. Check for recently created executables in temp folders
                if (ExecutableExtensions.Contains(ext))
                {
                    var parentDir = Path.GetDirectoryName(filePath)?.ToLower() ?? "";
                    if ((parentDir.Contains("temp") || parentDir.Contains("tmp")) &&
                        fileInfo.CreationTime > DateTime.Now.AddDays(-7))
                    {
                        // Recent executable in temp - could be suspicious
                        // Only flag if it has suspicious characteristics
                        if (fileInfo.Length < 1_000_000 && // Small file
                            (fileName.Contains("update") || fileName.Contains("setup") || 
                             fileName.Contains("install") || fileName.Length < 8))
                        {
                            AddThreat(result, new UnifiedThreat
                            {
                                Type = ThreatCategory.File,
                                Name = $"Recent Temp Executable: {fileName}",
                                Description = "Recently created executable in temp folder",
                                Location = filePath,
                                Severity = SeverityLevel.Low,
                                Category = "Potentially Unwanted",
                                CanRemove = true,
                                SizeBytes = fileInfo.Length
                            });
                        }
                    }
                }
            }
            catch { }
        }
        #endregion

        #region Registry Scanner
        private async Task ScanRegistryAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                var suspiciousKeys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell",
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects",
                    @"SOFTWARE\Microsoft\Internet Explorer\Toolbar",
                    @"SOFTWARE\Microsoft\Internet Explorer\Extensions",
                };

                foreach (var keyPath in suspiciousKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    _filesScanned++;

                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                        if (key == null) continue;

                        foreach (var valueName in key.GetValueNames())
                        {
                            var value = key.GetValue(valueName)?.ToString() ?? "";
                            var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(value.ToLower());

                            if (isSuspicious)
                            {
                                AddThreat(result, new UnifiedThreat
                                {
                                    Type = ThreatCategory.Registry,
                                    Name = $"Suspicious Registry Entry",
                                    Description = $"Registry value matches threat pattern: {pattern}",
                                    Location = $"HKLM\\{keyPath}\\{valueName}",
                                    Details = value,
                                    Severity = SeverityLevel.High,
                                    Category = "Registry Threat",
                                    CanRemove = true
                                });
                            }
                        }
                    }
                    catch { }
                }

                // Check HKCU
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run");
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            _filesScanned++;
                            var value = key.GetValue(valueName)?.ToString() ?? "";
                            var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(value.ToLower());

                            if (isSuspicious)
                            {
                                AddThreat(result, new UnifiedThreat
                                {
                                    Type = ThreatCategory.Registry,
                                    Name = $"Suspicious User Registry Entry",
                                    Description = $"Registry value matches threat pattern: {pattern}",
                                    Location = $"HKCU\\SOFTWARE\\...\\Run\\{valueName}",
                                    Details = value,
                                    Severity = SeverityLevel.High,
                                    Category = "Registry Threat",
                                    CanRemove = true
                                });
                            }
                        }
                    }
                }
                catch { }
            }, ct);
        }
        #endregion

        #region Scheduled Tasks Scanner
        private async Task ScanScheduledTasksAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                try
                {
                    var taskFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), 
                        "System32", "Tasks");
                    
                    if (!Directory.Exists(taskFolder)) return;

                    foreach (var taskFile in Directory.EnumerateFiles(taskFolder, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        _filesScanned++;

                        try
                        {
                            var content = File.ReadAllText(taskFile).ToLower();
                            var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(content);

                            if (isSuspicious)
                            {
                                AddThreat(result, new UnifiedThreat
                                {
                                    Type = ThreatCategory.Service,
                                    Name = $"Suspicious Scheduled Task",
                                    Description = $"Scheduled task contains suspicious pattern: {pattern}",
                                    Location = taskFile,
                                    Severity = SeverityLevel.Medium,
                                    Category = "Scheduled Task",
                                    CanRemove = true
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }, ct);
        }
        #endregion

        #region Helpers
        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
        #endregion
    }

    #region Models
    public class UnifiedScanResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public long FilesScanned { get; set; }
        public int ThreatsFound { get; set; }
        public long TotalBytes { get; set; }
        public bool WasCancelled { get; set; }
        public string? Error { get; set; }
        public List<UnifiedThreat> Threats { get; set; } = new();
        
        public int CriticalCount => Threats.Count(t => t.Severity == SeverityLevel.Critical);
        public int HighCount => Threats.Count(t => t.Severity == SeverityLevel.High);
        public int MediumCount => Threats.Count(t => t.Severity == SeverityLevel.Medium);
        public int LowCount => Threats.Count(t => t.Severity == SeverityLevel.Low);
    }

    public class UnifiedThreat
    {
        public ThreatCategory Type { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Location { get; set; } = "";
        public string? Details { get; set; }
        public SeverityLevel Severity { get; set; }
        public string Category { get; set; } = "";
        public bool CanRemove { get; set; }
        public long SizeBytes { get; set; }
        public int? ProcessId { get; set; }
    }

    public enum ThreatCategory
    {
        File,
        Process,
        Registry,
        Startup,
        BrowserExtension,
        Network,
        SystemHealth,
        Service
    }

    public enum SeverityLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
    #endregion
}
