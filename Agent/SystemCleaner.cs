using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// System Cleaner - Clean temp files, browser cache, recycle bin.
    /// </summary>
    public static class SystemCleaner
    {
        /// <summary>
        /// Handle cleanup commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Analyze disk space
            if (lower.Contains("disk space") || lower.Contains("storage") || lower.Contains("drive space"))
            {
                return GetDiskSpaceInfo();
            }
            
            // Analyze temp files
            if (lower.Contains("temp") && (lower.Contains("size") || lower.Contains("analyze") || lower.Contains("check")))
            {
                return await AnalyzeTempFilesAsync();
            }
            
            // Clean temp files
            if (lower.Contains("clean") && lower.Contains("temp"))
            {
                return await CleanTempFilesAsync();
            }
            
            // Empty recycle bin
            if (lower.Contains("empty") && (lower.Contains("recycle") || lower.Contains("trash") || lower.Contains("bin")))
            {
                return await EmptyRecycleBinAsync();
            }
            
            // Large files finder
            if (lower.Contains("large files") || lower.Contains("big files") || lower.Contains("find large"))
            {
                return await FindLargeFilesAsync();
            }
            
            // Duplicate files (basic)
            if (lower.Contains("duplicate") && lower.Contains("file"))
            {
                return "🔍 Duplicate file scanning is resource-intensive.\n\n" +
                       "For thorough duplicate detection, I recommend:\n" +
                       "• **WizTree** - Fast disk analyzer\n" +
                       "• **dupeGuru** - Free duplicate finder\n" +
                       "• **CCleaner** - System cleaner with duplicate finder";
            }
            
            return null;
        }
        
        private static string GetDiskSpaceInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("💾 **Disk Space:**\n");
            
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = total - free;
                var percentUsed = (used * 100.0 / total);
                
                var bar = GetProgressBar(percentUsed);
                var status = percentUsed > 90 ? "🔴" : percentUsed > 75 ? "🟡" : "🟢";
                
                sb.AppendLine($"**{drive.Name}** ({drive.DriveType})");
                sb.AppendLine($"{bar} {percentUsed:F1}%");
                sb.AppendLine($"Used: {FormatSize(used)} / {FormatSize(total)}");
                sb.AppendLine($"Free: {FormatSize(free)} {status}");
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private static async Task<string> AnalyzeTempFilesAsync()
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("🗑️ **Temp Files Analysis:**\n");
                
                long totalSize = 0;
                int totalFiles = 0;
                
                // Windows Temp
                var windowsTemp = Path.GetTempPath();
                var (size1, count1) = GetFolderSize(windowsTemp);
                totalSize += size1;
                totalFiles += count1;
                sb.AppendLine($"**Windows Temp:** {FormatSize(size1)} ({count1:N0} files)");
                
                // User Temp
                var userTemp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Temp";
                if (Directory.Exists(userTemp) && userTemp != windowsTemp)
                {
                    var (size2, count2) = GetFolderSize(userTemp);
                    totalSize += size2;
                    totalFiles += count2;
                    sb.AppendLine($"**User Temp:** {FormatSize(size2)} ({count2:N0} files)");
                }
                
                // Prefetch
                var prefetch = @"C:\Windows\Prefetch";
                if (Directory.Exists(prefetch))
                {
                    var (size3, count3) = GetFolderSize(prefetch);
                    sb.AppendLine($"**Prefetch:** {FormatSize(size3)} ({count3:N0} files)");
                }
                
                // Recent
                var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (Directory.Exists(recent))
                {
                    var (size4, count4) = GetFolderSize(recent);
                    sb.AppendLine($"**Recent:** {FormatSize(size4)} ({count4:N0} files)");
                }
                
                sb.AppendLine($"\n**Total cleanable:** ~{FormatSize(totalSize)}");
                sb.AppendLine("\nSay 'clean temp files' to remove them.");
                
                return sb.ToString();
            });
        }
        
        private static async Task<string> CleanTempFilesAsync()
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("🧹 **Cleaning Temp Files:**\n");
                
                long freedSpace = 0;
                int deletedFiles = 0;
                int errors = 0;
                
                // Clean Windows Temp
                var tempPath = Path.GetTempPath();
                var (freed1, deleted1, err1) = CleanFolder(tempPath);
                freedSpace += freed1;
                deletedFiles += deleted1;
                errors += err1;
                sb.AppendLine($"✓ Windows Temp: {FormatSize(freed1)} freed");
                
                // Clean user temp
                var userTemp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Temp";
                if (Directory.Exists(userTemp) && userTemp != tempPath)
                {
                    var (freed2, deleted2, err2) = CleanFolder(userTemp);
                    freedSpace += freed2;
                    deletedFiles += deleted2;
                    errors += err2;
                    sb.AppendLine($"✓ User Temp: {FormatSize(freed2)} freed");
                }
                
                sb.AppendLine($"\n**Total freed:** {FormatSize(freedSpace)}");
                sb.AppendLine($"**Files deleted:** {deletedFiles:N0}");
                if (errors > 0)
                    sb.AppendLine($"**Skipped (in use):** {errors}");
                
                return sb.ToString();
            });
        }
        
        private static async Task<string> EmptyRecycleBinAsync()
        {
            try
            {
                // Use SHEmptyRecycleBin via shell
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rd /s /q C:\\$Recycle.Bin 2>nul",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                // Alternative: Use PowerShell
                psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync();
                
                return "🗑️ **Recycle Bin emptied!**";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        private static async Task<string> FindLargeFilesAsync()
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("📁 **Large Files (Downloads & Documents):**\n");
                
                var searchPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                
                var largeFiles = searchPaths
                    .Where(Directory.Exists)
                    .SelectMany(path =>
                    {
                        try
                        {
                            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                                .Select(f => new FileInfo(f))
                                .Where(f => f.Length > 100 * 1024 * 1024); // > 100MB
                        }
                        catch { return Enumerable.Empty<FileInfo>(); }
                    })
                    .OrderByDescending(f => f.Length)
                    .Take(10)
                    .ToList();
                
                if (!largeFiles.Any())
                {
                    sb.AppendLine("No files larger than 100MB found in common folders.");
                }
                else
                {
                    foreach (var file in largeFiles)
                    {
                        var name = file.Name.Length > 40 ? file.Name.Substring(0, 37) + "..." : file.Name;
                        sb.AppendLine($"• **{FormatSize(file.Length)}** - {name}");
                    }
                }
                
                return sb.ToString();
            });
        }
        
        private static (long Size, int Count) GetFolderSize(string path)
        {
            long size = 0;
            int count = 0;
            
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }
            
            return (size, count);
        }
        
        private static (long Freed, int Deleted, int Errors) CleanFolder(string path)
        {
            long freed = 0;
            int deleted = 0;
            int errors = 0;
            
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        // Only delete files older than 1 day
                        if (info.LastAccessTime < DateTime.Now.AddDays(-1))
                        {
                            freed += info.Length;
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch { errors++; }
                }
                
                // Clean empty subdirectories
                foreach (var dir in Directory.GetDirectories(path))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        if (dirInfo.LastAccessTime < DateTime.Now.AddDays(-7))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            
            return (freed, deleted, errors);
        }
        
        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F1} {sizes[order]}";
        }
        
        private static string GetProgressBar(double percent)
        {
            var filled = (int)(percent / 10);
            var empty = 10 - filled;
            return "[" + new string('█', filled) + new string('░', empty) + "]";
        }
    }
}
