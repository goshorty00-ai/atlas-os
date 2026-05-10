using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Ledger;

namespace AtlasAI.SecuritySuite.Services
{
    /// <summary>
    /// Watches the Windows hosts file for changes and creates ledger events with revert capability.
    /// </summary>
    public class HostsFileWatcher : IDisposable
    {
        private static readonly Lazy<HostsFileWatcher> _instance = new(() => new HostsFileWatcher());
        public static HostsFileWatcher Instance => _instance.Value;

        private readonly string _hostsPath;
        private readonly string _backupDir;
        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private string? _previousContent;
        private string? _previousHash;
        private string? _lastWrittenHash; // Track our own writes to avoid loops
        private DateTime _lastWriteTime = DateTime.MinValue;
        private readonly object _lock = new();
        private bool _isDisposed;
        private bool _isInitialized;

        // Debounce window in milliseconds
        private const int DebounceMs = 1500;
        // Ignore window after our own writes
        private const int IgnoreWindowMs = 2000;

        public event Action<string>? StatusChanged;

        private HostsFileWatcher()
        {
            // Get hosts file path
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            _hostsPath = Path.Combine(systemRoot, "System32", "drivers", "etc", "hosts");

            // Setup backup directory
            _backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI", "HostsBackups");
            Directory.CreateDirectory(_backupDir);
        }

        /// <summary>
        /// Start watching the hosts file
        /// </summary>
        public void Start()
        {
            if (_isInitialized) return;

            try
            {
                // Capture baseline snapshot
                CaptureBaseline();

                // Setup FileSystemWatcher
                var directory = Path.GetDirectoryName(_hostsPath)!;
                var fileName = Path.GetFileName(_hostsPath);

                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnHostsFileChanged;
                _watcher.Created += OnHostsFileChanged;

                _isInitialized = true;
                StatusChanged?.Invoke("Hosts file watcher started");
                Debug.WriteLine($"[HostsWatcher] Started watching: {_hostsPath}");

                // Add initial ledger event
                var initEvent = new LedgerEvent
                {
                    Category = LedgerCategory.FileSystem,
                    Severity = LedgerSeverity.Info,
                    Title = "Hosts file monitoring active",
                    WhyItMatters = "Atlas is now watching for changes to your hosts file."
                };
                initEvent.WithEvidence("Path", _hostsPath, isPath: true)
                         .WithEvidence("Baseline Hash", _previousHash ?? "unknown")
                         .WithAction(LedgerAction.Dismiss("Got it"));

                LedgerManager.Instance.AddEvent(initEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HostsWatcher] Failed to start: {ex.Message}");
                StatusChanged?.Invoke($"Failed to start hosts watcher: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop watching
        /// </summary>
        public void Stop()
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _isInitialized = false;
            StatusChanged?.Invoke("Hosts file watcher stopped");
        }

        private void CaptureBaseline()
        {
            try
            {
                if (File.Exists(_hostsPath))
                {
                    _previousContent = File.ReadAllText(_hostsPath);
                    _previousHash = ComputeHash(_previousContent);
                    Debug.WriteLine($"[HostsWatcher] Baseline captured, hash: {_previousHash.Substring(0, 16)}...");

                    // Save baseline to disk for persistence
                    SaveBackup(_previousContent, "baseline");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HostsWatcher] Failed to capture baseline: {ex.Message}");
            }
        }

        private void OnHostsFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce: reset timer on each event
            lock (_lock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceMs, Timeout.Infinite);
            }
        }

        private void OnDebounceElapsed(object? state)
        {
            lock (_lock)
            {
                try
                {
                    // Check if we're in the ignore window after our own write
                    if ((DateTime.Now - _lastWriteTime).TotalMilliseconds < IgnoreWindowMs)
                    {
                        Debug.WriteLine("[HostsWatcher] Ignoring change (within ignore window after our write)");
                        return;
                    }

                    // Read current content
                    string currentContent;
                    try
                    {
                        // Wait a bit for file to be released
                        Thread.Sleep(100);
                        currentContent = File.ReadAllText(_hostsPath);
                    }
                    catch (IOException)
                    {
                        // File might be locked, retry once
                        Thread.Sleep(500);
                        try
                        {
                            currentContent = File.ReadAllText(_hostsPath);
                        }
                        catch
                        {
                            Debug.WriteLine("[HostsWatcher] Could not read hosts file (locked)");
                            return;
                        }
                    }

                    var currentHash = ComputeHash(currentContent);

                    // Check if this is our own write
                    if (currentHash == _lastWrittenHash)
                    {
                        Debug.WriteLine("[HostsWatcher] Ignoring change (matches our last write)");
                        return;
                    }

                    // Check if content actually changed
                    if (currentHash == _previousHash)
                    {
                        Debug.WriteLine("[HostsWatcher] No actual change detected");
                        return;
                    }

                    Debug.WriteLine($"[HostsWatcher] Change detected! Old hash: {_previousHash?.Substring(0, 16)}..., New hash: {currentHash.Substring(0, 16)}...");

                    // Calculate diff summary
                    var diffSummary = GetDiffSummary(_previousContent ?? "", currentContent);

                    // Create ledger event
                    CreateChangeEvent(currentContent, currentHash, diffSummary);

                    // Update previous state (but keep backup for revert)
                    _previousContent = currentContent;
                    _previousHash = currentHash;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HostsWatcher] Error processing change: {ex.Message}");
                }
            }
        }

