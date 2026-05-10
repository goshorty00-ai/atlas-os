using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Full system control - same capabilities as Kiro.
    /// File ops, directory listing, command execution, process management, etc.
    /// </summary>
    public static class PowerShellRunner
    {
        /// <summary>
        /// Try to handle system-related requests
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // === NVIDIA OVERLAY ===
            if ((lower.Contains("kill") || lower.Contains("stop") || lower.Contains("disable")) && 
                lower.Contains("nvidia") && lower.Contains("overlay"))
                return await KillNvidiaOverlayAsync();
            
            // === PROCESS LISTING ===
            if (lower == "what's running" || lower == "whats running" || 
                lower == "list processes" || lower == "show processes" ||
                lower == "running apps" || lower == "what apps are running" ||
                lower == "ps" || lower == "processes")
                return await ListRunningAppsAsync();
            
            // === KILL PROCESS ===
            if (lower.StartsWith("taskkill ") || lower.StartsWith("kill process "))
            {
                var processName = lower.Replace("taskkill ", "").Replace("kill process ", "").Trim();
                return await KillProcessByNameAsync(processName);
            }
            
            // === DIRECTORY LISTING ===
            if (lower.StartsWith("ls ") || lower.StartsWith("dir ") || lower.StartsWith("list "))
                return await ListDirectoryAsync(ExtractPath(input, new[] { "ls ", "dir ", "list " }));
            if (lower == "ls" || lower == "dir" || lower == "list files" || lower == "list directory")
                return await ListDirectoryAsync(".");
            
            // === READ FILE ===
            if (lower.StartsWith("read ") || lower.StartsWith("cat ") || lower.StartsWith("type ") ||
                lower.StartsWith("show file ") || lower.StartsWith("open file "))
                return await ReadFileAsync(ExtractPath(input, new[] { "read ", "cat ", "type ", "show file ", "open file " }));
            
            // === WRITE FILE ===
            if (lower.StartsWith("write ") || lower.StartsWith("create file ") || lower.StartsWith("save "))
                return await HandleWriteFileAsync(input);
            
            // === APPEND TO FILE ===
            if (lower.StartsWith("append ") || lower.StartsWith("add to "))
                return await HandleAppendFileAsync(input);
            
            // === DELETE FILE ===
            if (lower.StartsWith("delete file ") || lower.StartsWith("rm "))
                return await DeleteFileAsync(ExtractPath(input, new[] { "delete file ", "rm " }));
            
            // === CREATE DIRECTORY ===
            if (lower.StartsWith("mkdir ") || lower.StartsWith("create folder ") || lower.StartsWith("create directory "))
                return await CreateDirectoryAsync(ExtractPath(input, new[] { "mkdir ", "create folder ", "create directory " }));
            
            // === COPY FILE ===
            if (lower.StartsWith("cp ") || (lower.StartsWith("copy ") && lower.Contains(" to ")))
                return await HandleCopyAsync(input);
            
            // === MOVE FILE ===
            if (lower.StartsWith("mv ") || lower.StartsWith("rename ") || (lower.StartsWith("move ") && lower.Contains(" to ")))
                return await HandleMoveAsync(input);
            
            // === SEARCH FILES ===
            if (lower.StartsWith("find ") && lower.Contains(" in "))
                return await HandleSearchAsync(input);
            if (lower.StartsWith("grep ") || lower.StartsWith("search for "))
                return await HandleGrepAsync(input);
            
            // === SYSTEM INFO ===
            if (lower == "sysinfo" || lower == "system info" || lower == "systeminfo" || 
                lower == "pc info" || lower == "computer info")
                return await GetSystemInfoAsync();
            
            // === DISK SPACE ===
            if (lower == "disk space" || lower == "disk usage" || lower == "storage" || 
                lower == "free space" || lower == "df")
                return await GetDiskSpaceAsync();
            
            // === NETWORK INFO ===
            if (lower == "ip" || lower == "ipconfig" || lower == "my ip" || lower == "network info")
                return await GetNetworkInfoAsync();
            
            // === ENVIRONMENT VARIABLES ===
            if (lower.StartsWith("env ") || lower == "env" || lower == "environment")
                return await GetEnvironmentAsync(input);
            
            // === RUN COMMAND (explicit) ===
            if (lower.StartsWith("run ") || lower.StartsWith("exec ") || lower.StartsWith("execute "))
                return await RunCommandAsync(input.Substring(input.IndexOf(' ') + 1).Trim());
            
            // === POWERSHELL COMMAND (explicit) ===
            if (lower.StartsWith("powershell ") || lower.StartsWith("pwsh "))
                return await RunPowerShellAsync(input.Substring(input.IndexOf(' ') + 1).Trim());
            
            // === SERVICES ===
            if (lower == "services" || lower == "list services" || lower == "running services")
                return await ListServicesAsync();
            if (lower.StartsWith("start service "))
                return await ControlServiceAsync(input.Substring(14).Trim(), "start");
            if (lower.StartsWith("stop service "))
                return await ControlServiceAsync(input.Substring(13).Trim(), "stop");
            if (lower.StartsWith("restart service "))
                return await ControlServiceAsync(input.Substring(16).Trim(), "restart");
            
            // === INSTALLED PROGRAMS ===
            if (lower == "installed programs" || lower == "installed apps" || lower == "programs" ||
                lower == "list programs" || lower == "what's installed")
                return await ListInstalledProgramsAsync();
            
            // === STARTUP PROGRAMS ===
            if (lower == "startup programs" || lower == "startup apps" || lower == "startup")
                return await ListStartupProgramsAsync();
            
            // === SCHEDULED TASKS ===
            if (lower == "scheduled tasks" || lower == "tasks" || lower == "task scheduler")
                return await ListScheduledTasksAsync();
            
            // === PORTS ===
            if (lower == "ports" || lower == "listening ports" || lower == "netstat" || lower == "open ports")
                return await ListOpenPortsAsync();
            
            // === WIFI ===
            if (lower == "wifi" || lower == "wifi networks" || lower == "wireless networks")
                return await ListWifiNetworksAsync();
            if (lower.StartsWith("connect wifi ") || lower.StartsWith("connect to wifi "))
                return await ConnectWifiAsync(input.Substring(input.LastIndexOf(' ') + 1).Trim());
            
            // === BATTERY ===
            if (lower == "battery" || lower == "battery status" || lower == "power status")
                return await GetBatteryStatusAsync();
            
            // === UPTIME ===
            if (lower == "uptime" || lower == "how long running" || lower == "system uptime")
                return await GetUptimeAsync();
            
            // === CLIPBOARD ===
            if (lower == "clipboard" || lower == "paste" || lower == "what's copied")
                return GetClipboard();
            if (lower.StartsWith("copy ") && lower.Contains(" to clipboard"))
            {
                var text = Regex.Match(input, @"copy\s+(.+?)\s+to clipboard", RegexOptions.IgnoreCase).Groups[1].Value;
                return SetClipboard(text);
            }
            
            return null;
        }

        #region Helper Methods
        
        private static string ExtractPath(string input, string[] prefixes)
        {
            foreach (var prefix in prefixes)
                if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return input.Substring(prefix.Length).Trim().Trim('"', '\'');
            return input.Trim().Trim('"', '\'');
        }
        
        private static async Task<string> HandleWriteFileAsync(string input)
        {
            var match = Regex.Match(input, @"(?:write|save)\s+[""'](.+?)[""']\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return await WriteFileAsync(match.Groups[2].Value.Trim(), match.Groups[1].Value);
            match = Regex.Match(input, @"create file\s+(.+?)\s+with\s+[""'](.+)[""']", RegexOptions.IgnoreCase);
            if (match.Success)
                return await WriteFileAsync(match.Groups[1].Value.Trim(), match.Groups[2].Value);
            return "❌ Format: write \"content\" to filename.txt";
        }
        
        private static async Task<string> HandleAppendFileAsync(string input)
        {
            var match = Regex.Match(input, @"(?:append|add)\s+[""'](.+?)[""']\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return await AppendFileAsync(match.Groups[2].Value.Trim(), match.Groups[1].Value);
            return "❌ Format: append \"content\" to filename.txt";
        }
        
        private static async Task<string> HandleCopyAsync(string input)
        {
            var match = Regex.Match(input, @"(?:copy|cp)\s+(.+?)\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return await CopyFileAsync(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            return "❌ Format: copy source.txt to destination.txt";
        }
        
        private static async Task<string> HandleMoveAsync(string input)
        {
            var match = Regex.Match(input, @"(?:move|mv|rename)\s+(.+?)\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return await MoveFileAsync(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            return "❌ Format: move source.txt to destination.txt";
        }
        
        private static async Task<string> HandleSearchAsync(string input)
        {
            var match = Regex.Match(input, @"find\s+(.+?)\s+in\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return await FindFilesAsync(match.Groups[2].Value.Trim(), match.Groups[1].Value.Trim());
            return "❌ Format: find *.txt in C:\\folder";
        }
        
        private static async Task<string> HandleGrepAsync(string input)
        {
            var match = Regex.Match(input, @"(?:grep|search for)\s+[""'](.+?)[""']\s+in\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return await GrepAsync(match.Groups[2].Value.Trim(), match.Groups[1].Value);
            return "❌ Format: grep \"pattern\" in file.txt";
        }
        
        #endregion

        #region File Operations
        
        public static async Task<string> ListDirectoryAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    if (!Path.IsPathRooted(path)) path = Path.GetFullPath(path);
                    if (!Directory.Exists(path)) return $"❌ Directory not found: {path}";
                    
                    var sb = new StringBuilder();
                    sb.AppendLine($"📁 **{path}**\n");
                    
                    foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                        sb.AppendLine($"📂 {new DirectoryInfo(dir).Name}/");
                    foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                    {
                        var info = new FileInfo(file);
                        sb.AppendLine($"📄 {info.Name} ({FormatSize(info.Length)})");
                    }
                    
                    sb.AppendLine($"\n{Directory.GetDirectories(path).Length} folders, {Directory.GetFiles(path).Length} files");
                    return sb.ToString();
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> ReadFileAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    if (!File.Exists(path)) return $"❌ File not found: {path}";
                    var content = File.ReadAllText(path);
                    if (content.Length > 5000) content = content.Substring(0, 5000) + "\n\n... [truncated]";
                    return $"📄 **{Path.GetFileName(path)}**\n```\n{content}\n```";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> WriteFileAsync(string path, string content)
        {
            return await Task.Run(() =>
            {
                try
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(path, content);
                    return $"✓ Written to {path}";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> AppendFileAsync(string path, string content)
        {
            return await Task.Run(() =>
            {
                try
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    File.AppendAllText(path, content + Environment.NewLine);
                    return $"✓ Appended to {path}";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> DeleteFileAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    if (File.Exists(path)) { File.Delete(path); return $"✓ Deleted: {path}"; }
                    if (Directory.Exists(path)) { Directory.Delete(path, true); return $"✓ Deleted folder: {path}"; }
                    return $"❌ Not found: {path}";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> CreateDirectoryAsync(string path)
        {
            return await Task.Run(() =>
            {
                try { Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(path)); return $"✓ Created: {path}"; }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> CopyFileAsync(string source, string dest)
        {
            return await Task.Run(() =>
            {
                try
                {
                    source = Environment.ExpandEnvironmentVariables(source);
                    dest = Environment.ExpandEnvironmentVariables(dest);
                    if (File.Exists(source)) { File.Copy(source, dest, true); return $"✓ Copied {source} → {dest}"; }
                    if (Directory.Exists(source)) { CopyDir(source, dest); return $"✓ Copied folder {source} → {dest}"; }
                    return $"❌ Not found: {source}";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
        
        public static async Task<string> MoveFileAsync(string source, string dest)
        {
            return await Task.Run(() =>
            {
                try
                {
                    source = Environment.ExpandEnvironmentVariables(source);
                    dest = Environment.ExpandEnvironmentVariables(dest);
                    if (File.Exists(source)) { File.Move(source, dest, true); return $"✓ Moved {source} → {dest}"; }
                    if (Directory.Exists(source)) { Directory.Move(source, dest); return $"✓ Moved folder {source} → {dest}"; }
                    return $"❌ Not found: {source}";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> FindFilesAsync(string directory, string pattern)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(Environment.ExpandEnvironmentVariables(directory), pattern, SearchOption.AllDirectories).Take(50).ToList();
                    if (!files.Any()) return $"❌ No files matching '{pattern}'";
                    var sb = new StringBuilder($"🔍 Found {files.Count} files:\n");
                    foreach (var f in files) sb.AppendLine($"  {f}");
                    return sb.ToString();
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> GrepAsync(string path, string pattern)
        {
            return await Task.Run(() =>
            {
                try
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    var sb = new StringBuilder(); var matches = 0;
                    var files = File.Exists(path) ? new[] { path } : Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(f => !IsBinary(f)).Take(100);
                    foreach (var file in files)
                    {
                        try
                        {
                            var lines = File.ReadAllLines(file);
                            for (int i = 0; i < lines.Length && matches < 50; i++)
                                if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                { matches++; sb.AppendLine($"{file}:{i + 1}: {lines[i].Trim()}"); }
                        } catch { }
                    }
                    return matches == 0 ? $"❌ No matches for '{pattern}'" : $"🔍 Found {matches} matches:\n```\n{sb}\n```";
                }
                catch (Exception ex) { return $"❌ Error: {ex.Message}"; }
            });
        }
        
        private static bool IsBinary(string p) => new[] { ".exe", ".dll", ".bin", ".zip", ".png", ".jpg", ".mp3", ".mp4", ".pdf" }.Contains(Path.GetExtension(p).ToLower());
        private static string FormatSize(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:F1} KB" : b < 1073741824 ? $"{b / 1048576.0:F1} MB" : $"{b / 1073741824.0:F1} GB";
        
        #endregion

        #region Process Management
        
        public static async Task<string> KillNvidiaOverlayAsync()
        {
            var sb = new StringBuilder("🎯 Killing NVIDIA Overlay...\n");
            int killed = 0;
            foreach (var proc in new[] { "NVIDIA Overlay", "nvcontainer", "NVIDIA Share", "nvsphelper64" })
            {
                var result = await RunCommandAsync($"taskkill /F /IM \"{proc}.exe\"");
                if (result.Contains("SUCCESS")) { killed++; sb.AppendLine($"✓ Killed {proc}"); }
            }
            sb.AppendLine(killed > 0 ? $"\n✅ Killed {killed} process(es)\n💡 Permanently disable: NVIDIA App → System → Statistics Overlay" : "❌ No NVIDIA overlay running");
            return sb.ToString();
        }
        
        public static async Task<string> ListRunningAppsAsync()
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder("📋 **Running Applications:**\n\n");
                foreach (var p in Process.GetProcesses().Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)).OrderBy(p => p.ProcessName))
                    try { sb.AppendLine($"• {p.ProcessName} - {p.MainWindowTitle} ({p.WorkingSet64 / 1048576} MB)"); } catch { }
                return sb.ToString();
            });
        }
        
        public static async Task<string> KillProcessByNameAsync(string name)
        {
            return await Task.Run(() =>
            {
                var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
                if (procs.Length == 0) return $"❌ {name} not running";
                int killed = 0;
                foreach (var p in procs) try { p.Kill(); killed++; } catch { }
                return killed > 0 ? $"✓ Killed {killed} {name} process(es)" : $"❌ Couldn't kill {name}";
            });
        }
        
        #endregion
        
        #region System Info
        
        public static async Task<string> GetSystemInfoAsync()
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder("💻 **System Information:**\n\n");
                sb.AppendLine($"🖥️ Computer: {Environment.MachineName}");
                sb.AppendLine($"👤 User: {Environment.UserName}");
                sb.AppendLine($"🪟 OS: {Environment.OSVersion}");
                sb.AppendLine($"🔧 .NET: {Environment.Version}");
                sb.AppendLine($"💾 RAM: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0:F1} GB");
                sb.AppendLine($"🧮 Processors: {Environment.ProcessorCount}");
                sb.AppendLine($"📁 System: {Environment.SystemDirectory}");
                return sb.ToString();
            });
        }
        
        public static async Task<string> GetDiskSpaceAsync()
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder("💾 **Disk Space:**\n\n");
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                    sb.AppendLine($"📀 {d.Name} - {FormatSize(d.AvailableFreeSpace)} free / {FormatSize(d.TotalSize)} ({d.DriveFormat})");
                return sb.ToString();
            });
        }
        
        public static async Task<string> GetNetworkInfoAsync()
        {
            var result = await RunCommandAsync("ipconfig");
            return $"🌐 **Network Info:**\n```\n{result}\n```";
        }
        
        public static async Task<string> GetEnvironmentAsync(string input)
        {
            return await Task.Run(() =>
            {
                var lower = input.ToLowerInvariant();
                if (lower.StartsWith("env "))
                {
                    var name = input.Substring(4).Trim();
                    var val = Environment.GetEnvironmentVariable(name);
                    return val != null ? $"🔧 {name} = {val}" : $"❌ Variable '{name}' not found";
                }
                var sb = new StringBuilder("🔧 **Environment Variables:**\n\n");
                foreach (var key in new[] { "PATH", "TEMP", "USERNAME", "COMPUTERNAME", "OS", "PROCESSOR_ARCHITECTURE" })
                    sb.AppendLine($"• {key} = {Environment.GetEnvironmentVariable(key)}");
                return sb.ToString();
            });
        }
        
        public static async Task<string> GetUptimeAsync()
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return await Task.FromResult($"⏱️ System uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");
        }
        
        public static async Task<string> GetBatteryStatusAsync()
        {
            var result = await RunPowerShellAsync("(Get-WmiObject Win32_Battery | Select-Object EstimatedChargeRemaining, BatteryStatus).EstimatedChargeRemaining");
            return result.Contains("Error") ? "🔌 No battery (desktop)" : $"🔋 Battery: {result.Trim()}%";
        }
        
        #endregion

        #region Services & Programs
        
        public static async Task<string> ListServicesAsync()
        {
            var result = await RunPowerShellAsync("Get-Service | Where-Object {$_.Status -eq 'Running'} | Select-Object -First 30 Name, DisplayName | Format-Table -AutoSize");
            return $"⚙️ **Running Services (top 30):**\n```\n{result}\n```";
        }
        
        public static async Task<string> ControlServiceAsync(string name, string action)
        {
            var cmd = action switch { "start" => "Start-Service", "stop" => "Stop-Service", "restart" => "Restart-Service", _ => "" };
            var result = await RunPowerShellAsync($"{cmd} -Name '{name}' -ErrorAction SilentlyContinue; if($?) {{ 'Success' }} else {{ 'Failed' }}");
            return result.Contains("Success") ? $"✓ {action}ed service: {name}" : $"❌ Failed to {action} {name}";
        }
        
        public static async Task<string> ListInstalledProgramsAsync()
        {
            var result = await RunPowerShellAsync("Get-ItemProperty HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\* | Select-Object -First 30 DisplayName, DisplayVersion | Where-Object {$_.DisplayName} | Format-Table -AutoSize");
            return $"📦 **Installed Programs (top 30):**\n```\n{result}\n```";
        }
        
        public static async Task<string> ListStartupProgramsAsync()
        {
            var result = await RunPowerShellAsync("Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location | Format-Table -AutoSize");
            return $"🚀 **Startup Programs:**\n```\n{result}\n```";
        }
        
        public static async Task<string> ListScheduledTasksAsync()
        {
            var result = await RunPowerShellAsync("Get-ScheduledTask | Where-Object {$_.State -eq 'Ready'} | Select-Object -First 20 TaskName, State | Format-Table -AutoSize");
            return $"📅 **Scheduled Tasks (top 20):**\n```\n{result}\n```";
        }
        
        #endregion
        
        #region Network
        
        public static async Task<string> ListOpenPortsAsync()
        {
            var result = await RunCommandAsync("netstat -an | findstr LISTENING");
            return $"🔌 **Listening Ports:**\n```\n{result}\n```";
        }
        
        public static async Task<string> ListWifiNetworksAsync()
        {
            var result = await RunCommandAsync("netsh wlan show networks mode=bssid");
            return $"📶 **WiFi Networks:**\n```\n{result}\n```";
        }
        
        public static async Task<string> ConnectWifiAsync(string ssid)
        {
            var result = await RunCommandAsync($"netsh wlan connect name=\"{ssid}\"");
            return result.Contains("successfully") ? $"✓ Connected to {ssid}" : $"❌ Failed to connect: {result}";
        }
        
        #endregion
        
        #region Clipboard
        
        public static string GetClipboard()
        {
            try
            {
                string? text = null;
                var thread = new System.Threading.Thread(() => { try { text = Clipboard.GetText(); } catch { } });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join(1000);
                return string.IsNullOrEmpty(text) ? "📋 Clipboard is empty" : $"📋 **Clipboard:**\n```\n{text}\n```";
            }
            catch { return "❌ Couldn't read clipboard"; }
        }
        
        public static string SetClipboard(string text)
        {
            try
            {
                var thread = new System.Threading.Thread(() => { try { Clipboard.SetText(text); } catch { } });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join(1000);
                return $"✓ Copied to clipboard";
            }
            catch { return "❌ Couldn't set clipboard"; }
        }
        
        #endregion

        #region Command Execution
        
        public static async Task<string> RunPowerShellAsync(string command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(psi);
                    if (process == null) return "Failed to start PowerShell";
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    return string.IsNullOrEmpty(error) ? output.Trim() : $"{output}\nError: {error}".Trim();
                }
                catch (Exception ex) { return $"Error: {ex.Message}"; }
            });
        }
        
        public static async Task<string> RunCommandAsync(string command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(psi);
                    if (process == null) return "Failed to start command";
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    return $"{output}{error}".Trim();
                }
                catch (Exception ex) { return $"Error: {ex.Message}"; }
            });
        }
        
        #endregion
    }
}
