using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AtlasAI.Commands
{
    public class OptimizeMemoryHandler : ICommandHandler
    {
        public string CommandName => "optimize_memory";

        public string GetDescription() => "Optimize system memory by clearing cache and running garbage collection";

        public bool CanExecute(CommandContext context) => true;

        public async Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            return await Task.Run(() =>
            {
                var results = new Dictionary<string, object>();

                // Get initial memory state
                long initialMemory = 0;
                try
                {
                    using var counter = new PerformanceCounter("Memory", "Available MBytes");
                    initialMemory = (long)counter.NextValue();
                    results["initial_available_mb"] = initialMemory;
                }
                catch { }

                // Force garbage collection
                var gcBefore = GC.GetTotalMemory(false);
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var gcAfter = GC.GetTotalMemory(true);
                var gcFreed = (gcBefore - gcAfter) / 1024 / 1024;

                results["gc_freed_mb"] = gcFreed;

                // Clear temp files
                var tempCleared = ClearTempFiles();
                results["temp_files_deleted"] = tempCleared;

                // Empty working set (Windows API)
                try
                {
                    var process = Process.GetCurrentProcess();
                    EmptyWorkingSet(process.Handle);
                }
                catch { }

                // Get final memory state
                long finalMemory = 0;
                try
                {
                    System.Threading.Thread.Sleep(500);
                    using var counter = new PerformanceCounter("Memory", "Available MBytes");
                    finalMemory = (long)counter.NextValue();
                    results["final_available_mb"] = finalMemory;
                    results["memory_freed_mb"] = finalMemory - initialMemory;
                }
                catch { }

                var message = $"Memory optimization complete. Freed {gcFreed}MB from GC, deleted {tempCleared} temp files.";

                return CommandResult.Success(CommandName, message, results);
            });
        }

        private int ClearTempFiles()
        {
            var deleted = 0;
            try
            {
                var tempPath = Path.GetTempPath();
                var files = Directory.GetFiles(tempPath, "*.tmp", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch { }
                }
            }
            catch { }

            return deleted;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        private void EmptyWorkingSet(IntPtr processHandle)
        {
            try
            {
                SetProcessWorkingSetSize(processHandle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }
    }
}
