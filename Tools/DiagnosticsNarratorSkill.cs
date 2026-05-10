using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Services;

namespace AtlasAI.Tools;

public static class DiagnosticsNarratorSkill
{
    public static Task<string> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        var raw = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return Task.FromResult<string>(null);

        var lower = raw.ToLowerInvariant();
        if (!IsQuery(lower))
            return Task.FromResult<string>(null);

        ct.ThrowIfCancellationRequested();

        ILogWatcherService watcher = null;
        try { watcher = AtlasAI.App.GetService<ILogWatcherService>(); } catch { watcher = null; }
        if (watcher == null)
            watcher = new LogWatcherService();

        try { watcher.Initialize(); } catch { }

        var last = watcher.GetLastNarration();
        if (last == null || last.IsEmpty)
        {
            // Fall back to just showing tail context.
            var tail = watcher.GetRecentLines();
            var tailText = tail != null && tail.Count > 0
                ? string.Join("\n", tail.Skip(Math.Max(0, tail.Count - 40)))
                : "(no recent log lines)";

            return Task.FromResult<string>(
                "I didn't find a clear last error yet.\n\nRecent log tail:\n```text\n" + Clip(tailText, 2400) + "\n```");
        }

        var sb = new StringBuilder();
        sb.AppendLine("🩺 What just went wrong");
        sb.AppendLine(last.Title);
        sb.AppendLine();
        sb.AppendLine("Explanation");
        sb.AppendLine(last.Explanation);

        if (last.Suggestions != null && last.Suggestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Self-heal suggestions");
            foreach (var s in last.Suggestions.Where(x => !string.IsNullOrWhiteSpace(x)).Take(6))
                sb.AppendLine("- " + s.Trim());
        }

        if (!string.IsNullOrWhiteSpace(last.RawEvidence))
        {
            sb.AppendLine();
            sb.AppendLine("Evidence");
            sb.AppendLine("```text");
            sb.AppendLine(Clip(last.RawEvidence.Trim(), 2400));
            sb.AppendLine("```");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    private static bool IsQuery(string lower)
    {
        if (lower == "what just went wrong") return true;
        if (lower == "what just went wrong?") return true;
        if (lower == "explain that error") return true;
        if (lower == "explain that error?") return true;
        if (lower == "show last error") return true;
        if (lower == "show last error?") return true;

        if (lower.Contains("what") && lower.Contains("went wrong")) return true;
        if (lower.Contains("last error")) return true;
        if (lower.StartsWith("explain") && lower.Contains("error")) return true;

        return false;
    }

    private static string Clip(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maxLen) return text;
        return text.Substring(0, maxLen) + "...";
    }
}
