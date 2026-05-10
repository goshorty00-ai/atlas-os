using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Lightweight intent routing to prevent misclassification of simple instructions.
    /// Routes messages to appropriate handlers BEFORE AI processing.
    /// </summary>
    public static class IntentRouter
    {
        public enum Intent
        {
            Unknown,
            PreferenceSet,      // "call me tommy", "my name is..."
            Question,           // Normal questions
            BugReport,          // "voice error", "it crashed"
            Command,            // Action requests
            Greeting            // "hi", "hello"
        }

        public class IntentResult
        {
            public Intent Intent { get; set; }
            public string? ExtractedValue { get; set; }  // e.g., extracted name
            public string? Reason { get; set; }
        }

        /// <summary>
        /// Detect intent from user message
        /// </summary>
        public static IntentResult DetectIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return new IntentResult { Intent = Intent.Unknown };

            var lower = message.ToLowerInvariant().Trim();

            // 1. Check for name preference
            var nameResult = TryExtractNamePreference(lower);
            if (nameResult != null)
            {
                Debug.WriteLine($"[IntentRouter] Detected: PreferenceSet (name='{nameResult}')");
                return new IntentResult 
                { 
                    Intent = Intent.PreferenceSet, 
                    ExtractedValue = nameResult,
                    Reason = "Name preference detected"
                };
            }

            // 2. Check for bug report
            if (IsBugReport(lower))
            {
                Debug.WriteLine($"[IntentRouter] Detected: BugReport");
                return new IntentResult 
                { 
                    Intent = Intent.BugReport,
                    Reason = "Error/crash keywords detected"
                };
            }

            // 3. Check for greeting
            if (IsGreeting(lower))
            {
                Debug.WriteLine($"[IntentRouter] Detected: Greeting");
                return new IntentResult 
                { 
                    Intent = Intent.Greeting,
                    Reason = "Greeting detected"
                };
            }

            // 4. Check for question
            if (IsQuestion(lower))
            {
                Debug.WriteLine($"[IntentRouter] Detected: Question");
                return new IntentResult 
                { 
                    Intent = Intent.Question,
                    Reason = "Question pattern detected"
                };
            }

            // 5. Default to command
            Debug.WriteLine($"[IntentRouter] Detected: Command (default)");
            return new IntentResult 
            { 
                Intent = Intent.Command,
                Reason = "Default classification"
            };
        }

        /// <summary>
        /// Try to extract name from preference-setting messages
        /// </summary>
        private static string? TryExtractNamePreference(string lower)
        {
            // Patterns: "call me X", "my name is X", "from now on call me X"
            var patterns = new[]
            {
                @"call me (\w+)",
                @"my name is (\w+)",
                @"from now on call me (\w+)",
                @"you can call me (\w+)",
                @"i'm (\w+)",
                @"i am (\w+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(lower, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    var name = match.Groups[1].Value;
                    // Capitalize first letter
                    return char.ToUpper(name[0]) + name.Substring(1);
                }
            }

            return null;
        }

        /// <summary>
        /// Check if message is a bug report
        /// </summary>
        private static bool IsBugReport(string lower)
        {
            var bugKeywords = new[]
            {
                "error", "crash", "crashed", "bug", "broken", "not working",
                "failed", "failure", "exception", "0x", "hresult"
            };

            foreach (var keyword in bugKeywords)
            {
                if (lower.Contains(keyword))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if message is a greeting
        /// </summary>
        private static bool IsGreeting(string lower)
        {
            var greetings = new[]
            {
                "hi", "hello", "hey", "good morning", "good afternoon",
                "good evening", "greetings", "yo", "sup", "what's up"
            };

            foreach (var greeting in greetings)
            {
                if (lower == greeting || lower.StartsWith(greeting + " ") || lower.StartsWith(greeting + ","))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if message is a question
        /// </summary>
        private static bool IsQuestion(string lower)
        {
            // Question words
            var questionWords = new[]
            {
                "what", "when", "where", "who", "why", "how", "which",
                "can you", "could you", "would you", "will you", "do you",
                "is", "are", "was", "were", "does", "did"
            };

            foreach (var word in questionWords)
            {
                if (lower.StartsWith(word + " ") || lower.StartsWith(word + "'"))
                    return true;
            }

            // Ends with question mark
            if (lower.TrimEnd().EndsWith("?"))
                return true;

            return false;
        }
    }
}
