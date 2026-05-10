using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Tools;

public static class ChatPersonalitySkill
{
    private static readonly Dictionary<string, string> PersonalityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["butler"] = "Butler",
        ["unfiltered"] = "Unfiltered",
        ["chaostesting"] = "ChaosTesting",
        ["chaos testing"] = "ChaosTesting",
        ["irish mate"] = "Irish Mate",
        ["irish"] = "Irish Mate",
        ["media centremaster"] = "MediaCentreMaster",
        ["media centre master"] = "MediaCentreMaster",
        ["media center master"] = "MediaCentreMaster",
        ["mediacentremaster"] = "MediaCentreMaster",
        ["totaldj"] = "TotalDJ",
        ["total dj"] = "TotalDJ",
        ["dj"] = "TotalDJ",
    };

    private static readonly HashSet<string> Valid = new(StringComparer.OrdinalIgnoreCase)
    {
        "Butler",
        "Unfiltered",
        "ChaosTesting",
        "Irish Mate",
        "MediaCentreMaster",
        "TotalDJ",
    };

    public static Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Task.FromResult<string?>(null);

        var clean = userMessage.Trim();

        // Supported:
        // - switch personality to X
        // - set personality to X
        // - switch chat personality to X
        // - set chat personality X
        var match = Regex.Match(clean,
            @"^\s*(?:switch|set)\s+(?:chat\s+)?personality\s*(?:to\s+)?(?<p>.+?)\s*$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return Task.FromResult<string?>(null);

        ct.ThrowIfCancellationRequested();

        var raw = (match.Groups["p"].Value ?? "").Trim().Trim('"', '\'', '.');
        if (string.IsNullOrWhiteSpace(raw))
            return Task.FromResult<string?>("❌ Please specify a personality name.");

        var key = Regex.Replace(raw, @"\s+", " ").Trim();

        string? canonical = null;
        if (PersonalityMap.TryGetValue(key, out var mapped))
            canonical = mapped;
        else
            canonical = Valid.FirstOrDefault(v => string.Equals(v, raw, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(canonical) || !Valid.Contains(canonical))
            return Task.FromResult<string?>(
                "❌ Unknown personality. Try: Butler, Unfiltered, ChaosTesting, Irish Mate, MediaCentreMaster, TotalDJ.");

        PreferencesStore.Instance.Update(p => p.ChatPersonality = canonical);
        return Task.FromResult<string?>($"✅ Chat personality set to {canonical}.");
    }
}
