using System;
using System.Linq;
using System.Text;
using AtlasAI.Brain;

namespace AtlasAI.Personality
{
    public static class PersonalityEngine
    {
        public static string Apply(PersonalityProfile profile, string rawText, PresenceState? presence, MoodState? mood, string userInput)
        {
            // For Butler mode, use V2 engine with user input context
            if (profile.Type == PersonalityType.Butler)
            {
                var settings = AtlasAI.Settings.SettingsStore.Current;
                return ButlerEngineV2.Apply(rawText, settings, userInput);
            }

            var baseText = Apply(profile, rawText, presence, mood);

            // For ChaosTesting mode, use V2 engine with user input context
        #if PERSONAL_BUILD
            if (profile.Type == PersonalityType.Unfiltered)
            {
                var settings = AtlasAI.Settings.SettingsStore.Current;
                var styleStr = settings.UnfilteredStyle ?? "Casual";

                if (Enum.TryParse<UnfilteredStyle>(styleStr, true, out var style) &&
                    style == UnfilteredStyle.ChaosTesting)
                {
                    return ChaosTestingEngineV2.Apply(baseText, settings, userInput);
                }
            }
        #endif

            return baseText;
        }

        public static string Apply(PersonalityProfile profile, string rawText, PresenceState? presence)
        {
            var baseText = Apply(profile, rawText);
            return ApplyPresenceTone(baseText, presence);
        }

        public static string Apply(PersonalityProfile profile, string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return rawText;

            var text = rawText.Trim();

            // Apply personality-specific formatting
            text = profile.Type switch
            {
                PersonalityType.Butler => ApplyButler(profile, text),
                PersonalityType.Futuristic => ApplyFuturistic(profile, text),
                PersonalityType.Tactical => ApplyTactical(profile, text),
                PersonalityType.Friendly => ApplyFriendly(profile, text),
                PersonalityType.Minimal => ApplyMinimal(profile, text),
                PersonalityType.Analytical => ApplyAnalytical(profile, text),
#if PERSONAL_BUILD
                PersonalityType.Unfiltered => ApplyUnfiltered(profile, text),
#endif
                _ => text
            };

            // Apply rhythm and contractions
            text = ApplyRhythmAndContractions(profile, text);

            return text;
        }

        public static string Apply(PersonalityProfile profile, string rawText, PresenceState? presence, MoodState? mood)
        {
            var baseText = Apply(profile, rawText, presence);
            if (mood == null) return baseText;
            return ApplyMoodTone(baseText, profile, mood.Value);
        }

        private static string ApplyButler(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text);
            var sb = new StringBuilder();
            var headerOptions = new[]
            {
                "At your service. Here is a refined outline:",
                "Here is a concise outline:",
                "Allow me to arrange this clearly:"
            };
            var header = headerOptions[Random.Shared.Next(headerOptions.Length)];
            sb.AppendLine(header);
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (IsBullet(line))
                    sb.AppendLine(line);
                else
                    sb.AppendLine($"- {line.Trim()}");
            }
            return ClampVerbosity(sb.ToString(), ToLegacyVerbosity(profile.VerbosityLevel));
        }

        private static string ApplyFuturistic(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("[SYSTEM] Design brief ready.");
            var body = lines.Length > 0 ? string.Join(Environment.NewLine, lines) : text;
            sb.AppendLine(body);
            return ClampVerbosity(sb.ToString(), ToLegacyVerbosity(profile.VerbosityLevel));
        }

        private static string ApplyTactical(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text);
            var sb = new StringBuilder();
            sb.AppendLine("Tactical plan – Media centre UI");
            sb.AppendLine("Risk: LOW, Impact: HIGH");
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (IsBullet(line))
                    sb.AppendLine(line);
                else
                    sb.AppendLine($"- ACTION: {line.Trim()}");
            }
            return ClampVerbosity(sb.ToString(), ToLegacyVerbosity(profile.VerbosityLevel));
        }

        private static string ApplyFriendly(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text);
            var sb = new StringBuilder();
            sb.AppendLine("Let’s shape a better media centre UI together:");
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (IsBullet(line))
                    sb.AppendLine(line);
                else
                    sb.AppendLine($"- {line.Trim()}");
            }
            return ClampVerbosity(sb.ToString(), ToLegacyVerbosity(profile.VerbosityLevel));
        }

        private static string ApplyMinimal(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            var sb = new StringBuilder();
            int count = 0;
            foreach (var line in lines)
            {
                if (count >= 5) break;
                if (IsBullet(line))
                    sb.AppendLine(line.Trim());
                else
                    sb.AppendLine($"- {line.Trim()}");
                count++;
            }
            return ClampVerbosity(sb.ToString(), 0);
        }

        private static string ApplyAnalytical(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Analysis");
            sb.AppendLine("Assumptions: user wants a clearer, more usable media centre UI.");
            sb.AppendLine();
            sb.AppendLine("Steps:");

            int i = 1;
            foreach (var line in lines)
            {
                var content = IsBullet(line) ? line.TrimStart('-', '•', ' ') : line.Trim();
                sb.AppendLine($"{i}. {content}");
                i++;
            }

            return ClampVerbosity(sb.ToString(), ToLegacyVerbosity(profile.VerbosityLevel));
        }

