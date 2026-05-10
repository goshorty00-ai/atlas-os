using System;
using System.Collections.Generic;
using System.Linq;
using AtlasAI.Core;

namespace AtlasAI.Services;

/// <summary>
/// Minimal, non-creepy personality learning.
/// Stores only small preference signals in PreferencesStore (IDs/levels), never message history.
/// </summary>
public static class PersonalityLearningService
{
    private static readonly string[] ProfanityKeywords =
    {
        "fuck", "fucking", "shit", "shite", "bollocks", "ffs", "feck", "feckin", "crap", "damn", "wtf"
    };

    /// <summary>
    /// Observe a user message and (optionally) auto-adjust banter level upward if the user swears repeatedly.
    /// Returns the new banter level if changed.
    /// </summary>
    public static int? ObserveUserMessageForBanter(string userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) return null;

        var lower = t.ToLowerInvariant();
        if (!ContainsAny(lower, ProfanityKeywords))
            return null;

        var now = DateTime.UtcNow;
        var prefs = PreferencesStore.Instance.Current;

        int? changedTo = null;

        PreferencesStore.Instance.Update(p =>
        {
            // (Re)start a short learning window.
            if (p.ProfanityLearningWindowStartUtc == DateTime.MinValue || (now - p.ProfanityLearningWindowStartUtc).TotalMinutes > 60)
            {
                p.ProfanityLearningWindowStartUtc = now;
                p.ProfanityLearningHits = 0;
            }

            p.ProfanityLearningHits++;

            // Only adjust at most once per 12 hours.
            var canAdjust = p.LastBanterAutoAdjustUtc == DateTime.MinValue || (now - p.LastBanterAutoAdjustUtc).TotalHours >= 12;

            // Threshold: 3 profanity-hits within the 60 min window.
            if (canAdjust && p.ProfanityLearningHits >= 3)
            {
                var before = Math.Clamp(p.ChatBanterLevel, 1, 5);
                var after = Math.Clamp(before + 1, 1, 5);
                if (after != before)
                {
                    p.ChatBanterLevel = after;
                    changedTo = after;
                }

                p.LastBanterAutoAdjustUtc = now;
            }
        });

        return changedTo;
    }

    /// <summary>
    /// Normalize Command Center tab/module IDs into stable strings.
    /// </summary>
    public static string NormalizeModuleId(string tabName)
    {
        var t = (tabName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) return "";

        // Keep existing IDs stable for storage.
        return t.ToUpperInvariant();
    }

    public static (string ModuleId, int Count)? GetMostUsedModuleSnapshot(UserPreferences prefs)
    {
        try
        {
            if (prefs?.MostUsedModules == null || prefs.MostUsedModules.Count == 0)
                return null;

            var best = prefs.MostUsedModules
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(best.Key) || best.Value <= 0)
                return null;

            return (best.Key, best.Value);
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
