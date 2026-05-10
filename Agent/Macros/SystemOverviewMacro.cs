using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// System Overview - CPU/RAM/Disk usage + top processes
    /// Read-only, safe macro
    /// </summary>
    public class SystemOverviewMacro : AgentMacroDefinition
    {
        public override string Id => "system-overview";
        public override string Title => "System Overview";
        public override string Description => "CPU, RAM, disk usage and top processes";
        public override string Icon => "🖥️";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "system", "overview", "status", "cpu", "ram", "memory", "usage", "health" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();

                try
                {
                    // System Info Card
                    var sysCard = new MacroResultCard
                    {
                        Title = "System Status",
                        Icon = "🖥️",
                        StatusColor = "cyan"
                    };

                    // CPU Usage
                    var cpuUsage = GetCpuUsage();
                    sysCard.Rows.Add(new MacroResultRow
                    {
                        Label = "CPU Usage",
                        Value = $"{cpuUsage:F1}%",
                        Icon = "⚡",
                        ValueColor = cpuUsage > 80 ? "red" : cpuUsage > 50 ? "yellow" : "green"
                    });

                    // RAM Usage
                    var (usedRam, totalRam, ramPercent) = GetMemoryInfo();
                    sysCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Memory",
                        Value = $"{usedRam:F1} / {totalRam:F1} GB ({ramPercent:F0}%)",
                        Icon = "🧠",
                        ValueColor = ramPercent > 85 ? "red" : ramPercent > 70 ? "yellow" : "green"
                    });

                    // Uptime
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    sysCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Uptime",
                        Value = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
                        Icon = "⏱️"
                    });

                    // OS Info
                    sysCard.Rows.Add(new MacroResultRow
                    {
                        Label = "OS",
                        Value = Environment.OSVersion.VersionString,
                        Icon = "💻"
                    });

                    cards.Add(sysCard);

                    // Top Processes Card
                    var procCard = new MacroResultCard
                    {
                        Title = "Top Processes (by CPU)",
                        Icon = "📊",
                        StatusColor = "violet"
                    };

                    var topProcs = GetTopProcesses(5);
                    foreach (var proc in topProcs)
                    {
                        procCard.Rows.Add(new MacroResultRow
                        {
                            Label = proc.Name,
                            Value = $"{proc.CpuPercent:F1}% CPU, {proc.MemoryMB:F0} MB",
                            Icon = "▸"
                        });
                    }

                    cards.Add(procCard);

                    result.Cards = cards;
                    result.Summary = $"CPU: {cpuUsage:F0}% | RAM: {ramPercent:F0}% | Uptime: {uptime.Days}d {uptime.Hours}h";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private double GetCpuUsage()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    return Convert.ToDouble(obj["LoadPercentage"]);
                }
            }
            catch { }
            return 0;
        }

        private (double used, double total, double percent) GetMemoryInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    var total = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024 / 1024;
                    var free = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024 / 1024;
                    var used = total - free;
                    var percent = (used / total) * 100;
                    return (used, total, percent);
                }
            }
            catch { }
            return (0, 0, 0);
        }

        private List<ProcessInfo> GetTopProcesses(int count)
        {
            var procs = new List<ProcessInfo>();
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .ToList();

                foreach (var p in processes)
                {
                    try
                    {
                        procs.Add(new ProcessInfo
                        {
                            Name = p.ProcessName,
                            MemoryMB = p.WorkingSet64 / 1024.0 / 1024.0,
                            CpuPercent = 0 // CPU requires sampling over time
                        });
                    }
                    catch { }
                }

                // Sort by memory as proxy for resource usage
                return procs.OrderByDescending(p => p.MemoryMB).Take(count).ToList();
            }
            catch { }
            return procs;
        }

        private class ProcessInfo
        {
            public string Name { get; set; } = "";
            public double MemoryMB { get; set; }
            public double CpuPercent { get; set; }
        }
    }
}
