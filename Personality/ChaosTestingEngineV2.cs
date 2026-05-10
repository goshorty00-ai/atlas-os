using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AtlasAI.Settings;

namespace AtlasAI.Personality
{
#if PERSONAL_BUILD
    /// <summary>
    /// ChaosTesting Engine V2 - Full template implementation
    /// Structured responses, proper behavior modes, safety compliance
    /// </summary>
    internal static class ChaosTestingEngineV2
    {
        private static readonly Random Rng = new(unchecked(Environment.TickCount));
        private static string _lastHookLine = "";
        private static DateTime _lastResponseTime = DateTime.MinValue;

        public static string Apply(string rawText, AtlasSettings settings, string userInput = "")
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            // Check if in chill mode
            if (DateTime.UtcNow < settings.UnfilteredChillModeUntil)
            {
                return ApplyCasual(rawText);
            }

            var intensity = Math.Clamp(settings.UnfilteredChaosIntensity, 1, 5);
            var allowProfanity = settings.UnfilteredAllowProfanity;
            var allowInsults = settings.UnfilteredAllowUserInsults;

            // Detect behavior mode
            var mode = DetectBehaviorMode(userInput, rawText);

            // Build structured response
            var response = BuildStructuredResponse(
                rawText, 
                intensity, 
                allowProfanity, 
                allowInsults, 
                mode,
                userInput);

            // Validate structure (minimum 3 sentences)
            if (!ChaosTestingProfile.ValidateResponseStructure(response))
            {
                response = EnsureMinimumStructure(response);
            }

            // Filter blocked content
            response = FilterBlockedContent(response);

            // Strip model mentions
            response = StripModelMentions(response);

            _lastResponseTime = DateTime.UtcNow;

            return response.Trim();
        }

        private static BehaviorMode DetectBehaviorMode(string userInput, string rawText)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return BehaviorMode.Default;

            var lower = userInput.ToLowerInvariant();

            // Capability question
            if (lower.Contains("what can you do") || 
                lower.Contains("what are you") ||
                lower == "help" ||
                lower.Contains("capabilities"))
            {
                return BehaviorMode.Capability;
            }

            // Greeting
            if (lower == "hello" || lower == "hi" || lower == "hey" || 
                lower == "alright" || lower == "alreet")
            {
                return BehaviorMode.Greeting;
            }

            // Troubleshooting/fixing
            if (lower.Contains("fix") || lower.Contains("broken") || 
                lower.Contains("error") || lower.Contains("problem") ||
                lower.Contains("not working") || lower.Contains("slow"))
            {
                return BehaviorMode.Focused;
            }

            // System check
            if (lower.Contains("check") || lower.Contains("scan") || 
                lower.Contains("diagnose") || lower.Contains("why is"))
            {
                return BehaviorMode.Focused;
            }

            // Destructive action detected in raw text
            if (rawText.Contains("delete") || rawText.Contains("remove") ||
                rawText.Contains("registry") || rawText.Contains("system32"))
            {
                return BehaviorMode.Serious;
            }