        private void CreateChangeEvent(string newContent, string newHash, string diffSummary)
        {
            var lastWrite = File.GetLastWriteTime(_hostsPath);

            // Store backup for revert
            var backupPath = SaveBackup(_previousContent ?? "", $"before_{DateTime.Now:yyyyMMdd_HHmmss}");

            var evt = new LedgerEvent
            {
                Category = LedgerCategory.FileSystem,
                Severity = LedgerSeverity.High,
                Title = "Hosts file changed",
                WhyItMatters = "Hosts file changes can redirect websites and are commonly used for adware or network tampering.",
                BackupData = backupPath // Store backup path for revert
            };

            evt.WithEvidence("Path", _hostsPath, isPath: true)
               .WithEvidence("Last Modified", lastWrite.ToString("yyyy-MM-dd HH:mm:ss"))
               .WithEvidence("SHA256", newHash)
               .WithEvidence("Changes", diffSummary);

            // Add actions
            evt.WithAction(new LedgerAction
            {
                Label = "⏪ Revert",
                Type = LedgerActionType.Revert,
                Data = backupPath,
                RequiresConfirmation = true
            });

            evt.WithAction(LedgerAction.Inspect("📂 Open Location", Path.GetDirectoryName(_hostsPath)));
            evt.WithAction(LedgerAction.Dismiss("✓ Dismiss"));

            // Register revert handler
            RegisterRevertHandler(evt.Id, backupPath);

            LedgerManager.Instance.AddEvent(evt);
            StatusChanged?.Invoke($"Hosts file changed: {diffSummary}");
        }

        private void RegisterRevertHandler(string eventId, string backupPath)
        {
            // Store the revert data in a way the LedgerManager can use
            // The actual revert is handled by ExecuteRevertAsync
        }

        /// <summary>
        /// Revert the hosts file to a previous state
        /// </summary>
        public async Task<(bool Success, string Message)> RevertAsync(string backupPath)
        {
            try
            {
                // Check if backup exists
                if (!File.Exists(backupPath))
                {
                    return (false, "Backup file not found");
                }

                var backupContent = await File.ReadAllTextAsync(backupPath);

                // Check if we need elevation
                if (!IsElevated())
                {
                    // Try to write anyway - might work if user has permissions
                    try
                    {
                        await WriteHostsFileAsync(backupContent);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return (false, "⚠️ Administrator privileges required. Please run Atlas as Administrator to revert the hosts file.");
                    }
                }
                else
                {
                    await WriteHostsFileAsync(backupContent);
                }

                // Create restoration event
                var restoredHash = ComputeHash(backupContent);
                var restoreEvent = new LedgerEvent
                {
                    Category = LedgerCategory.FileSystem,
                    Severity = LedgerSeverity.Info,
                    Title = "Hosts file restored",
                    WhyItMatters = "The hosts file was successfully reverted to its previous state."
                };

                restoreEvent.WithEvidence("Path", _hostsPath, isPath: true)
                           .WithEvidence("Restored At", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                           .WithEvidence("SHA256", restoredHash)
                           .WithAction(LedgerAction.Dismiss("Got it"));

                LedgerManager.Instance.AddEvent(restoreEvent);

                // Update our tracking
                _previousContent = backupContent;
                _previousHash = restoredHash;

                return (true, "✅ Hosts file restored successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HostsWatcher] Revert failed: {ex.Message}");
                return (false, $"❌ Revert failed: {ex.Message}");
            }
        }

        private async Task WriteHostsFileAsync(string content)
        {
            // Mark that we're about to write
            var hash = ComputeHash(content);
            _lastWrittenHash = hash;
            _lastWriteTime = DateTime.Now;

            // Try to write with retry
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(_hostsPath, content);
                    Debug.WriteLine($"[HostsWatcher] Successfully wrote hosts file, hash: {hash.Substring(0, 16)}...");
                    return;
                }
                catch (IOException) when (i < 2)
                {
                    // File might be locked, wait and retry
                    await Task.Delay(500);
                }
            }

            throw new IOException("Could not write to hosts file - file may be locked by another process");
        }

        private string SaveBackup(string content, string suffix)
        {
            var backupPath = Path.Combine(_backupDir, $"hosts_{suffix}.bak");
            try
            {
                File.WriteAllText(backupPath, content);
                Debug.WriteLine($"[HostsWatcher] Saved backup: {backupPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HostsWatcher] Failed to save backup: {ex.Message}");
            }
            return backupPath;
        }

        private static string ComputeHash(string content)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static string GetDiffSummary(string oldContent, string newContent)
        {
            var oldLines = oldContent.Split('\n', StringSplitOptions.None);
            var newLines = newContent.Split('\n', StringSplitOptions.None);

            var added = 0;
            var removed = 0;

            // Simple line count comparison
            var oldSet = new System.Collections.Generic.HashSet<string>(oldLines);
            var newSet = new System.Collections.Generic.HashSet<string>(newLines);

            foreach (var line in newLines)
            {
                if (!oldSet.Contains(line) && !string.IsNullOrWhiteSpace(line))
                    added++;
            }

            foreach (var line in oldLines)
            {
                if (!newSet.Contains(line) && !string.IsNullOrWhiteSpace(line))
                    removed++;
            }

            if (added == 0 && removed == 0)
                return "Content modified (whitespace or formatting)";

            var parts = new System.Collections.Generic.List<string>();
            if (added > 0) parts.Add($"{added} line{(added == 1 ? "" : "s")} added");
            if (removed > 0) parts.Add($"{removed} line{(removed == 1 ? "" : "s")} removed");

            return string.Join(", ", parts);
        }

        private static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
