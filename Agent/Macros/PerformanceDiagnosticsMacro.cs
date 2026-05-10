using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Performance Diagnostics - Detect common issues (read-only advisory)
    /// </summary>
    public class PerformanceDiagnosticsMacro : AgentMacroDefinition
    {
        public override string Id => "performance-diagnostics";
        public override string Title => "Performance Diagnostics";
        public override string Description => "Detect common performance issues";
        public override string Icon => "🔧";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "diagnose", "performance", "slow", "issues", "problems", "optimize", "speed", "lag" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();
                var issues = new List<DiagnosticIssue>();

                try
                {
                    // Check CPU usage
                    var cpuUsage = GetCpuUsage();
                    if (cpuUsage > 80)
                    {
                        issues.Add(new DiagnosticIssue
                        {
                            Severity = "High",
                            Category = "CPU",
                            Message = $"High CPU usage: {cpuUsage:F0}%",
                            Recommendation = "Check for resource-heavy processes"
                        });
                    }

                    // Check memory usage
                    var (usedRam, totalRam, ramPercent) = GetMemoryInfo();
                    if (ramPercent > 85)
                    {
                        issues.Add(new DiagnosticIssue
                        {
                            Severity = "High",
                            Category = "Memory",
                            Message = $"High memory usage: {ramPercent:F0}%",
                            Recommendation = "Close unused applications"
                        });
                    }
                    else if (ramPercent > 70)
                    {
                        issues.Add(new DiagnosticIssue
                        {
                            Severity = "Medium",
                            Category = "Memory",
                            Message = $"Elevated memory usage: {ramPercent:F0}%",
                            Recommendation = "Monitor memory-heavy apps"
                        });
                    }

                    // Check disk space
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    {
                        var freePercent = (drive.AvailableFreeSpace / (double)drive.TotalSize) * 100;
                        var freeGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;

                        if (freePercent < 10 || freeGB < 10)
                        {
                            issues.Add(new DiagnosticIssue
                            {
                                Severity = "High",
                                Category = "Disk",
                                Message = $"Low disk space on {drive.Name}: {freeGB:F1} GB free",
                                Recommendation = "Free up disk space"
                            });
                        }
                        else if (freePercent < 20)
                        {
                            issues.Add(new DiagnosticIssue
                            {
                                Severity = "Medium",
                                Category = "Disk",
                                Message = $"Disk space getting low on {drive.Name}: {freeGB:F1} GB free",
                                Recommendation = "Consider cleanup"
                            });
                        }
                    }

                    // Check startup items count
                    var startupCount = GetStartupItemCount();
                    if (startupCount > 15)
                    {
                        issues.Add(new DiagnosticIssue
                        {
                            Severity = "Medium",
                            Category = "Startup",
                            Message = $"Many startup items: {startupCount} programs",
                            Recommendation = "Review startup programs"
                        });
                    }

                    // Check for high-CPU processes
                    var highCpuProcs = GetHighMemoryProcesses(5);
                    if (highCpuProcs.Any(p => p.MemoryMB > 2000))
                    {
                        var heavyProc = highCpuProcs.First(p => p.MemoryMB > 2000);
                        issues.Add(new DiagnosticIssue
                        {
                            Severity = "Medium",
                            Category = "Process",
                            Message = $"Heavy process: {heavyProc.Name} using {heavyProc.MemoryMB:F0} MB",
                            Recommendation = "Check if this is expected"
                        });
                    }

                    // Check uptime
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    if (uptime.TotalDays > 7)
                    {
                        issues.Add(new DiagnosticIssue
                        {
                            Severity = "Low",
                            Category = "System",
                            Message = $"System uptime: {uptime.Days} days",
                            Recommendation = "Consider restarting for updates"
                        });
                    }

                    // Build results cards
                    var summaryCard = new MacroResultCard
                    {
                        Title = "Diagnostic Summary",
                        Icon = "🔧",
                        StatusColor = issues.Any(i => i.Severity == "High") ? "red" :
                                     issues.Any(i => i.Severity == "Medium") ? "yellow" : "green"
                    };

                    var highCount = issues.Count(i => i.Severity == "High");
                    var medCount = issues.Count(i => i.Severity == "Medium");
                    var lowCount = issues.Count(i => i.Severity == "Low");

                    summaryCard.Rows.Add(new MacroResultRow
                    {
                        Label = "High Priority",
                        Value = highCount.ToString(),
                        Icon = "🔴",
                        ValueColor = highCount > 0 ? "red" : "green"
                    });

                    summaryCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Medium Priority",
                        Value = medCount.ToString(),
                        Icon = "🟡",
                        ValueColor = medCount > 0 ? "yellow" : "green"
                    });

                    summaryCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Low Priority",
                        Value = lowCount.ToString(),
                        Icon = "🔵"
                    });

                    cards.Add(summaryCard);

                    // Issues card
                    if (issues.Any())
                    {
                        var issuesCard = new MacroResultCard
                        {
                            Title = "Issues Found",
                            Icon = "⚠️",
                            StatusColor = "yellow"
                        };

                        foreach (var issue in issues.OrderByDescending(i => i.Severity == "High" ? 3 : i.Severity == "Medium" ? 2 : 1))
                        {
                            issuesCard.Rows.Add(new MacroResultRow
                            {
                                Label = $"[{issue.Category}]",
                                Value = issue.Message,
                                Icon = issue.Severity == "High" ? "🔴" : issue.Severity == "Medium" ? "🟡" : "🔵",
                                ValueColor = issue.Severity == "High" ? "red" : issue.Severity == "Medium" ? "yellow" : null
                            });
                        }

                        cards.Add(issuesCard);
                    }
                    else
                    {
                        var healthyCard = new MacroResultCard
                        {
                            Title = "System Health",
                            Icon = "✓",
                            StatusColor = "green"
                        };

                        healthyCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Status",
                            Value = "No issues detected",
                            Icon = "✓",
                            ValueColor = "green"
                        });

                        healthyCard.Rows.Add(new MacroResultRow
                        {
                            Label = "CPU",
                            Value = $"{cpuUsage:F0}%",
                            Icon = "⚡"
                        });

                        healthyCard.Rows.Add(new MacroResultRow
                        {
                            Label = "Memory",
                            Value = $"{ramPercent:F0}%",
                            Icon = "🧠"
                        });

                        cards.Add(healthyCard);
                    }

                    // Resource snapshot
                    var resourceCard = new MacroResultCard
                    {
                        Title = "Current Resources",
                        Icon = "📊",
                        StatusColor = "cyan"
                    };

                    resourceCard.Rows.Add(new MacroResultRow
                    {
                        Label = "CPU",
                        Value = $"{cpuUsage:F0}%",
                        Icon = "⚡",
                        ValueColor = cpuUsage > 80 ? "red" : cpuUsage > 50 ? "yellow" : "green"
                    });

                    resourceCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Memory",
                        Value = $"{usedRam:F1} / {totalRam:F1} GB ({ramPercent:F0}%)",
                        Icon = "🧠",
                        ValueColor = ramPercent > 85 ? "red" : ramPercent > 70 ? "yellow" : "green"
                    });

                    resourceCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Processes",
                        Value = Process.GetProcesses().Length.ToString(),
                        Icon = "📋"
                    });

                    resourceCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Uptime",
                        Value = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
                        Icon = "⏱️"
                    });

                    cards.Add(resourceCard);

                    result.Cards = cards;
                    result.Summary = issues.Any()
                        ? $"Found {issues.Count} issue(s): {highCount} high, {medCount} medium, {lowCount} low"
                        : "✓ System running healthy";
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
                    return Convert.ToDouble(obj["LoadPercentage"]);
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
                    return (used, total, (used / total) * 100);
                }
            }
            catch { }
            return (0, 0, 0);
        }

        private int GetStartupItemCount()
        {
            int count = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (key != null) count += key.GetValueNames().Length;

                using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (key2 != null) count += key2.GetValueNames().Length;
            }
            catch { }
            return count;
        }

        private List<ProcessInfo> GetHighMemoryProcesses(int count)
        {
            var procs = new List<ProcessInfo>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        procs.Add(new ProcessInfo
                        {
                            Name = p.ProcessName,
                            MemoryMB = p.WorkingSet64 / 1024.0 / 1024.0
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return procs.OrderByDescending(p => p.MemoryMB).Take(count).ToList();
        }

        private class DiagnosticIssue
        {
            public string Severity { get; set; } = "";
            public string Category { get; set; } = "";
            public string Message { get; set; } = "";
            public string Recommendation { get; set; } = "";
        }

        private class ProcessInfo
        {
            public string Name { get; set; } = "";
            public double MemoryMB { get; set; }
        }
    }
}
