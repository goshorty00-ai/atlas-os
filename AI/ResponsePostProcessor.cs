using System;
using System.Text.RegularExpressions;
using AtlasAI.Core;
using AtlasAI.Settings;
using AtlasAI.Personality;
using AtlasAI.Brain;

namespace AtlasAI.AI;

internal static class ResponsePostProcessor
{
    // User requirement: strip roleplay/stage direction markers if they slip through.
    // - Remove: *...* and [...] for short action tags
    // - Remove: (...) only when it looks like an action tag
    private static readonly Regex AsteriskActionRegex = new(@"\*[^*\r\n]{1,40}\*", RegexOptions.Compiled);
    private static readonly Regex BracketActionRegex = new(@"\[[^\]\r\n]{1,40}\]", RegexOptions.Compiled);
    private static readonly Regex ParenCandidateRegex = new(@"\(([^)""\r\n]{1,40})\)", RegexOptions.Compiled);

    private static readonly Regex ActionKeywordRegex = new(@"\b(sigh|sighs|chuckle|chuckles|laugh|laughs|groan|groans|snort|snorts|snicker|snickers|smirk|smirks|grin|grins|eyeroll|eye\s*roll|shrug|shrugs|dramatic|dramatically|whisper|whispers)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string CleanAssistantText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var original = text;

        // Remove *...* and [...] blocks (short) outright.
        text = AsteriskActionRegex.Replace(text, "");
        text = BracketActionRegex.Replace(text, "");

        // Remove (...) only if it looks like an action tag.
        text = ParenCandidateRegex.Replace(text, m =>
        {
            var inner = (m.Groups[1].Value ?? string.Empty).Trim();
            if (LooksLikeActionTag(inner))
                return "";
            return m.Value;
        });

        // If any action keywords remain, try to nudge them out when they appear as standalone interjections.
        // (Avoids over-stripping normal prose.)
        text = Regex.Replace(text, @"(^|\n)\s*(sighs|chuckles|laughs)\b\s*[,\.-]*\s*", "$1", RegexOptions.IgnoreCase);

        // Collapse whitespace from removals.
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = text.Trim();

        if (!string.Equals(original, text, StringComparison.Ordinal))
        {
            try
            {
                AppLogger.LogInfo("[PostProcess] Stripped stage-direction markers from assistant output.");
            }
            catch
            {
            }
        }

        // Debug signal if the model still tries to sneak them in.
        try
        {
            if (ActionKeywordRegex.IsMatch(text))
                AppLogger.LogWarning("[PostProcess] Stage-direction keyword still present after cleanup.");
        }
        catch
        {
        }

        // Enforce “no AI/human” phrasing across all models.
        try
        {
            text = NormalizeIdentityAndHumanPhrasing(text);
        }
        catch
        {
        }

        return text;
    }

    public static string CleanAssistantText(string? text, string? latestUserText)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        text = CleanAssistantText(text);

        // Apply slider-driven tone pass so behavior is consistent across providers/models.
        try
        {
            text = ApplyPersonalityRewrite(text, latestUserText);
        }
        catch
        {
        }

        // Redact absolute paths unless the user explicitly asked for a path/location.
        try
        {
            text = RedactLocalPathsUnlessAsked(text, latestUserText);
        }
        catch
        {
        }

