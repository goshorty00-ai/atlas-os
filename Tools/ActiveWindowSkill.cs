using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Agent;
using AtlasAI.InAppAssistant.Services;
using AtlasAI.Services;

namespace AtlasAI.Tools;

public static class ActiveWindowSkill
{
    private const uint WM_CLOSE = 0x0010;

    private sealed class PendingClose
    {
        public DateTime CreatedUtc { get; init; }
        public IntPtr WindowHandle { get; init; }
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = "";
        public string WindowTitle { get; init; } = "";
        public string PreviewText { get; init; } = "";
    }

    private static readonly object Gate = new();
    private static PendingClose? _pendingClose;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly WindowsContextService Context = new();

    private static void RecordActionSafe(string action)
    {
        try
        {
            var mem = AtlasAI.App.GetService<IMemoryService>();
            mem?.RecordAction(action);
        }
        catch
        {
        }
    }

    public static async Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        var raw = userMessage.Trim();
        var msg = raw.ToLowerInvariant();

        // Pending close confirmation flow.
        var pending = GetPendingClose();
        if (pending != null)
        {
            if (IsConfirm(msg))
                return ConfirmPendingClose(pending);

            if (IsCancel(msg))
            {
                ClearPendingClose();
                return "✅ Cancelled pending close.";
            }

            if (IsShowPreview(msg))
                return pending.PreviewText;
        }

        if (IsWhatsThis(msg))
        {
            RecordActionSafe("what's this");
            return DescribeActiveWindow();
        }

        if (IsWhatsOpen(msg))
        {
            RecordActionSafe("what's open");
            return DescribeActiveWindow();
        }

        if (IsCloseThis(msg))
        {
            RecordActionSafe("close this");
            return CloseActiveWindow();
        }

        if (IsActiveWindowQuery(msg))
        {
            RecordActionSafe("active window");
            return DescribeActiveWindow(verbose: true);
        }

        if (TryParseSwitchTo(raw, out var targetApp))
        {
            RecordActionSafe($"switch to {targetApp}");
            return await SwitchToAppAsync(targetApp, ct);
        }

