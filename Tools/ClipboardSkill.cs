using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Tools;

public static class ClipboardSkill
{
    public static async Task<string> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        var raw = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var msg = Normalize(raw);

        // Read
        if (msg == "paste" || msg == "read clipboard" || msg == "paste clipboard" || msg == "clipboard")
            return await ReadClipboardAsync(ct);

        // Read file-drop list from clipboard
        if (msg == "paste files" || msg == "paste file" || msg == "paste file list" || msg == "paste filelist" ||
            msg == "paste file contents" || msg == "paste files here" || msg == "paste files contents")
        {
            var includeContents = msg.Contains("content", StringComparison.Ordinal) || msg.Contains("here", StringComparison.Ordinal);
            return await PasteFilesFromClipboardAsync(includeContents, ct);
        }

        // Write (clipboard text)
        // NOTE: Do NOT intercept filesystem copy operations (handled by FileButlerSkill).
        if (msg.StartsWith("copy:", StringComparison.Ordinal) ||
            (msg.StartsWith("copy ", StringComparison.Ordinal) && !LooksLikeFileCopyOperation(msg)))
        {
            var payload = ExtractAfterPrefix(raw, new[] { "copy ", "copy:" });
            if (string.IsNullOrWhiteSpace(payload))
                return "⚠️ Tell me what to copy, e.g. `copy: hello world` or `set clipboard hello world`.";

            return await WriteClipboardAsync(payload, ct);
        }

        if (msg.StartsWith("write clipboard ", StringComparison.Ordinal) || msg.StartsWith("set clipboard ", StringComparison.Ordinal))
        {
            var payload = ExtractAfterPrefix(raw, new[] { "write clipboard ", "set clipboard " });
            if (string.IsNullOrWhiteSpace(payload))
                return "⚠️ Tell me what to write to the clipboard.";

            return await WriteClipboardAsync(payload, ct);
        }

        // Clean / format / summarize operate on current clipboard text.
        if (msg == "clean" || msg == "clean clipboard")
            return await TransformClipboardAsync(TransformClean, "Cleaned", ct);

        if (msg == "format" || msg == "format clipboard")
            return await TransformClipboardAsync(TransformFormat, "Formatted", ct);

        if (msg == "summarize" || msg == "summarize clipboard")
            return await SummarizeClipboardAsync(ct);

