using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI;

namespace AtlasAI.Tools;

public static class NavigationSkill
{
    private static readonly object Gate = new();
    private static readonly Stack<string> BackStack = new();
    private static readonly Stack<string> ForwardStack = new();
    private static string _currentTab;

    private static readonly (string Tab, string SidebarKey, string[] Tokens)[] Modules =
    {
        ("AI CHAT", "Chat", new[] { "chat", "ai chat" }),
        ("AI MEDIA CENTRE", "Media", new[] { "media", "media centre", "media center", "centre", "center" }),
        ("AI DJ BOOTH", "DJ", new[] { "dj", "dj booth", "music", "booth" }),
        ("AI DOWNLOADS", "Downloads", new[] { "downloads", "download", "download manager" }),
        ("AI SECURITY", "Security", new[] { "security", "security suite", "system monitor" }),
        ("AI CREATE", "Create", new[] { "create", "creator" }),
        ("AI CODE", "Code", new[] { "code", "coding", "dev", "developer" }),
    };

    public static async Task<string> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        var raw = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var msg = Normalize(raw);

        // back / forward
        if (IsBack(msg))
            return await NavigateBackAsync(ct);

        if (IsForward(msg))
            return await NavigateForwardAsync(ct);

        // open settings
        if (IsOpenSettings(msg))
            return await OpenSettingsAsync(ct);

        // focus search
        if (IsFocusSearch(msg))
            return await FocusSearchAsync(ct);

        // open module
        if (TryParseOpenModule(msg, out var tab, out var sidebarKey))
            return await OpenModuleAsync(tab, sidebarKey, ct);