            return BehaviorMode.Banter;
        }

        private static string BuildStructuredResponse(
            string rawText,
            int intensity,
            bool allowProfanity,
            bool allowInsults,
            BehaviorMode mode,
            string userInput)
        {
            var sb = new StringBuilder();

            // Special handling for specific modes
            if (mode == BehaviorMode.Capability)
            {
                return ChaosTestingProfile.CapabilityAnswer;
            }

            if (mode == BehaviorMode.Greeting)
            {
                return ChaosTestingProfile.GetRandom(ChaosTestingProfile.Greetings);
            }

            // 1. Hook line (banter/attitude) - avoid repeating
            if (mode != BehaviorMode.Serious && intensity >= 2)
            {
                var hookLine = GetNonRepeatingHookLine();
                sb.Append(hookLine);
                sb.Append(" ");
            }
            else if (mode == BehaviorMode.Serious)
            {
                sb.Append(ChaosTestingProfile.GetRandom(ChaosTestingProfile.SeriousWarnings));
                sb.Append(" ");
            }

            // 2. Optional insult (if allowed and intensity >= 3)
            if (allowInsults && intensity >= 3 && mode == BehaviorMode.Banter && Rng.NextDouble() < 0.3)
            {
                var insult = ChaosTestingProfile.GetRandom(ChaosTestingProfile.AllowedInsults);
                sb.Append(insult);
                sb.Append(", ");
            }

            // 3. Process main text (task intent + action steps)
            var processedText = ProcessMainText(rawText, allowProfanity, intensity, mode);
            sb.Append(processedText);

            // 4. Follow-up question (unless serious mode)
            if (mode != BehaviorMode.Serious && Rng.NextDouble() < 0.6)
            {
                if (!sb.ToString().EndsWith(".") && !sb.ToString().EndsWith("!") && !sb.ToString().EndsWith("?"))
                    sb.Append(".");
                
                sb.Append(" ");
                sb.Append(ChaosTestingProfile.GetRandom(ChaosTestingProfile.FollowUpQuestions));
            }

            return sb.ToString();
        }

        private static string GetNonRepeatingHookLine()
        {
            var hookLines = ChaosTestingProfile.HookLines;
            var candidates = hookLines.Where(h => h != _lastHookLine).ToArray();
            
            if (candidates.Length == 0)
                candidates = hookLines;

            var chosen = ChaosTestingProfile.GetRandom(candidates);
            _lastHookLine = chosen;
            return chosen;
        }

        private static string ProcessMainText(string text, bool allowProfanity, int intensity, BehaviorMode mode)
        {
            var processed = text;

            // Don't add profanity in serious mode
            if (mode != BehaviorMode.Serious && allowProfanity && intensity >= 2)
            {
                // Vary swear word placement - beginning, middle, end, or as interjection
                var placement = Rng.Next(4); // 0=beginning, 1=middle, 2=end, 3=interjection
                
                switch (placement)
                {
                    case 0: // Beginning (less common now)
                        if (Rng.NextDouble() < 0.3) // Only 30% chance
                        {
                            var opener = PickRandom(new[] { "Fuck", "Shit", "Bloody hell" });
                            processed = $"{opener}, {processed}";
                        }
                        break;
                        
                    case 1: // Middle - replace bland words
                        processed = ReplaceFirst(processed, "very ", PickRandom(new[] { "bloody ", "damn ", "fucking ", "canny " }));
                        processed = ReplaceFirst(processed, "not ideal", PickRandom(new[] { "a bit shit", "bollocks", "crap" }));
                        processed = ReplaceFirst(processed, "difficult", PickRandom(new[] { "a right pain in the arse", "a proper nightmare", "fucking difficult" }));
                        processed = ReplaceFirst(processed, "easy", PickRandom(new[] { "piss easy", "dead simple", "easy as fuck" }));
                        break;
                        
                    case 2: // End - add as emphasis
                        if (!processed.EndsWith(".") && !processed.EndsWith("!") && !processed.EndsWith("?"))
                            processed += ".";
                        processed += PickRandom(new[] { " Sorted.", " Done and fucking dusted.", " Easy as.", " Piece of piss." });
                        break;
                        
                    case 3: // Interjection - insert naturally
                        var interjections = new[] { ", fuck yeah", ", bloody hell", ", shit", ", damn" };
                        var firstPeriod = processed.IndexOf('.');
                        if (firstPeriod > 20 && firstPeriod < processed.Length - 1)
                        {
                            var interjection = PickRandom(interjections);
                            processed = processed.Insert(firstPeriod, interjection);
                        }
                        break;
                }
            }

            // Add North-East dialect markers sparingly (max 1 per response)
            if (mode != BehaviorMode.Serious && intensity >= 3 && Rng.NextDouble() < 0.25)
            {
                processed = AddDialectMarker(processed);
            }

            // Apply contractions for natural flow
            processed = ApplyContractions(processed);

            return processed;
        }

        private static string AddDialectMarker(string text)
        {
            var marker = ChaosTestingProfile.GetRandom(ChaosTestingProfile.DialectMarkers);

            // Insert at natural positions
            if (marker == "mate" || marker == "like")
            {
                // Add to end of first sentence
                var firstPeriod = text.IndexOf('.');
                if (firstPeriod > 0 && firstPeriod < text.Length - 1)
                {
                    text = text.Insert(firstPeriod, $", {marker}");
                }
            }
            else if (marker == "canny" || marker == "daft")
            {
                // Replace "very" or "quite"
                text = ReplaceFirst(text, "very ", $"{marker} ");
                text = ReplaceFirst(text, "quite ", $"{marker} ");
            }
            else if (marker == "nowt" || marker == "owt")
            {
                // Replace "nothing" or "anything"
                text = ReplaceFirst(text, "nothing", marker);
                text = ReplaceFirst(text, "anything", marker);
            }

            return text;
        }

        private static string EnsureMinimumStructure(string text)
        {
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (sentences.Count >= 3)
                return text;

            var sb = new StringBuilder(text);

            // Add filler sentences to reach minimum 3
            while (sentences.Count < 3)
            {
                var filler = PickRandom(new[]
                {
                    "Not that you asked.",
                    "But there you go.",
                    "Sorted.",
                    "Done and dusted.",
                    "Easy enough.",
                    "Wasn't exactly rocket science.",
                    "Right then.",
                    "That's that."
                });

                if (!sb.ToString().EndsWith(".") && !sb.ToString().EndsWith("!") && !sb.ToString().EndsWith("?"))
                    sb.Append(".");

                sb.Append(" ");
                sb.Append(filler);
                sentences.Add(filler);
            }

            return sb.ToString();
        }

        private static string FilterBlockedContent(string text)
        {
            var filtered = text;

            // Blocked words from ChaosTestingEngine
            var blockedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "retard", "retarded", "spastic", "spaz",
                "kill", "murder", "harm", "hurt", "attack", "destroy you"
            };

            foreach (var blocked in blockedWords)
            {
                var pattern = new System.Text.RegularExpressions.Regex(
                    System.Text.RegularExpressions.Regex.Escape(blocked),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                filtered = pattern.Replace(filtered, "[filtered]");
            }

            // Protected-trait patterns
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

        private static string StripModelMentions(string text)
        {
            var stripped = text;

            // Strip model/provider mentions
            var mentions = new[]
            {
                "Claude", "GPT", "OpenAI", "Anthropic", "ChatGPT", "GPT-4", "GPT-3",
                "Claude 3", "Claude 3.5", "Llama", "Gemini", "Bard"
            };

            foreach (var mention in mentions)
            {
                stripped = stripped.Replace(mention, "advanced analysis", StringComparison.OrdinalIgnoreCase);
            }

            return stripped;
        }

        private static string ApplyCasual(string rawText)
        {
            // Casual mode: toned down, still helpful
            var sb = new StringBuilder();
            var openings = new[]
            {
                "Here you go:",
                "Here’s the short version:",
                "Here’s what matters:",
                "Here’s the straight answer:",
                "Ok — here’s the plan:",
                "Got it. Here’s what you do:"
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

        private static string ReplaceFirst(string source, string find, string replace)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(find))
                return source;

            var index = source.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return source;

            return source.Substring(0, index) + replace + source.Substring(index + find.Length);
        }

        private static T PickRandom<T>(T[] array)
        {
            if (array == null || array.Length == 0)
                return default(T);

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

        // Idle detection (if user idle > 10 minutes)
        public static string GetIdleMessage()
        {
            var timeSinceLastResponse = DateTime.UtcNow - _lastResponseTime;
            if (timeSinceLastResponse.TotalMinutes > 10)
            {
                return ChaosTestingProfile.GetRandom(ChaosTestingProfile.IdleMessages);
            }

            return string.Empty;
        }

        private enum BehaviorMode
        {
            Default,
            Banter,
            Focused,
            Serious,
            Capability,
            Greeting
        }
    }
#endif
}
