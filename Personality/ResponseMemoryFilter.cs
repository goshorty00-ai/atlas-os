using System;
using System.Linq;
using System.Text.RegularExpressions;
using AtlasAI.Brain;

namespace AtlasAI.Personality
{
    internal static class ResponseMemoryFilter
    {
        public static string Apply(PersonalityProfile profile, string text, ShortTermMemory memory)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var recent = memory.GetRecentAssistantTexts(20);
            if (recent.Length == 0) return text;

            var normalized = Normalize(text);
            foreach (var prev in recent)
            {
                var sim = Similarity(normalized, Normalize(prev));
                if (sim >= 0.7)
                {
                    var varied = ResponseVariationEngine.Apply(profile, text, memory);
                    if (Similarity(Normalize(varied), Normalize(prev)) < 0.7)
                        return varied;
                    break;
                }
            }
            return text;
        }

        private static string Normalize(string s)
        {
            var x = s.ToLowerInvariant();
            x = Regex.Replace(x, @"\s+", " ").Trim();
            return x;
        }

        private static double Similarity(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
            var ta = a.Split(' ').Where(t => t.Length > 2).Distinct().ToArray();
            var tb = b.Split(' ').Where(t => t.Length > 2).Distinct().ToArray();
            if (ta.Length == 0 || tb.Length == 0) return 0;
            var inter = ta.Intersect(tb).Count();
            var union = ta.Union(tb).Count();
            return union == 0 ? 0 : (double)inter / union;
        }
    }
}
