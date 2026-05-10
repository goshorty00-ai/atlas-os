using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class ScanSystemHandler : ICommandHandler
    {
        public string CommandName => "scan_system";

        public string GetDescription() => "Perform a comprehensive system scan for threats and issues";

        public bool CanExecute(CommandContext context) => true;

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var results = new Dictionary<string, object>();
                var issues = new List<string>();

                // Scan processes
                var processes = Process.GetProcesses();
                var highMemoryProcs = processes
                    .Select(p => { try { return (p.ProcessName, Mb: p.WorkingSet64 / 1024 / 1024); } catch { return (p.ProcessName, Mb: 0L); } })
                    .Where(x => x.Mb > 500)
                    .OrderByDescending(x => x.Mb)
                    .Take(5)
                    .ToList();

                results["total_processes"] = processes.Length;
                results["high_memory_processes"] = highMemoryProcs.Count;

                if (highMemoryProcs.Any())
                {
                    results["high_memory_list"] = highMemoryProcs
                        .Select(x => $"{x.ProcessName} ({x.Mb}MB)")
                        .ToList();
                }

                // Scan temp files
                var tempPath = Path.GetTempPath();
                var tempFiles = 0;
                var tempSize = 0L;
                try
                {
                    var files = Directory.GetFiles(tempPath, "*.*", SearchOption.TopDirectoryOnly);
                    tempFiles = files.Length;
                    tempSize = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
                }
                catch { }

                results["temp_files"] = tempFiles;
                results["temp_size_mb"] = tempSize / 1024 / 1024;

                if (tempFiles > 1000)
                    issues.Add($"{tempFiles} temporary files found ({tempSize / 1024 / 1024}MB)");

                // Check disk space
                try
                {
                    var drive = new DriveInfo("C:");
                    var freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
                    results["disk_free_percent"] = Math.Round(freePercent, 1);
                    results["disk_free_gb"] = drive.AvailableFreeSpace / 1024 / 1024 / 1024;

                    if (freePercent < 10)
                        issues.Add($"Low disk space: {freePercent:F1}% free");
                }
                catch { }

                // Check startup programs
                var startupCount = 0;
                try
                {
                    var startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    if (Directory.Exists(startupPath))
                        startupCount = Directory.GetFiles(startupPath).Length;
                }
                catch { }

                results["startup_programs"] = startupCount;

                if (startupCount > 10)
                    issues.Add($"{startupCount} startup programs detected");

                results["issues_found"] = issues.Count;
                if (issues.Any())
                    results["issues"] = issues;

                var message = issues.Any()
                    ? $"System scan complete. {issues.Count} issue(s) found."
                    : "System scan complete. No issues detected.";

                return CommandResult.Success(CommandName, message, results);
            });
        }
    }
}
