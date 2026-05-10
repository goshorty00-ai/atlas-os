using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Tools;

public static class DjSuggestionSkill
{
    public static Task<string?> TryExecuteAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Task.FromResult<string?>(null);

        var clean = userMessage.Trim();

        if (!Regex.IsMatch(clean, @"^\s*suggest\s+load\s+track\s*$", RegexOptions.IgnoreCase))
            return Task.FromResult<string?>(null);

        ct.ThrowIfCancellationRequested();

        return Task.FromResult<string?>(
            "🎧 Ready when you are. Tell me what vibe you want (e.g., 'play upbeat house' or 'play chill lo-fi').");
    }
}
