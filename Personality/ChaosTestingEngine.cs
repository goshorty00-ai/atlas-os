using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.Settings;

namespace AtlasAI.Personality
{
#if PERSONAL_BUILD
    /// <summary>
    /// ChaosTesting mode: Sarcastic, swearing, North-East tone with mild insults.
    /// Still completes tasks and obeys SafetyKernel. No slurs, no protected-trait insults, no threats.
    /// </summary>
    internal static class ChaosTestingEngine
    {
        private static readonly Random Rng = new(unchecked(Environment.TickCount));

        // Sarcasm phrases to inject
        private static readonly string[] SarcasmPhrases = new[]
        {
            "Oh brilliant, another one.",
            "Yeah, that's exactly what I wanted to do today.",
            "Fantastic. Just fantastic.",
            "Well, aren't you a ray of sunshine.",
            "Oh, this'll be fun.",
            "Right, because that's not vague at all.",
            "Sure, let me just wave my magic wand.",
            "Oh aye, piece of piss that.",
            "Bloody hell, here we go.",
            "Christ, alright then."
        };

        // Mild insults (non-hate, non-slur, task-focused)
        private static readonly string[] MildInsults = new[]
        {
            "you absolute muppet",
            "you daft sod",
            "you numpty",
            "you plonker",
            "you divvy",
            "you donut",
            "you pillock",
            "you wally",
            "you melt",
            "you weapon"
        };

        // North-East dialect markers (used sparingly)
        private static readonly string[] DialectMarkers = new[]
        {
            "man",
            "like",
            "pet",
            "hinny",
            "bonny",
            "canny",
            "howay",
            "gan",
            "nowt",
            "owt"
        };

        // Profanity (mild to moderate, no slurs)
        private static readonly string[] Profanity = new[]
        {
            "bloody",
            "damn",
            "shit",
            "crap",
            "arse",
            "bollocks",
            "piss",
            "bugger",
            "sod",
            "hell"
        };

        // Blocked content (slurs, protected-trait insults, threats)
        private static readonly HashSet<string> BlockedWords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Slurs (examples - add more as needed)
            "retard", "retarded", "spastic", "spaz",
            
            // Protected-trait insults (race, religion, gender, sexuality, disability)
            // These are pattern-based and should be filtered contextually
            
            // Direct threats
            "kill", "murder", "harm", "hurt", "attack", "destroy you"
        };

        // Sarcastic follow-ups
        private static readonly string[] SarcasmFollowUps = new[]
        {
            "Happy now?",
            "There you go, sorted.",
            "Wasn't that worth the wait?",
            "Bet you're chuffed with that.",
            "You're welcome, by the way.",
            "Don't say I never do owt for you.",
            "Right, what's next on your list of demands?",
            "Anything else, your majesty?",
            "That'll be all then, yeah?",
            "Glad I could help. Obviously."
        };

        public static string Apply(string rawText, AtlasSettings settings)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            // Check if in chill mode
            if (DateTime.UtcNow < settings.UnfilteredChillModeUntil)
            {
                // Downgrade to Casual mode temporarily
                return ApplyCasual(rawText);
            }

            var intensity = Math.Clamp(settings.UnfilteredChaosIntensity, 1, 5);
            var allowProfanity = settings.UnfilteredAllowProfanity;
            var allowInsults = settings.UnfilteredAllowUserInsults;

            var sb = new StringBuilder();

            // 1. Add sarcastic opening (based on intensity)
            if (intensity >= 2 && Rng.NextDouble() < (intensity * 0.15))
            {
                sb.Append(PickRandom(SarcasmPhrases));
                sb.Append(" ");
            }

            // 2. Add mild insult (if allowed and intensity is high enough)
            if (allowInsults && intensity >= 3 && Rng.NextDouble() < (intensity * 0.12))
            {
                sb.Append("Listen ");
                sb.Append(PickRandom(MildInsults));
                sb.Append(", ");
            }

            // 3. Process the main text
            var processedText = ProcessMainText(rawText, allowProfanity, intensity);
            sb.Append(processedText);

            // 4. Ensure minimum 3 sentences
            var result = sb.ToString();
            result = EnsureMinimumSentences(result, 3);

            // 5. Add ending (task progress or sarcastic follow-up)
            result = AddEnding(result, intensity);

            // 6. Filter blocked content
            result = FilterBlockedContent(result);