        return text;
    }

    private static string RedactLocalPathsUnlessAsked(string text, string? latestUserText)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // If the user asked for a path or provided one, don't redact.
        var user = (latestUserText ?? string.Empty);
        var userAskedForPaths =
            Regex.IsMatch(user, @"[A-Za-z]:\\", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(user, @"\\\\", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(user, @"\b(path|file|files|folder|directory|location|where is|where's|full path)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (userAskedForPaths)
            return text;

        var segments = SplitByCodeFences(text);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].isCode) continue;
            var s = segments[i].content;

            // Windows drive paths like D:\Atlas.OS\bin\x64\
            s = Regex.Replace(
                s,
                @"(?<![A-Za-z0-9_])([A-Za-z]:\\[^\s""'<>\r\n]+)",
                "[path]",
                RegexOptions.CultureInvariant);

            // UNC paths like \\server\share\folder
            s = Regex.Replace(
                s,
                @"(?<![A-Za-z0-9_])(\\\\[^\s""'<>\r\n\\]+\\[^\s""'<>\r\n]+)",
                "[path]",
                RegexOptions.CultureInvariant);

            segments[i] = (s, false);
        }

        return RejoinSegments(segments);
    }

    private static string NormalizeIdentityAndHumanPhrasing(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Avoid modifying code fences.
        var segments = SplitByCodeFences(text);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].isCode) continue;
            var s = segments[i].content;

            // Remove common identity disclaimers.
            s = Regex.Replace(s, @"\bAs an (AI|artificial intelligence)( language model)?\b\s*,?\s*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI(?:'m| am) an (AI|artificial intelligence)( language model)?\b\s*,?\s*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI(?:'m| am) (?:just|only) an? (AI|assistant|model)\b\s*,?\s*", "", RegexOptions.IgnoreCase);

            // Replace “humans” wording.
            s = Regex.Replace(s, @"\bhumans?\b", "people", RegexOptions.IgnoreCase);

            // Clean up any double spaces left by removals.
            s = Regex.Replace(s, @"[ \t]{2,}", " ");
            segments[i] = (s, false);
        }

        return RejoinSegments(segments);
    }

    private static string ApplyPersonalityRewrite(string text, string? latestUserText)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var settings = SettingsStore.Current;
        var level = Math.Clamp(settings?.UnfilteredChaosIntensity ?? 3, 1, 5);

        // Level 1 should be clean/professional regardless of model behavior.
        if (level <= 1)
        {
            text = StripProfanity(text);
        }

        // Prefer the deep personality rewriter to keep tone consistent.
        var ctx = new AgentContext
        {
            ActiveSection = AtlasAI.Brain.SectionAgentContext.CurrentSection,
            CurrentPresence = AtlasAI.Brain.PresenceState.Working
        };

        var userText = latestUserText ?? string.Empty;

        var personalityType = PersonalityType.Butler;
        try
        {
            if (level <= 1)
            {
                personalityType = PersonalityType.Butler;
            }
            else if (level >= 4)
            {
                personalityType = PersonalityType.Unfiltered;
            }
            else
            {
                // Mid levels: respect user-selected personality.
                if (!Enum.TryParse(settings?.PersonalitySelected ?? "", ignoreCase: true, out personalityType))
                    personalityType = PersonalityType.Butler;
            }
        }
        catch
        {
            personalityType = PersonalityType.Butler;
        }

        ctx.CurrentPersonality = personalityType;

        var profile = DeepPersonalityProfile.Get(personalityType);
        var rewritten = PersonalityRewriter.Apply(profile, text, ctx, userText);
        var finalText = string.IsNullOrWhiteSpace(rewritten) ? text : rewritten.Trim();

        if (level >= 4 && !(settings?.UnfilteredAllowProfanity ?? true))
            finalText = StripProfanity(finalText);

        return finalText;
    }

    private static string StripProfanity(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var segments = SplitByCodeFences(text);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].isCode) continue;
            var s = segments[i].content;
            s = Regex.Replace(s, @"\bfuck(?:ing|ed|s)?\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bshit(?:ty|ting|s)?\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bcunt\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bwanker\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\basshole\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bprick\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bbollocks\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(bastard|arse)\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[ \t]{2,}", " ");
            s = Regex.Replace(s, @"\s+([\.,!?])", "$1");
            segments[i] = (s, false);
        }
        return RejoinSegments(segments);
    }

    private static (string content, bool isCode)[] SplitByCodeFences(string text)
    {
        // Very small, reliable splitter for ``` fenced code.
        if (text.IndexOf("```", StringComparison.Ordinal) < 0)
            return new[] { (text, false) };

        var parts = new System.Collections.Generic.List<(string content, bool isCode)>();
        var remaining = text;
        while (true)
        {
            var start = remaining.IndexOf("```", StringComparison.Ordinal);
            if (start < 0)
            {
                if (remaining.Length > 0) parts.Add((remaining, false));
                break;
            }

            if (start > 0)
                parts.Add((remaining.Substring(0, start), false));

            var afterStart = remaining.Substring(start);
            var end = afterStart.IndexOf("```", 3, StringComparison.Ordinal);
            if (end < 0)
            {
                // Unclosed fence; treat rest as code.
                parts.Add((afterStart, true));
                break;
            }

            var codeChunk = afterStart.Substring(0, end + 3);
            parts.Add((codeChunk, true));
            remaining = afterStart.Substring(end + 3);
        }
        return parts.ToArray();
    }

    private static string RejoinSegments((string content, bool isCode)[] segments)
    {
        if (segments.Length == 1) return segments[0].content;
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments)
            sb.Append(seg.content);
        return sb.ToString();
    }

    private static bool LooksLikeActionTag(string inner)
    {
        if (string.IsNullOrWhiteSpace(inner)) return false;

        // Most parentheses in normal writing are fine; only strip when it contains action-ish language.
        if (ActionKeywordRegex.IsMatch(inner))
            return true;

        // If it's very short and verb-y (e.g., "shrugs", "sigh") treat as stage direction.
        if (inner.Length <= 16)
        {
            var lowered = inner.ToLowerInvariant();
            if (lowered.EndsWith("s") || lowered.EndsWith("ing"))
                return true;
        }

        return false;
    }
}
