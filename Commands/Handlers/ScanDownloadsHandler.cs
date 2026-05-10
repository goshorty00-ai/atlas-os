using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class ScanDownloadsHandler : ICommandHandler
    {
        public string CommandName => "scan_downloads";

        public string GetDescription() => "Scan Downloads folder for suspicious files";

        public bool CanExecute(CommandContext context) => true;

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var results = new Dictionary<string, object>();
                var downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");

                if (!Directory.Exists(downloadsPath))
                {
                    return CommandResult.Error(CommandName, "Downloads folder not found");
                }

                var executableExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".dll", ".com"
                };

                var files = Directory.GetFiles(downloadsPath, "*.*", SearchOption.TopDirectoryOnly);
                var executables = new List<Dictionary<string, object>>();
                var recentFiles = new List<Dictionary<string, object>>();
                var largeFiles = new List<Dictionary<string, object>>();

                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var ext = info.Extension.ToLowerInvariant();
                        var isExecutable = executableExts.Contains(ext);

                        if (isExecutable)
                        {
                            executables.Add(new Dictionary<string, object>
                            {
                                ["name"] = info.Name,
                                ["size_mb"] = info.Length / 1024 / 1024,
                                ["modified"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                                ["extension"] = ext
                            });
                        }

                        if (info.LastWriteTime > DateTime.Now.AddDays(-7))
                        {
                            recentFiles.Add(new Dictionary<string, object>
                            {
                                ["name"] = info.Name,
                                ["size_mb"] = info.Length / 1024 / 1024,
                                ["modified"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                        }

                        if (info.Length > 100 * 1024 * 1024) // > 100MB
                        {
                            largeFiles.Add(new Dictionary<string, object>
                            {
                                ["name"] = info.Name,
                                ["size_mb"] = info.Length / 1024 / 1024
                            });
                        }
                    }
                    catch { }
                }

                results["total_files"] = files.Length;
                results["executables_found"] = executables.Count;
                results["recent_files"] = recentFiles.Count;
                results["large_files"] = largeFiles.Count;

                if (executables.Any())
                    results["executables"] = executables.Take(10).ToList();

                if (recentFiles.Any())
                    results["recent"] = recentFiles.Take(10).ToList();

                if (largeFiles.Any())
                    results["large"] = largeFiles.Take(5).ToList();

                var message = executables.Any()
                    ? $"Downloads scan complete. {files.Length} files scanned, {executables.Count} executable(s) found."
                    : $"Downloads scan complete. {files.Length} files scanned, no executables found.";

                return CommandResult.Success(CommandName, message, results);
            });
        }
    }
}
