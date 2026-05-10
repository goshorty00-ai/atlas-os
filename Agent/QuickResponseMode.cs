using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AtlasAI.Settings;
using AtlasAI.Voice;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick response mode - generates fast, concise responses without AI.
    /// For common questions and confirmations that don't need full AI processing.
    /// Now uses Jarvis-style responses for greetings and conversation.
    /// </summary>
    public static class QuickResponseMode
    {
        // Canned responses for common patterns - NON-CONVERSATIONAL only
        // Greetings/introductions are now handled by ResponseStyleController
        private static readonly Dictionary<string, string[]> QuickResponses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Thanks - Jarvis style
            { "thanks", new[] { "You are welcome.", "Of course.", "Happy to assist." } },
            { "thank you", new[] { "You are welcome.", "My pleasure.", "Glad to help." } },
            { "cheers", new[] { "Of course.", "Anytime.", "You are welcome." } },
            { "ta", new[] { "Of course.", "You are welcome.", "Certainly." } },
            
            // Confirmations - Jarvis style
            { "ok", new[] { "Understood.", "Very well.", "Acknowledged." } },
            { "okay", new[] { "Understood.", "Very well.", "Acknowledged." } },
            { "cool", new[] { "Indeed.", "Very well.", "Understood." } },
            { "nice", new[] { "Indeed.", "Agreed.", "Very well." } },
            { "great", new[] { "Excellent.", "Very good.", "Indeed." } },
            { "perfect", new[] { "Excellent.", "Very good.", "Splendid." } },
            { "awesome", new[] { "Excellent.", "Very good.", "Indeed." } },
            
            // Status checks - Jarvis style
            { "you there", new[] { "I am here.", "At your service.", "Present." } },
            { "are you there", new[] { "I am here.", "Yes, I am here.", "At your service." } },
            { "you awake", new[] { "Always operational.", "Systems active.", "I am here." } },
            
            // Bye - Jarvis style
            { "bye", new[] { "Goodbye.", "Until next time.", "Farewell." } },
            { "goodbye", new[] { "Goodbye.", "Farewell.", "Until we meet again." } },
            { "see you", new[] { "Until next time.", "Goodbye.", "Farewell." } },
            { "later", new[] { "Until later.", "Goodbye.", "Farewell." } },
            { "cya", new[] { "Goodbye.", "Until next time.", "Farewell." } },
            
            // Affirmations - Jarvis style
            { "yes", new[] { "Understood.", "Very well.", "Acknowledged." } },
            { "yeah", new[] { "Understood.", "Very well.", "Acknowledged." } },
            { "yep", new[] { "Understood.", "Very well.", "Acknowledged." } },
            { "no", new[] { "Understood.", "Very well.", "As you wish." } },
            { "nope", new[] { "Understood.", "Very well.", "As you wish." } },
            { "nah", new[] { "Understood.", "Very well.", "As you wish." } },
            
            // Misc - Jarvis style
            { "never mind", new[] { "Of course.", "Understood.", "Very well." } },
            { "nevermind", new[] { "Of course.", "Understood.", "Very well." } },
            { "forget it", new[] { "Forgotten.", "Of course.", "Very well." } },
            { "stop", new[] { "Stopped.", "Understood.", "Done." } },
            { "cancel", new[] { "Cancelled.", "Understood.", "Done." } },
        };
        
        // Pattern-based responses
        private static readonly List<(Regex Pattern, Func<Match, string> Response)> PatternResponses = new()
        {
            // Time
            (new Regex(@"^what('s| is) the time\??$", RegexOptions.IgnoreCase), 
                _ => $"The time is {DateTime.Now:h:mm tt}."),
            (new Regex(@"^what time is it\??$", RegexOptions.IgnoreCase), 
                _ => $"It is {DateTime.Now:h:mm tt}."),
            (new Regex(@"^time\??$", RegexOptions.IgnoreCase), 
                _ => $"{DateTime.Now:h:mm tt}."),
            
            // Date
            (new Regex(@"^what('s| is) the date\??$", RegexOptions.IgnoreCase), 
                _ => $"Today is {DateTime.Now:dddd, MMMM d, yyyy}."),
            (new Regex(@"^what('s| is) today('s date)?\??$", RegexOptions.IgnoreCase), 
                _ => $"Today is {DateTime.Now:dddd, MMMM d}."),
            (new Regex(@"^what day is it\??$", RegexOptions.IgnoreCase), 
                _ => $"Today is {DateTime.Now:dddd}."),
            
            // Math (simple)
            (new Regex(@"^what('s| is) (\d+)\s*[\+]\s*(\d+)\??$", RegexOptions.IgnoreCase), 
                m => $"{int.Parse(m.Groups[2].Value) + int.Parse(m.Groups[3].Value)}."),
            (new Regex(@"^what('s| is) (\d+)\s*[\-]\s*(\d+)\??$", RegexOptions.IgnoreCase), 
                m => $"{int.Parse(m.Groups[2].Value) - int.Parse(m.Groups[3].Value)}."),
            (new Regex(@"^what('s| is) (\d+)\s*[\*x]\s*(\d+)\??$", RegexOptions.IgnoreCase), 
                m => $"{int.Parse(m.Groups[2].Value) * int.Parse(m.Groups[3].Value)}."),
            (new Regex(@"^what('s| is) (\d+)\s*[\/]\s*(\d+)\??$", RegexOptions.IgnoreCase), 
                m => int.Parse(m.Groups[3].Value) != 0 ? $"{int.Parse(m.Groups[2].Value) / int.Parse(m.Groups[3].Value)}." : "Cannot divide by zero."),
            (new Regex(@"^(\d+)\s*[\+]\s*(\d+)\s*=?\??$", RegexOptions.IgnoreCase), 
                m => $"{int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value)}."),
            (new Regex(@"^(\d+)\s*[\-]\s*(\d+)\s*=?\??$", RegexOptions.IgnoreCase), 
                m => $"{int.Parse(m.Groups[1].Value) - int.Parse(m.Groups[2].Value)}."),
            (new Regex(@"^(\d+)\s*[\*x]\s*(\d+)\s*=?\??$", RegexOptions.IgnoreCase), 
                m => $"{int.Parse(m.Groups[1].Value) * int.Parse(m.Groups[2].Value)}."),
        };
        
        private static readonly Random _random = new();
        
        /// <summary>
        /// Try to get a quick response without AI. Returns null if AI is needed.
        /// Greetings and introductions are now routed to ResponseStyleController.
        /// </summary>
        public static string? TryGetQuickResponse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            
            var text = input.Trim();
            var lower = text.ToLowerInvariant();
            
            // Remove trailing punctuation for matching
            var normalized = lower.TrimEnd('.', '!', '?', ' ');

            var customRule = TryMatchCustomSpeechRule(normalized);
            if (!string.IsNullOrWhiteSpace(customRule))
            {
                return customRule;
            }
            
            // === ROUTE GREETINGS/INTRODUCTIONS TO JARVIS RESPONSE SYSTEM ===
            // Check if this is a conversational message that should use Jarvis templates
            var conversationResult = ResponseStyleController.Instance.ProcessInput(text);
            if (conversationResult.IsConversational && !string.IsNullOrEmpty(conversationResult.Response))
            {
                System.Diagnostics.Debug.WriteLine($"[QuickResponseMode] Routed to Jarvis: {conversationResult.Intent}");
                return conversationResult.Response;
            }
            
            // Check exact matches for non-conversational responses
            if (QuickResponses.TryGetValue(normalized, out var responses))
            {
                return responses[_random.Next(responses.Length)];
            }
            
            // Check pattern matches
            foreach (var (pattern, responseFunc) in PatternResponses)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    try
                    {
                        return responseFunc(match);
                    }
                    catch { }
                }
            }
            
            // Check for "thanks for X" patterns
            if (normalized.StartsWith("thanks for") || normalized.StartsWith("thank you for"))
            {
                return QuickResponses["thanks"][_random.Next(QuickResponses["thanks"].Length)];
            }
            
            return null; // Need AI
        }

        private static string? TryMatchCustomSpeechRule(string normalized)
        {
            try
            {
                var normalizedInput = NormalizeRuleText(normalized);
                if (string.IsNullOrWhiteSpace(normalizedInput))
                    return null;

                var rules = SettingsStore.Current.CustomSpeechRules;
                return rules
                    .Where(static rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.Phrase) && !string.IsNullOrWhiteSpace(rule.ResponseText))
                    .Select(rule => new
                    {
                        Rule = rule,
                        Phrase = NormalizeRuleText(rule.Phrase),
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Phrase))
                    .Where(item => normalizedInput.Equals(item.Phrase, StringComparison.Ordinal))
                    .OrderByDescending(item => item.Phrase.Length)
                    .Select(item => item.Rule.ResponseText.Trim())
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeRuleText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Trim().ToLowerInvariant();
            normalized = Regex.Replace(normalized, "\\s+", " ");
            normalized = normalized.Trim('"', '\'', '`');
            normalized = normalized.TrimEnd('.', '!', '?', ',', ';', ':', ' ');
            return normalized;
        }
        
        /// <summary>
        /// Shorten an AI response for voice output
        /// </summary>
        public static string ShortenForVoice(string response, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(response)) return response;
            if (response.Length <= maxLength) return response;
            
            // Try to find a good break point
            var shortened = response;
            
            // Remove markdown formatting
            shortened = Regex.Replace(shortened, @"\*\*([^*]+)\*\*", "$1");
            shortened = Regex.Replace(shortened, @"\*([^*]+)\*", "$1");
            shortened = Regex.Replace(shortened, @"```[\s\S]*?```", "[code]");
            shortened = Regex.Replace(shortened, @"`([^`]+)`", "$1");
            
            // Remove bullet points and lists
            shortened = Regex.Replace(shortened, @"^[\-\*•]\s*", "", RegexOptions.Multiline);
            shortened = Regex.Replace(shortened, @"^\d+\.\s*", "", RegexOptions.Multiline);
            
            // Get first sentence or two
            var sentences = Regex.Split(shortened, @"(?<=[.!?])\s+");
            var result = "";
            foreach (var sentence in sentences)
            {
                if (result.Length + sentence.Length > maxLength) break;
                result += sentence + " ";
            }
            
            if (string.IsNullOrWhiteSpace(result))
            {
                // Just truncate
                result = shortened.Substring(0, Math.Min(maxLength, shortened.Length));
                var lastSpace = result.LastIndexOf(' ');
                if (lastSpace > maxLength / 2)
                    result = result.Substring(0, lastSpace);
            }
            
            return result.Trim();
        }
        
        /// <summary>
        /// Check if input is just a simple acknowledgment that doesn't need a response
        /// </summary>
        public static bool IsAcknowledgment(string input)
        {
            var lower = input.Trim().ToLowerInvariant().TrimEnd('.', '!', '?');
            var acks = new[] { "ok", "okay", "k", "kk", "cool", "nice", "great", "awesome", 
                              "perfect", "got it", "understood", "alright", "right", "yep", 
                              "yeah", "yes", "no", "nope", "nah" };
            return acks.Contains(lower);
        }
    }
}
