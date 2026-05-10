using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskSchedulerLib = Microsoft.Win32.TaskScheduler;
using AtlasAI.ActionHistory;
using AtlasAI.Ledger;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Install Interceptor - Quietly observes what installers change, then explains it to the user.
    /// This is an observer + explainer, NOT a blocker or sandbox.
    /// </summary>
    public class InstallInterceptor
    {
        private static InstallInterceptor? _instance;
        public static InstallInterceptor Instance => _instance ??= new InstallInterceptor();

        // State tracking
        private bool _isWatching;
        private string? _currentInstallerName;
        private int? _currentInstallerPid;
        private DateTime _installStartTime;
        private SystemSnapshot? _beforeSnapshot;

        // Process watcher
        private ManagementEventWatcher? _processStartWatcher;
        private ManagementEventWatcher? _processStopWatcher;
        private readonly HashSet<int> _trackedInstallerPids = new();

        // Settings
        public bool IsEnabled { get; set; } = true;
        public bool SilentWatchMode { get; set; } = false;

        // Events
        public event Action<string>? StatusChanged;
        public event Action<InstallAnalysis>? InstallAnalysisComplete;

        // Installer process patterns
        private static readonly string[] InstallerPatterns = new[]
        {
            "setup", "install", "msiexec", "uninst", "update", "patch",
            "deploy", "installer", "_setup", "-setup", ".tmp"
        };

        private InstallInterceptor() { }

        public void Start()
        {
            if (_isWatching) return;

            try
            {
                StartProcessWatchers();
                _isWatching = true;
                StatusChanged?.Invoke("Install interceptor active");
                Debug.WriteLine("[InstallInterceptor] Started");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstallInterceptor] Start failed: {ex.Message}");
                StatusChanged?.Invoke($"Install interceptor failed: {ex.Message}");
            }
        }

        public void Stop()
        {
            _processStartWatcher?.Stop();
            _processStartWatcher?.Dispose();
            _processStopWatcher?.Stop();
            _processStopWatcher?.Dispose();
            _isWatching = false;
            StatusChanged?.Invoke("Install interceptor stopped");
        }

        #region Process Watching

        private void StartProcessWatchers()
        {
            try
            {
                // Watch for process starts
                var startQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
                _processStartWatcher = new ManagementEventWatcher(startQuery);
                _processStartWatcher.EventArrived += OnProcessStarted;
                _processStartWatcher.Start();

                // Watch for process stops
                var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
                _processStopWatcher = new ManagementEventWatcher(stopQuery);
                _processStopWatcher.EventArrived += OnProcessStopped;
                _processStopWatcher.Start();

                Debug.WriteLine("[InstallInterceptor] Process watchers started");
            }
            catch (Exception ex)
            {
                // Process watching requires elevated privileges - fail silently
                Debug.WriteLine($"[InstallInterceptor] Process watcher failed (may need admin): {ex.Message}");
            }
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            if (!IsEnabled) return;

            try
            {
                var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
                var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

                if (IsInstallerProcess(processName))
                {
                    Debug.WriteLine($"[InstallInterceptor] Installer detected: {processName} (PID: {processId})");
                    _ = BeginWatchingInstallAsync(processName, processId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstallInterceptor] OnProcessStarted error: {ex.Message}");
            }
        }

        private void OnProcessStopped(object sender, EventArrivedEventArgs e)
        {
            if (!IsEnabled) return;

            try
            {
                var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

                if (_trackedInstallerPids.Contains(processId))
                {
                    Debug.WriteLine($"[InstallInterceptor] Tracked installer exited: PID {processId}");
                    _trackedInstallerPids.Remove(processId);

                    // If this was our main tracked installer, analyze
                    if (_currentInstallerPid == processId)
                    {
                        _ = AnalyzeInstallCompletionAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstallInterceptor] OnProcessStopped error: {ex.Message}");
            }
        }

        private bool IsInstallerProcess(string processName)
        {
            var lower = processName.ToLower();
            return InstallerPatterns.Any(p => lower.Contains(p));
        }

        #endregion

        #region Install Watching

        private async Task BeginWatchingInstallAsync(string installerName, int pid)
        {
            // Don't start a new watch if we're already watching
            if (_beforeSnapshot != null && _trackedInstallerPids.Count > 0)
            {
                // Just track this as a child installer process
                _trackedInstallerPids.Add(pid);
                return;
            }

            _currentInstallerName = installerName;
            _currentInstallerPid = pid;
            _installStartTime = DateTime.Now;
            _trackedInstallerPids.Add(pid);

            // Capture system state BEFORE install
            _beforeSnapshot = await CaptureSystemSnapshotAsync();

            StatusChanged?.Invoke($"Watching install: {installerName}");
            Debug.WriteLine($"[InstallInterceptor] Captured before-snapshot for {installerName}");
        }

        private async Task AnalyzeInstallCompletionAsync()
        {
            if (_beforeSnapshot == null || string.IsNullOrEmpty(_currentInstallerName))
            {
                ResetWatchState();
                return;
            }

            // Wait a moment for all changes to settle
            await Task.Delay(2000);

            // Capture system state AFTER install
            var afterSnapshot = await CaptureSystemSnapshotAsync();

            // Compare and analyze
            var analysis = CompareSnapshots(_beforeSnapshot, afterSnapshot);
            analysis.InstallerName = _currentInstallerName;
            analysis.InstallDuration = DateTime.Now - _installStartTime;

            // Classify the install
            ClassifyInstall(analysis);

            // Log to ActionHistory
            LogInstallToHistory(analysis);

            // Notify based on classification and silent mode
            await NotifyUserAsync(analysis);

            // Fire event for UI
            InstallAnalysisComplete?.Invoke(analysis);

            // Reset state
            ResetWatchState();
        }

        private void ResetWatchState()
        {
            _beforeSnapshot = null;
            _currentInstallerName = null;
            _currentInstallerPid = null;
            _trackedInstallerPids.Clear();
        }

        #endregion

        #region System Snapshot

        private async Task<SystemSnapshot> CaptureSystemSnapshotAsync()
        {
            var snapshot = new SystemSnapshot { CapturedAt = DateTime.Now };

            await Task.Run(() =>
            {
                // Capture services
                try
                {
                    foreach (var svc in ServiceController.GetServices())
                    {
                        snapshot.Services[svc.ServiceName] = new ServiceInfo
                        {
                            Name = svc.ServiceName,
                            DisplayName = svc.DisplayName,
                            Status = svc.Status.ToString(),
                            StartType = GetServiceStartType(svc.ServiceName)
                        };
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InstallInterceptor] Service capture error: {ex.Message}");
                }

                // Capture startup entries (registry)
                CaptureStartupEntries(snapshot);

                // Capture scheduled tasks
                CaptureScheduledTasks(snapshot);

                // Capture running processes
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        snapshot.RunningProcesses[proc.Id] = proc.ProcessName;
                    }
                    catch { }
                }
            });

            return snapshot;
        }

        private string GetServiceStartType(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                var start = key?.GetValue("Start");
                return start switch
                {
                    0 => "Boot",
                    1 => "System",
                    2 => "Automatic",
                    3 => "Manual",
                    4 => "Disabled",
                    _ => "Unknown"
                };
            }
            catch { return "Unknown"; }
        }

        private void CaptureStartupEntries(SystemSnapshot snapshot)
        {
            var registryPaths = new[]
            {
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
                (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
            };

            foreach (var (root, path) in registryPaths)
            {
                try
                {
                    using var key = root.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        var fullKey = $"{root.Name}\\{path}\\{name}";
                        snapshot.StartupEntries[fullKey] = new StartupInfo
                        {
                            Name = name,
                            Command = value,
                            Location = $"{root.Name}\\{path}"
                        };
                    }
                }
                catch { }
            }
        }

        private void CaptureScheduledTasks(SystemSnapshot snapshot)
        {
            try
            {
                using var ts = new TaskSchedulerLib.TaskService();
                CaptureTasksRecursive(ts.RootFolder, snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InstallInterceptor] Task capture error: {ex.Message}");
            }
        }

        private void CaptureTasksRecursive(TaskSchedulerLib.TaskFolder folder, SystemSnapshot snapshot)
        {
            try
            {
                foreach (var task in folder.Tasks)
                {
                    try
                    {
                        var action = task.Definition.Actions.FirstOrDefault();
                        var actionPath = action is TaskSchedulerLib.ExecAction exec ? exec.Path : action?.ActionType.ToString() ?? "";

                        snapshot.ScheduledTasks[task.Path] = new TaskInfo
                        {
                            Name = task.Name,
                            Path = task.Path,
                            IsEnabled = task.Enabled,
                            ActionPath = actionPath
                        };
                    }
                    catch { }
                }

                foreach (var subFolder in folder.SubFolders)
                {
                    CaptureTasksRecursive(subFolder, snapshot);
                }
            }
            catch { }
        }

        #endregion

        #region Analysis

        private InstallAnalysis CompareSnapshots(SystemSnapshot before, SystemSnapshot after)
        {
            var analysis = new InstallAnalysis();

            // Find new services
            foreach (var kvp in after.Services)
            {
                if (!before.Services.ContainsKey(kvp.Key))
                {
                    analysis.NewServices.Add(kvp.Value);
                }
            }

            // Find new startup entries
            foreach (var kvp in after.StartupEntries)
            {
                if (!before.StartupEntries.ContainsKey(kvp.Key))
                {
                    analysis.NewStartupEntries.Add(kvp.Value);
                }
            }

            // Find new scheduled tasks
            foreach (var kvp in after.ScheduledTasks)
            {
                if (!before.ScheduledTasks.ContainsKey(kvp.Key))
                {
                    analysis.NewScheduledTasks.Add(kvp.Value);
                }
            }

            // Find new background processes (processes that weren't running before)
            var beforeProcs = new HashSet<string>(before.RunningProcesses.Values.Select(p => p.ToLower()));
            var newProcs = after.RunningProcesses.Values
                .Where(p => !beforeProcs.Contains(p.ToLower()))
                .Distinct()
                .ToList();

            // Filter out common system processes
            var systemProcs = new HashSet<string> { "conhost", "cmd", "powershell", "werfault", "dllhost" };
            analysis.NewBackgroundProcesses = newProcs.Where(p => !systemProcs.Contains(p.ToLower())).ToList();

            return analysis;
        }

        private void ClassifyInstall(InstallAnalysis analysis)
        {
            int riskScore = 0;

            // Score based on changes
            riskScore += analysis.NewServices.Count * 2;
            riskScore += analysis.NewStartupEntries.Count * 3;
            riskScore += analysis.NewScheduledTasks.Count * 2;
            riskScore += analysis.NewBackgroundProcesses.Count;

            // Check for hidden/suspicious patterns
            foreach (var svc in analysis.NewServices)
            {
                if (svc.StartType == "Automatic") riskScore++;
                if (string.IsNullOrEmpty(svc.DisplayName)) riskScore += 2;
            }

            foreach (var task in analysis.NewScheduledTasks)
            {
                if (task.ActionPath.Contains("powershell", StringComparison.OrdinalIgnoreCase)) riskScore++;
                if (task.ActionPath.Contains("cmd", StringComparison.OrdinalIgnoreCase)) riskScore++;
            }

            // Classify
            if (riskScore == 0)
            {
                analysis.Classification = InstallClassification.Expected;
                analysis.ClassificationReason = "No background components added.";
            }
            else if (riskScore <= 3)
            {
                analysis.Classification = InstallClassification.Expected;
                analysis.ClassificationReason = "Normal install behavior.";
            }
            else if (riskScore <= 7)
            {
                analysis.Classification = InstallClassification.Unusual;
                analysis.ClassificationReason = "Adds background components.";
            }
            else
            {
                analysis.Classification = InstallClassification.HighAttention;
                analysis.ClassificationReason = "Significant persistence or hidden behavior.";
            }

            // Check context awareness for similar past installs
            CheckInstallHistory(analysis);
        }

        private void CheckInstallHistory(InstallAnalysis analysis)
        {
            // Use ContextAwareness to check if user has installed similar software
            // This is a simplified check - could be enhanced with ML
            var installerLower = analysis.InstallerName?.ToLower() ?? "";

            // Common software that legitimately adds services/startup
            var knownLegitimate = new[]
            {
                "chrome", "firefox", "edge", "steam", "discord", "spotify",
                "nvidia", "amd", "intel", "realtek", "logitech", "razer",
                "microsoft", "adobe", "autodesk", "unity", "unreal"
            };

            if (knownLegitimate.Any(k => installerLower.Contains(k)))
            {
                if (analysis.Classification == InstallClassification.Unusual)
                {
                    analysis.ClassificationReason += " (Known software - likely legitimate)";
                }
            }
        }

        #endregion

        #region Notification & Logging

        private void LogInstallToHistory(InstallAnalysis analysis)
        {
            var record = new ActionRecord
            {
                Type = ActionType.ScanPerformed,
                Description = $"Install analyzed: {analysis.InstallerName}",
                CanUndo = false,
                Timestamp = DateTime.Now
            };

            ActionHistoryManager.Instance.RecordAction(record);

            // Also log to Ledger for detailed tracking
            var evt = new LedgerEvent
            {
                Category = LedgerCategory.Software,
                Severity = analysis.Classification switch
                {
                    InstallClassification.Expected => LedgerSeverity.Info,
                    InstallClassification.Unusual => LedgerSeverity.Medium,
                    InstallClassification.HighAttention => LedgerSeverity.High,
                    _ => LedgerSeverity.Info
                },
                Title = $"Install completed: {analysis.InstallerName}",
                WhyItMatters = analysis.ClassificationReason
            };

            evt.WithEvidence("Installer", analysis.InstallerName ?? "Unknown")
               .WithEvidence("Duration", $"{analysis.InstallDuration.TotalSeconds:F0} seconds")
               .WithEvidence("Classification", analysis.Classification.ToString())
               .WithEvidence("New Services", analysis.NewServices.Count.ToString())
               .WithEvidence("New Startup Items", analysis.NewStartupEntries.Count.ToString())
               .WithEvidence("New Scheduled Tasks", analysis.NewScheduledTasks.Count.ToString())
               .WithEvidence("New Background Processes", analysis.NewBackgroundProcesses.Count.ToString());

            // Add actions based on what was installed
            if (analysis.NewStartupEntries.Any())
            {
                evt.WithAction(new LedgerAction
                {
                    Label = "⏸️ Disable Startup Items",
                    Type = LedgerActionType.Block,
                    Data = JsonSerializer.Serialize(analysis.NewStartupEntries)
                });
            }

            if (analysis.NewServices.Any())
            {
                evt.WithAction(new LedgerAction
                {
                    Label = "⏸️ Disable Services",
                    Type = LedgerActionType.Block,
                    Data = JsonSerializer.Serialize(analysis.NewServices)
                });
            }

            evt.WithAction(LedgerAction.Dismiss("✓ Keep Everything"));

            LedgerManager.Instance.AddEvent(evt);
        }

        private async Task NotifyUserAsync(InstallAnalysis analysis)
        {
            // Silent Watch Mode handling
            if (SilentWatchMode)
            {
                if (analysis.Classification == InstallClassification.Expected)
                {
                    // Fully silent
                    Debug.WriteLine($"[InstallInterceptor] Silent: Expected install - {analysis.InstallerName}");
                    return;
                }
                else if (analysis.Classification == InstallClassification.Unusual)
                {
                    // Logged silently
                    Debug.WriteLine($"[InstallInterceptor] Silent: Unusual install logged - {analysis.InstallerName}");
                    return;
                }
                // High Attention - escalate once (fall through to notification)
            }

            // Only notify for Unusual or HighAttention
            if (analysis.Classification == InstallClassification.Expected)
            {
                return;
            }

            // Build summary message
            var summary = BuildAnalysisSummary(analysis);
            StatusChanged?.Invoke(summary);
        }

        public string BuildAnalysisSummary(InstallAnalysis analysis)
        {
            var parts = new List<string>();
            parts.Add($"📦 **Install Analysis: {analysis.InstallerName}**\n");

            // Classification badge
            var badge = analysis.Classification switch
            {
                InstallClassification.Expected => "✅ Expected",
                InstallClassification.Unusual => "⚡ Unusual",
                InstallClassification.HighAttention => "⚠️ High Attention",
                _ => "❓ Unknown"
            };
            parts.Add($"**Classification:** {badge}");
            parts.Add($"_{analysis.ClassificationReason}_\n");

            // Changes summary
            if (analysis.TotalChanges == 0)
            {
                parts.Add("No system changes detected.");
            }
            else
            {
                parts.Add("**Changes detected:**");

                if (analysis.NewServices.Any())
                {
                    parts.Add($"• {analysis.NewServices.Count} new service(s)");
                    foreach (var svc in analysis.NewServices.Take(3))
                    {
                        parts.Add($"  - {svc.DisplayName ?? svc.Name} ({svc.StartType})");
                    }
                    if (analysis.NewServices.Count > 3)
                        parts.Add($"  - ...and {analysis.NewServices.Count - 3} more");
                }

                if (analysis.NewStartupEntries.Any())
                {
                    parts.Add($"• {analysis.NewStartupEntries.Count} new startup item(s)");
                    foreach (var entry in analysis.NewStartupEntries.Take(3))
                    {
                        parts.Add($"  - {entry.Name}");
                    }
                }

                if (analysis.NewScheduledTasks.Any())
                {
                    parts.Add($"• {analysis.NewScheduledTasks.Count} new scheduled task(s)");
                    foreach (var task in analysis.NewScheduledTasks.Take(3))
                    {
                        parts.Add($"  - {task.Name}");
                    }
                }

                if (analysis.NewBackgroundProcesses.Any())
                {
                    parts.Add($"• {analysis.NewBackgroundProcesses.Count} new background process(es)");
                }
            }

            parts.Add($"\n_Duration: {analysis.InstallDuration.TotalSeconds:F0}s_");

            return string.Join("\n", parts);
        }

        #endregion

        #region Manual Trigger

        /// <summary>
        /// Manually trigger install watching (for testing or when process detection fails)
        /// </summary>
        public async Task ManualWatchStartAsync(string installerName)
        {
            await BeginWatchingInstallAsync(installerName, -1);
        }

        /// <summary>
        /// Manually trigger install completion analysis
        /// </summary>
        public async Task ManualWatchCompleteAsync()
        {
            await AnalyzeInstallCompletionAsync();
        }

        #endregion
    }

    #region Data Classes

    public class SystemSnapshot
    {
        public DateTime CapturedAt { get; set; }
        public Dictionary<string, ServiceInfo> Services { get; set; } = new();
        public Dictionary<string, StartupInfo> StartupEntries { get; set; } = new();
        public Dictionary<string, TaskInfo> ScheduledTasks { get; set; } = new();
        public Dictionary<int, string> RunningProcesses { get; set; } = new();
    }

    public class ServiceInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
    }

    public class StartupInfo
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Location { get; set; } = "";
    }

    public class TaskInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsEnabled { get; set; }
        public string ActionPath { get; set; } = "";
    }

    public enum InstallClassification
    {
        Expected,      // Normal install behavior
        Unusual,       // Adds background components
        HighAttention  // Persistence or hidden behavior
    }

    public class InstallAnalysis
    {
        public string? InstallerName { get; set; }
        public TimeSpan InstallDuration { get; set; }
        public InstallClassification Classification { get; set; }
        public string ClassificationReason { get; set; } = "";

        public List<ServiceInfo> NewServices { get; set; } = new();
        public List<StartupInfo> NewStartupEntries { get; set; } = new();
        public List<TaskInfo> NewScheduledTasks { get; set; } = new();
        public List<string> NewBackgroundProcesses { get; set; } = new();

        public int TotalChanges => NewServices.Count + NewStartupEntries.Count + 
                                   NewScheduledTasks.Count + NewBackgroundProcesses.Count;
    }

    #endregion
}
