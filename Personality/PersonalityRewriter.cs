using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AtlasAI.Brain;

namespace AtlasAI.Personality
{
    public static class PersonalityRewriter
    {
        private static readonly Regex CodeFence = new(@"```[\s\S]*?```", RegexOptions.Compiled);
        private static readonly Regex PlanBlock = new(@"\[(PLAN_JSON|OPEN_PATH)\][\s\S]*?\[\/\1\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Combined = new("(" + @"```[\s\S]*?```" + ")|(" + @"\[(PLAN_JSON|OPEN_PATH)\][\s\S]*?\[\/\1\]" + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Apply(DeepPersonalityProfile profile, string finalText, AgentContext ctx, string userText)
        {
            if (string.IsNullOrWhiteSpace(finalText)) return finalText ?? "";

            // Special routing for greeting / frustration / simple triggers
            var lowered = (userText ?? "").Trim().ToLowerInvariant();
            if (Regex.IsMatch(lowered, @"^(hello|hi|hey)\\b") ||
                Regex.IsMatch(lowered, @"^(just\\s+)?say\\s+(hello|hi|hey)\\b") ||
                Regex.IsMatch(lowered, @"^(give\\s+me\\s+)?a\\s+(quick\\s+)?(hello|hi|hey)\\b") ||
                Regex.IsMatch(lowered, @"^(greet\\s+me|say\\s+hi\\s+to\\s+me)\\b"))
            {
                var greet = GreetingBank.GetGreeting(profile.Type, DateTime.Now);
                return greet;
            }
            if (lowered.Contains("repeating yourself") || lowered.Contains("repeat yourself"))
            {
                var fr = AntiRepeatMemory.Pick($"frustration:{profile.Type}", profile.FrustrationResponses, 5);
                return fr;
            }

            var parts = Split(finalText);
            var sb = new StringBuilder(finalText.Length + 32);

            foreach (var p in parts)
            {
                if (p.Protected)
                {
                    sb.Append(p.Text);
                    continue;
                }
                var lines = p.Text.Replace("\r\n", "\n").Split('\n');
                lines = AntiRepeatMemory.AvoidBackToBackLines(lines).ToArray();
                var processed = AdjustTone(profile, lines);
                sb.Append(processed);
            }

#if PERSONAL_BUILD
            if (profile.Type == PersonalityType.Unfiltered)
            {
                sb.Clear();
                foreach (var p in parts)
                {
                    if (p.Protected) sb.Append(p.Text);
                    else sb.Append(UnfilteredStyleEngine.Apply(p.Text, ctx));
                }
            }
#endif
            return sb.ToString();
        }

        private static List<Chunk> Split(string text)
        {
            var list = new List<Chunk>();
            var idx = 0;
            foreach (Match m in Combined.Matches(text))
            {
                if (m.Index > idx) list.Add(new Chunk(text.Substring(idx, m.Index - idx), false));
                list.Add(new Chunk(m.Value, true));
                idx = m.Index + m.Length;
            }
            if (idx < text.Length) list.Add(new Chunk(text.Substring(idx), false));
            return list;
        }

        private static string AdjustTone(DeepPersonalityProfile profile, string[] lines)
        {
            var body = string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l)));

            // Rhythm / verbosity
            body = profile.Verbosity switch
            {
                VerbosityDefault.Short => Compress(body, 2),
                VerbosityDefault.Medium => Compress(body, 6),
                _ => body
            };

            // Tone hints (lightweight, avoid content changes)
            if (profile.Tone == ToneKind.Formal)
            {
                body = body.Replace("!", ".").Replace(" okay ", " ", StringComparison.OrdinalIgnoreCase);
            }
            else if (profile.Tone == ToneKind.Technical)
            {
                body = Regex.Replace(body, @"\bI think\b", "Evidence suggests", RegexOptions.IgnoreCase);
            }

            return body;
        }

        private static string Compress(string text, int maxLines)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Take(maxLines);
            return string.Join(Environment.NewLine, lines);
        }

        private readonly struct Chunk
        {
            public Chunk(string text, bool @protected)
            {
                Text = text;
                Protected = @protected;
            }
            public string Text { get; }
            public bool Protected { get; }
        }
    }
}
