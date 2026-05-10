using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class CleanTempFilesHandler : ICommandHandler
    {
        public string CommandName => "clean_temp_files";

        public string GetDescription() => "Clean temporary files from system temp folders";

        public bool CanExecute(CommandContext context) => true;

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var results = new Dictionary<string, object>();
                var totalDeleted = 0;
                var totalFreed = 0L;
                var errors = 0;

                var tempPaths = new[]
                {
                    Path.GetTempPath(),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
                };

                foreach (var tempPath in tempPaths.Distinct())
                {
                    if (!Directory.Exists(tempPath)) continue;

                    try
                    {
                        var files = Directory.GetFiles(tempPath, "*.*", SearchOption.TopDirectoryOnly);

                        foreach (var file in files)
                        {
                            try
                            {
                                var info = new FileInfo(file);
                                var size = info.Length;

                                // Only delete files older than 1 day
                                if (info.LastAccessTime < DateTime.Now.AddDays(-1))
                                {
                                    File.Delete(file);
                                    totalDeleted++;
                                    totalFreed += size;
                                }
                            }
                            catch
                            {
                                errors++;
                            }
                        }
                    }
                    catch { }
                }

                results["files_deleted"] = totalDeleted;
                results["space_freed_mb"] = totalFreed / 1024 / 1024;
                results["errors"] = errors;

                var message = $"Cleaned {totalDeleted} temporary file(s), freed {totalFreed / 1024 / 1024}MB";

                return CommandResult.Success(CommandName, message, results);
            });
        }
    }
}