        return null;
    }

    private static async Task<string> OpenModuleAsync(string tab, string sidebarKey, CancellationToken ct)
    {
        var window = GetCommandCenterWindow();
        if (window == null)
            return $"⚠️ Can't navigate: Command Center window not found.\nACTION: OPEN_MODULE {tab}";

        ct.ThrowIfCancellationRequested();

        lock (Gate)
        {
            InitializeCurrentTabIfNeeded(window);

            if (!string.Equals(_currentTab, tab, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(_currentTab))
                    BackStack.Push(_currentTab!);

                _currentTab = tab;
                ForwardStack.Clear();
            }
        }

        await window.Dispatcher.InvokeAsync(() => window.NavigateToTab(tab, sidebarKey));
        return $"✅ Switched to {tab}.\nACTION: OPEN_MODULE {tab}";
    }

    private static async Task<string> NavigateBackAsync(CancellationToken ct)
    {
        var window = GetCommandCenterWindow();
        if (window == null)
            return "⚠️ Can't go back: Command Center window not found.\nACTION: BACK";

        ct.ThrowIfCancellationRequested();

        string target = null;
        lock (Gate)
        {
            InitializeCurrentTabIfNeeded(window);

            if (BackStack.Count > 0)
            {
                target = BackStack.Pop();
                if (!string.IsNullOrWhiteSpace(_currentTab))
                    ForwardStack.Push(_currentTab!);
                _currentTab = target;
            }
        }

        if (string.IsNullOrWhiteSpace(target))
            return "↩ Nothing to go back to.\nACTION: BACK";

        await window.Dispatcher.InvokeAsync(() => window.NavigateToTab(target!));
        return $"✅ Back to {target}.\nACTION: BACK {target}";
    }

    private static async Task<string> NavigateForwardAsync(CancellationToken ct)
    {
        var window = GetCommandCenterWindow();
        if (window == null)
            return "⚠️ Can't go forward: Command Center window not found.\nACTION: FORWARD";

        ct.ThrowIfCancellationRequested();

        string target = null;
        lock (Gate)
        {
            InitializeCurrentTabIfNeeded(window);

            if (ForwardStack.Count > 0)
            {
                target = ForwardStack.Pop();
                if (!string.IsNullOrWhiteSpace(_currentTab))
                    BackStack.Push(_currentTab!);
                _currentTab = target;
            }
        }

        if (string.IsNullOrWhiteSpace(target))
            return "↪ Nothing to go forward to.\nACTION: FORWARD";

        await window.Dispatcher.InvokeAsync(() => window.NavigateToTab(target!));
        return $"✅ Forward to {target}.\nACTION: FORWARD {target}";
    }

    private static async Task<string> OpenSettingsAsync(CancellationToken ct)
    {
        var window = GetCommandCenterWindow();
        if (window == null)
        {
            // Settings can still open even if Command Center isn't found.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var existing = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsLoaded);
                    if (existing != null)
                    {
                        existing.Activate();
                        if (existing.WindowState == WindowState.Minimized)
                            existing.WindowState = WindowState.Normal;
                        return;
                    }

                    var w2 = new SettingsWindow();
                    w2.Owner = Application.Current?.MainWindow;
                    w2.ShowDialog();
                }
                catch
                {
                }
            });

            return "⚙️ Opened settings.\nACTION: OPEN_SETTINGS";
        }

        ct.ThrowIfCancellationRequested();
        await window.Dispatcher.InvokeAsync(() => window.OpenAtlasSettings());
        return "⚙️ Opened settings.\nACTION: OPEN_SETTINGS";
    }

    private static async Task<string> FocusSearchAsync(CancellationToken ct)
    {
        var window = GetCommandCenterWindow();
        if (window == null)
            return "⚠️ Can't focus search: Command Center window not found.\nACTION: FOCUS_SEARCH";

        ct.ThrowIfCancellationRequested();

        var focused = await window.Dispatcher.InvokeAsync(() => window.TryFocusSearch());
        return focused
            ? "🔎 Search box focused.\nACTION: FOCUS_SEARCH"
            : "⚠️ Couldn't find a search box in the current view.\nACTION: FOCUS_SEARCH";
    }

    private static CommandCenterWindow GetCommandCenterWindow()
    {
        try
        {
            if (Application.Current == null) return null;

            // Prefer an active CommandCenterWindow if present.
            foreach (Window w in Application.Current.Windows)
            {
                if (w is CommandCenterWindow ccw && ccw.IsLoaded)
                {
                    if (ccw.IsActive) return ccw;
                }
            }

            return Application.Current.Windows.OfType<CommandCenterWindow>().FirstOrDefault(w => w.IsLoaded)
                   ?? Application.Current.MainWindow as CommandCenterWindow;
        }
        catch
        {
            return null;
        }
    }

    private static void InitializeCurrentTabIfNeeded(CommandCenterWindow window)
    {
        if (!string.IsNullOrWhiteSpace(_currentTab)) return;

        try
        {
            var tab = (window.CurrentTab ?? string.Empty).Trim();
            _currentTab = string.IsNullOrWhiteSpace(tab) ? "AI CHAT" : tab;
        }
        catch
        {
            _currentTab = "AI CHAT";
        }
    }

    private static bool TryParseOpenModule(string normalizedMsg, out string tab, out string sidebarKey)
    {
        tab = string.Empty;
        sidebarKey = string.Empty;

        if (!ContainsAny(normalizedMsg, "open ", "go to ", "goto ", "switch to ", "show ", "take me to "))
            return false;

        foreach (var m in Modules)
        {
            if (m.Tokens.Any(t => normalizedMsg.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                tab = m.Tab;
                sidebarKey = m.SidebarKey;
                return true;
            }
        }

        return false;
    }

    private static bool IsBack(string msg)
        => msg == "back" || msg == "go back" || msg == "previous" || msg == "prev" || msg == "back please" || msg.StartsWith("go back ", StringComparison.Ordinal);

    private static bool IsForward(string msg)
        => msg == "forward" || msg == "go forward" || msg == "next" || msg == "forward please" || msg.StartsWith("go forward ", StringComparison.Ordinal);

    private static bool IsOpenSettings(string msg)
    {
        // Prefer in-app settings only for explicit/short requests.
        // Avoid hijacking Windows settings pages: "wifi settings", "bluetooth settings", etc.
        if (!ContainsAny(msg, "settings", "preferences", "prefs")) return false;

        if (ContainsAny(msg, "wifi", "bluetooth", "display", "sound", "network", "windows settings", "system settings"))
            return false;

        return msg == "settings" || msg == "open settings" || msg == "open preferences" || msg == "preferences" || msg == "open prefs" || msg.Contains("atlas settings", StringComparison.OrdinalIgnoreCase) || msg.Contains("app settings", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFocusSearch(string msg)
        => ContainsAny(msg, "focus search", "focus the search", "focus search box", "focus search bar", "go to search", "search bar");

    private static string Normalize(string text)
    {
        var s = (text ?? string.Empty).Trim().ToLowerInvariant();
        s = s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        s = s.Replace("\t", " ", StringComparison.Ordinal);
        while (s.Contains("  ", StringComparison.Ordinal)) s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }

    private static bool ContainsAny(string msg, params string[] parts)
    {
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (msg.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
