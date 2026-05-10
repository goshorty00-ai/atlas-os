using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Process Manager - List, kill, and manage running processes.
    /// </summary>
    public static class ProcessManager
    {
        /// <summary>
        /// Handle process management commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // List processes by memory
            if (lower.Contains("top process") || lower.Contains("memory hog") || 
                lower.Contains("what's using memory") || lower.Contains("high memory"))
            {
                return GetTopProcessesByMemory();
            }
            
            // List processes by CPU
            if (lower.Contains("cpu hog") || lower.Contains("what's using cpu") || lower.Contains("high cpu"))
            {
                return await GetTopProcessesByCpuAsync();
            }
            
            // List all processes
            if (lower == "list processes" || lower == "show processes" || lower == "running processes")
            {
                return GetRunningProcesses();
            }
            
            // Find process
            if (lower.StartsWith("find process ") || lower.StartsWith("search process "))
            {
                var query = input.Substring(input.IndexOf(' ') + 1).Trim();
                return FindProcess(query);
            }
            
            // Kill process by name
            if ((lower.StartsWith("kill process ") || lower.StartsWith("end process ")) && !lower.Contains("id"))
            {
                var name = input.Substring(input.IndexOf(' ') + 1).Trim();
                return await KillProcessByNameAsync(name);
            }
            
            // Kill process by ID
            if (lower.Contains("kill") && lower.Contains("pid"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"pid\s*(\d+)");
                if (match.Success)
                {
                    var pid = int.Parse(match.Groups[1].Value);
                    return await KillProcessByIdAsync(pid);
                }
            }
            
            // Process count
            if (lower.Contains("how many process") || lower.Contains("process count"))
            {
                var count = Process.GetProcesses().Length;
                return $"📊 **Running Processes:** {count}";
            }
            
            return null;
        }
        
        private static string GetTopProcessesByMemory()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🧠 **Top Processes by Memory:**\n");
            
            var processes = Process.GetProcesses()
                .Where(p => { try { return p.WorkingSet64 > 0; } catch { return false; } })
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                .Take(15)
                .ToList();
            
            long totalMemory = 0;
            
            foreach (var p in processes)
            {
                try
                {
                    var memory = p.WorkingSet64;
                    totalMemory += memory;
                    var name = p.ProcessName.Length > 25 ? p.ProcessName.Substring(0, 22) + "..." : p.ProcessName;
                    sb.AppendLine($"• **{FormatSize(memory)}** - {name} (PID: {p.Id})");
                }
                catch { }
            }
            
            sb.AppendLine($"\n**Top 15 Total:** {FormatSize(totalMemory)}");
            
            return sb.ToString();
        }
        
        private static async Task<string> GetTopProcessesByCpuAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("⚡ **Top Processes by CPU:**\n");
            sb.AppendLine("_Measuring CPU usage (2 seconds)..._\n");
            
            // Get initial CPU times
            var processes = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new { Process = p, StartCpu = p.TotalProcessorTime };
                    }
                    catch { return null; }
                })
                .Where(x => x != null)
                .ToList();
            
            await Task.Delay(2000);
            
            // Calculate CPU usage
            var cpuUsage = processes
                .Select(x =>
                {
                    try
                    {
                        var endCpu = x!.Process.TotalProcessorTime;
                        var cpuUsed = (endCpu - x.StartCpu).TotalMilliseconds;
                        var cpuPercent = cpuUsed / 2000.0 * 100 / Environment.ProcessorCount;
                        return new { x.Process, CpuPercent = cpuPercent };
                    }
                    catch { return null; }
                })
                .Where(x => x != null && x.CpuPercent > 0.1)
                .OrderByDescending(x => x!.CpuPercent)
                .Take(10)
                .ToList();
            
            sb.Clear();
            sb.AppendLine("⚡ **Top Processes by CPU:**\n");
            
            foreach (var item in cpuUsage)
            {
                try
                {
                    var name = item!.Process.ProcessName.Length > 25 
                        ? item.Process.ProcessName.Substring(0, 22) + "..." 
                        : item.Process.ProcessName;
                    sb.AppendLine($"• **{item.CpuPercent:F1}%** - {name} (PID: {item.Process.Id})");
                }
                catch { }
            }
            
            if (!cpuUsage.Any())
                sb.AppendLine("No significant CPU usage detected.");
            
            return sb.ToString();
        }
        
        private static string GetRunningProcesses()
        {
            var sb = new StringBuilder();
            sb.AppendLine("📋 **Running Processes:**\n");
            
            var processes = Process.GetProcesses()
                .OrderBy(p => p.ProcessName)
                .GroupBy(p => p.ProcessName)
                .Take(30)
                .ToList();
            
            foreach (var group in processes)
            {
                var count = group.Count();
                var totalMem = group.Sum(p => { try { return p.WorkingSet64; } catch { return 0L; } });
                var countStr = count > 1 ? $" ({count})" : "";
                sb.AppendLine($"• {group.Key}{countStr} - {FormatSize(totalMem)}");
            }
            
            sb.AppendLine($"\n**Total:** {Process.GetProcesses().Length} processes");
            
            return sb.ToString();
        }
        
        private static string FindProcess(string query)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🔍 **Processes matching '{query}':**\n");
            
            var matches = Process.GetProcesses()
                .Where(p => p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.ProcessName)
                .Take(20)
                .ToList();
            
            if (!matches.Any())
            {
                sb.AppendLine("No matching processes found.");
            }
            else
            {
                foreach (var p in matches)
                {
                    try
                    {
                        sb.AppendLine($"• **{p.ProcessName}** (PID: {p.Id}) - {FormatSize(p.WorkingSet64)}");
                    }
                    catch { }
                }
            }
            
            return sb.ToString();
        }
        
        private static async Task<string> KillProcessByNameAsync(string name)
        {
            var processes = Process.GetProcessesByName(name);
            
            if (!processes.Any())
            {
                // Try partial match
                processes = Process.GetProcesses()
                    .Where(p => p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            
            if (!processes.Any())
                return $"❌ No process found matching '{name}'";
            
            if (processes.Length > 5)
                return $"⚠️ Found {processes.Length} matching processes. Be more specific or use PID.";
            
            var killed = 0;
            var errors = 0;
            
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    await p.WaitForExitAsync();
                    killed++;
                }
                catch
                {
                    errors++;
                }
            }
            
            if (killed > 0 && errors == 0)
                return $"✅ Killed {killed} process(es): {name}";
            if (killed > 0)
                return $"⚠️ Killed {killed}, failed {errors} (may need admin rights)";
            
            return $"❌ Couldn't kill process (may need admin rights)";
        }
        
        private static async Task<string> KillProcessByIdAsync(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                var name = process.ProcessName;
                process.Kill();
                await process.WaitForExitAsync();
                return $"✅ Killed process: {name} (PID: {pid})";
            }
            catch (ArgumentException)
            {
                return $"❌ No process with PID {pid}";
            }
            catch (Exception ex)
            {
                return $"❌ Couldn't kill process: {ex.Message}";
            }
        }
        
        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F1} {sizes[order]}";
        }
    }
}