        return null;
    }

    private static bool LooksLikeFileCopyOperation(string normalizedMsg)
    {
        // Examples:
        // - copy files from X to Y
        // - copy X to Y
        // - copy folder X to Y
        if (string.IsNullOrWhiteSpace(normalizedMsg)) return false;

        var m = normalizedMsg.Trim();
        if (!m.StartsWith("copy ", StringComparison.Ordinal)) return false;

        if (m.StartsWith("copy files", StringComparison.Ordinal) ||
            m.StartsWith("copy file", StringComparison.Ordinal) ||
            m.StartsWith("copy folder", StringComparison.Ordinal) ||
            m.StartsWith("copy directory", StringComparison.Ordinal))
            return true;

        // Heuristic: "copy ... to ..." is far more likely to be a filesystem op than a clipboard op.
        if (Regex.IsMatch(m, @"^copy\s+.+\s+to\s+.+$", RegexOptions.IgnoreCase))
            return true;

        if (Regex.IsMatch(m, @"^copy\s+from\s+.+\s+to\s+.+$", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    private static async Task<string> ReadClipboardAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var text = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!Clipboard.ContainsText()) return null;
                    return Clipboard.GetText();
                }
                catch
                {
                    return null;
                }
            });

            text = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "📋 Clipboard is empty (or non-text).";

            // Keep chat usable: clip very large pastes.
            if (text.Length > 4000)
                text = text.Substring(0, 3800).TrimEnd() + "\n\n…(truncated)";

            return "📋 Clipboard:\n```text\n" + text + "\n```";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "⚠️ Clipboard read failed: " + ex.Message;
        }
    }

    private static async Task<string> PasteFilesFromClipboardAsync(bool includeContents, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            StringCollection? files = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (Clipboard.ContainsFileDropList())
                        files = Clipboard.GetFileDropList();
                }
                catch
                {
                    files = null;
                }
            });

            var paths = files?.Cast<string>().Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new();
            if (paths.Count == 0)
                return "📋 Clipboard has no files. Copy files in Explorer first, then run `paste files`.";

            var sb = new StringBuilder();
            sb.AppendLine($"📎 Clipboard files: {paths.Count}");
            foreach (var p in paths.Take(20))
                sb.AppendLine($"- {p}");
            if (paths.Count > 20)
                sb.AppendLine($"... and {paths.Count - 20} more");

            if (!includeContents)
                return sb.ToString().TrimEnd();

            sb.AppendLine();
            sb.AppendLine("Contents (text files only):");

            var maxFiles = 3;
            var maxCharsTotal = 9000;
            var used = 0;

            foreach (var path in paths.Take(maxFiles))
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(path))
                {
                    sb.AppendLine($"\n--- {path} (not found) ---");
                    continue;
                }

                if (!LooksLikeTextFile(path))
                {
                    sb.AppendLine($"\n--- {path} (not a text file) ---");
                    continue;
                }

                string content;
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Length > 256 * 1024)
                    {
                        sb.AppendLine($"\n--- {path} (too large: {fi.Length:N0} bytes) ---");
                        continue;
                    }

                    content = await File.ReadAllTextAsync(path, ct);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"\n--- {path} (read failed: {ex.Message}) ---");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine($"\n--- {path} (empty) ---");
                    continue;
                }

                content = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

                var remaining = Math.Max(0, maxCharsTotal - used);
                if (remaining <= 0)
                {
                    sb.AppendLine("\n...(truncated: reached size limit)");
                    break;
                }

                if (content.Length > remaining)
                    content = content.Substring(0, remaining).TrimEnd() + "\n\n…(truncated)";

                used += content.Length;

                sb.AppendLine($"\n--- {path} ---");
                sb.AppendLine("```text");
                sb.AppendLine(content.TrimEnd());
                sb.AppendLine("```");
            }

            if (paths.Count > maxFiles)
                sb.AppendLine($"\n...(only showing first {maxFiles} file(s))");

            return sb.ToString().TrimEnd();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "⚠️ Paste files failed: " + ex.Message;
        }
    }

    private static bool LooksLikeTextFile(string path)
    {
        var ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
        return ext is ".txt" or ".md" or ".json" or ".jsonl" or ".xml" or ".yaml" or ".yml" or ".csv" or
               ".log" or ".ini" or ".config" or ".cs" or ".xaml" or ".csproj" or ".sln" or ".slnx" or
               ".ps1" or ".cmd" or ".bat" or ".ts" or ".js" or ".html" or ".css";
    }

    private static async Task<string> WriteClipboardAsync(string payload, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var text = (payload ?? string.Empty);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch
                {
                }
            });

            return "✅ Copied to clipboard.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "⚠️ Clipboard write failed: " + ex.Message;
        }
    }

    private static async Task<string> TransformClipboardAsync(Func<string, string> transform, string label, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            string current = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                    current = null;
                }
            });

            current = current ?? string.Empty;
            if (string.IsNullOrWhiteSpace(current))
                return "📋 Clipboard is empty (or non-text).";

            var next = transform(current);
            if (string.Equals(next, current, StringComparison.Ordinal))
                return $"📋 Clipboard already looks {label.ToLowerInvariant()}.";

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Clipboard.SetText(next);
                }
                catch
                {
                }
            });

            return $"✅ {label} clipboard text.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "⚠️ Clipboard update failed: " + ex.Message;
        }
    }

    private static async Task<string> SummarizeClipboardAsync(CancellationToken ct)
    {
        // No disk I/O here. Only reads clipboard and produces a response.
        string current = null;
        try
        {
            ct.ThrowIfCancellationRequested();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                        current = Clipboard.GetText();
                }
                catch
                {
                    current = null;
                }
            });

            current = (current ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(current))
                return "📋 Clipboard is empty (or non-text).";

            // Simple offline summary (deterministic). Keep it fast/safe.
            var lines = current.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal)
                .Split('\n');

            var nonEmptyLines = 0;
            var maxLineLen = 0;
            foreach (var l in lines)
            {
                var t = (l ?? string.Empty).Trim();
                if (t.Length == 0) continue;
                nonEmptyLines++;
                if (t.Length > maxLineLen) maxLineLen = t.Length;
            }

            var kind = GuessKind(current);
            var preview = current.Length <= 240 ? current : current.Substring(0, 240).TrimEnd() + "…";

            var sb = new StringBuilder();
            sb.AppendLine("🧾 Clipboard summary");
            sb.AppendLine($"- Type: {kind}");
            sb.AppendLine($"- Length: {current.Length:N0} chars");
            sb.AppendLine($"- Lines: {lines.Length:N0} ({nonEmptyLines:N0} non-empty)");
            sb.AppendLine($"- Longest line: {maxLineLen:N0} chars");
            sb.AppendLine();
            sb.AppendLine("Preview:");
            sb.AppendLine("```text");
            sb.AppendLine(preview);
            sb.Append("```");

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return "⚠️ Clipboard summarize failed: " + ex.Message;
        }
    }

    private static string TransformClean(string input)
    {
        var s = input ?? string.Empty;
        s = s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        // Trim trailing whitespace per line.
        var lines = s.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = (lines[i] ?? string.Empty).TrimEnd();

        s = string.Join("\n", lines).Trim();
        s = Regex.Replace(s, "\\n{3,}", "\n\n");
        s = Regex.Replace(s, "[\\t ]{2,}", " ");
        return s;
    }

    private static string TransformFormat(string input)
    {
        var s = TransformClean(input);
        if (string.IsNullOrWhiteSpace(s)) return s;

        // Pretty JSON if it looks like JSON.
        if (LooksLikeJson(s))
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                // fall through
            }
        }

        // Lightweight bullet formatting: collapse comma-separated lists into lines if short.
        if (s.Length <= 2000 && s.Contains(",", StringComparison.Ordinal) && !s.Contains("\n", StringComparison.Ordinal))
        {
            var parts = s.Split(',').Select(p => (p ?? string.Empty).Trim()).Where(p => p.Length > 0).ToArray();
            if (parts.Length >= 3)
                return string.Join("\n", parts.Select(p => "- " + p));
        }

        return s;
    }

    private static bool LooksLikeJson(string s)
    {
        var t = (s ?? string.Empty).TrimStart();
        return t.StartsWith("{", StringComparison.Ordinal) || t.StartsWith("[", StringComparison.Ordinal);
    }

    private static string GuessKind(string s)
    {
        var t = (s ?? string.Empty).TrimStart();
        if (LooksLikeJson(t)) return "JSON (maybe)";
        if (Regex.IsMatch(t, @"^https?://\S+$", RegexOptions.IgnoreCase)) return "URL";
        if (Regex.IsMatch(t, @"^[\w\-]{20,}\.[\w\-]{10,}\.[\w\-]{10,}$")) return "Token (JWT-like)";
        if (Regex.IsMatch(t, @"(?m)^\s*using\s+\w+;|\bclass\b|\bnamespace\b")) return "Code (C#-like)";
        return "Text";
    }

    private static string Normalize(string text)
    {
        var s = (text ?? string.Empty).Trim().ToLowerInvariant();
        s = s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        s = s.Replace("\t", " ", StringComparison.Ordinal);
        while (s.Contains("  ", StringComparison.Ordinal)) s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }

    private static string ExtractAfterPrefix(string raw, string[] prefixes)
    {
        if (raw == null) return string.Empty;
        foreach (var p in prefixes)
        {
            if (raw.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return raw.Substring(p.Length).Trim();
        }
        return string.Empty;
    }
}
