using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.ITManagement
{
    /// <summary>
    /// Central IT Management service - coordinates all IT management features
    /// </summary>
    public class ITManagementService : IDisposable
    {
        private static ITManagementService? _instance;
        private static readonly object _lock = new();
        
        public SystemHealthMonitor HealthMonitor { get; }
        public AutomationScriptLibrary ScriptLibrary { get; }
        public NetworkDiscovery NetworkDiscovery { get; }
        public ProactiveIssueDetector IssueDetector { get; }
        
        public event Action<string>? OnNotification;
        public event Action<DetectedIssue>? OnCriticalIssue;
        
        public static ITManagementService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ITManagementService();
                    }
                }
                return _instance;
            }
        }
        
        private ITManagementService()
        {
            HealthMonitor = new SystemHealthMonitor();
            ScriptLibrary = new AutomationScriptLibrary();
            NetworkDiscovery = new NetworkDiscovery();
            IssueDetector = new ProactiveIssueDetector(HealthMonitor);
            
            // Wire up events
            HealthMonitor.OnAlertTriggered += alert =>
            {
                OnNotification?.Invoke($"⚠️ {alert.Message}");
            };
            
            IssueDetector.OnIssueDetected += issue =>
            {
                if (issue.Severity == IssueSeverity.Critical)
                {
                    OnCriticalIssue?.Invoke(issue);
                    OnNotification?.Invoke($"🔴 CRITICAL: {issue.Title}");
                }
                else
                {
                    OnNotification?.Invoke($"🔵 Issue detected: {issue.Title}");
                }
            };
            
            ScriptLibrary.OnScriptOutput += output =>
            {
                Debug.WriteLine($"[Script] {output}");
            };
        }
        
        /// <summary>
        /// Start all monitoring services
        /// </summary>
        public void StartAllMonitoring()
        {
            HealthMonitor.StartMonitoring();
            IssueDetector.StartAnalysis();
            Debug.WriteLine("[ITManagement] All monitoring services started");
        }
        
        /// <summary>
        /// Stop all monitoring services
        /// </summary>
        public void StopAllMonitoring()
        {
            HealthMonitor.StopMonitoring();
            IssueDetector.StopAnalysis();
            Debug.WriteLine("[ITManagement] All monitoring services stopped");
        }
        
        /// <summary>
        /// Get a comprehensive system report
        /// </summary>
        public async Task<string> GetSystemReportAsync()
        {
            var health = HealthMonitor.CollectHealthData();
            var issues = await IssueDetector.RunFullAnalysisAsync();
            
            var report = $"""
                ═══════════════════════════════════════════
                       ATLAS AI - SYSTEM HEALTH REPORT
                       {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                ═══════════════════════════════════════════
                
                📊 SYSTEM OVERVIEW
                ───────────────────────────────────────────
                CPU Usage:     {health.CpuUsage}%
                RAM Usage:     {health.RamUsage}% ({health.UsedRamMB:N0} MB / {health.TotalRamMB:N0} MB)
                System Uptime: {health.UptimeHours} hours
                
                💾 STORAGE
                ───────────────────────────────────────────
                {string.Join("\n", health.Drives.Select(d => 
                    $"Drive {d.Name,-4} {d.UsagePercent,3}% used | {d.FreeGB,6} GB free / {d.TotalGB,6} GB total"))}
                
                🌐 NETWORK
                ───────────────────────────────────────────
                {string.Join("\n", health.NetworkAdapters.Select(n => 
                    $"{n.Name}: {(n.IsConnected ? "Connected" : "Disconnected")} @ {n.Speed} Mbps"))}
                
                📈 TOP PROCESSES (by memory)
                ───────────────────────────────────────────
                {string.Join("\n", health.TopProcesses.Take(5).Select(p => 
                    $"{p.Name,-25} {p.MemoryMB,6} MB | {p.Threads,3} threads"))}
                
                ⚠️ ACTIVE ISSUES ({IssueDetector.ActiveIssues.Count})
                ───────────────────────────────────────────
                {(IssueDetector.ActiveIssues.Any() 
                    ? string.Join("\n", IssueDetector.ActiveIssues.Select(i => 
                        $"[{i.Severity}] {i.Title}"))
                    : "✅ No issues detected")}
                
                ═══════════════════════════════════════════
                """;
            
            return report;
        }
        
        /// <summary>
        /// Process natural language IT commands
        /// </summary>
        public async Task<string> ProcessITCommandAsync(string command)
        {
            var cmd = command.ToLower();
            
            // System health queries
            if (cmd.Contains("health") || cmd.Contains("status") || cmd.Contains("how is my"))
            {
                return HealthMonitor.GetHealthSummary();
            }
            
            if (cmd.Contains("report") || cmd.Contains("full status") || cmd.Contains("system report"))
            {
                return await GetSystemReportAsync();
            }
            
            // Cleanup commands
            if (cmd.Contains("clean") && (cmd.Contains("temp") || cmd.Contains("temporary")))
            {
                var result = await ScriptLibrary.ExecuteScriptAsync("cleanup_temp");
                return result.Message;
            }
            
            if (cmd.Contains("empty") && cmd.Contains("recycle"))
            {
                var result = await ScriptLibrary.ExecuteScriptAsync("empty_recycle_bin");
                return result.Message;
            }
            
            // Network commands
            if (cmd.Contains("scan") && cmd.Contains("network"))
            {
                OnNotification?.Invoke("🔍 Starting network scan...");
                var devices = await NetworkDiscovery.ScanNetworkAsync();
                return NetworkDiscovery.GetDiscoverySummary();
            }
            
            if (cmd.Contains("network") && cmd.Contains("info"))
            {
                var info = NetworkDiscovery.GetLocalNetworkInfo();
                return $"""
                    Network Information:
                    IP Address: {info.LocalIP}
                    Subnet: {info.SubnetMask}
                    Gateway: {info.Gateway}
                    DNS: {info.DnsServer}
                    Adapter: {info.AdapterName}
                    MAC: {info.MacAddress}
                    """;
            }
            
            if (cmd.Contains("flush") && cmd.Contains("dns"))
            {
                var result = await ScriptLibrary.ExecuteScriptAsync("flush_dns");
                return result.Message;
            }
            
            if (cmd.Contains("speed") && cmd.Contains("test"))
            {
                OnNotification?.Invoke("🌐 Running speed test...");
                var result = await ScriptLibrary.ExecuteScriptAsync("speed_test");
                return result.Message;
            }
            
            // Security commands
            if (cmd.Contains("virus") || cmd.Contains("malware") || cmd.Contains("scan"))
            {
                if (cmd.Contains("quick"))
                {
                    OnNotification?.Invoke("🛡️ Starting quick virus scan...");
                    var result = await ScriptLibrary.ExecuteScriptAsync("windows_defender_scan");
                    return result.Message;
                }
            }
            
            if (cmd.Contains("firewall"))
            {
                var result = await ScriptLibrary.ExecuteScriptAsync("firewall_status");
                return result.Message;
            }
            
            if (cmd.Contains("update") && (cmd.Contains("check") || cmd.Contains("windows")))
            {
                OnNotification?.Invoke("🔄 Checking for updates...");
                var result = await ScriptLibrary.ExecuteScriptAsync("check_updates");
                return result.Message;
            }
            
            // Maintenance commands
            if (cmd.Contains("startup") && (cmd.Contains("program") || cmd.Contains("optimize")))
            {
                var result = await ScriptLibrary.ExecuteScriptAsync("optimize_startup");
                return result.Message;
            }
            
            if (cmd.Contains("restore point") || cmd.Contains("backup point"))
            {
                OnNotification?.Invoke("💾 Creating restore point...");
                var result = await ScriptLibrary.ExecuteScriptAsync("create_restore_point");
                return result.Message;
            }
            
            // Issue detection
            if (cmd.Contains("issue") || cmd.Contains("problem"))
            {
                if (cmd.Contains("check") || cmd.Contains("find") || cmd.Contains("detect"))
                {
                    OnNotification?.Invoke("🔍 Analyzing system for issues...");
                    await IssueDetector.RunFullAnalysisAsync();
                    return IssueDetector.GetIssuesSummary();
                }
                return IssueDetector.GetIssuesSummary();
            }
            
            // List available scripts
            if (cmd.Contains("what can you") || cmd.Contains("help") || cmd.Contains("commands"))
            {
                return GetAvailableCommands();
            }
            
            // Run specific script by name
            foreach (var script in ScriptLibrary.Scripts)
            {
                if (cmd.Contains(script.Name.ToLower()) || cmd.Contains(script.Id))
                {
                    OnNotification?.Invoke($"🚀 Running: {script.Name}");
                    var result = await ScriptLibrary.ExecuteScriptAsync(script.Id);
                    return result.Message;
                }
            }
            
            return "I didn't understand that IT command. Say 'help' to see what I can do.";
        }
        
        public string GetAvailableCommands()
        {
            var categories = ScriptLibrary.Scripts
                .GroupBy(s => s.Category)
                .OrderBy(g => g.Key);
            
            var commands = $"""
                🖥️ ATLAS AI - IT MANAGEMENT COMMANDS
                ═══════════════════════════════════════════
                
                📊 MONITORING
                • "system health" - Quick health check
                • "system report" - Full detailed report
                • "check for issues" - Proactive issue detection
                
                🌐 NETWORK
                • "network info" - Show network configuration
                • "scan network" - Discover devices on network
                • "speed test" - Test internet speed
                • "flush dns" - Clear DNS cache
                
                🧹 CLEANUP
                • "clean temp files" - Remove temporary files
                • "empty recycle bin" - Clear recycle bin
                
                🔒 SECURITY
                • "quick virus scan" - Run Windows Defender scan
                • "check firewall" - Verify firewall status
                • "check updates" - Check for Windows updates
                
                ⚙️ MAINTENANCE
                • "startup programs" - List/manage startup items
                • "create restore point" - Create system restore point
                
                {string.Join("\n\n", categories.Select(c => 
                    $"📁 {c.Key.ToString().ToUpper()}\n" + 
                    string.Join("\n", c.Select(s => $"  {s.Icon} {s.Name} - {s.Description}"))))}
                """;
            
            return commands;
        }
        
        /// <summary>
        /// Auto-fix an issue if possible
        /// </summary>
        public async Task<string> AutoFixIssueAsync(string issueId)
        {
            var issue = IssueDetector.ActiveIssues.FirstOrDefault(i => i.Id == issueId);
            if (issue == null)
                return $"Issue '{issueId}' not found or already resolved.";
            
            if (!issue.AutoFixAvailable || string.IsNullOrEmpty(issue.AutoFixScriptId))
                return $"No automatic fix available for '{issue.Title}'. Manual intervention required.";
            
            OnNotification?.Invoke($"🔧 Auto-fixing: {issue.Title}");
            var result = await ScriptLibrary.ExecuteScriptAsync(issue.AutoFixScriptId);
            
            if (result.Success)
            {
                // Re-run analysis to check if issue is resolved
                await IssueDetector.RunFullAnalysisAsync();
                return $"✅ Fix applied: {result.Message}";
            }
            
            return $"❌ Fix failed: {result.Message}";
        }
        
        public void Dispose()
        {
            StopAllMonitoring();
            HealthMonitor.Dispose();
            IssueDetector.Dispose();
        }
    }
}
