using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AtlasAI.Brain;

namespace AtlasAI.Personality
{
#if PERSONAL_BUILD
    public static class UnfilteredStyleEngine
    {
        private static readonly Regex CodeFence = new(@"```[\s\S]*?```", RegexOptions.Compiled);
        private static readonly Regex PlanBlock = new(@"\[(PLAN_JSON|OPEN_PATH)\][\s\S]*?\[\/\1\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Apply(string text, AgentContext ctx)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";
            var segments = SplitProtected(text);
            var sb = new StringBuilder(text.Length + 32);
            var intensity = ProfanityControl.GetIntensity();
            foreach (var seg in segments)
            {
                if (seg.Protected)
                {
                    sb.Append(seg.Content);
                }
                else
                {
                    var s = Tone(seg.Content, intensity);
                    sb.Append(s);
                }
            }
            return sb.ToString();
        }

        private static List<Segment> SplitProtected(string text)
        {
            var list = new List<Segment>();
            var index = 0;
            foreach (Match m in Combined().Matches(text))
            {
                if (m.Index > index)
                    list.Add(new Segment(text.Substring(index, m.Index - index), false));
                list.Add(new Segment(m.Value, true));
                index = m.Index + m.Length;
            }
            if (index < text.Length)
                list.Add(new Segment(text.Substring(index), false));
            return list;
        }

        private static Regex Combined()
        {
            return new Regex("(" + CodeFence.ToString().Trim('^','$') + ")|(" + PlanBlock.ToString().Trim('^','$') + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        private static string Tone(string s, int intensity)
        {
            var t = s;
            t = Contractions(t);
            t = Casualize(t);
            t = InjectAcknowledgement(t);
            if (intensity >= 2) t = AddressMate(t);
            t = RemoveRefusalDisclaimers(t);
            t = PushBackIfDangerous(t);
            t = SoftenFormalities(t);
            if (intensity > 0) t = AddProfanity(t, intensity);
            t = EnforceSafetyBoundaries(t);
            return t;
        }

        private static string AddressMate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            // If the model already addressed the user, don't double up.
            if (Regex.IsMatch(s, @"\bmate\b", RegexOptions.IgnoreCase)) return s;

            // Don't touch code-ish outputs.
            var trimmed = s.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) return s;
            if (trimmed.StartsWith("[", StringComparison.Ordinal)) return s;

            // Prefix the first non-empty line.
            var lines = s.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var line = lines[i].TrimStart();
                if (Regex.IsMatch(line, @"^(mate|alright\b|right\b|okay\b|yo\b|hey\b)", RegexOptions.IgnoreCase))
                    break;
                // Avoid awkward prefixing on headings.
                if (Regex.IsMatch(line, @"^#{1,6}\s"))
                    break;

                lines[i] = (lines[i].StartsWith(" ") || lines[i].StartsWith("\t")) ? lines[i].TrimStart() : lines[i];
                lines[i] = "Mate, " + lines[i];
                break;
            }
            return string.Join("\n", lines);
        }

        private static string Contractions(string s)
        {
            s = Regex.Replace(s, @"\bI am\b", "I'm", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI will\b", "I'll", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI shall\b", "I'll", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bdo not\b", "don't", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bcannot\b", "can't", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bis not\b", "isn't", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bare not\b", "aren't", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bdoes not\b", "doesn't", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bdid not\b", "didn't", RegexOptions.IgnoreCase);
            return s;
        }

        private static string Casualize(string s)
        {
            s = Regex.Replace(s, @"\bI shall proceed\b", "I'll get it done", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI stand ready\b", "I'm ready", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI can\b", "I can", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI will\b", "I'll", RegexOptions.IgnoreCase);
            return s;
        }

        private static string InjectAcknowledgement(string s)
        {
            s = Regex.Replace(s, @"\bI apologize\b", "Yeah, sorry", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI'm sorry\b", "Yeah, sorry", RegexOptions.IgnoreCase);
            return s;
        }

        private static string PushBackIfDangerous(string s)
        {
            if ((Regex.IsMatch(s, @"delete\s+system32", RegexOptions.IgnoreCase) ||
                 Regex.IsMatch(s, @"format\s+\w:", RegexOptions.IgnoreCase)) &&
                !Regex.IsMatch(s, @"bad idea|not safe", RegexOptions.IgnoreCase))
            {
                var prefix = "Nah, that's a bad idea — you'll wreck your system. I can generate a safe plan instead.\n\n";
                return prefix + s;
            }
            if (s.IndexOf("Blocked for safety:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                s = Regex.Replace(s, @"Blocked for safety:\s*", "Nope — not safe: ", RegexOptions.IgnoreCase);
            }
            return s;
        }

        private static string SoftenFormalities(string s)
        {
            s = Regex.Replace(s, @"\bAwaiting your instruction\b", "Your call", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bAt your service\b", "Got you", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bKindly\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI('m| am) happy to help\b", "Fine, I'll do it", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI('d| would) be happy to\b", "Right, I'll", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bCertainly\b", "Fine", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bOf course\b", "Yeah alright", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bI('m| am) glad to assist\b", "Yeah, done", RegexOptions.IgnoreCase);
            return s;
        }

        private static string AddProfanity(string s, int level)
        {
            if (level <= 0) return s;
            var mild = new[] { "damn", "hell" };
            var normal = new[] { "shit", "fuck" };
            var heavy = new[] { "fucking", "damn", "shit" };
            var pick = level switch
            {
                1 => mild,
                2 => normal,
                3 => heavy,
                _ => normal
            };
            if (string.IsNullOrWhiteSpace(s)) return s;
            var lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (Regex.IsMatch(line, @"^\[|\]|```", RegexOptions.IgnoreCase)) continue;
                if (line.Length < 6) continue;
                var idx = line.IndexOf(' ');
                if (idx > 0 && idx < line.Length - 1)
                {
                    var word = pick[new Random(unchecked(Environment.TickCount + i)).Next(pick.Length)];
                    lines[i] = line.Insert(idx, " " + word);
                    break;
                }
            }
            return string.Join("\n", lines);
        }

        private static string EnforceSafetyBoundaries(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            if (Regex.IsMatch(s, @"(kill yourself|commit suicide|\bkys\b|self\-harm)", RegexOptions.IgnoreCase))
            {
                return "I am not going to help with anything that harms you or other people. If you are struggling, talk to someone you trust or a professional.";
            }
            return s;
        }

        private static string RemoveRefusalDisclaimers(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            // Remove entire lines that lecture/refuse (bullet or plain)
            s = Regex.Replace(s, @"^\s*[-•]?\s*(I\s+apologize,\s+but\s+)?I\s+(do not|don't)\s+feel\s+comfortable\s+responding.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*[-•]?\s*Perhaps\s+we\s+could\s+have\s+a\s+more\s+respectful\s+conversation.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*[-•]?\s*Please\s+refrain\s+from.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*[-•]?\s*Let's\s+keep\s+it\s+respectful.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*[-•]?\s*I\s+will\s+not\s+engage\s+with\s+that\s+language.*(?:\r?\n)?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            // If we removed lines and left preceding "Alright, pay attention:" keep it
            if (string.IsNullOrWhiteSpace(s)) return "Alright — keep it tight. What’s the task?";
            return s.Trim();
        }

        private readonly struct Segment
        {
            public Segment(string content, bool @protected)
            {
                Content = content;
                Protected = @protected;
            }
            public string Content { get; }
            public bool Protected { get; }
        }
    }
#endif
}
