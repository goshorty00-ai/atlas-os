using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Agent;
using AtlasAI.Core;
using Microsoft.Win32;

namespace AtlasAI.Tools
{
    public static class WindowsSkillSystemSkill
    {
        private sealed record PendingDangerousAction(
            string SkillName,
            string Summary,
            DateTime CreatedAtUtc,
            Func<CancellationToken, Task<string>> ExecuteAsync);

        private static readonly object _pendingLock = new object();
        private static PendingDangerousAction? _pending;

        // Keep short to avoid stale confirmations.
        private static readonly TimeSpan PendingTtl = TimeSpan.FromSeconds(60);

        // Minimal critical list; blocks the most common OS-essential processes.
        private static readonly HashSet<string> CriticalProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csrss",
            "wininit",
            "winlogon",
            "lsass",
            "services",
            "smss",
            "system",
            "registry",
            "secure system",
            "fontdrvhost",
            "sihost"
        };

        public static async Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
        {
            var clean = (userMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clean)) return null;

            // Handle pending CONFIRM gate first.
            var pending = GetPendingIfValidOrClear();
            if (pending != null)
            {
                if (IsConfirm(clean))
                {
                    ClearPending();
                    var result = await pending.ExecuteAsync(ct);
                    AppLogger.Log($"[Skill] Executed: {pending.SkillName} ({pending.Summary})");
                    return result;
                }

                // Any other message cancels the pending action for safety.
                ClearPending();
            }

            // SAFE: List processes
            if (IsListProcesses(clean))
            {
                var result = await SystemTool.GetProcessesAsync();
                AppLogger.Log("[Skill] Executed: ListProcesses");
                return result;
            }

            // SAFE: Screenshot
            if (IsScreenshot(clean))
            {
                var result = await SystemTool.TakeScreenshotAsync();
                AppLogger.Log("[Skill] Executed: Screenshot");
                return result;
            }

            // SAFE: Search files (read-only)
            if (TryParseSearchFiles(clean, out var searchPath, out var searchQuery))
            {
                var result = await SystemTool.FindFilesAsync(searchPath, searchQuery);
                AppLogger.Log($"[Skill] Executed: SearchFiles ({searchQuery})");
                return result;
            }

            // SAFE: Set volume
            if (TryParseSetVolume(clean, out var volumeLevel))
            {
                var result = await SystemTool.SetVolumeAsync(volumeLevel);
                AppLogger.Log($"[Skill] Executed: SetVolume ({volumeLevel})");
                return result;
            }

