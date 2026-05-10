using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Agent Action Engine - Routes commands to safe one-click utilities
    /// All LowRisk actions execute without confirmation
    /// </summary>
    public class AgentActionEngine
    {
        private static AgentActionEngine? _instance;
        public static AgentActionEngine Instance => _instance ??= new AgentActionEngine();

        private readonly List<AgentActionDefinition> _actions = new();
        private readonly List<ActionActivityEntry> _activityLog = new();
        private const int MaxActivityEntries = 50;

        public event EventHandler<ActionResult>? ActionExecuted;
        public event EventHandler<ActionActivityEntry>? ActivityLogged;

        public IReadOnlyList<AgentActionDefinition> Actions => _actions.AsReadOnly();
        public IReadOnlyList<ActionActivityEntry> ActivityLog => _activityLog.AsReadOnly();

        private AgentActionEngine()
        {
            RegisterBuiltInActions();
        }

        private void RegisterBuiltInActions()
        {
            // Networking
            _actions.Add(new CopyNetworkSnapshotAction());
            _actions.Add(new OpenNetworkSettingsAction());
            
            // Windows Settings Launchers
            _actions.Add(new OpenWindowsSettingsAction());
            _actions.Add(new OpenSoundSettingsAction());
            _actions.Add(new OpenBluetoothSettingsAction());
            
            // Diagnostics / Convenience
            _actions.Add(new CopySystemOverviewAction());
            _actions.Add(new ExportDiagnosticsReportAction());
            _actions.Add(new OpenAtlasLogsFolderAction());
            _actions.Add(new OpenTaskManagerAction());
            
            // Apps
            _actions.Add(new OpenDisplaySettingsAction());

            // Media
            _actions.Add(new ScanMediaLibraryAction());
            _actions.Add(new OrganizeMediaLibraryAction());
        }

        /// <summary>
        /// Find the best matching action for the input
        /// </summary>
        public AgentActionDefinition? FindAction(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var matches = _actions
                .Where(a => a.Matches(input))
                .OrderByDescending(a => a.GetMatchScore(input))
                .ToList();

            return matches.FirstOrDefault();
        }

        /// <summary>
        /// Try to execute an action matching the input
        /// Returns null if no action matches
        /// </summary>
        public async Task<ActionResult?> TryExecuteAsync(string input)
        {
            var action = FindAction(input);
            if (action == null)
                return null;

            return await ExecuteActionAsync(action);
        }

        /// <summary>
        /// Execute a specific action by ID
        /// </summary>
        public async Task<ActionResult?> ExecuteByIdAsync(string actionId)
        {
            var action = _actions.FirstOrDefault(a => a.Id.Equals(actionId, StringComparison.OrdinalIgnoreCase));
            if (action == null)
                return null;

            return await ExecuteActionAsync(action);
        }

        /// <summary>
        /// Execute an action and log the activity
        /// </summary>
        private async Task<ActionResult> ExecuteActionAsync(AgentActionDefinition action)
        {
            var sw = Stopwatch.StartNew();
            ActionResult result;

            try
            {
                // Safety check - only allow LowRisk actions without confirmation
                if (action.Risk != ActionRiskLevel.LowRisk)
                {
                    result = new ActionResult
                    {
                        Success = false,
                        ErrorMessage = "Blocked: requires LIVE mode + confirmation"
                    };
                }
                else
                {
                    result = await action.ExecuteAsync();
                }
            }
            catch (Exception ex)
            {
                result = new ActionResult
                {
                    Success = false,
                    ErrorMessage = $"Execution failed: {ex.Message}"
                };
                Debug.WriteLine($"[ActionEngine] Error executing {action.Id}: {ex.Message}");
            }

            sw.Stop();
            result.ExecutionTime = sw.Elapsed;

            // Log activity
            var activity = new ActionActivityEntry
            {
                Timestamp = DateTime.Now,
                ActionId = action.Id,
                ActionTitle = action.Title,
                Category = action.Category,
                Duration = sw.Elapsed,
                Success = result.Success
            };
            LogActivity(activity);

            ActionExecuted?.Invoke(this, result);

            return result;
        }

        private void LogActivity(ActionActivityEntry entry)
        {
            _activityLog.Insert(0, entry);
            if (_activityLog.Count > MaxActivityEntries)
                _activityLog.RemoveAt(_activityLog.Count - 1);

            ActivityLogged?.Invoke(this, entry);
        }

        /// <summary>
        /// Get all available action summaries for command palette
        /// </summary>
        public List<ActionSummary> GetActionSummaries()
        {
            return _actions.Select(a => new ActionSummary
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                Icon = a.Icon,
                Category = a.Category,
                Keywords = a.Keywords
            }).ToList();
        }

        /// <summary>
        /// Get suggested actions for a macro result
        /// </summary>
        public List<string> GetSuggestedActionsForMacro(string macroId)
        {
            return macroId switch
            {
                "system-overview" => new List<string> { "copy-system-overview", "open-task-manager", "export-diagnostics" },
                "network-snapshot" => new List<string> { "copy-network-snapshot", "open-network-settings" },
                "security-status" => new List<string> { "open-windows-settings" },
                "disk-health" => new List<string> { "export-diagnostics", "open-windows-settings" },
                "performance-diagnostics" => new List<string> { "open-task-manager", "export-diagnostics" },
                "installed-apps" => new List<string> { "open-windows-settings" },
                "startup-inventory" => new List<string> { "open-task-manager" },
                "event-viewer" => new List<string> { "export-diagnostics", "open-atlas-logs" },
                _ => new List<string>()
            };
        }
    }

    #region Activity Entry

    public class ActionActivityEntry
    {
        public DateTime Timestamp { get; set; }
        public string ActionId { get; set; } = "";
        public string ActionTitle { get; set; } = "";
        public ActionCategory Category { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
    }

    public class ActionSummary
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public ActionCategory Category { get; set; }
        public string[] Keywords { get; set; } = Array.Empty<string>();
    }

    #endregion


    #region Networking Actions

    /// <summary>
    /// Copy Network Snapshot - Copies IP/DNS/Gateway/Wi-Fi to clipboard
    /// </summary>
    public class CopyNetworkSnapshotAction : AgentActionDefinition
    {
        public override string Id => "copy-network-snapshot";
        public override string Title => "Copy Network Snapshot";
        public override string Description => "Copy IP, DNS, Gateway, Wi-Fi name to clipboard";
        public override string Icon => "📋";
        public override ActionCategory Category => ActionCategory.Networking;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "copy network", "copy ip", "network clipboard", "copy dns", "network snapshot clipboard" };

        public override async Task<ActionResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("=== Network Snapshot ===");
                    sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();

                    // Local IP
                    var localIp = GetLocalIP();
                    sb.AppendLine($"Local IP: {localIp ?? "Unknown"}");

                    // Gateway
                    var gateway = GetGateway();
                    sb.AppendLine($"Gateway: {gateway ?? "Unknown"}");

                    // DNS
                    var dns = GetDnsServers();
                    sb.AppendLine($"DNS: {(dns.Any() ? string.Join(", ", dns) : "Unknown")}");

                    // Active interface
                    var activeIface = GetActiveInterface();
                    if (activeIface != null)
                    {
                        sb.AppendLine($"Interface: {activeIface.Name}");
                        sb.AppendLine($"Type: {activeIface.NetworkInterfaceType}");
                    }

                    var text = sb.ToString();
                    
                    // Copy to clipboard on UI thread
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));

                    return new ActionResult
                    {
                        Success = true,
                        Message = "Network snapshot copied to clipboard",
                        Output = text
                    };
                }
                catch (Exception ex)
                {
                    return new ActionResult { Success = false, ErrorMessage = ex.Message };
                }
            });
        }

        private string? GetLocalIP()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString();
            }
            catch { return null; }
        }

        private string? GetGateway()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address.ToString();
            }
            catch { return null; }
        }

        private List<string> GetDnsServers()
        {
            try
            {
                var iface = GetActiveInterface();
                if (iface != null)
                {
                    return iface.GetIPProperties().DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString())
                        .ToList();
                }
            }
            catch { }
            return new List<string>();
        }

        private NetworkInterface? GetActiveInterface()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                    n.GetIPProperties().GatewayAddresses.Any());
        }
    }

    /// <summary>
    /// Open Network Settings - Opens Windows network settings via ms-settings URI
    /// </summary>
    public class OpenNetworkSettingsAction : AgentActionDefinition
    {
        public override string Id => "open-network-settings";
        public override string Title => "Open Network Settings";
        public override string Description => "Open Windows Network & Internet settings";
        public override string Icon => "🌐";
        public override ActionCategory Category => ActionCategory.Networking;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "network settings", "internet settings", "wifi settings", "open network", "network config" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network",
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Network Settings"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    #endregion

    #region Windows Settings Launchers

    /// <summary>
    /// Open Windows Settings - Opens main Windows settings
    /// </summary>
    public class OpenWindowsSettingsAction : AgentActionDefinition
    {
        public override string Id => "open-windows-settings";
        public override string Title => "Open Windows Settings";
        public override string Description => "Open Windows Settings app";
        public override string Icon => "⚙️";
        public override ActionCategory Category => ActionCategory.UI;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "windows settings", "settings", "open settings", "system settings", "control panel" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:",
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Windows Settings"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>
    /// Open Sound Settings
    /// </summary>
    public class OpenSoundSettingsAction : AgentActionDefinition
    {
        public override string Id => "open-sound-settings";
        public override string Title => "Open Sound Settings";
        public override string Description => "Open Windows Sound settings";
        public override string Icon => "🔊";
        public override ActionCategory Category => ActionCategory.UI;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "sound settings", "audio settings", "volume settings", "speaker settings", "open sound" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:sound",
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Sound Settings"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>
    /// Open Bluetooth Settings
    /// </summary>
    public class OpenBluetoothSettingsAction : AgentActionDefinition
    {
        public override string Id => "open-bluetooth-settings";
        public override string Title => "Open Bluetooth Settings";
        public override string Description => "Open Windows Bluetooth settings";
        public override string Icon => "📶";
        public override ActionCategory Category => ActionCategory.UI;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "bluetooth settings", "bluetooth", "open bluetooth", "pair device", "bluetooth devices" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:bluetooth",
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Bluetooth Settings"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>
    /// Open Display Settings
    /// </summary>
    public class OpenDisplaySettingsAction : AgentActionDefinition
    {
        public override string Id => "open-display-settings";
        public override string Title => "Open Display Settings";
        public override string Description => "Open Windows Display settings";
        public override string Icon => "🖥️";
        public override ActionCategory Category => ActionCategory.UI;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "display settings", "screen settings", "monitor settings", "resolution", "open display" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:display",
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Display Settings"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    #endregion

    #region Diagnostics / Convenience Actions

    /// <summary>
    /// Copy System Overview - Copies system info to clipboard
    /// </summary>
    public class CopySystemOverviewAction : AgentActionDefinition
    {
        public override string Id => "copy-system-overview";
        public override string Title => "Copy System Overview";
        public override string Description => "Copy CPU, RAM, disk usage to clipboard";
        public override string Icon => "📋";
        public override ActionCategory Category => ActionCategory.Diagnostics;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "copy system", "system clipboard", "copy specs", "copy overview", "system info clipboard" };

        public override async Task<ActionResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("=== System Overview ===");
                    sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();

                    // OS
                    sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
                    sb.AppendLine($"Machine: {Environment.MachineName}");
                    sb.AppendLine($"User: {Environment.UserName}");
                    sb.AppendLine($"Processors: {Environment.ProcessorCount}");
                    sb.AppendLine();

                    // Uptime
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    sb.AppendLine($"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");

                    // Memory (basic)
                    var gcMem = GC.GetTotalMemory(false) / 1024 / 1024;
                    sb.AppendLine($"Atlas Memory: {gcMem} MB");

                    // Drives
                    sb.AppendLine();
                    sb.AppendLine("Drives:");
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    {
                        var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                        var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                        sb.AppendLine($"  {drive.Name} {freeGb:F1} GB free / {totalGb:F1} GB total");
                    }

                    var text = sb.ToString();
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));

                    return new ActionResult
                    {
                        Success = true,
                        Message = "System overview copied to clipboard",
                        Output = text
                    };
                }
                catch (Exception ex)
                {
                    return new ActionResult { Success = false, ErrorMessage = ex.Message };
                }
            });
        }
    }

    /// <summary>
    /// Export Diagnostics Report - Saves JSON/MD report to AppData
    /// </summary>
    public class ExportDiagnosticsReportAction : AgentActionDefinition
    {
        public override string Id => "export-diagnostics";
        public override string Title => "Export Diagnostics Report";
        public override string Description => "Save diagnostics report to AppData\\AtlasAI\\reports";
        public override string Icon => "📄";
        public override ActionCategory Category => ActionCategory.Diagnostics;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "export diagnostics", "save report", "diagnostics report", "export report", "save diagnostics" };

        public override async Task<ActionResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var reportsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AtlasAI", "reports");
                    Directory.CreateDirectory(reportsDir);

                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var filePath = Path.Combine(reportsDir, $"diagnostics_{timestamp}.json");

                    var report = new
                    {
                        GeneratedAt = DateTime.Now,
                        System = new
                        {
                            OS = Environment.OSVersion.VersionString,
                            Machine = Environment.MachineName,
                            User = Environment.UserName,
                            Processors = Environment.ProcessorCount,
                            Is64Bit = Environment.Is64BitOperatingSystem,
                            UptimeMs = Environment.TickCount64
                        },
                        Drives = DriveInfo.GetDrives()
                            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                            .Select(d => new
                            {
                                Name = d.Name,
                                Label = d.VolumeLabel,
                                Format = d.DriveFormat,
                                TotalBytes = d.TotalSize,
                                FreeBytes = d.AvailableFreeSpace
                            }).ToArray(),
                        Network = new
                        {
                            IsConnected = NetworkInterface.GetIsNetworkAvailable(),
                            Interfaces = NetworkInterface.GetAllNetworkInterfaces()
                                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                           n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                                .Select(n => new
                                {
                                    Name = n.Name,
                                    Type = n.NetworkInterfaceType.ToString(),
                                    Speed = n.Speed
                                }).ToArray()
                        },
                        AtlasMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024
                    };

                    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, json);

                    return new ActionResult
                    {
                        Success = true,
                        Message = $"Report saved to {Path.GetFileName(filePath)}",
                        OpenFilePath = filePath
                    };
                }
                catch (Exception ex)
                {
                    return new ActionResult { Success = false, ErrorMessage = ex.Message };
                }
            });
        }
    }

    /// <summary>
    /// Open Atlas Logs Folder
    /// </summary>
    public class OpenAtlasLogsFolderAction : AgentActionDefinition
    {
        public override string Id => "open-atlas-logs";
        public override string Title => "Open Atlas Logs Folder";
        public override string Description => "Open the Atlas AI logs folder in Explorer";
        public override string Icon => "📂";
        public override ActionCategory Category => ActionCategory.Files;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "open logs", "atlas logs", "log folder", "view logs", "logs folder" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                var logsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AtlasAI");
                Directory.CreateDirectory(logsDir);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = logsDir,
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Atlas logs folder",
                    OpenFilePath = logsDir
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>
    /// Open Task Manager
    /// </summary>
    public class OpenTaskManagerAction : AgentActionDefinition
    {
        public override string Id => "open-task-manager";
        public override string Title => "Open Task Manager";
        public override string Description => "Launch Windows Task Manager";
        public override string Icon => "📊";
        public override ActionCategory Category => ActionCategory.Apps;
        public override ActionRiskLevel Risk => ActionRiskLevel.LowRisk;
        public override string[] Keywords => new[] { "task manager", "taskmgr", "open task manager", "processes", "performance monitor" };

        public override Task<ActionResult> ExecuteAsync()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskmgr.exe",
                    UseShellExecute = true
                });

                return Task.FromResult(new ActionResult
                {
                    Success = true,
                    Message = "Opened Task Manager"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }

    #endregion
}