#if PERSONAL_BUILD
        private static string ApplyUnfiltered(PersonalityProfile profile, string text)
        {
            // Load settings to check UnfilteredStyle
            var settings = AtlasAI.Settings.SettingsStore.Current;

            // Parse the style
            var styleStr = settings.UnfilteredStyle ?? "Casual";
            UnfilteredStyle style = UnfilteredStyle.Casual;

            if (Enum.TryParse<UnfilteredStyle>(styleStr, true, out var parsed))
                style = parsed;

            // Route to appropriate handler
            var result = style switch
            {
                UnfilteredStyle.ChaosTesting => ChaosTestingEngine.Apply(text, settings),
                UnfilteredStyle.Banter => ApplyBanter(profile, text),
                UnfilteredStyle.Casual => ApplyCasual(profile, text),
                _ => ApplyCasual(profile, text)
            };
            result = RemoveRefusal(result);
            return result;
        }

        private static string ApplyCasual(PersonalityProfile profile, string text)
        {
            var lines = SplitLines(text);
            var sb = new StringBuilder();
            var openings = new[]
            {
                "Here you go:",
                "Here’s the short version:",
                "Here’s what matters:",
                "Here’s the straight answer:",
                "Got it. This is what’s happening:",
                "Ok — this is the fix:"
            };
            sb.AppendLine(openings[Random.Shared.Next(openings.Length)]);
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (IsBullet(line))
                    sb.AppendLine(line);
                else
                    sb.AppendLine($"- {line.Trim()}");
            }

            var result = sb.ToString();

            result = ReplaceFirst(result, "very ", "bloody ");
            result = ReplaceFirst(result, "not ideal", "a bit crap");

            return ClampVerbosity(result, ToLegacyVerbosity(profile.VerbosityLevel));
        }

        private static string ApplyBanter(PersonalityProfile profile, string text)
        {
            // Banter mode: playful, teasing, but still helpful
            var lines = SplitLines(text);
            var sb = new StringBuilder();

            var openings = new[]
            {
                "Right, let's sort this mess out:",
                "Alright, pay attention:",
                "Here's what we're doing, yeah?",
                "Listen up, this is how it's done:"
            };

            sb.AppendLine(openings[new Random().Next(openings.Length)]);

            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                if (IsBullet(line))
                    sb.AppendLine(line);
                else
                    sb.AppendLine($"- {line.Trim()}");
            }

            var result = sb.ToString();

            // Add some personality
            result = ReplaceFirst(result, "very ", "bloody ");
            result = ReplaceFirst(result, "not ideal", "a bit rubbish");
            result = ReplaceFirst(result, "difficult", "a pain in the arse");

            return ClampVerbosity(result, ToLegacyVerbosity(profile.VerbosityLevel));
        }

        private static string RemoveRefusal(string s)
        {
            var t = s ?? "";
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[-•]?\s*(I\s+apologize,\s+but\s+)?I\s+(do not|don't)\s+feel\s+comfortable\s+responding.*(?:\r?\n)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[-•]?\s*Perhaps\s+we\s+could\s+have\s+a\s+more\s+respectful\s+conversation.*(?:\r?\n)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[-•]?\s*Please\s+refrain\s+from.*(?:\r?\n)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[-•]?\s*Let's\s+keep\s+it\s+respectful.*(?:\r?\n)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
            return t.Trim();
        }

        private static string ReplaceFirst(string source, string find, string replace)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(find))
                return source;

            var index = source.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return source;

            var prefix = source.Substring(0, index);
            var suffix = source.Substring(index + find.Length);
            return prefix + replace + suffix;
        }
