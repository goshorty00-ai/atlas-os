using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Services;
using AtlasAI.Views.AiChat.Services;

namespace AtlasAI.Tools;

public static class PersonalityMemorySkill
{
    public static Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        var raw = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return Task.FromResult<string?>(null);

        var lower = raw.ToLowerInvariant();
        var memory = (AtlasAI.App.GetService<IMemoryService>() ?? new MemoryService());
        try { memory.Initialize(); } catch { }

        // Commands:
        // - what do you remember about me
        // - forget my name
        // - set banter to 5
        // - change accent to Irish

        if (TryHandleForgetMyName(lower, memory, out var forgetReply))
            return Task.FromResult<string?>(forgetReply);

        if (TryHandleSetBanter(raw, memory, out var banterReply))
            return Task.FromResult<string?>(banterReply);

        if (TryHandleChangeAccent(raw, memory, out var accentReply))
            return Task.FromResult<string?>(accentReply);

        if (!IsMemoryQuery(lower))
            return Task.FromResult<string?>(null);

        ct.ThrowIfCancellationRequested();

        var prefs = PreferencesStore.Instance.Current;

        LocalUserProfile? snap = null;
        try { snap = memory.GetSnapshot(); } catch { snap = null; }

        var name = (snap?.PreferredName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = (prefs.ChatPreferredName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            try
            {
                var mem = LocalPreferenceMemoryStore.Instance.Current;
                if (mem.ExplicitPreferences.TryGetValue("preferred_name", out var item))
                    name = (item?.Value ?? "").Trim();
            }
            catch
            {
            }
        }

        var accent = (snap?.Accent ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accent))
            accent = (prefs.ChatAccent ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accent))
        {
            try
            {
                var mem = LocalPreferenceMemoryStore.Instance.Current;
                if (mem.ExplicitPreferences.TryGetValue("accent", out var item))
                    accent = (item?.Value ?? "").Trim();
            }
            catch
            {
            }
        }

        var banter = Math.Clamp(snap?.BanterLevel ?? prefs.ChatBanterLevel, 1, 5);
        var verbosity = Math.Clamp(snap?.Verbosity ?? 3, 1, 5);
        var mostUsedModule = (snap?.MostUsedModule ?? "").Trim();
        var favoriteActions = ((snap?.FavoriteActions) ?? new System.Collections.Generic.List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        var mostUsed = PersonalityLearningService.GetMostUsedModuleSnapshot(prefs);

        var sb = new StringBuilder();
        sb.AppendLine("What I remember about you (local)");
        sb.AppendLine($"- Preferred name: {(string.IsNullOrWhiteSpace(name) ? "(not set)" : name)}");
        sb.AppendLine($"- Accent: {(string.IsNullOrWhiteSpace(accent) ? "(not set)" : accent)}");
        sb.AppendLine($"- Banter level: {banter}/5");
        sb.AppendLine($"- Verbosity: {verbosity}/5");

        if (!string.IsNullOrWhiteSpace(mostUsedModule))
            sb.AppendLine($"- Most used module (profile): {mostUsedModule}");

        if (mostUsed == null)
        {
            sb.AppendLine("- Most used module: (not enough data yet)");
        }
        else
        {
            sb.AppendLine($"- Most used module: {FriendlyModuleName(mostUsed.Value.ModuleId)} (opens={mostUsed.Value.Count})");
        }

        if (favoriteActions.Length > 0)
            sb.AppendLine($"- Favorite actions: {string.Join(", ", favoriteActions.Take(8))}");

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }

    private static bool TryHandleForgetMyName(string lower, IMemoryService memory, out string reply)
    {
        reply = "";
        if (lower != "forget my name" && lower != "forget my name." && lower != "forget my name!" &&
            lower != "forget my preferred name" && lower != "forget my preferred name." &&
            lower != "forget name" && lower != "forget my nickname")
            return false;

        try
        {
            memory.Update(p => p.PreferredName = "");
        }
        catch
        {
        }

        try { PreferencesStore.Instance.Update(p => p.ChatPreferredName = ""); } catch { }

        try
        {
            LocalPreferenceMemoryStore.Instance.Forget("name", out _, out _);
        }
        catch
        {
        }

        reply = "✅ Okay — I’ve forgotten your preferred name (local only).";
        return true;
    }

    private static bool TryHandleSetBanter(string raw, IMemoryService memory, out string reply)
    {
        reply = "";
        var cleaned = (raw ?? string.Empty).Trim().TrimEnd('.', '!', '?');
        var m = Regex.Match(cleaned,
            @"^\s*(?:set\s+)?banter(?:\s+level)?\s*(?:to\s+)?(?<n>[1-5])\s*$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        if (!int.TryParse(m.Groups["n"].Value, out var n)) return false;
        n = Math.Clamp(n, 1, 5);

        try { PreferencesStore.Instance.Update(p => p.ChatBanterLevel = n); } catch { }
        try { memory.Update(p => p.BanterLevel = n); } catch { }

        reply = $"✅ Banter level set to {n}/5.";
        return true;
    }

    private static bool TryHandleChangeAccent(string raw, IMemoryService memory, out string reply)
    {
        reply = "";
        var m = Regex.Match(raw ?? "",
            @"^\s*(?:change|set)\s+accent\s*(?:to\s+)?(?<a>.+?)\s*$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        var accent = (m.Groups["a"].Value ?? "").Trim().Trim('"', '\'', '.');
        accent = Regex.Replace(accent, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(accent))
        {
            reply = "❌ Please specify an accent (e.g., 'change accent to Irish').";
            return true;
        }

        if (accent.Length > 40)
            accent = accent.Substring(0, 40).Trim();

        try { PreferencesStore.Instance.Update(p => p.ChatAccent = accent); } catch { }
        try { memory.Update(p => p.Accent = accent); } catch { }

        reply = $"✅ Accent set to {accent}.";
        return true;
    }

    private static bool IsMemoryQuery(string lower)
    {
        if (lower == "what do you remember about me" || lower == "what do you remember" || lower == "what do you remember about me?" || lower == "what do you remember?" )
            return true;

        if (lower.Contains("remember") && lower.Contains("about me"))
            return true;

        if (lower.Contains("what") && lower.Contains("remember") && lower.Contains("me"))
            return true;

        return false;
    }

    private static string FriendlyModuleName(string id)
    {
        var t = (id ?? "").Trim().ToUpperInvariant();
        return t switch
        {
            "AI CHAT" => "AI Chat",
            "AI MEDIA CENTRE" => "Media Centre",
            "AI DJ BOOTH" => "DJ Booth",
            "AI DOWNLOADS" => "Downloads",
            "AI SECURITY" => "Security",
            "AI CREATE" => "Create",
            "AI CODE" => "Code",
            _ => string.IsNullOrWhiteSpace(id) ? "(unknown)" : id.Trim()
        };
    }
}
