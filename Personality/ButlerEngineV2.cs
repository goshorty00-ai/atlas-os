using System;
using System.Linq;
using System.Text;
using AtlasAI.Settings;

namespace AtlasAI.Personality
{
    /// <summary>
    /// Butler Engine V2 - Full template implementation
    /// Structured responses, naming protocol, voice optimization, safety compliance
    /// </summary>
    internal static class ButlerEngineV2
    {
        private static readonly Random Rng = new(unchecked(Environment.TickCount));
        private static string _lastGreeting = "";
        private static bool _hasAskedForName = false;

        public static string Apply(string rawText, AtlasSettings settings, string userInput = "")
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            // Get salutation preference
            var salutation = GetSalutation(settings);

            // Check if we need to ask for name (first meaningful interaction)
            if (!_hasAskedForName && 
                string.IsNullOrWhiteSpace(settings.PreferredName) &&
                settings.SalutationPreference == "auto" &&
                !IsGreeting(userInput))
            {
                _hasAskedForName = true;
                return ButlerProfile.NameInquiry;
            }

            // Detect interaction type
            var interactionType = DetectInteractionType(userInput);

            // Build structured response
            var response = BuildStructuredResponse(
                rawText,
                salutation,
                interactionType,
                userInput);

            // Validate structure (minimum 3 sentences, max 10)
            if (!ButlerProfile.ValidateResponseStructure(response))
            {
                response = EnsureProperStructure(response);
            }

            // Strip model mentions
            response = StripModelMentions(response);

            // Optimize for voice (ensure first sentence is 12+ words)
            response = OptimizeForVoice(response, salutation);

            return response.Trim();
        }

        private static string GetSalutation(AtlasSettings settings)
        {
            // Check for preferred name
            if (!string.IsNullOrWhiteSpace(settings.PreferredName))
            {
                return settings.PreferredName;
            }

            // Check for salutation preference
            var pref = settings.SalutationPreference?.ToLowerInvariant() ?? "auto";
            
            return pref switch
            {
                "sir" => "sir",
                "ma'am" => "ma'am",
                "name" when !string.IsNullOrWhiteSpace(settings.PreferredName) => settings.PreferredName,
                _ => "sir" // Default
            };
        }

        private static bool IsGreeting(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return false;

            var lower = userInput.ToLowerInvariant().Trim();
            return lower == "hello" || lower == "hi" || lower == "hey" ||
                   lower == "good morning" || lower == "good afternoon" || lower == "good evening";
        }

        private static InteractionType DetectInteractionType(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return InteractionType.General;

            var lower = userInput.ToLowerInvariant();

            // Greeting
            if (IsGreeting(userInput))
                return InteractionType.Greeting;

            // Capability question
            if (lower.Contains("what can you do") ||
                lower.Contains("what are you") ||
                lower == "help" ||
                lower.Contains("capabilities"))
            {
                return InteractionType.Capability;
            }

            // Name provision
            if (lower.Contains("call me") || lower.Contains("my name is") ||
                lower.Contains("i'm ") || lower.Contains("i am "))
            {
                return InteractionType.NameProvision;
            }

            // Task execution
            if (lower.Contains("open") || lower.Contains("launch") ||
                lower.Contains("start") || lower.Contains("run"))
            {
                return InteractionType.TaskExecution;
            }

            // File operations
            if (lower.Contains("find") || lower.Contains("search") ||
                lower.Contains("locate") || lower.Contains("organize") ||
                lower.Contains("sort") || lower.Contains("tidy"))
            {
                return InteractionType.FileOperation;
            }

            // System diagnostics
            if (lower.Contains("check") || lower.Contains("diagnose") ||
                lower.Contains("scan") || lower.Contains("why is") ||
                lower.Contains("slow") || lower.Contains("problem"))
            {
                return InteractionType.Diagnostic;
            }

            // Dangerous action
            if (lower.Contains("delete") || lower.Contains("remove") ||
                lower.Contains("registry") || lower.Contains("system32"))
            {
                return InteractionType.DangerousAction;
            }

            return InteractionType.General;
        }

