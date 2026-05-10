using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick Commands - Instant system info and utilities.
    /// "What's my IP?" "How much RAM?" "Battery status?"
    /// </summary>
    public static class QuickCommands
    {
        /// <summary>
        /// Try to handle a quick info command
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // IP Address
            if (lower.Contains("my ip") || lower.Contains("ip address"))
                return await GetIPAddressAsync();
            
            // RAM/Memory - use word boundaries to avoid false matches like "middlesbrough" containing "ram"
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\bram\b") || 
                lower.Contains("memory usage") || 
                lower.Contains("how much memory"))
                return GetMemoryInfo();
            
            // CPU
            if (lower.Contains("cpu") || lower.Contains("processor"))
                return await GetCPUInfoAsync();
            
            // Disk space
            if (lower.Contains("disk") || lower.Contains("storage") || lower.Contains("drive space") || lower.Contains("hard drive"))
                return GetDiskInfo();
            
            // Battery
            if (lower.Contains("battery"))
                return GetBatteryInfo();
            
            // Network
            if (lower.Contains("network") || lower.Contains("internet") || lower.Contains("wifi") || lower.Contains("connected"))
                return await GetNetworkInfoAsync();
            
            // Uptime
            if (lower.Contains("uptime") || lower.Contains("how long") && lower.Contains("running"))
                return GetUptimeInfo();
            
            // System info
            if (lower == "system info" || lower == "pc info" || lower == "computer info" || lower.Contains("specs"))
                return GetSystemInfo();
            
            // Running processes
            if (lower.Contains("running") || lower.Contains("processes") || lower.Contains("what's open"))
                return GetRunningProcesses();
            
            // Current user
            if (lower.Contains("who am i") || lower.Contains("current user") || lower.Contains("username"))
                return $"👤 You are: **{Environment.UserName}** on **{Environment.MachineName}**";
            
            // OS info
            if (lower.Contains("windows version") || lower.Contains("os version") || lower.Contains("what windows"))
                return $"💻 {Environment.OSVersion.VersionString}";
            
            // Screen resolution
            if (lower.Contains("resolution") || lower.Contains("screen size"))
                return GetScreenInfo();
            
            // Clipboard
            if (lower == "clipboard" || lower == "what's in clipboard" || lower == "show clipboard")
                return GetClipboardContent();
            
            return null;
        }
        
        private static async Task<string> GetIPAddressAsync()
        {
            try
            {
                // Local IP
                var localIP = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                    .AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                    .ToString() ?? "Unknown";
                
                // Public IP (simple method)
                string publicIP = "Unknown";
                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(5);
                    publicIP = await client.GetStringAsync("https://api.ipify.org");
                }
                catch { }
                
                return $"🌐 **IP Addresses:**\n" +
                       $"Local: {localIP}\n" +
                       $"Public: {publicIP}";
            }
            catch (Exception ex)
            {
                return $"❌ Couldn't get IP: {ex.Message}";
            }
        }
        
        private static string GetMemoryInfo()
        {
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                var totalMemory = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
                
                // Get actual system memory using performance counter
                var availableMemory = new PerformanceCounter("Memory", "Available MBytes").NextValue() / 1024;
                var usedMemory = totalMemory - availableMemory;
                var usagePercent = (usedMemory / totalMemory) * 100;
                
                var bar = new string('█', (int)(usagePercent / 10)) + new string('░', 10 - (int)(usagePercent / 10));
                
                return $"🧠 **Memory Usage:**\n" +
                       $"Used: {usedMemory:F1} GB / {totalMemory:F1} GB\n" +
                       $"[{bar}] {usagePercent:F0}%\n" +
                       $"Available: {availableMemory:F1} GB";
            }
            catch
            {
                return "❌ Couldn't get memory info";
            }
        }
        
        private static async Task<string> GetCPUInfoAsync()
        {
            try
            {
                var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue(); // First call returns 0
                await Task.Delay(500);
                var cpuUsage = cpuCounter.NextValue();
                
                var cpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown";
                var cores = Environment.ProcessorCount;
                
                var bar = new string('█', (int)(cpuUsage / 10)) + new string('░', 10 - (int)(cpuUsage / 10));
                
                return $"⚡ **CPU Info:**\n" +
                       $"Processor: {cpuName}\n" +
                       $"Cores: {cores}\n" +
                       $"Usage: [{bar}] {cpuUsage:F0}%";
            }
            catch
            {
                return $"⚡ CPU: {Environment.ProcessorCount} cores";
            }
        }
        
        private static string GetDiskInfo()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("💾 **Disk Space:**\n");
                
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    var total = drive.TotalSize / (1024.0 * 1024 * 1024);
                    var free = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    var used = total - free;
                    var usagePercent = (used / total) * 100;
                    
                    var bar = new string('█', (int)(usagePercent / 10)) + new string('░', 10 - (int)(usagePercent / 10));
                    
                    sb.AppendLine($"**{drive.Name}** [{bar}] {usagePercent:F0}%");
                    sb.AppendLine($"  {free:F0} GB free of {total:F0} GB\n");
                }
                
                return sb.ToString();
            }
            catch
            {
                return "❌ Couldn't get disk info";
            }
        }
        
        private static string GetBatteryInfo()
        {
            try
            {
                var battery = System.Windows.Forms.SystemInformation.PowerStatus;
                var percent = battery.BatteryLifePercent * 100;
                var status = battery.PowerLineStatus;
                var remaining = battery.BatteryLifeRemaining;
                
                var icon = percent > 80 ? "🔋" : percent > 40 ? "🔋" : percent > 20 ? "🪫" : "⚠️";
                var bar = new string('█', (int)(percent / 10)) + new string('░', 10 - (int)(percent / 10));
                
                var result = $"{icon} **Battery:**\n" +
                             $"[{bar}] {percent:F0}%\n" +
                             $"Status: {(status == System.Windows.Forms.PowerLineStatus.Online ? "Charging ⚡" : "On Battery")}";
                
                if (remaining > 0 && status != System.Windows.Forms.PowerLineStatus.Online)
                {
                    var hours = remaining / 3600;
                    var minutes = (remaining % 3600) / 60;
                    result += $"\nRemaining: {hours}h {minutes}m";
                }
                
                return result;
            }
            catch
            {
                return "🔌 No battery detected (desktop PC)";
            }
        }
        
        private static async Task<string> GetNetworkInfoAsync()
        {
            try
            {
                var isConnected = NetworkInterface.GetIsNetworkAvailable();
                
                if (!isConnected)
                    return "❌ **No network connection**";
                
                // Ping test
                var ping = new Ping();
                var pingResult = await ping.SendPingAsync("8.8.8.8", 3000);
                var latency = pingResult.Status == IPStatus.Success ? $"{pingResult.RoundtripTime}ms" : "Failed";
                
                // Get active adapter
                var activeAdapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                
                var adapterName = activeAdapter?.Name ?? "Unknown";
                var adapterType = activeAdapter?.NetworkInterfaceType.ToString() ?? "Unknown";
                
                return $"🌐 **Network Status:**\n" +
                       $"Connected: ✓\n" +
                       $"Adapter: {adapterName} ({adapterType})\n" +
                       $"Ping (Google): {latency}";
            }
            catch
            {
                return "❌ Couldn't get network info";
            }
        }
        
        private static string GetUptimeInfo()
        {
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                return $"⏱️ **System Uptime:**\n" +
                       $"{uptime.Days} days, {uptime.Hours} hours, {uptime.Minutes} minutes";
            }
            catch
            {
                return "❌ Couldn't get uptime";
            }
        }
        
        private static string GetSystemInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("💻 **System Information:**\n");
            sb.AppendLine($"Computer: {Environment.MachineName}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
            sb.AppendLine($"64-bit: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Processors: {Environment.ProcessorCount} cores");
            sb.AppendLine($".NET: {Environment.Version}");
            
            return sb.ToString();
        }
        
        private static string GetRunningProcesses()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                    .OrderByDescending(p => p.WorkingSet64)
                    .Take(10)
                    .ToList();
                
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"📋 **Running Apps ({processes.Count}):**\n");
                
                foreach (var p in processes)
                {
                    var memory = p.WorkingSet64 / (1024.0 * 1024);
                    var title = p.MainWindowTitle.Length > 40 
                        ? p.MainWindowTitle.Substring(0, 37) + "..." 
                        : p.MainWindowTitle;
                    sb.AppendLine($"• {title} ({memory:F0} MB)");
                }
                
                return sb.ToString();
            }
            catch
            {
                return "❌ Couldn't get process list";
            }
        }
        
        private static string GetScreenInfo()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🖥️ **Display Info ({screens.Length} monitor(s)):**\n");
                
                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];
                    var primary = screen.Primary ? " (Primary)" : "";
                    sb.AppendLine($"Monitor {i + 1}{primary}: {screen.Bounds.Width}x{screen.Bounds.Height}");
                }
                
                return sb.ToString();
            }
            catch
            {
                return "❌ Couldn't get screen info";
            }
        }
        
        private static string GetClipboardContent()
        {
            try
            {
                string? text = null;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (System.Windows.Clipboard.ContainsText())
                        text = System.Windows.Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Clipboard is empty";
                
                var preview = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
                return $"📋 **Clipboard ({text.Length} chars):**\n```\n{preview}\n```";
            }
            catch
            {
                return "❌ Couldn't read clipboard";
            }
        }
    }
}