            // SAFE: Unzip file
            if (TryParseUnzipFile(clean, out var zipPath, out var unzipDest))
            {
                var expandedZip = ExpandPath(zipPath);
                var expandedDest = ExpandPath(unzipDest);
                if (!File.Exists(expandedZip))
                    return $"❌ Zip not found: {expandedZip}";

                if (string.IsNullOrWhiteSpace(expandedDest))
                    expandedDest = Path.Combine(Path.GetDirectoryName(expandedZip) ?? Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(expandedZip));

                Directory.CreateDirectory(expandedDest);

                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(expandedZip, expandedDest, overwriteFiles: true);
                }, ct);

                AppLogger.Log($"[Skill] Executed: UnzipFile ({expandedZip} -> {expandedDest})");
                return $"✅ Unzipped to: {expandedDest}";
            }

            // DANGEROUS: Shutdown
            if (IsShutdown(clean))
            {
                SetPending(new PendingDangerousAction(
                    SkillName: "Shutdown",
                    Summary: "shutdown",
                    CreatedAtUtc: DateTime.UtcNow,
                    ExecuteAsync: _ => SystemTool.ShutdownAsync(60)));

                return "⚠️ Dangerous action requested: shutdown\n\nType CONFIRM to proceed.";
            }

            // DANGEROUS: Kill process
            if (TryParseKillProcess(clean, out var killTarget))
            {
                SetPending(new PendingDangerousAction(
                    SkillName: "KillProcess",
                    Summary: killTarget,
                    CreatedAtUtc: DateTime.UtcNow,
                    ExecuteAsync: async _ =>
                    {
                        if (IsCriticalProcessName(killTarget))
                            return $"🚫 Blocked: '{killTarget}' is a system-critical process.";

                        // Use existing app control close (it kills by name), since it's already integrated.
                        return await EnhancedAppControl.CloseAppAsync(killTarget);
                    }));

                return $"⚠️ Dangerous action requested: kill process '{killTarget}'\n\nType CONFIRM to proceed.";
            }

            // SAFE: Close app (blocked for critical processes)
            if (TryParseCloseApp(clean, out var closeTarget))
            {
                if (IsCriticalProcessName(closeTarget))
                {
                    AppLogger.Log($"[Skill] Executed: CloseApp (BLOCKED: {closeTarget})");
                    return $"🚫 Blocked: '{closeTarget}' is a system-critical process.";
                }

                var result = await EnhancedAppControl.CloseAppAsync(closeTarget);
                AppLogger.Log($"[Skill] Executed: CloseApp ({closeTarget})");
                return result;
            }

            // SAFE: Open app
            if (TryParseOpenApp(clean, out var openTarget))
            {
                var result = await EnhancedAppControl.OpenAppAsync(openTarget);
                AppLogger.Log($"[Skill] Executed: OpenApp ({openTarget})");
                return result;
            }

            // DANGEROUS: Delete folder
            if (TryParseDeleteFolder(clean, out var folderPath))
            {
                SetPending(new PendingDangerousAction(
                    SkillName: "DeleteFolder",
                    Summary: folderPath,
                    CreatedAtUtc: DateTime.UtcNow,
                    ExecuteAsync: async _ =>
                    {
                        var fullPath = ExpandPath(folderPath);
                        if (string.IsNullOrWhiteSpace(fullPath)) return "❌ Invalid folder path.";

                        if (!Directory.Exists(fullPath))
                            return $"❌ Folder not found: {fullPath}";

                        if (IsRootPath(fullPath))
                            return "🚫 Blocked: refusing to delete a drive root.";

                        // SafetyKernel gate (if safety mode blocks deletes, respect it)
                        var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                            OperationType.FolderDelete,
                            OperationRisk.High,
                            $"Delete folder: {fullPath}",
                            new Dictionary<string, object> { ["path"] = fullPath });

                        if (safetyCheck.Decision == SafetyDecision.Blocked)
                            return safetyCheck.Message;

                        Directory.Delete(fullPath, recursive: true);
                        return $"✅ Deleted folder: {fullPath}";
                    }));

                return $"⚠️ Dangerous action requested: delete folder '{folderPath}'\n\nType CONFIRM to proceed.";
            }

            // DANGEROUS: Registry modify (set value)
            if (TryParseRegistrySet(clean, out var hive, out var subKey, out var valueName, out var valueData))
            {
                var summary = $"{hive}\\{subKey} {valueName}={valueData}";
                SetPending(new PendingDangerousAction(
                    SkillName: "RegistryModify",
                    Summary: summary,
                    CreatedAtUtc: DateTime.UtcNow,
                    ExecuteAsync: async _ =>
                    {
                        var safetyCheck = await SafetyKernel.Instance.CheckAndBlockAsync(
                            OperationType.RegistryWrite,
                            OperationRisk.High,
                            $"Registry set: {summary}",
                            new Dictionary<string, object>
                            {
                                ["hive"] = hive,
                                ["subKey"] = subKey,
                                ["name"] = valueName,
                                ["value"] = valueData
                            });

                        if (safetyCheck.Decision == SafetyDecision.Blocked)
                            return safetyCheck.Message;

                        try
                        {
                            using var baseKey = OpenHive(hive);
                            using var key = baseKey.CreateSubKey(subKey, writable: true);
                            if (key == null) return "❌ Failed to open or create registry key.";
                            key.SetValue(valueName, valueData, RegistryValueKind.String);
                            return $"✅ Registry updated: {summary}";
                        }
                        catch (Exception ex)
                        {
                            return $"❌ Registry modify failed: {ex.Message}";
                        }
                    }));

                return $"⚠️ Dangerous action requested: registry modify\n\n{summary}\n\nType CONFIRM to proceed.";
            }

            return null;
        }

        private static bool IsConfirm(string message)
            => string.Equals(message.Trim(), "CONFIRM", StringComparison.OrdinalIgnoreCase);

        private static bool IsListProcesses(string message)
        {
            var lower = message.Trim().ToLowerInvariant();
            return lower is "list processes" or "processes" or "show processes" or "list running processes";
        }

        private static bool IsScreenshot(string message)
        {
            var lower = message.ToLowerInvariant();
            return lower.Contains("screenshot") || lower.Contains("screen capture") || lower.Contains("snip");
        }

        private static bool IsShutdown(string message)
        {
            var lower = message.ToLowerInvariant();
            return lower == "shutdown" || lower.Contains("shut down") || lower.Contains("power off") || lower.StartsWith("shutdown ");
        }

        private static bool TryParseSetVolume(string message, out int level)
        {
            level = 0;
            var m = Regex.Match(message, @"^\s*(?:set\s+)?volume\s*(?:to)?\s*(?<n>\d{1,3})\s*%?\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups["n"].Value, out level)) return false;
            level = Math.Clamp(level, 0, 100);
            return true;
        }

        private static bool TryParseOpenApp(string message, out string target)
        {
            target = "";
            var m = Regex.Match(message, @"^\s*(?:open|launch|start|run)\s+(?<t>.+?)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            target = TrimOuterQuotes(m.Groups["t"].Value.Trim());
            return !string.IsNullOrWhiteSpace(target);
        }

        private static bool TryParseCloseApp(string message, out string target)
        {
            target = "";
            var m = Regex.Match(message, @"^\s*(?:close|quit|exit)\s+(?<t>.+?)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            target = TrimOuterQuotes(m.Groups["t"].Value.Trim());
            return !string.IsNullOrWhiteSpace(target);
        }

        private static bool TryParseKillProcess(string message, out string target)
        {
            target = "";
            var m = Regex.Match(message, @"^\s*(?:kill|end|terminate|kill\s+process|end\s+process|terminate\s+process)\s+(?<t>.+?)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            target = TrimOuterQuotes(m.Groups["t"].Value.Trim());
            return !string.IsNullOrWhiteSpace(target);
        }

        private static bool TryParseDeleteFolder(string message, out string folder)
        {
            folder = "";
            var m = Regex.Match(message, @"^\s*(?:delete|remove)\s+(?:folder|directory)\s+(?<p>.+?)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            folder = TrimOuterQuotes(m.Groups["p"].Value.Trim());
            return !string.IsNullOrWhiteSpace(folder);
        }

        private static bool TryParseSearchFiles(string message, out string path, out string query)
        {
            path = "";
            query = "";

            // Examples:
            // - search files for report in C:\Work
            // - search files report
            var m = Regex.Match(message, @"^\s*(?:search\s+files|find\s+files)\s+(?:for\s+)?(?<q>.+?)(?:\s+(?:in|under)\s+(?<p>.+))?\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;

            query = TrimOuterQuotes(m.Groups["q"].Value.Trim());
            path = m.Groups["p"].Success ? TrimOuterQuotes(m.Groups["p"].Value.Trim()) : "";

            if (string.IsNullOrWhiteSpace(query)) return false;

            path = ExpandPath(path);
            return true;
        }

        private static bool TryParseUnzipFile(string message, out string zipPath, out string destination)
        {
            zipPath = "";
            destination = "";

            // Examples:
            // - unzip C:\Work\a.zip
            // - unzip "C:\Work\a.zip" to "C:\Work\out"
            // - extract C:\Work\a.zip to C:\Work\out
            var m = Regex.Match(message,
                @"^\s*(?:unzip|extract)\s+(?<zip>.+?)(?:\s+(?:to|into)\s+(?<dest>.+))?\s*$",
                RegexOptions.IgnoreCase);

            if (!m.Success) return false;

            zipPath = TrimOuterQuotes(m.Groups["zip"].Value.Trim());
            destination = m.Groups["dest"].Success ? TrimOuterQuotes(m.Groups["dest"].Value.Trim()) : "";

            return !string.IsNullOrWhiteSpace(zipPath);
        }

        private static bool TryParseRegistrySet(string message, out string hive, out string subKey, out string valueName, out string valueData)
        {
            hive = "";
            subKey = "";
            valueName = "";
            valueData = "";

            // Command format:
            // registry set "HKCU\\Software\\MyApp" "MyValue" "Some data"
            // registry set HKLM\\Software\\MyApp Name Value
            var tokens = SplitArgs(message);
            if (tokens.Count < 5) return false;

            if (!tokens[0].Equals("registry", StringComparison.OrdinalIgnoreCase) && !tokens[0].Equals("reg", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!tokens[1].Equals("set", StringComparison.OrdinalIgnoreCase) && !tokens[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                return false;

            var keyPath = tokens[2];
            if (string.IsNullOrWhiteSpace(keyPath)) return false;

            (hive, subKey) = SplitHive(keyPath);
            if (string.IsNullOrWhiteSpace(hive) || string.IsNullOrWhiteSpace(subKey)) return false;

            valueName = tokens[3];
            valueData = string.Join(" ", tokens.Skip(4));

            return !string.IsNullOrWhiteSpace(valueName) && !string.IsNullOrWhiteSpace(valueData);
        }

        private static (string hive, string subKey) SplitHive(string keyPath)
        {
            var normalized = keyPath.Replace("/", "\\").Trim().Trim('\\');
            if (normalized.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            {
                var idx = normalized.IndexOf('\\');
                if (idx <= 0) return (normalized.ToUpperInvariant(), "");
                return (normalized.Substring(0, idx).ToUpperInvariant(), normalized.Substring(idx + 1));
            }

            if (normalized.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                return ("HKEY_CURRENT_USER", normalized.Substring(4).TrimStart('\\'));
            if (normalized.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
                return ("HKEY_LOCAL_MACHINE", normalized.Substring(4).TrimStart('\\'));
            if (normalized.StartsWith("HKCR", StringComparison.OrdinalIgnoreCase))
                return ("HKEY_CLASSES_ROOT", normalized.Substring(4).TrimStart('\\'));
            if (normalized.StartsWith("HKU", StringComparison.OrdinalIgnoreCase))
                return ("HKEY_USERS", normalized.Substring(3).TrimStart('\\'));
            if (normalized.StartsWith("HKCC", StringComparison.OrdinalIgnoreCase))
                return ("HKEY_CURRENT_CONFIG", normalized.Substring(4).TrimStart('\\'));

            return ("", "");
        }

        private static RegistryKey OpenHive(string hive)
        {
            return hive.ToUpperInvariant() switch
            {
                "HKEY_CURRENT_USER" => Registry.CurrentUser,
                "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                "HKEY_USERS" => Registry.Users,
                "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
                _ => Registry.CurrentUser
            };
        }

        private static bool IsCriticalProcessName(string input)
        {
            var name = (input ?? string.Empty).Trim().ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            return CriticalProcessNames.Contains(name);
        }

        private static bool IsRootPath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetPathRoot(full);
                if (string.IsNullOrWhiteSpace(root)) return false;
                return string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var trimmed = TrimOuterQuotes(path.Trim());
            try
            {
                return Environment.ExpandEnvironmentVariables(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        private static string TrimOuterQuotes(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
                return text.Substring(1, text.Length - 2);
            return text;
        }

        private static List<string> SplitArgs(string input)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            var current = "";
            char? quote = null;
            foreach (var c in input)
            {
                if (quote == null && (c == '"' || c == '\''))
                {
                    quote = c;
                    continue;
                }

                if (quote != null && c == quote)
                {
                    quote = null;
                    continue;
                }

                if (quote == null && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current);
                        current = "";
                    }
                    continue;
                }

                current += c;
            }

            if (current.Length > 0) result.Add(current);
            return result;
        }

        private static PendingDangerousAction? GetPendingIfValidOrClear()
        {
            lock (_pendingLock)
            {
                if (_pending == null) return null;
                if (DateTime.UtcNow - _pending.CreatedAtUtc > PendingTtl)
                {
                    _pending = null;
                    return null;
                }
                return _pending;
            }
        }

        private static void SetPending(PendingDangerousAction pending)
        {
            lock (_pendingLock)
            {
                _pending = pending;
            }
        }

        private static void ClearPending()
        {
            lock (_pendingLock)
            {
                _pending = null;
            }
        }
    }
}
