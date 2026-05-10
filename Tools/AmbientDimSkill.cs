using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Core;

namespace AtlasAI.Tools;

public static class AmbientDimSkill
{
    public static Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Task.FromResult<string?>(null);

        var clean = userMessage.Trim();

        // Supported:
        // - enable ambient dim
        // - disable ambient dim
        // - ambient dim on/off
        var match = Regex.Match(clean,
            @"^\s*(?<verb>enable|disable)\s+ambient\s+dim\s*$",
            RegexOptions.IgnoreCase);

        bool? enabled = null;
        if (match.Success)
        {
            enabled = string.Equals(match.Groups["verb"].Value, "enable", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var match2 = Regex.Match(clean,
                @"^\s*ambient\s+dim\s+(?<state>on|off)\s*$",
                RegexOptions.IgnoreCase);
            if (match2.Success)
                enabled = string.Equals(match2.Groups["state"].Value, "on", StringComparison.OrdinalIgnoreCase);
        }

        if (!enabled.HasValue)
            return Task.FromResult<string?>(null);

        ct.ThrowIfCancellationRequested();

        PreferencesStore.Instance.Update(p => p.AmbientDimEnabled = enabled.Value);
        return Task.FromResult<string?>(enabled.Value ? "✅ Ambient dim enabled." : "✅ Ambient dim disabled.");
    }
}
