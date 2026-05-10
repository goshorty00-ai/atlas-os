using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Tracks recent phrases to prevent repetition.
    /// Rewrites repeated sentences using template variants.
    /// </summary>
    public class PhraseCooldown
    {
        private static PhraseCooldown? _instance;
        private static readonly object _lock = new();

        public static PhraseCooldown Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new PhraseCooldown();
                    }
                }
                return _instance;
            }
        }

        private readonly Queue<string> _recentResponses = new();
        private readonly HashSet<string> _recentSentences = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxHistory = 8;

        private static readonly Random _random = new();

        // Common closers and their variants
        private static readonly Dictionary<string, string[]> CloserVariants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["How may I assist."] = new[] { "What can I help with.", "What do you need.", "What's next.", "Anything else." },
            ["How may I assist you."] = new[] { "What can I do for you.", "What else do you need.", "Ready for the next task." },
            ["How may I assist you today."] = new[] { "What would you like to tackle.", "What's on your mind.", "What can I do." },
            ["Is there anything else."] = new[] { "What else.", "Need anything more.", "Anything else on your list." },
            ["What else may I assist with."] = new[] { "What's next.", "Anything else.", "What do you need." },
            ["Understood."] = new[] { "Got it.", "Right.", "Very well.", "Certainly." },
            ["Done."] = new[] { "Complete.", "Finished.", "Sorted.", "All done." },
            ["Processing."] = new[] { "Working on it.", "One moment.", "On it.", "Handling that." }
        };

        // Acknowledgement variants
        private static readonly Dictionary<string, string[]> AckVariants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Understood. How may I assist."] = new[] { "Got it. What's next.", "Right. What do you need.", "Very well. Anything else." },
            ["Certainly. Processing now."] = new[] { "Of course. Working on it.", "Right away. One moment.", "On it now." },
            ["Very well. One moment."] = new[] { "Understood. Just a moment.", "Right. Processing.", "Got it. Working on that." }
        };

        private PhraseCooldown()
        {
            Debug.WriteLine("[PhraseCooldown] Initialized");
        }

        /// <summary>
        /// Check and rewrite response if it contains repeated phrases.
        /// </summary>
        public string ApplyCooldown(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return response;

            var result = response;

            // Check full response first
            if (AckVariants.TryGetValue(response.Trim(), out var fullVariants))
            {
                if (_recentSentences.Contains(response.Trim()))
                {
                    result = fullVariants[_random.Next(fullVariants.Length)];
                    Debug.WriteLine($"[PhraseCooldown] Replaced full response: '{response}' → '{result}'");
                }
            }
            else
            {
                // Check individual sentences
                var sentences = SplitSentences(response);
                var modified = false;

                for (int i = 0; i < sentences.Count; i++)
                {
                    var sentence = sentences[i].Trim();
                    
                    if (_recentSentences.Contains(sentence))
                    {
                        // Try to find a variant
                        if (CloserVariants.TryGetValue(sentence, out var variants))
                        {
                            var replacement = variants[_random.Next(variants.Length)];
                            sentences[i] = replacement;
                            modified = true;
                            Debug.WriteLine($"[PhraseCooldown] Replaced: '{sentence}' → '{replacement}'");
                        }
                    }
                }

                if (modified)
                {
                    result = string.Join(" ", sentences);
                }
            }

            // Record this response
            RecordResponse(result);

            return result;
        }

        /// <summary>
        /// Check if a specific phrase was used recently.
        /// </summary>
        public bool WasRecentlyUsed(string phrase)
        {
            return _recentSentences.Contains(phrase.Trim());
        }

        /// <summary>
        /// Get a variant of a phrase if available.
        /// </summary>
        public string GetVariant(string phrase)
        {
            if (CloserVariants.TryGetValue(phrase.Trim(), out var variants))
            {
                return variants[_random.Next(variants.Length)];
            }
            return phrase;
        }

        /// <summary>
        /// Reset cooldown history.
        /// </summary>
        public void Reset()
        {
            _recentResponses.Clear();
            _recentSentences.Clear();
            Debug.WriteLine("[PhraseCooldown] Reset");
        }

        private void RecordResponse(string response)
        {
            _recentResponses.Enqueue(response);
            
            // Extract and record sentences
            var sentences = SplitSentences(response);
            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length > 3)
                {
                    _recentSentences.Add(trimmed);
                }
            }

            // Maintain history limit
            while (_recentResponses.Count > MaxHistory)
            {
                var old = _recentResponses.Dequeue();
                // Remove old sentences from tracking
                var oldSentences = SplitSentences(old);
                foreach (var s in oldSentences)
                {
                    _recentSentences.Remove(s.Trim());
                }
            }
        }

        private List<string> SplitSentences(string text)
        {
            // Split on sentence boundaries but keep the punctuation
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return sentences;
        }
    }
}
