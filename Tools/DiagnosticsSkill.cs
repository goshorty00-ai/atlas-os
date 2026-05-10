using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Tools
{
    public static class DiagnosticsSkill
    {
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI");

        private static readonly string CrashLogPath = Path.Combine(AppDataDir, "crash_log.txt");
        private static readonly string LogsDir = Path.Combine(AppDataDir, "logs");

        public static Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
        {
            var raw = (userMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return Task.FromResult<string?>(null);

            var lower = raw.Trim().ToLowerInvariant();

            // Tail commands
            // Examples:
            // - tail log
            // - tail 200
            // - tail 200 crash log
            // - diagnostics tail last 100 lines
            if (IsTailRequest(lower))
            {
                var n = ExtractTailLineCount(lower) ?? 120;
                var which = DetectLogSelector(lower);
                return TailAndSummarizeAsync(n, which, ct);
            }

            // Summarize without tail
            if (Regex.IsMatch(lower, @"\b(summarize|analyze|diagnose)\b") && Regex.IsMatch(lower, @"\b(log|logs|crash)\b"))
            {
                var which = DetectLogSelector(lower);
                return TailAndSummarizeAsync(200, which, ct);
            }

            return Task.FromResult<string?>(null);
        }

        private static bool IsTailRequest(string lower)
        {
            return lower.StartsWith("tail", StringComparison.Ordinal) ||
                   lower.StartsWith("tail ", StringComparison.Ordinal) ||
                   lower.StartsWith("diagnostics tail", StringComparison.Ordinal) ||
                   lower.StartsWith("logs tail", StringComparison.Ordinal) ||
                   lower.StartsWith("show logs", StringComparison.Ordinal) ||
                   lower.StartsWith("show log", StringComparison.Ordinal) ||
                   lower.StartsWith("show crash log", StringComparison.Ordinal);
        }

        private enum LogSelector
        {
            CrashLog,
            NewestLogInLogsDir
        }

        private static LogSelector DetectLogSelector(string lower)
        {
            // Explicit selectors
            if (lower.Contains("crash", StringComparison.OrdinalIgnoreCase))
                return LogSelector.CrashLog;

            // If user said "log" (but not crash), assume they mean the active rolling logs under %AppData%\AtlasAI\logs.
            if (lower.Contains(" log", StringComparison.OrdinalIgnoreCase) || lower.Contains("logs", StringComparison.OrdinalIgnoreCase))
                return LogSelector.NewestLogInLogsDir;

            // Default: newest log (more "app log"-like), with fallback to crash_log.txt.
            return LogSelector.NewestLogInLogsDir;
        }

        private static int? ExtractTailLineCount(string lower)
        {
            var m = Regex.Match(lower, @"\b(?:last\s+)?(\d{1,5})\s*(?:lines|line)?\b", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            {
                if (n < 1) n = 1;
                if (n > 5000) n = 5000;
                return n;
            }

            return null;
        }

        private static async Task<string?> TailAndSummarizeAsync(int lines, LogSelector selector, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var requested = selector switch
                {
                    LogSelector.CrashLog => CrashLogPath,
                    LogSelector.NewestLogInLogsDir => FindNewestLogFileInLogsDir(),
                    _ => FindNewestLogFileInLogsDir()
                };

                // Robust fallback so "tail log" works even if the logs dir is empty.
                var path = requested;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    var fallback = selector == LogSelector.CrashLog
                        ? FindNewestLogFileInLogsDir()
                        : CrashLogPath;

                    if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback))
                        path = fallback;
                }

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return $"❌ No log file found. Tried: {CrashLogPath} and {LogsDir}";

                var tailLines = await Task.Run(() => TailLines(path, lines, maxBytesToScan: 1024 * 1024), ct);
                if (tailLines.Count == 0)
                    return "ℹ️ Log is empty.";

                var tailText = string.Join("\n", tailLines);
                var analysis = Analyze(tailLines);

                var sb = new StringBuilder();
                sb.AppendLine("🧪 Diagnostics");
                sb.AppendLine($"Log: {path}");
                sb.AppendLine($"Tail: last {Math.Min(lines, tailLines.Count)} line(s)");
                sb.AppendLine();

                // Required output structure
                sb.AppendLine("1) Plain English");
                sb.AppendLine(analysis.PlainEnglish);
                sb.AppendLine();
                sb.AppendLine("2) Likely file/method");
                sb.AppendLine(analysis.LikelyLocation);
                sb.AppendLine();
                sb.AppendLine("3) Next step");
                sb.AppendLine(analysis.NextStep);

                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine("Log tail:");
                sb.AppendLine("```text");
                sb.AppendLine(ClipForChat(tailText, 8000));
                sb.AppendLine("```");

                return sb.ToString().TrimEnd();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "❌ Diagnostics failed: " + ex.Message;
            }
        }

        private static string FindNewestLogFileInLogsDir()
        {
            try
            {
                if (!Directory.Exists(LogsDir))
                    return "";

                var candidates = Directory.GetFiles(LogsDir)
                    .Where(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToList();

                return candidates.FirstOrDefault()?.FullName ?? "";
            }
            catch
            {
                return "";
            }
        }

        private sealed class AnalysisResult
        {
            public string PlainEnglish { get; init; } = "No obvious exception detected in the selected tail.";
            public string LikelyLocation { get; init; } = "Unknown (no stack trace line found).";
            public string NextStep { get; init; } = "If the issue is reproducible, trigger it again and then run `tail crash log` to capture a fresh stack trace.";
        }

        private static AnalysisResult Analyze(List<string> tailLines)
        {
            var joined = string.Join("\n", tailLines);

            // Prefer the last crash block in crash_log.txt format.
            var crashBlock = ExtractLastCrashBlock(joined);
            var focusText = string.IsNullOrWhiteSpace(crashBlock) ? joined : crashBlock;

            var (exceptionKind, exceptionMessage) = DetectExceptionKindAndMessage(focusText);
            var (method, file, line) = ExtractLastStackFrame(focusText);

            var location = !string.IsNullOrEmpty(method)
                ? (file != "" && line != 0
                    ? $"{method} in {file}:line {line}"
                    : method)
                : "Unknown (no stack trace line found).";

            var plain = BuildPlainEnglish(exceptionKind, exceptionMessage);
            var next = BuildNextStep(exceptionKind, exceptionMessage, method, file);

            return new AnalysisResult
            {
                PlainEnglish = plain,
                LikelyLocation = location,
                NextStep = next
            };
        }

        private static string ExtractLastCrashBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // Crash blocks are appended with "=== CRASH [timestamp] ===".
            var idx = text.LastIndexOf("=== CRASH [", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";

            return text.Substring(idx).Trim();
        }

        private static (string kind, string message) DetectExceptionKindAndMessage(string text)
        {
            // App crash log has a "Message: ..." line.
            var msgLine = Regex.Match(text, @"^Message:\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var msg = msgLine.Success ? (msgLine.Groups[1].Value ?? "").Trim() : "";

            // Sometimes exception type is logged elsewhere; try to capture it.
            var typeToken = Regex.Match(text, @"\b(System\.[A-Za-z0-9_\.]+Exception)\b");
            var type = typeToken.Success ? typeToken.Groups[1].Value.Trim() : "";

            // Infer from message patterns.
            var lower = (msg ?? "").ToLowerInvariant();
            if (string.IsNullOrEmpty(type))
            {
                if (lower.Contains("object reference not set", StringComparison.Ordinal)) type = "System.NullReferenceException";
                else if (lower.Contains("could not find file", StringComparison.Ordinal) || lower.Contains("could not find a part of the path", StringComparison.Ordinal)) type = "System.IO.FileNotFoundException";
                else if (lower.Contains("access is denied", StringComparison.Ordinal) || lower.Contains("unauthorized", StringComparison.Ordinal)) type = "System.UnauthorizedAccessException";
                else if (lower.Contains("the calling thread must be sta", StringComparison.Ordinal) || lower.Contains("the calling thread cannot access this object", StringComparison.Ordinal)) type = "Threading/Dispatcher (cross-thread)";
                else if (lower.Contains("cannot locate resource", StringComparison.Ordinal) || lower.Contains("xaml", StringComparison.Ordinal)) type = "XAML resource/parse";
                else if (lower.Contains("one or more errors occurred", StringComparison.Ordinal)) type = "AggregateException";
                else if (lower.Contains("a task was canceled", StringComparison.Ordinal) || lower.Contains("operation canceled", StringComparison.Ordinal)) type = "TaskCanceledException";
            }

            if (string.IsNullOrWhiteSpace(type))
                type = "Unknown";

            if (string.IsNullOrWhiteSpace(msg))
                msg = "(no message captured)";

            return (type, msg);
        }

        private static (string method, string file, int line) ExtractLastStackFrame(string text)
        {
            // Typical .NET stack trace line:
            // at Namespace.Type.Method(...) in C:\Path\File.cs:line 123
            var matches = Regex.Matches(text, @"\bat\s+(?<method>[^\r\n]+?)\s+in\s+(?<file>[A-Za-z]:\\[^\r\n]+?):line\s+(?<line>\d+)", RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                var last = matches[matches.Count - 1];
                var method = (last.Groups["method"].Value ?? "").Trim();
                var file = (last.Groups["file"].Value ?? "").Trim();
                var lineStr = (last.Groups["line"].Value ?? "").Trim();
                _ = int.TryParse(lineStr, out var line);
                return (method, file, line);
            }

            // Fallback: "at Namespace.Type.Method" without file info.
            var m2 = Regex.Match(text, @"\bat\s+(?<method>AtlasAI\.[^\r\n]+)", RegexOptions.IgnoreCase);
            if (m2.Success)
                return ((m2.Groups["method"].Value ?? "").Trim(), "", 0);

            return ("", "", 0);
        }

        private static string BuildPlainEnglish(string kind, string message)
        {
            if (kind.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase))
                return $"A code path tried to use an object that was null. The app hit a NullReference-style crash with message: {message}";

            if (kind.Contains("FileNotFoundException", StringComparison.OrdinalIgnoreCase))
                return $"The app expected a file/folder that wasn’t there (or the path was wrong). Message: {message}";

            if (kind.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase))
                return $"The app tried to read/write something Windows blocked (permissions / protected folder). Message: {message}";

            if (kind.Contains("XAML", StringComparison.OrdinalIgnoreCase) || kind.Contains("xaml", StringComparison.OrdinalIgnoreCase))
                return $"The UI failed to load a XAML resource or parse a XAML element. Message: {message}";

            if (kind.Contains("cross-thread", StringComparison.OrdinalIgnoreCase) || kind.Contains("Dispatcher", StringComparison.OrdinalIgnoreCase))
                return $"Something updated WPF UI from the wrong thread (dispatcher/STA issue). Message: {message}";

            if (kind.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase))
                return $"A task/operation was cancelled. If this happened during shutdown/navigation it may be benign; if it crashed, a cancellation may be escaping as an error. Message: {message}";

            if (kind.Contains("AggregateException", StringComparison.OrdinalIgnoreCase))
                return $"Multiple underlying errors were wrapped into a single failure (AggregateException style). Message: {message}";

            if (kind == "Unknown")
                return $"No standard exception signature found in the tail. Last captured message: {message}";

            return $"Detected: {kind}. Message: {message}";
        }

        private static string BuildNextStep(string kind, string message, string method, string file)
        {
            if (kind.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrEmpty(method)
                    ? $"Add a null-guard/log around the values used in {method}, then reproduce once to confirm which reference is null."
                    : "Reproduce once, then use `tail crash log` and look for the last `at ... in ...:line ...` frame; add null-guards around that line.";
            }

            if (kind.Contains("FileNotFoundException", StringComparison.OrdinalIgnoreCase))
                return "Confirm the referenced path exists, and add a safe fallback (create directory / show user prompt / skip) instead of assuming the file is present.";

            if (kind.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase))
                return "Move the write target to a user-writable folder (e.g. `%AppData%\\AtlasAI`) or add an explicit permission/selection step before writing.";

            if (kind.Contains("XAML", StringComparison.OrdinalIgnoreCase) || kind.Contains("xaml", StringComparison.OrdinalIgnoreCase))
                return "Check the referenced resource keys/URIs and ensure the XAML file builds as `Page/Resource`. If it’s a binding/Template issue, reproduce with `PresentationTraceSources` temporarily enabled.";

            if (kind.Contains("Dispatcher", StringComparison.OrdinalIgnoreCase) || kind.Contains("cross-thread", StringComparison.OrdinalIgnoreCase))
                return "Ensure UI-bound updates run on `Application.Current.Dispatcher`. If a background task is touching UI state, marshal back to the dispatcher.";

            if (kind.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase))
                return "If cancellation is expected, catch `OperationCanceledException` and treat as non-error; otherwise, track which cancellation token is being triggered and why.";

            return "Run `tail crash log` right after reproducing so the last stack frame is captured, then fix the topmost AtlasAI stack frame shown.";
        }

        private static List<string> TailLines(string path, int lineCount, int maxBytesToScan)
        {
            if (lineCount < 1) lineCount = 1;
            if (lineCount > 5000) lineCount = 5000;

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length == 0) return new List<string>();

                var bytesToRead = (int)Math.Min(fs.Length, maxBytesToScan);
                fs.Seek(-bytesToRead, SeekOrigin.End);

                var buffer = new byte[bytesToRead];
                var read = fs.Read(buffer, 0, bytesToRead);

                var text = Encoding.UTF8.GetString(buffer, 0, read);
                text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

                var lines = text.Split('\n');
                var tail = lines.Skip(Math.Max(0, lines.Length - lineCount)).ToList();

                // If we started mid-line, drop the first partial line for cleanliness.
                if (bytesToRead < fs.Length && tail.Count > 0)
                {
                    // Only drop if the read segment doesn't start at line boundary.
                    // Heuristic: if the first character isn't a newline and file is longer than scanned.
                    if (!text.StartsWith("\n", StringComparison.Ordinal))
                        tail = tail.Skip(1).ToList();
                }

                // Trim trailing empty line from split.
                while (tail.Count > 0 && string.IsNullOrWhiteSpace(tail.Last()))
                    tail.RemoveAt(tail.Count - 1);

                return tail;
            }
            catch
            {
                // Fallback: slow but safe.
                try
                {
                    var all = File.ReadAllLines(path);
                    return all.Skip(Math.Max(0, all.Length - lineCount)).ToList();
                }
                catch
                {
                    return new List<string>();
                }
            }
        }

        private static string ClipForChat(string text, int maxChars)
        {
            text = text ?? string.Empty;
            if (text.Length <= maxChars) return text;
            return text.Substring(text.Length - maxChars, maxChars);
        }
    }
}
