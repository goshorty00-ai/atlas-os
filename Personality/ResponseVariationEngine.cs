using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.Brain;

namespace AtlasAI.Personality
{
    internal static class ResponseVariationEngine
    {
        private static readonly Queue<string> _recentResponses = new();
        private static readonly Queue<string> _recentOpeners = new();
        private const int RecentCapacity = 15;
        private enum StructureType
        {
            DirectStatement,
            StatusPlusSuggestion,
            QuestionFirst,
            DataFirst
        }

        private static readonly Random _rng = new(unchecked(Environment.TickCount));

        public static string Apply(PersonalityProfile profile, string baseText, ShortTermMemory memory)
        {
            if (string.IsNullOrWhiteSpace(baseText))
                return string.Empty;

#if PERSONAL_BUILD
            // For Unfiltered mode, skip all the restructuring - just return the raw response
            if (profile.Type == PersonalityType.Unfiltered)
            {
                return baseText.Trim();
            }
#endif

            var last = memory.LastResponseStyle;
            var type = ChooseStructure(profile, last);

            var lines = SplitLines(baseText);
            lines = RandomizeBulletBlocks(lines);
            lines = MaybeOmitGreeting(lines);

            var oneLine = _rng.NextDouble() < 0.12;
            string result = type switch
            {
                StructureType.DirectStatement => ComposeDirect(lines, oneLine),
                StructureType.StatusPlusSuggestion => ComposeStatusSuggestion(lines, oneLine),
                StructureType.QuestionFirst => ComposeQuestionFirst(lines, oneLine),
                StructureType.DataFirst => ComposeDataFirst(lines, oneLine),
                _ => ComposeDirect(lines, oneLine)
            };

            result = ApplySynonyms(result);
            result = VarySentenceLength(result);
            result = EnforceUniqueOpener(result);

            Remember(_recentResponses, result);
            memory.SetLastResponseStyle(type.ToString());
            return result.Trim();
        }

        private static StructureType ChooseStructure(PersonalityProfile profile, string last)
        {
            var preferred = profile.StructurePattern switch
            {
                ResponseStructurePattern.StatusThenSuggestion => StructureType.StatusPlusSuggestion,
                ResponseStructurePattern.DataThenOptions => StructureType.DataFirst,
                ResponseStructurePattern.RiskSummaryFirst => StructureType.StatusPlusSuggestion,
                ResponseStructurePattern.DirectAnswer => StructureType.DirectStatement,
                _ => StructureType.DirectStatement
            };

            for (int attempts = 0; attempts < 2; attempts++)
            {
                var roll = _rng.NextDouble();
                StructureType chosen;
                if (roll < 0.5)
                {
                    chosen = preferred;
                }
                else
                {
                    var n = _rng.Next(4);
                    chosen = (StructureType)n;
                }
                if (!string.Equals(chosen.ToString(), last, StringComparison.Ordinal))
                    return chosen;
            }
            return last switch
            {
                nameof(StructureType.DirectStatement) => StructureType.StatusPlusSuggestion,
                nameof(StructureType.StatusPlusSuggestion) => StructureType.QuestionFirst,
                nameof(StructureType.QuestionFirst) => StructureType.DataFirst,
                nameof(StructureType.DataFirst) => StructureType.DirectStatement,
                _ => StructureType.DirectStatement
            };
        }

        private static List<string> SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private static List<string> MaybeOmitGreeting(List<string> lines)
        {
            if (lines.Count == 0) return lines;
            if (_rng.NextDouble() >= 0.25) return lines;
            var first = lines[0];
            if (StartsWithAny(first, "good ", "welcome", "hello", "greetings"))
                return lines.Skip(1).ToList();
            return lines;
        }

        private static List<string> RandomizeBulletBlocks(List<string> lines)
        {
            var result = new List<string>();
            int i = 0;
            while (i < lines.Count)
            {
                if (IsBullet(lines[i]))
                {
                    var start = i;
                    var block = new List<string>();
                    while (i < lines.Count && IsBullet(lines[i]))
                    {
                        block.Add(lines[i]);
                        i++;
                    }
                    for (int j = block.Count - 1; j > 0; j--)
                    {
                        int k = _rng.Next(j + 1);
                        (block[j], block[k]) = (block[k], block[j]);
                    }
                    result.AddRange(block);
                }
                else
                {
                    result.Add(lines[i]);
                    i++;
                }
            }
            return result;
        }