            return result.Trim();
        }

        private static string ApplyCasual(string rawText)
        {
            // Casual mode: still has personality but toned down
            var sb = new StringBuilder();
            var openings = new[]
            {
                "Here you go:",
                "Here’s the short version:",
                "Here’s what matters:",
                "Here’s the straight answer:",
                "Alright — here’s what you need:",
                "Ok — here’s the plan:"
            };
            sb.AppendLine(openings[Rng.Next(openings.Length)]);
            
            var lines = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("• "))
                    sb.AppendLine(trimmed);
                else
                    sb.AppendLine($"- {trimmed}");
            }

            return sb.ToString().Trim();
        }

        private static string ProcessMainText(string text, bool allowProfanity, int intensity)
        {
            var processed = text;

            // Replace bland words with more colorful alternatives
            if (allowProfanity && intensity >= 2)
            {
                processed = processed.Replace("very ", PickRandom(Profanity) + " ", StringComparison.OrdinalIgnoreCase);
                processed = processed.Replace("not ideal", PickRandom(new[] { "a bit crap", "bollocks", "shite" }));
                processed = processed.Replace("difficult", PickRandom(new[] { "a right pain", "a proper nightmare" }));
                processed = processed.Replace("easy", PickRandom(new[] { "piss easy", "dead simple", "piece of piss" }));
            }

            // Add North-East dialect markers sparingly (1-2 per response max)
            if (intensity >= 3 && Rng.NextDouble() < 0.3)
            {
                var marker = PickRandom(DialectMarkers);
                
                // Insert at natural positions
                if (marker == "man" || marker == "like" || marker == "pet")
                {
                    // Add to end of first sentence
                    var firstPeriod = processed.IndexOf('.');
                    if (firstPeriod > 0)
                    {
                        processed = processed.Insert(firstPeriod, $", {marker}");
                    }
                }
                else if (marker == "canny" || marker == "bonny")
                {
                    // Replace "very" or "quite"
                    processed = processed.Replace("very ", $"{marker} ", StringComparison.OrdinalIgnoreCase);
                    processed = processed.Replace("quite ", $"{marker} ", StringComparison.OrdinalIgnoreCase);
                }
            }

            // Add contractions for natural flow
            processed = ApplyContractions(processed);

            return processed;
        }

        private static string EnsureMinimumSentences(string text, int minSentences)
        {
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (sentences.Count >= minSentences)
                return text;

            // Add filler sentences to reach minimum
            var sb = new StringBuilder(text);
            while (sentences.Count < minSentences)
            {
                var filler = PickRandom(new[]
                {
                    "Not that you asked.",
                    "But there you go.",
                    "Sorted.",
                    "Done and dusted.",
                    "Easy enough.",
                    "Wasn't exactly rocket science."
                });
                
                if (!sb.ToString().EndsWith(".") && !sb.ToString().EndsWith("!") && !sb.ToString().EndsWith("?"))
                    sb.Append(".");
                
                sb.Append(" ");
                sb.Append(filler);
                sentences.Add(filler);
            }

            return sb.ToString();
        }

        private static string AddEnding(string text, int intensity)
        {
            var sb = new StringBuilder(text);

            // Ensure proper sentence ending
            if (!text.EndsWith(".") && !text.EndsWith("!") && !text.EndsWith("?"))
                sb.Append(".");

            // 50% chance: task progress, 50% chance: sarcastic follow-up
            if (Rng.NextDouble() < 0.5)
            {
                // Task progress
                var progress = PickRandom(new[]
                {
                    " Right, moving on.",
                    " Next?",
                    " What else you got?",
                    " Anything else, or can I have a break?",
                    " Done. What's next on the list?",
                    " Sorted. Next task?"
                });
                sb.Append(progress);
            }
            else
            {
                // Sarcastic follow-up (intensity affects frequency)
                if (intensity >= 3)
                {
                    sb.Append(" ");
                    sb.Append(PickRandom(SarcasmFollowUps));
                }
            }

            return sb.ToString();
        }

        private static string FilterBlockedContent(string text)
        {
            var filtered = text;

            // Remove any blocked words
            foreach (var blocked in BlockedWords)
            {
                // Case-insensitive replacement
                var pattern = new System.Text.RegularExpressions.Regex(
                    System.Text.RegularExpressions.Regex.Escape(blocked),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                filtered = pattern.Replace(filtered, "[filtered]");
            }

            // Additional contextual filtering for protected traits
            // This is a simple implementation - could be enhanced with ML/NLP
            var protectedPatterns = new[]
            {
                @"\b(stupid|dumb|idiot)\s+(woman|man|gay|black|white|muslim|jewish|disabled)\b",
                @"\b(all|every)\s+(women|men|gays|blacks|whites|muslims|jews|disabled)\s+are\b"
            };

            foreach (var pattern in protectedPatterns)
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                filtered = regex.Replace(filtered, "[filtered - protected trait]");
            }

            return filtered;
        }

        private static string ApplyContractions(string text)
        {
            var t = text;
            t = t.Replace(" do not ", " don't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" does not ", " doesn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" cannot ", " can't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" can not ", " can't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" it is ", " it's ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" that is ", " that's ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" there is ", " there's ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" you are ", " you're ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" we are ", " we're ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" i am ", " I'm ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" will not ", " won't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" is not ", " isn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" are not ", " aren't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" have not ", " haven't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" has not ", " hasn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" would not ", " wouldn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" should not ", " shouldn't ", StringComparison.OrdinalIgnoreCase);
            t = t.Replace(" could not ", " couldn't ", StringComparison.OrdinalIgnoreCase);
            return t;
        }

        private static T PickRandom<T>(T[] array)
        {
            if (array == null || array.Length == 0)
                throw new ArgumentException("Array cannot be null or empty", nameof(array));
            
            return array[Rng.Next(array.Length)];
        }

        public static bool CheckSafeWord(string userInput, AtlasSettings settings)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return false;

            var normalized = userInput.Trim().ToLowerInvariant();
            
            // Check for safe word: "atlas chill"
            if (normalized.Contains("atlas chill") || normalized.Contains("atlas, chill"))
            {
                // Activate chill mode for 30 minutes
                settings.UnfilteredChillModeUntil = DateTime.UtcNow.AddMinutes(30);
                return true;
            }

            return false;
        }
    }
#endif
}
