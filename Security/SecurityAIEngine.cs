using AtlasAI.AI;
using AtlasAI.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AtlasAI.Security
{
    /// <summary>
    /// Handles AI chat commands from the security dashboard.
    /// Routes natural language queries to the LLM with real system context injected.
    /// Also handles direct action commands: kill_process, scan_system, optimize_memory, etc.
    /// </summary>
    public static class SecurityAIEngine
    {
        private static readonly List<(string role, string content)> _history = new();
        private static readonly object _historyLock = new();

        private const string SystemPrompt = @"You are Atlas Guardian, an AI security and system management assistant embedded in a Windows desktop application.
You have access to real-time system telemetry. Be concise, technical, and actionable.
When asked about system state, use the provided context. Never make up data.
Format responses in plain text — no markdown headers, no bullet asterisks. Use short paragraphs.
If asked to perform an action (kill process, scan, optimize), confirm what you will do and report results.";

        /// <summary>
        /// Process a chat message from the UI. Returns the AI response as a JSON string ready for PostWebMessageAsJson.
        /// </summary>
        public static async Task<string> ProcessChatAsync(string userMessage)
        {
            try
            {
                Debug.WriteLine($"[SecurityAI] Processing message: {userMessage}");

                // Check for direct action commands first
                var actionResult = await TryHandleActionCommandAsync(userMessage);
                if (actionResult != null)
                {
                    Debug.WriteLine($"[SecurityAI] Action command handled");
                    return BuildChatResponse(actionResult);
                }

                Debug.WriteLine($"[SecurityAI] Sending to AI backend");

                // Build context-enriched prompt
                var context = BuildSystemContext();
                var fullMessage = string.IsNullOrWhiteSpace(context)
                    ? userMessage
                    : $"[System Context]\n{context}\n\n[User Query]\n{userMessage}";

                // Build message list for SendMessageAsync
                var messages = new List<object>
                {
                    new { role = "system", content = SystemPrompt }
                };

                lock (_historyLock)
                {
                    foreach (var (role, content) in _history.TakeLast(16))
                        messages.Add(new { role, content });
                }

                messages.Add(new { role = "user", content = fullMessage });

                var aiResponse = await AIManager.SendMessageAsync("security_chat", messages, 600);
                var response = aiResponse?.Success == true ? aiResponse.Content : null;

                if (string.IsNullOrWhiteSpace(response))
                {
                    if (aiResponse != null && !string.IsNullOrEmpty(aiResponse.Error))
                        response = $"AI Error: {aiResponse.Error}\n\nPlease configure your AI API key in Settings.";
                    else
                        response = "I'm having trouble connecting to the AI backend. Please check:\n\n1. Your API key is configured in Settings\n2. You have an active internet connection\n3. Your API provider (Claude/OpenAI/Gemini) is accessible";
                }

                // Store in history
                lock (_historyLock)
                {
                    _history.Add(("user", userMessage));
                    _history.Add(("assistant", response));
                    if (_history.Count > 40) _history.RemoveRange(0, 2);
                }

                Debug.WriteLine($"[SecurityAI] Response ready");
                return BuildChatResponse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityAI] Error: {ex.Message}");
                return BuildChatResponse($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles structured action commands sent from the UI buttons/commands.
        /// Returns null if not an action command.
        /// </summary>
        private static async Task<string?> TryHandleActionCommandAsync(string message)
        {
            var lower = message.ToLowerInvariant().Trim();

            try
            {
                // Try command router first
                var commandRouter = CommandRouter.Instance;
                var registeredCommands = commandRouter.GetRegisteredCommands().ToList();

                // Check if message matches any registered command
                foreach (var cmd in registeredCommands)
                {
                    if (lower.StartsWith(cmd) || lower.Contains(cmd.Replace("_", " ")))
                    {
                        Debug.WriteLine($"[SecurityAI] Executing command: {cmd}");
                        var result = await commandRouter.ParseAndExecuteAsync(message);
                        Debug.WriteLine($"[SecurityAI] Command result: {result.Status}");
                        return BuildCommandResponse(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecurityAI] Command router error: {ex.Message}");
                return $"Command execution failed: {ex.Message}";
            }

            // Legacy command handling for backward compatibility
            if (lower.StartsWith("kill_process ") || lower.Contains("kill process"))
            {
                var name = lower.Replace("kill_process", "").Replace("kill process", "").Trim();
                return await KillProcessAsync(name);
            }

            if (lower is "scan_system" or "scan my pc" or "scan system" or "jarvis scan my pc" || lower.Contains("scan my system"))
                return await RunSystemScanAsync();

            if (lower is "optimize_memory" or "fix performance" or "jarvis fix performance" or "optimize memory")
                return await OptimizeMemoryAsync();

            if (lower is "scan_downloads" or "scan my downloads" or "jarvis scan my downloads")
                return await ScanDownloadsAsync();

            if (lower.Contains("suspicious activity") || lower.Contains("show suspicious"))
                return GetSuspiciousActivityReport();

            if (lower.Contains("why is my pc slow") || lower.Contains("what's using my ram") || lower.Contains("whats using my ram"))
                return GetPerformanceReport();

            return null;
        }

        private static string BuildCommandResponse(CommandResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine(result.Message);

            if (result.Data != null && result.Data.Any())
            {
                sb.AppendLine();
                foreach (var (key, value) in result.Data)
                {
                    if (value is List<object> list)
                    {
                        sb.AppendLine($"{key}: {list.Count} items");
                    }
                    else if (value is Dictionary<string, object> dict)
                    {
                        sb.AppendLine($"{key}:");
                        foreach (var (k, v) in dict)
                        {
                            sb.AppendLine($"  {k}: {v}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"{key}: {value}");
                    }
                }
            }

            if (result.Status == "error" && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                sb.AppendLine();
                sb.AppendLine($"Error: {result.ErrorMessage}");
            }

            return sb.ToString().Trim();
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private static Task<string> KillProcessAsync(string processName)
        {
            return Task.Run(() =>
            {
                try
                {
                    processName = processName.Replace(".exe", "").Trim();
                    if (string.IsNullOrWhiteSpace(processName))
                        return "Please specify a process name. Example: kill process chrome";

                    var procs = Process.GetProcessesByName(processName);
                    if (procs.Length == 0)
                        return $"No process named '{processName}' found.";

                    foreach (var p in procs)
                    {
                        try { p.Kill(); } catch { }
                    }
                    return $"Terminated {procs.Length} instance(s) of {processName}.exe.";
                }
                catch (Exception ex)
                {
                    return $"Failed to kill process: {ex.Message}";
                }
            });
        }

        private static Task<string> RunSystemScanAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var sb = new StringBuilder();
                    var processes = Process.GetProcesses();
                    sb.AppendLine($"System scan complete. {processes.Length} processes running.");

                    var highMem = processes
                        .Select(p => { try { return (p.ProcessName, Mb: p.WorkingSet64 / 1024 / 1024); } catch { return (p.ProcessName, Mb: 0L); } })
                        .Where(x => x.Mb > 300)
                        .OrderByDescending(x => x.Mb)
                        .Take(5)
                        .ToList();

                    if (highMem.Any())
                    {
                        sb.AppendLine($"High memory processes: {string.Join(", ", highMem.Select(x => $"{x.ProcessName} ({x.Mb}MB)"))}.");
                    }

                    var snap = SecurityTelemetryService.Instance.GetSnapshot();
                    sb.AppendLine($"CPU: {snap.CpuPercent}%  RAM: {snap.RamPercent}%  Suspicious flags: {snap.SuspiciousFlagged}.");
                    sb.AppendLine(snap.SuspiciousFlagged == 0 ? "No threats detected. System is clean." : $"Review {snap.SuspiciousFlagged} flagged item(s) in the activity feed.");

                    return sb.ToString().Trim();
                }
                catch (Exception ex)
                {
                    return $"Scan error: {ex.Message}";
                }
            });
        }

        private static Task<string> OptimizeMemoryAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    // Force GC
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    // Clear temp files
                    var tempPath = Path.GetTempPath();
                    var deleted = 0;
                    try
                    {
                        foreach (var f in Directory.GetFiles(tempPath, "*.tmp"))
                        {
                            try { File.Delete(f); deleted++; } catch { }
                        }
                    }
                    catch { }

                    var snap = SecurityTelemetryService.Instance.GetSnapshot();
                    return $"Memory optimization complete. Cleared {deleted} temp files. Current RAM usage: {snap.RamPercent:F1}% ({snap.RamUsedMb}MB / {snap.RamTotalMb}MB).";
                }
                catch (Exception ex)
                {
                    return $"Optimization error: {ex.Message}";
                }
            });
        }

        private static Task<string> ScanDownloadsAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (!Directory.Exists(downloadsPath))
                        return "Downloads folder not found.";

                    var files = Directory.GetFiles(downloadsPath, "*.*", SearchOption.TopDirectoryOnly);
                    var executableExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".scr" };
                    var executables = files.Where(f => executableExts.Contains(Path.GetExtension(f))).ToList();
                    var recent = files
                        .Select(f => { try { return (f, info: new FileInfo(f)); } catch { return (f, info: (FileInfo?)null); } })
                        .Where(x => x.info != null && x.info.LastWriteTime > DateTime.Now.AddDays(-7))
                        .ToList();

                    var sb = new StringBuilder();
                    sb.AppendLine($"Downloads scan: {files.Length} files total, {recent.Count} modified in last 7 days.");
                    if (executables.Any())
                        sb.AppendLine($"Executables found ({executables.Count}): {string.Join(", ", executables.Select(Path.GetFileName).Take(5))}. Review these manually.");
                    else
                        sb.AppendLine("No executable files found. Downloads appear clean.");

                    return sb.ToString().Trim();
                }
                catch (Exception ex)
                {
                    return $"Scan error: {ex.Message}";
                }
            });
        }

        private static string GetSuspiciousActivityReport()
        {
            var snap = SecurityTelemetryService.Instance.GetSnapshot();
            if (snap.SuspiciousFlagged == 0)
                return "No suspicious activity detected. All monitored processes and network connections are within normal parameters.";

            return $"{snap.SuspiciousFlagged} suspicious event(s) flagged since monitoring started. Check the Activity Feed for details. Network connections: {snap.NetworkConnections}. Vulnerability score: {snap.VulnerabilityScore}/100.";
        }

        private static string GetPerformanceReport()
        {
            try
            {
                var snap = SecurityTelemetryService.Instance.GetSnapshot();
                var topProcs = Process.GetProcesses()
                    .Select(p => { try { return (p.ProcessName, Mb: p.WorkingSet64 / 1024 / 1024); } catch { return (p.ProcessName, Mb: 0L); } })
                    .OrderByDescending(x => x.Mb)
                    .Take(5)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"CPU: {snap.CpuPercent}%  RAM: {snap.RamPercent}% ({snap.RamUsedMb}MB used of {snap.RamTotalMb}MB).");
                sb.AppendLine($"Top memory consumers: {string.Join(", ", topProcs.Select(x => $"{x.ProcessName} ({x.Mb}MB)"))}.");

                if (snap.CpuPercent > 70)
                    sb.AppendLine("CPU is under heavy load. Consider closing unused applications.");
                if (snap.RamPercent > 80)
                    sb.AppendLine("RAM usage is high. Run 'optimize memory' to free up resources.");

                return sb.ToString().Trim();
            }
            catch
            {
                return "Unable to retrieve performance data.";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string BuildSystemContext()
        {
            try
            {
                var snap = SecurityTelemetryService.Instance.GetSnapshot();
                return $"CPU: {snap.CpuPercent}% | RAM: {snap.RamPercent}% ({snap.RamUsedMb}MB/{snap.RamTotalMb}MB) | " +
                       $"Processes: {snap.ProcessCount} | Network connections: {snap.NetworkConnections} | " +
                       $"Files scanned today: {snap.FilesScannedToday} | Suspicious flagged: {snap.SuspiciousFlagged} | " +
                       $"Vulnerability score: {snap.VulnerabilityScore}/100 | Status: {snap.OverallStatus}";
            }
            catch { return ""; }
        }

        private static string BuildChatResponse(string text)
        {
            return JsonSerializer.Serialize(new
            {
                type = "chat_response",
                text,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }
}
