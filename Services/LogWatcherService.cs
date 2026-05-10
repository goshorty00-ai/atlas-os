using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AtlasAI.Core;

namespace AtlasAI.Services;

public sealed class LogWatcherService : ILogWatcherService, IDisposable
{
    private readonly object _lock = new object();
    private readonly Queue<string> _tail = new Queue<string>();

    private Timer _timer;
    private bool _initialized;
    private string _path = "";
    private long _lastPosition;
    private DateTime _lastPathRefreshUtc = DateTime.MinValue;
    private DiagnosticsNarration _last = new DiagnosticsNarration();

    public void Initialize()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;

            _path = ResolveLogPath();
            _lastPosition = 0;
            _lastPathRefreshUtc = DateTime.UtcNow;

            try
            {
                // Tick-based tailing is more reliable than FileSystemWatcher for many log setups.
                _timer = new Timer(_ => SafeTick(), null, dueTime: 500, period: 750);
            }
            catch
            {
                _timer = null;
            }

            _initialized = true;
        }

        // Prime tail once.
        try { SafeTick(); } catch { }
    }

    public IReadOnlyList<string> GetRecentLines()
    {
        Initialize();
        lock (_lock)
        {
            return _tail.ToArray();
        }
    }

    public DiagnosticsNarration GetLastNarration()
    {
        Initialize();
        lock (_lock)
        {
            return new DiagnosticsNarration
            {
                CapturedAtUtc = _last.CapturedAtUtc,
                Title = _last.Title,
                Explanation = _last.Explanation,
                Suggestions = _last.Suggestions != null ? new List<string>(_last.Suggestions) : new List<string>(),
                RawEvidence = _last.RawEvidence
            };
        }
    }

    public void ReportException(string source, Exception ex)
    {
        if (ex == null) return;
        Initialize();

        try
        {
            var narration = NarrateException(source, ex);
            SetLastNarration(narration);
        }
        catch
        {
        }
    }

    public void ReportApiFailure(string subsystem, int statusCode, string details)
    {
        Initialize();
        try
        {
            var n = new DiagnosticsNarration
            {
                CapturedAtUtc = DateTime.UtcNow,
                Title = subsystem + " API error",
                Explanation = BuildHttpExplanation(subsystem, statusCode, details),
                Suggestions = BuildHttpSuggestions(statusCode),
                RawEvidence = (details ?? string.Empty).Trim()
            };

            SetLastNarration(n);
        }
        catch
        {
        }
    }

    private void SafeTick()
    {
        try
        {
            // Refresh path periodically in case of rotation.
            if (_lastPathRefreshUtc == DateTime.MinValue || (DateTime.UtcNow - _lastPathRefreshUtc).TotalSeconds >= 10)
            {
                _lastPathRefreshUtc = DateTime.UtcNow;
                var p = ResolveLogPath();
                if (!string.Equals(p, _path, StringComparison.OrdinalIgnoreCase))
                {
                    _path = p;
                    _lastPosition = 0;
                }
            }

            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
                return;

            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length < _lastPosition)
                    _lastPosition = 0;

                fs.Position = _lastPosition;
                using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                {
                    string line;
                    int added = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        added++;
                        AddLine(line);
                        TryParseFromLine(line);
                        if (added > 400) break; // avoid runaway if file is spammy
                    }
                }

                _lastPosition = fs.Position;
            }
        }
        catch
        {
            // Fail soft.
        }
    }

    private void AddLine(string line)
    {
        if (line == null) return;
        var t = line.TrimEnd('\r', '\n');
        if (t.Length == 0) return;

        lock (_lock)
        {
            _tail.Enqueue(t);
            while (_tail.Count > 200)
                _tail.Dequeue();
        }
    }

    private void TryParseFromLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var lower = line.ToLowerInvariant();

        // Unhandled/exception patterns
        if (lower.Contains("nullreferenceexception") || lower.Contains("object reference not set"))
        {
            SetLastNarration(new DiagnosticsNarration
            {
                CapturedAtUtc = DateTime.UtcNow,
                Title = "Null reference",
                Explanation = "A null reference was accessed (something expected to exist was null).",
                Suggestions = new List<string> { "Retry the action", "If it keeps happening, restart Atlas", "Clear cache if related to media/posters" },
                RawEvidence = line.Trim()
            });
            return;
        }

        // HTTP status patterns.
        if (TryExtractHttpStatus(lower, out var status))
        {
            SetLastNarration(new DiagnosticsNarration
            {
                CapturedAtUtc = DateTime.UtcNow,
                Title = "HTTP error " + status,
                Explanation = BuildHttpExplanation("API", status, line),
                Suggestions = BuildHttpSuggestions(status),
                RawEvidence = line.Trim()
            });
            return;
        }

        // Generic error line.
        if (lower.Contains("❌") || lower.Contains(" error") || lower.Contains("exception"))
        {
            // Don't spam; only record if it looks meaningful.
            if (lower.Contains("error") || lower.Contains("exception"))
            {
                SetLastNarration(new DiagnosticsNarration
                {
                    CapturedAtUtc = DateTime.UtcNow,
                    Title = "Error detected",
                    Explanation = SimplifyGenericError(line),
                    Suggestions = new List<string> { "Retry", "Check settings/keys if this is an API call", "Clear cache if media-related" },
                    RawEvidence = line.Trim()
                });
            }
        }
    }

    private void SetLastNarration(DiagnosticsNarration narration)
    {
        if (narration == null) return;

        lock (_lock)
        {
            _last = narration;
            _last.Suggestions ??= new List<string>();
        }

        try
        {
            Debug.WriteLine("[Diagnostics] Error parsed: " + (narration.Title ?? "(none)"));
        }
        catch
        {
        }
    }

    private static string ResolveLogPath()
    {
        try
        {
            var p = AppLogger.GetCurrentLogFilePath();
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                return p;
        }
        catch
        {
        }

        // Fall back to crash log.
        try
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
            var crash = Path.Combine(appDataDir, "crash_log.txt");
            if (File.Exists(crash)) return crash;
        }
        catch
        {
        }

        // Or newest file in logs dir.
        try
        {
            var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI", "logs");
            if (Directory.Exists(logsDir))
            {
                var newest = Directory.GetFiles(logsDir)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();
                return newest != null ? newest.FullName : "";
            }
        }
        catch
        {
        }

        return "";
    }

    private static bool TryExtractHttpStatus(string lowerLine, out int status)
    {
        status = 0;

        if (lowerLine.Contains(" 401") || lowerLine.Contains("unauthorized") || lowerLine.Contains("status: unauthorized"))
        {
            status = 401;
            return true;
        }

        if (lowerLine.Contains(" 404") || lowerLine.Contains("notfound") || lowerLine.Contains("not found") || lowerLine.Contains("status: notfound"))
        {
            status = 404;
            return true;
        }

        var m = Regex.Match(lowerLine, @"\bstatus\s*[:=]\s*(\d{3})\b", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
        {
            status = n;
            return true;
        }

        return false;
    }

    private static string BuildHttpExplanation(string subsystem, int statusCode, string details)
    {
        subsystem = string.IsNullOrWhiteSpace(subsystem) ? "API" : subsystem.Trim();

        if (statusCode == 401)
            return subsystem + " returned 401 (Unauthorized) → likely an invalid/expired API key or missing auth.";

        if (statusCode == 404)
            return subsystem + " returned 404 (Not Found) → often a wrong endpoint or an unavailable model/resource.";

        if (statusCode >= 500)
            return subsystem + " returned " + statusCode + " → server-side error (temporary outage or overload).";

        return subsystem + " returned " + statusCode + " → request failed.";
    }

    private static List<string> BuildHttpSuggestions(int statusCode)
    {
        if (statusCode == 401)
            return new List<string> { "Re-auth: check/update API key in Settings", "Retry", "If multiple providers exist, switch provider" };

        if (statusCode == 404)
            return new List<string> { "Retry with a different model/resource", "Re-check provider settings", "Clear cache if media resource" };

        if (statusCode >= 500)
            return new List<string> { "Retry", "Wait a minute and retry", "Switch provider if available" };

        return new List<string> { "Retry", "Check settings", "Clear cache if applicable" };
    }

    private static DiagnosticsNarration NarrateException(string source, Exception ex)
    {
        var kind = ex.GetType().Name;
        var msg = (ex.Message ?? string.Empty).Trim();

        var title = kind;
        var explanation = "An exception occurred.";
        var suggestions = new List<string> { "Retry", "Restart Atlas if it repeats" };

        if (kind.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("Object reference not set", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            title = "NullReferenceException";
            explanation = "A null reference was accessed (something expected to exist was null).";
            suggestions = new List<string> { "Retry", "Restart Atlas", "If this is media/posters, clear cache" };
        }

        var evidence = "Source: " + (source ?? "(unknown)") + "\n" + kind + ": " + msg;
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            evidence += "\n" + Clip(ex.StackTrace, 1200);

        return new DiagnosticsNarration
        {
            CapturedAtUtc = DateTime.UtcNow,
            Title = title,
            Explanation = explanation,
            Suggestions = suggestions,
            RawEvidence = evidence.TrimEnd()
        };
    }

    private static string SimplifyGenericError(string line)
    {
        var t = (line ?? string.Empty).Trim();
        if (t.Length > 220) t = t.Substring(0, 220) + "...";
        return "I saw an error line in the log: " + t;
    }

    private static string Clip(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maxLen) return text;
        return text.Substring(0, maxLen) + "...";
    }

    public void Dispose()
    {
        try { _timer?.Dispose(); } catch { }
        _timer = null;
    }
}
