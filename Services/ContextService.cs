using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using AtlasAI;
using AtlasAI.InAppAssistant.Models;
using AtlasAI.InAppAssistant.Services;

namespace AtlasAI.Services;

public sealed class ContextService : IContextService
{
    private static readonly WindowsContextService Windows = new();

    public ContextSnapshot CaptureSnapshot()
    {
        ActiveAppContext? active = null;

        try
        {
            active = Windows.GetActiveAppContext();
            var proc = (active.ProcessName ?? "").Trim();
            var title = (active.WindowTitle ?? "").Trim();
            var exe = (active.ExecutablePath ?? "").Trim();
            Debug.WriteLine($"[Context] ActiveWindow: process='{proc}' pid={active.ProcessId} title='{title}' exe='{exe}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Context] ActiveWindow: <error> {ex.Message}");
        }

        var (tabId, module) = TryGetAtlasModule();

        return new ContextSnapshot
        {
            CapturedAtUtc = DateTime.UtcNow,
            ActiveApp = active,
            AtlasTabId = tabId,
            AtlasModule = module,
        };
    }

    public string BuildPromptContextBlock(ContextSnapshot snapshot)
    {
        var s = snapshot ?? new ContextSnapshot();
        var active = s.ActiveApp;

        var sb = new StringBuilder();
        sb.AppendLine("CONTEXT (local / best-effort):");
        sb.AppendLine($"- Atlas module: {(string.IsNullOrWhiteSpace(s.AtlasModule) ? "Unknown" : s.AtlasModule)}");
        if (!string.IsNullOrWhiteSpace(s.AtlasTabId))
            sb.AppendLine($"- Atlas tab: {s.AtlasTabId}");

        if (active != null)
        {
            var proc = (active.ProcessName ?? "").Trim();
            var title = (active.WindowTitle ?? "").Trim();
            var exe = (active.ExecutablePath ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(proc)) sb.AppendLine($"- Active process: {proc}");
            if (active.ProcessId > 0) sb.AppendLine($"- Active PID: {active.ProcessId}");
            if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine($"- Active window title: {title}");
            if (!string.IsNullOrWhiteSpace(exe)) sb.AppendLine($"- Active exe path: {exe}");
        }

        return sb.ToString().TrimEnd();
    }

    private static (string TabId, string Module) TryGetAtlasModule()
    {
        try
        {
            var app = Application.Current;
            if (app == null) return ("", "");

            var commandCenter = app.Windows
                .OfType<CommandCenterWindow>()
                .FirstOrDefault(w => w != null);

            if (commandCenter == null) return ("", "");

            var tab = (commandCenter.CurrentTab ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(tab)) return ("", "");

            var module = tab.ToUpperInvariant() switch
            {
                "AI CHAT" => "Chat",
                "AI MEDIA CENTRE" => "Media",
                "AI DJ BOOTH" => "DJ",
                "AI DOWNLOADS" => "Downloader",
                "AI SECURITY" => "Security",
                _ => "Other",
            };

            return (tab, module);
        }
        catch
        {
            return ("", "");
        }
    }
}
