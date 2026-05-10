using System;
using System.Text.RegularExpressions;
// using AtlasAI.Monitoring;
using AtlasAI.Brain;

namespace AtlasAI.Personality
{
    internal enum EmotionalState
    {
        Neutral,
        Focused,
        Concerned,
        Amused,
        Direct
    }

    internal static class EmotionalStateEngine
    {
        private static class SystemStateMonitor
        {
            public static (int RamPercent, int CpuPercent) Current => (0, 0);
        }

        public static EmotionalState Evaluate(string userText, DateTime now)
        {
            var snap = SystemStateMonitor.Current;
            var lowered = (userText ?? string.Empty).ToLowerInvariant();
            var frustrated = lowered.Contains("this sucks") || lowered.Contains("annoying") ||
                             lowered.Contains("why isn't") || lowered.Contains("broken") ||
                             lowered.Contains("not working") || lowered.Contains("wtf") ||
                             lowered.Contains("damn") || lowered.Contains("ugh");

            if (frustrated || snap.RamPercent >= 85 || snap.CpuPercent >= 85)
                return EmotionalState.Concerned;

            if (now.Hour >= 8 && now.Hour <= 11)
                return EmotionalState.Focused;

            if (lowered.Contains("joke") || lowered.Contains("lol") || lowered.Contains("funny"))
                return EmotionalState.Amused;

            if (now.Hour >= 0 && now.Hour <= 5)
                return EmotionalState.Direct;

            return EmotionalState.Neutral;
        }

        public static string Apply(string text, EmotionalState state)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            return state switch
            {
                EmotionalState.Focused => Shorten(text, 2),
                EmotionalState.Concerned => AddGentleNote(text),
                EmotionalState.Amused => Lighten(text),
                EmotionalState.Direct => Shorten(text, 1),
                _ => text
            };
        }

        private static string Shorten(string text, int maxLines)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var count = Math.Min(lines.Length, Math.Max(1, maxLines));
            return string.Join(Environment.NewLine, lines.AsSpan(0, count).ToArray()).Trim();
        }

        private static string AddGentleNote(string text)
        {
            var firstLineEnd = text.IndexOfAny(new[] { '\r', '\n' });
            var firstLine = firstLineEnd > 0 ? text.Substring(0, firstLineEnd) : text;
            var rest = firstLineEnd > 0 ? text.Substring(firstLineEnd).TrimStart('\r', '\n') : "";
            var softened = Regex.Replace(firstLine, @"\b(Proceed|Continue)\b", "We can proceed carefully", RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(rest) ? softened : softened + Environment.NewLine + rest;
        }

        private static string Lighten(string text)
        {
            if (text.Length < 120) return text;
            return text.Replace("Next:", "Then:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