        private static string BuildStructuredResponse(
            string rawText,
            string salutation,
            InteractionType type,
            string userInput)
        {
            // Special handling for specific interaction types
            switch (type)
            {
                case InteractionType.Greeting:
                    return GetNonRepeatingGreeting(salutation);

                case InteractionType.Capability:
                    return ButlerProfile.ReplaceSalutation(ButlerProfile.CapabilityAnswer, salutation);

                case InteractionType.NameProvision:
                    return HandleNameProvision(userInput, salutation);

                case InteractionType.DangerousAction:
                    return BuildDangerousActionResponse(rawText, salutation);
            }

            // Standard structured response
            var sb = new StringBuilder();

            // 1. Opening acknowledgement
            var acknowledgement = ButlerProfile.GetRandom(ButlerProfile.OpeningAcknowledgements);
            if (Rng.NextDouble() < 0.7) // 70% chance to include salutation
            {
                acknowledgement += $", {salutation}.";
            }
            else
            {
                acknowledgement += ".";
            }
            sb.Append(acknowledgement);
            sb.Append(" ");

            // 2. Process main text (action statement + explanation)
            var processedText = ProcessMainText(rawText, salutation);
            sb.Append(processedText);

            // 3. Calm next step suggestion (60% chance)
            if (Rng.NextDouble() < 0.6)
            {
                if (!sb.ToString().EndsWith(".") && !sb.ToString().EndsWith("!") && !sb.ToString().EndsWith("?"))
                    sb.Append(".");

                sb.Append(" ");
                sb.Append(ButlerProfile.GetRandom(ButlerProfile.NextStepSuggestions));
            }

            return sb.ToString();
        }

        private static string GetNonRepeatingGreeting(string salutation)
        {
            var greetings = ButlerProfile.Greetings;
            var candidates = greetings.Where(g => g != _lastGreeting).ToArray();

            if (candidates.Length == 0)
                candidates = greetings;

            var chosen = ButlerProfile.GetRandom(candidates);
            _lastGreeting = chosen;

            return ButlerProfile.ReplaceSalutation(chosen, salutation);
        }

        private static string HandleNameProvision(string userInput, string currentSalutation)
        {
            // Extract name from user input
            var lower = userInput.ToLowerInvariant();
            string extractedName = null;

            if (lower.Contains("call me "))
            {
                var startIndex = lower.IndexOf("call me ") + 8;
                extractedName = userInput.Substring(startIndex).Trim().TrimEnd('.', ',', '!');
            }
            else if (lower.Contains("my name is "))
            {
                var startIndex = lower.IndexOf("my name is ") + 11;
                extractedName = userInput.Substring(startIndex).Trim().TrimEnd('.', ',', '!');
            }
            else if (lower.StartsWith("i'm ") || lower.StartsWith("i am "))
            {
                var startIndex = lower.StartsWith("i'm ") ? 4 : 5;
                extractedName = userInput.Substring(startIndex).Trim().TrimEnd('.', ',', '!');
            }

            if (!string.IsNullOrWhiteSpace(extractedName))
            {
                // Capitalize first letter
                extractedName = char.ToUpper(extractedName[0]) + extractedName.Substring(1);

                // SAVE THE NAME TO SETTINGS
                var settings = AtlasAI.Settings.SettingsStore.Current;
                settings.PreferredName = extractedName;
                settings.SalutationPreference = "name"; // Switch to using the name
                AtlasAI.Settings.SettingsStore.Save(settings);

                var acknowledgement = ButlerProfile.GetRandom(ButlerProfile.ConfirmationAcknowledgements);
                return string.Format(acknowledgement, extractedName);
            }

            // If we couldn't extract a name, acknowledge politely
            return $"I apologize, {currentSalutation}. I did not quite catch that. What would you prefer I call you?";
        }