        return null;
    }

    private static bool IsWhatsThis(string msg)
    {
        return msg == "what's this" || msg == "whats this" || msg == "what is this" ||
               msg == "what am i looking at" || msg == "what is this window" ||
               msg == "what app is this";
    }

    private static bool IsWhatsOpen(string msg)
    {
        return msg == "what is open" || msg == "what's open" || msg == "whats open" ||
               msg == "what is currently open" || msg == "what's currently open" || msg == "whats currently open";
    }

    private static bool IsActiveWindowQuery(string msg)
    {
        return msg == "active window" || msg == "what's in focus" || msg == "whats in focus" ||
               msg == "what is in focus" || msg == "what app is in focus" ||
               msg == "what window is active";
    }

    private static bool IsCloseThis(string msg)
    {
        return msg == "close this" || msg == "close this window" || msg == "close this app" ||
               msg == "close the active window" || msg == "close active window";
    }

    private static bool TryParseSwitchTo(string raw, out string target)
    {
        target = "";
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Avoid stealing in-app navigation: NavigationSkill already handles "switch to chat/media/dj/downloads/security/...".
        var lower = s.ToLowerInvariant();
        if (!lower.StartsWith("switch to ", StringComparison.Ordinal) &&
            !lower.StartsWith("focus ", StringComparison.Ordinal) &&
            !lower.StartsWith("activate ", StringComparison.Ordinal))
            return false;

        var m = System.Text.RegularExpressions.Regex.Match(s, "^(switch to|focus|activate)\\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        var q = (m.Groups[2].Value ?? "").Trim();
        if (q.StartsWith("app ", StringComparison.OrdinalIgnoreCase)) q = q.Substring(4).Trim();
        if (q.StartsWith("window ", StringComparison.OrdinalIgnoreCase)) q = q.Substring(7).Trim();
        if (string.IsNullOrWhiteSpace(q)) return false;

        var qLower = q.ToLowerInvariant();
        if (qLower == "chat" || qLower.Contains("media") || qLower.Contains("dj") || qLower.Contains("download") || qLower.Contains("security") || qLower.Contains("create") || qLower == "code")
            return false;

        target = q;
        return true;
    }

    private static async Task<string> SwitchToAppAsync(string targetApp, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var r = await EnhancedAppControl.FocusAppAsync(targetApp);
            return string.IsNullOrWhiteSpace(r) ? $"❌ Couldn't switch to {targetApp}." : r;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ActiveWindowSkill] SwitchTo failed: {ex.Message}");
            return $"❌ Couldn't switch to {targetApp}.";
        }
    }

    private static string DescribeActiveWindow(bool verbose = false)
    {
        try
        {
            var ctx = Context.GetActiveAppContext();
            var title = (ctx.WindowTitle ?? "").Trim();
            var process = (ctx.ProcessName ?? "Unknown").Trim();

            if (ctx.ProcessId == 0 && string.IsNullOrWhiteSpace(title))
                return "❌ I couldn't read the active window right now.";

            var friendly = FriendlyProcessName(process);
            var docHint = ExtractDocumentHint(title);

            if (!verbose)
            {
                if (!string.IsNullOrWhiteSpace(docHint))
                    return $"Looks like you've got {friendly} open on {docHint}.";

                if (!string.IsNullOrWhiteSpace(title))
                    return $"Looks like you've got {friendly} open: {title}.";

                return $"Looks like {friendly} is in focus.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("🪟 Active window");
            sb.AppendLine($"- App: {friendly} ({process})");
            if (ctx.ProcessId > 0) sb.AppendLine($"- PID: {ctx.ProcessId}");
            if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine($"- Title: {title}");
            if (!string.IsNullOrWhiteSpace(ctx.ExecutablePath)) sb.AppendLine($"- Path: {ctx.ExecutablePath}");
            sb.AppendLine($"- Category: {ctx.Category}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ActiveWindowSkill] Describe failed: {ex.Message}");
            return "❌ I couldn't read the active window right now.";
        }
    }

    private static string CloseActiveWindow()
    {
        try
        {
            var ctx = Context.GetActiveAppContext();
            if (ctx.WindowHandle == IntPtr.Zero)
                return "❌ No active window handle found.";

            var currentPid = 0;
            try { currentPid = Process.GetCurrentProcess().Id; } catch { currentPid = 0; }
            if (currentPid != 0 && ctx.ProcessId == currentPid)
                return "⚠️ The active window is Atlas. I won't close myself.";

            var proc = (ctx.ProcessName ?? "").Trim();
            var title = (ctx.WindowTitle ?? "").Trim();

            if (IsSensitiveProcess(proc))
            {
                var preview = BuildSensitiveClosePreview(proc, title, ctx.ProcessId, ctx.ExecutablePath);
                lock (Gate)
                {
                    _pendingClose = new PendingClose
                    {
                        CreatedUtc = DateTime.UtcNow,
                        WindowHandle = ctx.WindowHandle,
                        ProcessId = ctx.ProcessId,
                        ProcessName = proc,
                        WindowTitle = title,
                        PreviewText = preview,
                    };
                }
                return preview;
            }
            var ok = PostMessage(ctx.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (!ok)
                return $"⚠️ I couldn't send a close request (Win32 error {Marshal.GetLastWin32Error()}).";

            var procLabel = string.IsNullOrWhiteSpace(proc) ? "(unknown app)" : proc;
            if (string.IsNullOrWhiteSpace(title))
                return $"✅ Sent close request to {procLabel}.";

            return $"✅ Sent close request to: {title} ({procLabel}).";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ActiveWindowSkill] Close failed: {ex.Message}");
            return "❌ Couldn't close the active window.";
        }
    }

    private static string ConfirmPendingClose(PendingClose pending)
    {
        ClearPendingClose();

        try
        {
            if (pending.WindowHandle == IntPtr.Zero)
                return "❌ Pending close window handle missing.";

            var ok = PostMessage(pending.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (!ok)
                return $"⚠️ I couldn't send a close request (Win32 error {Marshal.GetLastWin32Error()}).";

            var proc = string.IsNullOrWhiteSpace(pending.ProcessName) ? "(unknown app)" : pending.ProcessName;
            if (string.IsNullOrWhiteSpace(pending.WindowTitle))
                return $"✅ Sent close request to {proc}.";

            return $"✅ Sent close request to: {pending.WindowTitle} ({proc}).";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ActiveWindowSkill] Confirm close failed: {ex.Message}");
            return "❌ Couldn't close the active window.";
        }
    }

    private static PendingClose? GetPendingClose()
    {
        lock (Gate)
        {
            if (_pendingClose == null) return null;
            if ((DateTime.UtcNow - _pendingClose.CreatedUtc) > TimeSpan.FromMinutes(2))
            {
                _pendingClose = null;
                return null;
            }
            return _pendingClose;
        }
    }

    private static void ClearPendingClose()
    {
        lock (Gate)
        {
            _pendingClose = null;
        }
    }

    private static bool IsConfirm(string lower)
    {
        var s = (lower ?? "").Trim();
        return s == "confirm" || s == "yes" || s == "y" || s == "ok" || s == "okay" ||
               s.StartsWith("confirm ", StringComparison.Ordinal) || s.StartsWith("yes ", StringComparison.Ordinal) ||
               s.StartsWith("ok ", StringComparison.Ordinal) || s.StartsWith("okay ", StringComparison.Ordinal);
    }

    private static bool IsCancel(string lower)
    {
        var s = (lower ?? "").Trim();
        return s == "cancel" || s == "no" || s == "n" || s == "stop" ||
               s.StartsWith("cancel ", StringComparison.Ordinal) || s.StartsWith("no ", StringComparison.Ordinal);
    }

    private static bool IsShowPreview(string lower)
    {
        var s = (lower ?? "").Trim();
        return s == "preview" || s == "show preview" || s == "show me the preview" || s == "show me preview";
    }

    private static bool IsSensitiveProcess(string processName)
    {
        var p = (processName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(p)) return true;

        // Conservative: require confirm for core Windows/session processes.
        return p.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("svchost", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("wininit", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("csrss", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("services", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("lsass", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("smss", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("system", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("registry", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("taskmgr", StringComparison.OrdinalIgnoreCase) ||
               p.Equals("securityhealthservice", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSensitiveClosePreview(string process, string title, int pid, string? exePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("⚠️ That looks like a system/sensitive process window.");
        sb.AppendLine($"- App: {FriendlyProcessName(process)} ({process})");
        if (pid > 0) sb.AppendLine($"- PID: {pid}");
        if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine($"- Title: {title}");
        if (!string.IsNullOrWhiteSpace(exePath)) sb.AppendLine($"- Path: {exePath}");
        sb.AppendLine("Reply 'confirm' to execute, or 'cancel' to abort.");
        return sb.ToString().TrimEnd();
    }

    private static string FriendlyProcessName(string process)
    {
        var p = (process ?? "").Trim();
        if (string.IsNullOrWhiteSpace(p)) return "an app";

        return p.ToLowerInvariant() switch
        {
            "code" => "Visual Studio Code",
            "devenv" => "Visual Studio",
            "chrome" => "Chrome",
            "msedge" => "Microsoft Edge",
            "firefox" => "Firefox",
            "explorer" => "File Explorer",
            "notepad" => "Notepad",
            "pwsh" => "PowerShell",
            "powershell" => "PowerShell",
            _ => p
        };
    }

    private static string? ExtractDocumentHint(string title)
    {
        var t = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return null;

        var idx = t.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
        {
            var left = t.Substring(0, idx).Trim();
            if (!string.IsNullOrWhiteSpace(left) && left.Length <= 120)
                return left;
        }

        return null;
    }
}