        private static string ComposeDirect(List<string> lines, bool oneLine)
        {
            if (lines.Count == 0) return "";
            var take = oneLine ? 1 : Math.Min(lines.Count, 2);
            var chosen = lines.Take(take).ToList();
            chosen[0] = Shorten(chosen[0]);
            if (chosen.Count > 1) chosen[1] = Shorten(chosen[1]);
            return string.Join(Environment.NewLine, chosen);
        }

        private static string ComposeStatusSuggestion(List<string> lines, bool oneLine)
        {
            if (lines.Count == 0) return "";
            var status = Shorten(lines[0]);
            var suggestion = lines.Skip(1).FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(suggestion))
                suggestion = "Proceed when ready.";
            suggestion = Shorten(suggestion);
            if (oneLine) return status;
            return status + Environment.NewLine + "Next: " + suggestion;
        }

        private static string ComposeQuestionFirst(List<string> lines, bool oneLine)
        {
            var question = "Shall I continue?";
            var info = lines.FirstOrDefault() ?? "";
            info = Shorten(info);
            if (oneLine) return question;
            if (string.IsNullOrWhiteSpace(info)) return question;
            return question + Environment.NewLine + info;
        }

        private static string ComposeDataFirst(List<string> lines, bool oneLine)
        {
            if (lines.Count == 0) return "";
            var data = Shorten(lines[0]);
            var follow = lines.Skip(1).FirstOrDefault();
            if (oneLine || string.IsNullOrWhiteSpace(follow))
                return data;
            follow = Shorten(follow);
            return data + Environment.NewLine + follow;
        }

        private static bool StartsWithAny(string s, params string[] tokens)
        {
            var lower = s.ToLowerInvariant();
            foreach (var t in tokens)
            {
                if (lower.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string Shorten(string s)
        {
            // DISABLED: Don't truncate responses - let them be complete
            return s;
            
            // Old truncation logic removed - was causing incomplete responses
            // if (string.IsNullOrWhiteSpace(s)) return s;
            // var max = _rng.Next(60, 120);
            // if (s.Length <= max) return s;
            // return s.Substring(0, max).TrimEnd('.', ';', ',', ' ') + ".";
        }

        private static string ApplySynonyms(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var map = new (string from, string to)[]
            {
                ("Proceed", "Continue"),
                ("Next:", "Then:"),
                ("Issue", "Problem"),
                ("Fix", "Resolve"),
                ("Ensure", "Make sure"),
                ("However", "That said"),
                ("Note", "Heads up"),
                ("Status", "State")
            };
            foreach (var (from, to) in map)
            {
                if (_rng.NextDouble() < 0.35)
                {
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(from)}\b",
                        to);
                }
            }
            return text;
        }

        private static string VarySentenceLength(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (_rng.NextDouble() < 0.25 && text.Contains("\n"))
            {
                var parts = text.Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                if (parts.Count >= 2 && parts[0].Length < 90)
                {
                    parts[0] = parts[0].TrimEnd('.', ';') + " — " + parts[1];
                    parts.RemoveAt(1);
                    return string.Join(Environment.NewLine, parts);
                }
            }
            return text;
        }

        private static string EnforceUniqueOpener(string text)
        {
            var opener = FirstWords(text, 3);
            if (!string.IsNullOrWhiteSpace(opener) && _recentOpeners.Contains(opener))
            {
                var alternatives = new[] { "Noted.", "Understood.", "Alright.", "Okay.", "Heads up:" };
                var alt = alternatives[_rng.Next(alternatives.Length)];
                var rest = StripFirstSentence(text);
                text = alt + (string.IsNullOrWhiteSpace(rest) ? "" : " " + rest);
                opener = FirstWords(text, 3);
            }
            Remember(_recentOpeners, opener);
            return text;
        }

        private static string FirstWords(string s, int count)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var span = s.Trim();
            int words = 0;
            int idx = 0;
            for (int i = 0; i < span.Length; i++)
            {
                if (char.IsWhiteSpace(span[i]))
                {
                    if (i > 0 && !char.IsWhiteSpace(span[i - 1]))
                    {
                        words++;
                        if (words >= count)
                        {
                            idx = i;
                            break;
                        }
                    }
                }
            }
            if (idx == 0) return span.ToLowerInvariant();
            return span.Substring(0, idx).ToLowerInvariant();
        }

        private static string StripFirstSentence(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var idx = s.IndexOfAny(new[] { '.', '!', '?' });
            if (idx < 0 || idx + 1 >= s.Length) return "";
            return s.Substring(idx + 1).TrimStart();
        }

        private static void Remember(Queue<string> q, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            q.Enqueue(s);
            while (q.Count > RecentCapacity) q.Dequeue();
        }

        private static bool IsBullet(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("- ") || trimmed.StartsWith("•");
        }
    }
}