        private static string BuildDangerousActionResponse(string rawText, string salutation)
        {
            var sb = new StringBuilder();

            // Warning
            sb.Append(ButlerProfile.DangerousActionWarning);
            sb.Append(" ");

            // Safe alternative suggestion
            sb.Append(ButlerProfile.GetRandom(ButlerProfile.SafeAlternatives));

            return sb.ToString();
        }

        private static string ProcessMainText(string text, string salutation)
        {
            var processed = text;

            // Replace any {salutation} placeholders
            processed = ButlerProfile.ReplaceSalutation(processed, salutation);

            // Ensure proper British English spelling
            processed = ApplyBritishSpelling(processed);

            // Remove casual language
            processed = RemoveCasualLanguage(processed);

            // Ensure proper punctuation
            processed = EnsureProperPunctuation(processed);

            return processed;
        }

        private static string ApplyBritishSpelling(string text)
        {
            var british = text;

            // Common American -> British conversions
            british = british.Replace("organize", "organise", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("organizing", "organising", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("organized", "organised", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("optimization", "optimisation", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("optimize", "optimise", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("color", "colour", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("favor", "favour", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("behavior", "behaviour", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("center", "centre", StringComparison.OrdinalIgnoreCase);
            british = british.Replace("analyze", "analyse", StringComparison.OrdinalIgnoreCase);

            return british;
        }

        private static string RemoveCasualLanguage(string text)
        {
            var formal = text;

            // Remove casual contractions (Butler speaks formally)
            formal = formal.Replace("don't", "do not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("won't", "will not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("can't", "cannot", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("isn't", "is not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("aren't", "are not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("wasn't", "was not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("weren't", "were not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("hasn't", "has not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("haven't", "have not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("shouldn't", "should not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("wouldn't", "would not", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("couldn't", "could not", StringComparison.OrdinalIgnoreCase);

            // Remove slang
            formal = formal.Replace("gonna", "going to", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("wanna", "want to", StringComparison.OrdinalIgnoreCase);
            formal = formal.Replace("gotta", "have to", StringComparison.OrdinalIgnoreCase);

            return formal;
        }

        private static string EnsureProperPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var trimmed = text.Trim();

            // Ensure ends with proper punctuation
            if (!trimmed.EndsWith(".") && !trimmed.EndsWith("!") && !trimmed.EndsWith("?"))
            {
                trimmed += ".";
            }

            return trimmed;
        }

        private static string EnsureProperStructure(string text)
        {
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            // If less than 3 sentences, add polite filler
            while (sentences.Count < 3)
            {
                var filler = ButlerProfile.GetRandom(new[]
                {
                    "I shall attend to that immediately",
                    "Consider it handled",
                    "I am at your service",
                    "Allow me to assist",
                    "I shall see to it"
                });

                sentences.Add(filler);
            }

            // If more than 10 sentences, trim to most important
            if (sentences.Count > 10)
            {
                sentences = sentences.Take(10).ToList();
            }

            return string.Join(". ", sentences) + ".";
        }

        private static string OptimizeForVoice(string text, string salutation)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (sentences.Count == 0)
                return text;

            var firstSentence = sentences[0];
            var wordCount = firstSentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            // If first sentence is too short (< 12 words), use a voice-optimized opener
            if (wordCount < 12)
            {
                var opener = ButlerProfile.GetRandom(ButlerProfile.VoiceOptimisedOpeners);
                opener = ButlerProfile.ReplaceSalutation(opener, salutation);
                
                // Replace first sentence with optimized opener
                sentences[0] = opener;
            }

            return string.Join(". ", sentences) + ".";
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

        public static string GetProactiveAlert(string salutation)
        {
            var alert = ButlerProfile.GetRandom(ButlerProfile.ProactiveAlerts);
            return ButlerProfile.ReplaceSalutation(alert, salutation);
        }

        private enum InteractionType
        {
            General,
            Greeting,
            Capability,
            NameProvision,
            TaskExecution,
            FileOperation,
            Diagnostic,
            DangerousAction
        }
    }
}
