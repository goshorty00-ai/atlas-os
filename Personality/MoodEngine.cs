using System;
using System.Text.RegularExpressions;

namespace AtlasAI.Personality
{
    public enum MoodState
    {
        Calm,
        Focused,
        Alert,
        Concerned,
#if PERSONAL_BUILD
        Irritated,
#endif
        Playful
    }

    public static class MoodEngine
    {
        public static MoodState FromSignals(double cpuUsage, int errorCount, int hourOfDay, double userSentiment)
        {
            if (errorCount >= 5) return MoodState.Concerned;
            if (cpuUsage >= 0.9 || (hourOfDay >= 0 && hourOfDay < 4)) return MoodState.Focused;
            if (cpuUsage >= 0.7 || errorCount >= 2) return MoodState.Alert;
            if (userSentiment < -0.4) return MoodState.Concerned;
            if (userSentiment > 0.6) return MoodState.Playful;
            return MoodState.Calm;
        }

        public static MoodState FromUserText(string userText, DateTime now)
        {
            var lower = (userText ?? "").ToLowerInvariant();
            var exclamations = Regex.Matches(lower, "!").Count;
            var errors = Regex.Matches(lower, "error|crash|fail|broken|issue|bug").Count;
#if PERSONAL_BUILD
            if (lower.Contains("wtf") || lower.Contains("this sucks") || lower.Contains("so stupid") || lower.Contains("why is this so slow"))
                return MoodState.Irritated;
#endif
            if (errors >= 3) return MoodState.Concerned;
            if (exclamations >= 2 || lower.Contains("urgent") || lower.Contains("now")) return MoodState.Alert;
            if (now.Hour >= 0 && now.Hour < 4) return MoodState.Focused;
            if (lower.StartsWith("hello") || lower.StartsWith("hi") || lower.StartsWith("hey")) return MoodState.Playful;
            return MoodState.Calm;
        }
    }
}
