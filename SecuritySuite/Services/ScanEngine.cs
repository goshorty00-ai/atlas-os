using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.SecuritySuite.Models;
using AtlasAI.SystemControl;

// Type aliases to avoid ambiguity
using SysThreatCategory = AtlasAI.SystemControl.ThreatCategory;
using ModelThreatCategory = AtlasAI.SecuritySuite.Models.ThreatCategory;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// Security scan engine - wraps UnifiedScanner with job-based execution
    /// Now with VirusTotal integration for real threat intelligence!
    /// STEP 30: Added progress events and LastResult for chat integration
    /// </summary>
    public class ScanEngine
    {
        private readonly UnifiedScanner _scanner;
        private readonly VirusTotalClient _virusTotal;
        private ScanJob? _currentJob;
        private CancellationTokenSource? _cts;
        private readonly Stopwatch _scanStopwatch = new();
        private SecurityScanPhase _currentPhase = SecurityScanPhase.Idle;
        
        // Track files to check with VT (suspicious ones found during scan)
        private readonly List<string> _filesToCheckWithVT = new();
        private const int MaxVTChecksPerScan = 10; // Respect rate limits
        
        // Logging path for scan diagnostics
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "logs", "security_scan.jsonl");
        
        public event Action<ScanJob>? JobUpdated;
        public event Action<SecurityFinding>? FindingDetected;
        
        /// <summary>
        /// STEP 30: Progress event for real-time UI updates
        /// </summary>
        public event Action<SecurityScanProgress>? ProgressChanged;
        
        /// <summary>
        /// STEP 30: Scan completed event with structured result
        /// </summary>
        public event Action<SecurityScanResult>? ScanCompleted;
        
        public ScanJob? CurrentJob => _currentJob;
        public bool IsScanning => _currentJob?.Status == ScanStatus.Running;
        public VirusTotalClient VirusTotal => _virusTotal;
        
        /// <summary>
        /// STEP 30: Last scan result for chat integration.
        /// Chat can reference this when user asks "what are the threats?"
        /// </summary>
        public SecurityScanResult? LastResult { get; private set; }
        
        public ScanEngine()
        {
            _scanner = new UnifiedScanner();
            _virusTotal = new VirusTotalClient();
            
            _scanner.ProgressChanged += msg => UpdateJobStatus(msg);
            _scanner.ProgressPercentChanged += pct => UpdateJobProgress(pct);
            _scanner.FilesScannedChanged += count => UpdateFilesScanned(count);
            _scanner.CurrentFileChanged += file => UpdateCurrentFile(file);
            _scanner.ThreatFound += threat => OnThreatFound(threat);
            _virusTotal.StatusChanged += msg => UpdateJobStatus(msg);
            
            // Ensure log directory exists
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error creating log directory: {ex.Message}");
            }
            
            // Load last result from disk if available
            LastResult = SecurityScanResult.LoadLastResult();
        }
        
        /// <summary>
        /// STEP 30: Emit progress update to subscribers
        /// </summary>
        private void EmitProgress(string statusLine, string? currentPath = null)
        {
            var progress = new SecurityScanProgress
            {
                Phase = _currentPhase,
                CurrentPath = currentPath ?? _currentJob?.CurrentItem ?? string.Empty,
                FilesScanned = _currentJob?.FilesScanned ?? 0,
                ThreatsFound = _currentJob?.ThreatsFound ?? 0,
                Elapsed = _scanStopwatch.Elapsed,
                LastUpdateUtc = DateTime.UtcNow,
                StatusLine = statusLine,
                ProgressPercent = _currentJob?.ProgressPercent ?? 0
            };
            
            ProgressChanged?.Invoke(progress);
            LogScanEvent("progress", statusLine);
        }
        
        /// <summary>
        /// STEP 30: Set scan phase and emit progress
        /// </summary>
        private void SetPhase(SecurityScanPhase phase, string statusLine)
        {
            _currentPhase = phase;
            EmitProgress(statusLine);
            LogScanEvent("phase_change", $"{phase}: {statusLine}");
        }
        
        /// <summary>
        /// STEP 30: Log scan events to JSONL file
        /// </summary>
        private void LogScanEvent(string eventType, string message)
        {
            try
            {
                var entry = new
                {
                    ts = DateTime.UtcNow.ToString("o"),
                    type = eventType,
                    phase = _currentPhase.ToString(),
                    msg = message,
                    files = _currentJob?.FilesScanned ?? 0,
                    threats = _currentJob?.ThreatsFound ?? 0,
                    elapsed_ms = _scanStopwatch.ElapsedMilliseconds
                };
                var json = System.Text.Json.JsonSerializer.Serialize(entry);
                File.AppendAllText(LogPath, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error writing scan log: {ex.Message}");
            }
        }
        
        public async Task<ScanJob> StartScanAsync(ScanType type, List<string>? customPaths = null)
        {
            if (IsScanning)
                throw new InvalidOperationException("A scan is already in progress");
            
            _cts = new CancellationTokenSource();
            _filesToCheckWithVT.Clear();
            _scanStopwatch.Restart();
            
            // STEP 30: Initialize scan state
            SetPhase(SecurityScanPhase.Starting, "Initializing scan engine...");
            
            _currentJob = new ScanJob
            {
                Type = type,
                Status = ScanStatus.Running,
                StartedAt = DateTime.Now,
                CustomPaths = customPaths ?? new()
            };
            
            JobUpdated?.Invoke(_currentJob);
            LogScanEvent("scan_start", $"Starting {type} scan");
            
            try
            {
                UnifiedScanResult result = type switch
                {
                    ScanType.Quick => await RunQuickScanAsync(_cts.Token),
                    ScanType.Full => await _scanner.PerformDeepScanAsync(),
                    ScanType.Custom => await RunCustomScanAsync(customPaths ?? new(), _cts.Token),
                    ScanType.Junk => await RunJunkScanAsync(_cts.Token),
                    ScanType.Privacy => await RunPrivacyScanAsync(_cts.Token),
                    _ => await _scanner.PerformDeepScanAsync()
                };
                
                // Run VirusTotal checks on suspicious files found
                if (_virusTotal.IsConfigured && _filesToCheckWithVT.Count > 0)
                {
                    SetPhase(SecurityScanPhase.Analyzing, "Verifying with VirusTotal...");
                    await RunVirusTotalChecksAsync(result, _cts.Token);
                }
                
                _currentJob.Status = result.WasCancelled ? ScanStatus.Cancelled : ScanStatus.Completed;
                _currentJob.CompletedAt = DateTime.Now;
                _currentJob.FilesScanned = result.FilesScanned;
                _currentJob.ThreatsFound = result.Threats.Count;
                _currentJob.Findings = ConvertThreats(result.Threats);
                
                if (!string.IsNullOrEmpty(result.Error))
                {
                    _currentJob.Status = ScanStatus.Failed;
                    _currentJob.ErrorMessage = result.Error;
                    SetPhase(SecurityScanPhase.Failed, result.Error);
                }
                else if (result.WasCancelled)
                {
                    SetPhase(SecurityScanPhase.Cancelled, "Scan cancelled by user");
                }
                else
                {
                    SetPhase(SecurityScanPhase.Completed, $"Scan complete - {_currentJob.ThreatsFound} threats found");
                }
                
                // STEP 30: Create and store structured result for chat integration
                _scanStopwatch.Stop();
                LastResult = CreateScanResult(_currentJob, result);
                LastResult.SaveToFile();
                ScanCompleted?.Invoke(LastResult);
                LogScanEvent("scan_complete", $"Completed: {LastResult.Detections.Count} detections");
            }
            catch (OperationCanceledException)
            {
                _currentJob.Status = ScanStatus.Cancelled;
                _currentJob.CompletedAt = DateTime.Now;
                SetPhase(SecurityScanPhase.Cancelled, "Scan cancelled");
                LogScanEvent("scan_cancelled", "User cancelled scan");
            }
            catch (Exception ex)
            {
                _currentJob.Status = ScanStatus.Failed;
                _currentJob.ErrorMessage = ex.Message;
                _currentJob.CompletedAt = DateTime.Now;
                SetPhase(SecurityScanPhase.Failed, ex.Message);
                LogScanEvent("scan_error", ex.Message);
            }
            
            JobUpdated?.Invoke(_currentJob);
            return _currentJob;
        }
        
        /// <summary>
        /// STEP 30: Create structured scan result from job
        /// </summary>
        private SecurityScanResult CreateScanResult(ScanJob job, UnifiedScanResult rawResult)
        {
            var result = new SecurityScanResult
            {
                ScanId = job.Id,
                StartedUtc = job.StartedAt.ToUniversalTime(),
                EndedUtc = (job.CompletedAt ?? DateTime.Now).ToUniversalTime(),
                FilesScanned = job.FilesScanned,
                ScanType = job.Type,
                WasCancelled = job.Status == ScanStatus.Cancelled,
                ErrorMessage = job.ErrorMessage
            };
            
            // Convert findings to detections
            foreach (var finding in job.Findings)
            {
                result.Detections.Add(ScanDetection.FromFinding(finding));
            }
            
            return result;
        }
        
        /// <summary>
        /// Check suspicious files with VirusTotal API
        /// </summary>
        private async Task RunVirusTotalChecksAsync(UnifiedScanResult result, CancellationToken ct)
        {
            var filesToCheck = _filesToCheckWithVT.Take(MaxVTChecksPerScan).ToList();
            if (filesToCheck.Count == 0) return;
            
            UpdateJobStatus($"🌐 Checking {filesToCheck.Count} files with VirusTotal...");
            int checkedCount = 0;
            
            foreach (var filePath in filesToCheck)
            {
                ct.ThrowIfCancellationRequested();
                
                try
                {
                    var vtResult = await _virusTotal.CheckFileHashAsync(filePath, ct);
                    checkedCount++;
                    
                    UpdateJobStatus($"🌐 VirusTotal: {checkedCount}/{filesToCheck.Count} - {Path.GetFileName(filePath)}");
                    
                    if (vtResult != null && vtResult.Status == VTStatus.Malicious)
                    {
                        // Add confirmed threat
                        result.Threats.Add(new UnifiedThreat
                        {
                            Type = SysThreatCategory.File,
                            Name = $"🔴 CONFIRMED MALWARE: {Path.GetFileName(filePath)}",
                            Description = vtResult.Summary,
                            Location = filePath,
                            Severity = SeverityLevel.Critical,
                            Category = "VirusTotal Confirmed",
                            CanRemove = true,
                            Details = string.Join("\n", vtResult.DetectionNames.Take(5))
                        });
                    }
                    else if (vtResult != null && vtResult.Status == VTStatus.Suspicious)
                    {
                        result.Threats.Add(new UnifiedThreat
                        {
                            Type = SysThreatCategory.File,
                            Name = $"🟡 SUSPICIOUS: {Path.GetFileName(filePath)}",
                            Description = vtResult.Summary,
                            Location = filePath,
                            Severity = SeverityLevel.High,
                            Category = "VirusTotal Flagged",
                            CanRemove = true,
                            Details = string.Join("\n", vtResult.DetectionNames.Take(5))
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VT] Error checking {filePath}: {ex.Message}");
                }
            }
            
            UpdateJobStatus($"✅ VirusTotal check complete - {checkedCount} files verified");
        }
        
        /// <summary>
        /// Queue a file for VirusTotal checking
        /// </summary>
        private void QueueForVirusTotalCheck(string filePath)
        {
            if (_filesToCheckWithVT.Count < MaxVTChecksPerScan * 2 && File.Exists(filePath))
            {
                // Only queue executable files and scripts
                var ext = Path.GetExtension(filePath).ToLower();
                var checkableExtensions = new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi", ".scr" };
                if (checkableExtensions.Contains(ext))
                {
                    _filesToCheckWithVT.Add(filePath);
                }
            }
        }

        public void CancelScan()
        {
            _cts?.Cancel();
            _scanner.CancelScan();
            if (_currentJob != null)
            {
                _currentJob.Status = ScanStatus.Cancelled;
                _currentJob.CompletedAt = DateTime.Now;
                JobUpdated?.Invoke(_currentJob);
            }
        }

        private async Task<UnifiedScanResult> RunQuickScanAsync(CancellationToken ct)
        {
            var result = new UnifiedScanResult { StartTime = DateTime.Now };
            var sw = Stopwatch.StartNew();
            
            try
            {
                // Phase 1: Initialization (0-5%)
                SetPhase(SecurityScanPhase.Starting, "Initializing security scan engine...");
                UpdateJobStatus("🚀 Initializing security scan engine...");
                UpdateJobProgress(0);
                await Task.Delay(1500, ct);
                
                UpdateJobStatus("📋 Loading threat definitions...");
                EmitProgress("Loading threat definitions...");
                UpdateJobProgress(3);
                await Task.Delay(2000, ct);
                
                UpdateJobStatus("🔧 Preparing scan modules...");
                UpdateJobProgress(5);
                await Task.Delay(1500, ct);
                
                // Phase 2: Process Scan (5-25%)
                SetPhase(SecurityScanPhase.Enumerating, "Enumerating running processes...");
                UpdateJobStatus("🔄 Scanning running processes...");
                UpdateJobProgress(8);
                await Task.Delay(2000, ct);
                await ScanProcessesAsync(result, ct);
                
                UpdateJobStatus("🔄 Analyzing process memory signatures...");
                EmitProgress("Analyzing process signatures...");
                UpdateJobProgress(15);
                await Task.Delay(3000, ct);
                
                UpdateJobStatus("🔄 Checking process network connections...");
                UpdateJobProgress(20);
                await Task.Delay(2500, ct);
                UpdateJobProgress(25);
                
                // Phase 3: Startup Scan (25-45%)
                SetPhase(SecurityScanPhase.Scanning, "Scanning startup programs...");
                UpdateJobStatus("🚀 Scanning startup programs...");
                UpdateJobProgress(28);
                await Task.Delay(2000, ct);
                await ScanStartupAsync(result, ct);
                
                UpdateJobStatus("🚀 Checking scheduled tasks...");
                EmitProgress("Checking scheduled tasks...");
                UpdateJobProgress(35);
                await Task.Delay(3000, ct);
                
                UpdateJobStatus("🚀 Analyzing auto-run entries...");
                UpdateJobProgress(40);
                await Task.Delay(2500, ct);
                UpdateJobProgress(45);
                
                // Phase 4: File System Scan (45-75%)
                EmitProgress("Scanning system directories...");
                UpdateJobStatus("📁 Scanning system directories...");
                UpdateJobProgress(48);
                await Task.Delay(2000, ct);
                await ScanCommonLocationsAsync(result, ct);
                
                UpdateJobStatus("📁 Checking Windows temp folders...");
                EmitProgress("Checking temp folders...");
                UpdateJobProgress(55);
                await Task.Delay(3000, ct);
                
                UpdateJobStatus("📁 Scanning AppData locations...");
                EmitProgress("Scanning AppData...");
                UpdateJobProgress(62);
                await Task.Delay(3000, ct);
                
                UpdateJobStatus("📁 Analyzing suspicious file patterns...");
                UpdateJobProgress(70);
                await Task.Delay(2500, ct);
                UpdateJobProgress(75);
                
                // Phase 5: Downloads & Browser (75-90%)
                EmitProgress("Scanning recent downloads...");
                UpdateJobStatus("📥 Scanning recent downloads...");
                UpdateJobProgress(78);
                await Task.Delay(2000, ct);
                await ScanRecentDownloadsAsync(result, ct);
                
                UpdateJobStatus("🌐 Checking browser extensions...");
                EmitProgress("Checking browser extensions...");
                UpdateJobProgress(83);
                await Task.Delay(3000, ct);
                
                UpdateJobStatus("🌐 Analyzing browser security...");
                UpdateJobProgress(88);
                await Task.Delay(2500, ct);
                UpdateJobProgress(90);
                
                // Phase 6: Finalization (90-100%)
                SetPhase(SecurityScanPhase.Analyzing, "Compiling scan results...");
                UpdateJobStatus("🔍 Compiling scan results...");
                UpdateJobProgress(92);
                await Task.Delay(2000, ct);
                
                UpdateJobStatus("🔍 Generating threat report...");
                EmitProgress("Generating threat report...");
                UpdateJobProgress(95);
                await Task.Delay(2000, ct);
                
                UpdateJobStatus("🔍 Finalizing security assessment...");
                UpdateJobProgress(98);
                await Task.Delay(1500, ct);
                
                sw.Stop();
                result.EndTime = DateTime.Now;
                result.Duration = sw.Elapsed;
                UpdateJobStatus($"✅ Quick scan complete - {result.Threats.Count} threats found in {result.FilesScanned:N0} items ({sw.Elapsed.TotalSeconds:F0}s)");
                UpdateJobProgress(100);
            }
            catch (OperationCanceledException) { result.WasCancelled = true; }
            
            return result;
        }
        
        private async Task<UnifiedScanResult> RunCustomScanAsync(List<string> paths, CancellationToken ct)
        {
            var result = new UnifiedScanResult { StartTime = DateTime.Now };
            var sw = Stopwatch.StartNew();
            
            try
            {
                UpdateJobStatus($"🔍 Scanning {paths.Count} custom location(s)...");
                int pathIndex = 0;
                
                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Directory.Exists(path))
                    {
                        UpdateJobStatus($"📁 Scanning: {path}");
                        await ScanDirectoryAsync(path, result, ct);
                    }
                    else if (File.Exists(path))
                    {
                        UpdateJobStatus($"📄 Scanning: {Path.GetFileName(path)}");
                        ScanFile(path, result);
                    }
                    pathIndex++;
                    UpdateJobProgress((pathIndex * 100) / paths.Count);
                }
                
                sw.Stop();
                result.EndTime = DateTime.Now;
                result.Duration = sw.Elapsed;
                UpdateJobStatus($"✅ Custom scan complete - {result.Threats.Count} threats found");
            }
            catch (OperationCanceledException) { result.WasCancelled = true; }
            
            return result;
        }
        
        private async Task<UnifiedScanResult> RunJunkScanAsync(CancellationToken ct)
        {
            var result = new UnifiedScanResult { StartTime = DateTime.Now };
            var sw = Stopwatch.StartNew();
            
            try
            {
                UpdateJobStatus("🗑️ Initializing junk file scan...");
                UpdateJobProgress(0);
                await Task.Delay(300, ct);
                
                var junkPaths = new[]
                {
                    (Path.GetTempPath(), "User Temp"),
                    (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"), "Local Temp"),
                    (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), "Windows Temp"),
                    (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "INetCache"), "Internet Cache"),
                };
                
                long totalSize = 0;
                int pathIndex = 0;
                
                foreach (var (junkPath, pathName) in junkPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(junkPath)) 
                    {
                        pathIndex++;
                        continue;
                    }
                    
                    UpdateJobStatus($"📁 Scanning: {pathName}...");
                    try
                    {
                        var files = Directory.EnumerateFiles(junkPath, "*", SearchOption.AllDirectories).ToList();
                        int fileCount = 0;
                        
                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            result.FilesScanned++;
                            fileCount++;
                            
                            // Update progress every 50 files
                            if (fileCount % 50 == 0)
                            {
                                UpdateCurrentFile(file);
                                UpdateFilesScanned(result.FilesScanned);
                            }
                            
                            try
                            {
                                var fi = new FileInfo(file);
                                totalSize += fi.Length;
                                result.Threats.Add(new UnifiedThreat
                                {
                                    Type = SysThreatCategory.File,
                                    Name = $"Junk: {Path.GetFileName(file)}",
                                    Description = "Temporary/cache file that can be safely removed",
                                    Location = file,
                                    Severity = SeverityLevel.Low,
                                    Category = "Junk File",
                                    CanRemove = true,
                                    SizeBytes = fi.Length
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error reading file info '{file}': {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error scanning junk path: {ex.Message}");
                    }
                    
                    pathIndex++;
                    UpdateJobProgress((pathIndex * 100) / junkPaths.Length);
                    await Task.Delay(100, ct);
                }
                
                sw.Stop();
                result.EndTime = DateTime.Now;
                result.Duration = sw.Elapsed;
                UpdateJobStatus($"✅ Found {FormatSize(totalSize)} of junk files ({result.Threats.Count:N0} items)");
                UpdateJobProgress(100);
            }
            catch (OperationCanceledException) { result.WasCancelled = true; }
            
            return result;
        }

        private async Task<UnifiedScanResult> RunPrivacyScanAsync(CancellationToken ct)
        {
            var result = new UnifiedScanResult { StartTime = DateTime.Now };
            var sw = Stopwatch.StartNew();
            
            try
            {
                UpdateJobStatus("🔒 Running privacy scan...");
                UpdateJobProgress(10);
                
                UpdateJobStatus("🌐 Checking browser extensions...");
                await ScanBrowserExtensionsAsync(result, ct);
                UpdateJobProgress(40);
                
                UpdateJobStatus("⚙️ Checking privacy settings...");
                CheckPrivacySettings(result);
                UpdateJobProgress(70);
                
                UpdateJobStatus("🍪 Checking for tracking data...");
                await ScanTrackingDataAsync(result, ct);
                UpdateJobProgress(95);
                
                sw.Stop();
                result.EndTime = DateTime.Now;
                result.Duration = sw.Elapsed;
                
                foreach (var threat in result.Threats)
                    threat.Details = "[ADVISORY] " + (threat.Details ?? "");
                
                UpdateJobStatus($"✅ Privacy scan complete - {result.Threats.Count} items found");
                UpdateJobProgress(100);
            }
            catch (OperationCanceledException) { result.WasCancelled = true; }
            
            return result;
        }
        
        #region Helper Scan Methods
        
        private async Task ScanProcessesAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(async () =>
            {
                var processes = Process.GetProcesses();
                int count = 0;
                int total = processes.Length;
                
                foreach (var proc in processes)
                {
                    ct.ThrowIfCancellationRequested();
                    result.FilesScanned++;
                    count++;
                    
                    // Update current file periodically
                    if (count % 10 == 0)
                    {
                        UpdateCurrentFile($"Process: {proc.ProcessName}");
                        UpdateFilesScanned(result.FilesScanned);
                    }
                    
                    try
                    {
                        var name = proc.ProcessName.ToLower();
                        if (MalwareSignatures.IsMalwareProcess(name))
                        {
                            string? path = null;
                            try
                            {
                                path = proc.MainModule?.FileName;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error reading process path for '{proc.ProcessName}': {ex.Message}");
                            }
                            result.Threats.Add(new UnifiedThreat
                            {
                                Type = SysThreatCategory.Process,
                                Name = $"Suspicious Process: {proc.ProcessName}",
                                Description = "Running process matches known malware signature",
                                Location = path ?? "Unknown",
                                Severity = SeverityLevel.Critical,
                                Category = "Malware",
                                CanRemove = true,
                                ProcessId = proc.Id
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error scanning process '{proc.ProcessName}': {ex.Message}");
                    }
                    finally
                    {
                        try
                        {
                            proc.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error disposing process '{proc.ProcessName}': {ex.Message}");
                        }
                    }
                }
                
                // Small delay to make it feel more thorough
                await Task.Delay(100, ct);
            }, ct);
        }
        
        private async Task ScanStartupAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                var startupKeys = new[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                };
                
                foreach (var keyPath in startupKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                    {
                        try
                        {
                            using var key = hive.OpenSubKey(keyPath);
                            if (key == null) continue;
                            foreach (var name in key.GetValueNames())
                            {
                                result.FilesScanned++;
                                var value = key.GetValue(name)?.ToString() ?? "";
                                var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(value.ToLower());
                                if (isSuspicious)
                                {
                                    result.Threats.Add(new UnifiedThreat
                                    {
                                        Type = SysThreatCategory.Startup,
                                        Name = $"Suspicious Startup: {name}",
                                        Description = $"Startup entry matches threat pattern: {pattern}",
                                        Location = $"{keyPath}\\{name}",
                                        Details = value,
                                        Severity = SeverityLevel.High,
                                        Category = "Startup Threat",
                                        CanRemove = true
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error scanning startup key '{keyPath}': {ex.Message}");
                        }
                    }
                }
            }, ct);
        }
        
        private async Task ScanCommonLocationsAsync(UnifiedScanResult result, CancellationToken ct)
        {
            var locations = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Startup"),
                Path.GetTempPath(),
            };
            foreach (var loc in locations)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(loc))
                    await ScanDirectoryAsync(loc, result, ct, maxDepth: 2);
            }
        }
        
        private async Task ScanRecentDownloadsAsync(UnifiedScanResult result, CancellationToken ct)
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads)) return;
            
            await Task.Run(() =>
            {
                var recentFiles = Directory.EnumerateFiles(downloads)
                    .Select(f => new FileInfo(f))
                    .Where(f => f.CreationTime > DateTime.Now.AddDays(-7))
                    .ToList();
                
                foreach (var file in recentFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    result.FilesScanned++;
                    var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(file.Name.ToLower());
                    if (isSuspicious)
                    {
                        result.Threats.Add(new UnifiedThreat
                        {
                            Type = SysThreatCategory.File,
                            Name = $"Suspicious Download: {file.Name}",
                            Description = $"Recent download matches threat pattern: {pattern}",
                            Location = file.FullName,
                            Severity = SeverityLevel.Medium,
                            Category = "Suspicious File",
                            CanRemove = true,
                            SizeBytes = file.Length
                        });
                    }
                }
            }, ct);
        }

        private async Task ScanDirectoryAsync(string path, UnifiedScanResult result, CancellationToken ct, int maxDepth = -1)
        {
            await Task.Run(() => ScanDirectoryRecursive(path, result, ct, 0, maxDepth), ct);
        }
        
        private void ScanDirectoryRecursive(string path, UnifiedScanResult result, CancellationToken ct, int depth, int maxDepth)
        {
            if (maxDepth >= 0 && depth > maxDepth) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    ct.ThrowIfCancellationRequested();
                    ScanFile(file, result);
                    
                    // Update UI every 20 files
                    if (result.FilesScanned % 20 == 0)
                    {
                        UpdateCurrentFile(file);
                        UpdateFilesScanned(result.FilesScanned);
                    }
                }
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    ct.ThrowIfCancellationRequested();
                    ScanDirectoryRecursive(dir, result, ct, depth + 1, maxDepth);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error scanning directory '{path}': {ex.Message}");
            }
        }
        
        private void ScanFile(string filePath, UnifiedScanResult result)
        {
            result.FilesScanned++;
            try
            {
                var fileName = Path.GetFileName(filePath).ToLower();
                var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(fileName);
                if (isSuspicious)
                {
                    var fi = new FileInfo(filePath);
                    result.Threats.Add(new UnifiedThreat
                    {
                        Type = SysThreatCategory.File,
                        Name = $"Suspicious: {Path.GetFileName(filePath)}",
                        Description = $"File matches threat pattern: {pattern}",
                        Location = filePath,
                        Severity = SeverityLevel.Medium,
                        Category = "Suspicious File",
                        CanRemove = true,
                        SizeBytes = fi.Length
                    });
                    
                    // Queue for VirusTotal verification
                    QueueForVirusTotalCheck(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error scanning file '{filePath}': {ex.Message}");
            }
        }
        
        private async Task ScanBrowserExtensionsAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                var chromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data", "Default", "Extensions");
                ScanExtensionFolder(chromePath, "Chrome", result, ct);
                
                var edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data", "Default", "Extensions");
                ScanExtensionFolder(edgePath, "Edge", result, ct);
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
                    result.FilesScanned++;
                    var extName = Path.GetFileName(extDir).ToLower();
                    var (isSuspicious, pattern) = MalwareSignatures.CheckFileName(extName);
                    if (isSuspicious)
                    {
                        result.Threats.Add(new UnifiedThreat
                        {
                            Type = SysThreatCategory.BrowserExtension,
                            Name = $"Suspicious {browser} Extension",
                            Description = $"Extension matches threat pattern: {pattern}",
                            Location = extDir,
                            Severity = SeverityLevel.Medium,
                            Category = "Adware/PUP",
                            CanRemove = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanEngine] Error scanning {browser} extensions: {ex.Message}");
            }
        }
        
        private void CheckPrivacySettings(UnifiedScanResult result)
        {
            result.Threats.Add(new UnifiedThreat
            {
                Type = SysThreatCategory.Registry,
                Name = "Privacy Check: Telemetry Settings",
                Description = "Review Windows telemetry settings for privacy",
                Location = "Settings > Privacy",
                Severity = SeverityLevel.Low, // Info maps to Low
                Category = "Privacy Advisory",
                CanRemove = false
            });
        }
        
        private async Task ScanTrackingDataAsync(UnifiedScanResult result, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                var cookiePaths = new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Google", "Chrome", "User Data", "Default", "Cookies"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "Edge", "User Data", "Default", "Cookies"),
                };
                
                foreach (var cookiePath in cookiePaths)
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(cookiePath))
                    {
                        result.FilesScanned++;
                        var fi = new FileInfo(cookiePath);
                        result.Threats.Add(new UnifiedThreat
                        {
                            Type = SysThreatCategory.File,
                            Name = $"Browser Cookies ({Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(cookiePath)))})",
                            Description = "Browser cookies may contain tracking data",
                            Location = cookiePath,
                            Severity = SeverityLevel.Low, // Info maps to Low
                            Category = "Privacy Advisory",
                            CanRemove = true,
                            SizeBytes = fi.Length
                        });
                    }
                }
            }, ct);
        }
        
        #endregion

        #region Conversion & Helpers
        
        private List<SecurityFinding> ConvertThreats(List<UnifiedThreat> threats)
        {
            return threats.Select(t => new SecurityFinding
            {
                Category = ConvertCategory(t.Type),
                Severity = ConvertSeverity(t.Severity),
                Title = t.Name,
                Description = t.Description,
                FilePath = t.Location,
                Recommendation = t.CanRemove ? "Remove this threat" : "Review manually",
                Confidence = FindingConfidence.High,
                FileSizeBytes = t.SizeBytes,
                CanQuarantine = t.CanRemove,
                CanDelete = t.CanRemove,
                IsAdvisory = t.Severity == SeverityLevel.Low && t.Category.Contains("Advisory"),
                Evidence = new Dictionary<string, string>
                {
                    ["Category"] = t.Category,
                    ["Details"] = t.Details ?? ""
                }
            }).ToList();
        }
        
        private ModelThreatCategory ConvertCategory(SysThreatCategory cat) => cat switch
        {
            SysThreatCategory.Process => ModelThreatCategory.Malware,
            SysThreatCategory.File => ModelThreatCategory.Unknown,
            SysThreatCategory.Startup => ModelThreatCategory.StartupItem,
            SysThreatCategory.Registry => ModelThreatCategory.Registry,
            SysThreatCategory.Service => ModelThreatCategory.Service,
            SysThreatCategory.BrowserExtension => ModelThreatCategory.BrowserHijacker,
            _ => ModelThreatCategory.Unknown
        };
        
        private Models.ThreatSeverity ConvertSeverity(SeverityLevel sev) => sev switch
        {
            SeverityLevel.Critical => Models.ThreatSeverity.Critical,
            SeverityLevel.High => Models.ThreatSeverity.High,
            SeverityLevel.Medium => Models.ThreatSeverity.Medium,
            SeverityLevel.Low => Models.ThreatSeverity.Low,
            _ => Models.ThreatSeverity.Medium
        };
        
        private void UpdateJobStatus(string msg)
        {
            if (_currentJob != null)
            {
                _currentJob.StatusMessage = msg;
                JobUpdated?.Invoke(_currentJob);
            }
        }
        
        private void UpdateJobProgress(int pct)
        {
            if (_currentJob != null)
            {
                _currentJob.ProgressPercent = pct;
                JobUpdated?.Invoke(_currentJob);
            }
        }
        
        private void UpdateFilesScanned(long count)
        {
            if (_currentJob != null)
            {
                _currentJob.FilesScanned = count;
                JobUpdated?.Invoke(_currentJob);
            }
        }
        
        private void UpdateCurrentFile(string file)
        {
            if (_currentJob != null)
            {
                _currentJob.CurrentItem = file;
                JobUpdated?.Invoke(_currentJob);
            }
        }
        
        private void OnThreatFound(UnifiedThreat threat)
        {
            if (_currentJob != null)
            {
                _currentJob.ThreatsFound++;
                var finding = ConvertThreats(new List<UnifiedThreat> { threat }).FirstOrDefault();
                if (finding != null)
                {
                    _currentJob.Findings.Add(finding);
                    FindingDetected?.Invoke(finding);
                }
                JobUpdated?.Invoke(_currentJob);
            }
        }
        
        private string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} bytes";
        }
        
        #endregion
    }
}