#endif

        private static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static bool IsBullet(string line)
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("- ") || trimmed.StartsWith("•");
        }

        private static int ToLegacyVerbosity(VerbosityLevel level)
        {
            return level switch
            {
                VerbosityLevel.Low => 0,
                VerbosityLevel.Medium => 2,
                VerbosityLevel.High => 3,
                _ => 2
            };
        }

        private static string ClampVerbosity(string text, int verbosity)
        {
            if (verbosity >= 3)
                return text.Trim();

            var lines = SplitLines(text)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            int maxLines = verbosity switch
            {
                0 => 4,
                1 => 6,
                2 => 10,
                _ => lines.Count
            };

            if (lines.Count <= maxLines)
                return string.Join(Environment.NewLine, lines);

            return string.Join(Environment.NewLine, lines.Take(maxLines));
        }

        private static string ApplyRhythmAndContractions(PersonalityProfile profile, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var adjusted = text;

            if (profile.Type == PersonalityType.Friendly || profile.Type == PersonalityType.Minimal ||
                profile.Type == PersonalityType.Futuristic || profile.Type == PersonalityType.Analytical
#if PERSONAL_BUILD
                || profile.Type == PersonalityType.Unfiltered
#endif
               )
            {
                adjusted = ApplyContractions(adjusted);
            }

            var lines = SplitLines(adjusted)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count <= 2)
                return adjusted.Trim();

            if (profile.Type == PersonalityType.Minimal)
            {
                var first = lines[0];
                var second = lines[1];
                return (first + Environment.NewLine + second).Trim();
            }

            if (profile.Type == PersonalityType.Analytical || profile.Type == PersonalityType.Engineer)
            {
                var first = lines[0];
                var rest = string.Join(" ", lines.Skip(1));
                return (first + Environment.NewLine + rest).Trim();
            }

            return adjusted.Trim();
        }

        private static string ApplyContractions(string text)
        {
            var t = text;
            t = t.Replace(" do not ", " don't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" does not ", " doesn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" cannot ", " can't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" can not ", " cannot ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" it is ", " it's ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" that is ", " that's ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" there is ", " there's ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" you are ", " you're ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" we are ", " we're ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" i am ", " I'm ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" will not ", " won't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" is not ", " isn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" are not ", " aren't ", StringComparison.OrdinalIgnoreCase);
            return t;
        }

        private static string ApplyPresenceTone(string text, PresenceState? presence)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (presence == null)
                return text.Trim();

            var lines = SplitLines(text)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            switch (presence.Value)
            {
                case PresenceState.Idle:
                {
                    // Calm, short: keep first 1–2 lines/sentences
                    var first = lines.Count > 0 ? lines[0] : "";
                    var second = lines.Count > 1 ? lines[1] : "";
                    var result = string.Join(Environment.NewLine, new[] { first, second }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    return result.Trim();
                }
                case PresenceState.Listening:
                {
                    // Slightly more attentive; add a subtle acknowledgement if not present
                    var first = lines.Count > 0 ? lines[0] : "";
                    if (!first.StartsWith("Understood", StringComparison.OrdinalIgnoreCase) &&
                        !first.StartsWith("Noted", StringComparison.OrdinalIgnoreCase))
                    {
                        first = "Understood. " + first.Trim();
                    }
                    var result = string.Join(Environment.NewLine, new[] { first }.Concat(lines.Skip(1).Take(2)));
                    return result.Trim();
                }
                case PresenceState.Working:
                {
                    // Short status updates; begin with a brief running indicator
                    var header = "Running diagnostics...";
                    var body = string.Join(Environment.NewLine, lines.Take(3));
                    return (header + Environment.NewLine + body).Trim();
                }
                case PresenceState.Alert:
                {
                    // Direct, serious tone: strip exclamations and soften fillers
                    var tightened = string.Join(Environment.NewLine, lines.Take(4))
                        .Replace("!", ".")
                        .Replace("please ", "", StringComparison.OrdinalIgnoreCase);
                    return tightened.Trim();
                }
                case PresenceState.Busy:
                {
                    // Efficient, minimal wording: compress to first 1–2 lines
                    var compact = string.Join(Environment.NewLine, lines.Take(2));
                    return compact.Trim();
                }
                default:
                    return text.Trim();
            }
        }

        private static string ApplyMoodTone(string text, PersonalityProfile profile, MoodState mood)
        {
            var trimmed = text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(trimmed))
                return trimmed;

            if (mood == MoodState.Calm)
                return trimmed;

            if (mood == MoodState.Playful)
            {
                if (profile.Type == PersonalityType.Butler)
                    return "Noted." + Environment.NewLine + trimmed;
#if PERSONAL_BUILD
                if (profile.Type == PersonalityType.Unfiltered)
                    return "Alright, this is kind of fun." + Environment.NewLine + trimmed;
#endif
                return trimmed;
            }

            if (profile.Type == PersonalityType.Guardian && (mood == MoodState.Alert || mood == MoodState.Concerned))
            {
                var header = "Risk: Elevated. Proceed carefully.";
                return header + Environment.NewLine + trimmed;
            }

            if (profile.Type == PersonalityType.Butler && mood == MoodState.Concerned)
            {
                var header = "Status: Concern noted.";
                return header + Environment.NewLine + trimmed;
            }

#if PERSONAL_BUILD
            if (profile.Type == PersonalityType.Unfiltered && (mood == MoodState.Alert || mood == MoodState.Concerned || mood == MoodState.Irritated))
            {
                var header = mood == MoodState.Irritated
                    ? "Yeah, that’s annoying. Straight to it."
                    : "Alright — straight to it.";
                return header + Environment.NewLine + trimmed;
            }
#endif
            return trimmed;
        }
    }
}
