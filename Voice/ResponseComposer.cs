using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using AtlasAI.AI;
using AtlasAI.Conversation.Models;

namespace AtlasAI.Voice
{
    /// <summary>
    /// STEP 30: ResponseComposer - Final processing of assistant responses before UI/TTS.
    /// 
    /// Responsibilities:
    /// 1. Enforce greeting/small-talk quality rules
    /// 2. Ban filler openers ("Greetings.", "How may I assist you today.")
    /// 3. Ensure response matches what TTS will speak (single source of truth)
    /// 4. Apply personality-appropriate tone
    /// </summary>
    public static class ResponseComposer
    {
        // Banned filler openers - these feel robotic/template-y
        private static readonly string[] BannedOpeners = new[]
        {
            "greetings.",
            "greetings!",
            "greetings,",
            "how may i assist you today",
            "how can i assist you today",
            "how may i help you today",
            "how can i help you today",
            "at your service",
            "i am at your service",
            "i'm at your service",
            "what can i do for you today",
            "how may i be of assistance",
            "how can i be of assistance",
            "i am here to help",
            "i'm here to help you",
            "hello! how can i",
            "hi! how can i",
            "hey! how can i",
            "good day!",
            "good day.",
            "salutations",
            "well met",
            "greetings and salutations"
        };

        // Replacement openers that feel more natural (Jarvis-style)
        private static readonly string[] NaturalOpeners = new[]
        {
            "What's on your mind?",
            "What would you like to tackle?",
            "Ready when you are.",
            "What can I help with?",
            "What's the plan?",
            "What are we working on?",
            "What do you need?",
            "Go ahead.",
            "I'm listening.",
            "Fire away."
        };

        private static readonly Random _random = new();

        /// <summary>
        /// Compose the final assistant message - this is the SINGLE SOURCE OF TRUTH
        /// for both UI display and TTS speech.
        /// </summary>
        public static FinalAssistantMessage Compose(
            string rawResponse,
            string userInput,
            string? userName = null,
            bool isGreeting = false)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return new FinalAssistantMessage
                {
                    Text = GetFallbackResponse(userInput, userName),
                    WasModified = true,
                    ModificationReason = "Empty response"
                };
            }

            var result = new FinalAssistantMessage
            {
                Text = rawResponse,
                WasModified = false
            };

            // Step 1: Remove banned filler openers
            result = RemoveBannedOpeners(result);

            // Step 2: Apply greeting rules if this is a greeting context
            if (isGreeting || IsGreetingResponse(rawResponse))
            {
                result = ApplyGreetingRules(result, userName);
            }

            // Step 3: Remove excessive self-references ("Atlas" mentioned too many times)
            result = RemoveExcessiveSelfReferences(result);

            // Step 4: Ensure response isn't too short for greetings
            if (isGreeting && result.Text.Split(' ').Length < 4)
            {
                result = EnhanceShortGreeting(result, userName);
            }

            // Step 5: Compute hash for TTS sync verification
            result.TextHash = AIDebugLogger.ComputeHash(result.Text);

            Debug.WriteLine($"[ResponseComposer] Final: \"{result.Text.Substring(0, Math.Min(80, result.Text.Length))}...\" (modified: {result.WasModified})");

            return result;
        }

        /// <summary>
        /// Remove banned filler openers from response
        /// </summary>
        private static FinalAssistantMessage RemoveBannedOpeners(FinalAssistantMessage msg)
        {
            var lower = msg.Text.ToLowerInvariant();
            
            foreach (var banned in BannedOpeners)
            {
                if (lower.StartsWith(banned))
                {
                    // Remove the banned opener
                    var remaining = msg.Text.Substring(banned.Length).TrimStart(' ', '.', '!', ',');
                    
                    if (string.IsNullOrWhiteSpace(remaining))
                    {
                        // Entire response was just the banned phrase
                        remaining = NaturalOpeners[_random.Next(NaturalOpeners.Length)];
                    }
                    else
                    {
                        // Capitalize first letter of remaining text
                        remaining = char.ToUpper(remaining[0]) + remaining.Substring(1);
                    }

                    return new FinalAssistantMessage
                    {
                        Text = remaining,
                        WasModified = true,
                        ModificationReason = $"Removed banned opener: '{banned}'"
                    };
                }
            }

            return msg;
        }

        /// <summary>
        /// Apply greeting-specific rules
        /// </summary>
        private static FinalAssistantMessage ApplyGreetingRules(FinalAssistantMessage msg, string? userName)
        {
            var text = msg.Text;
            var modified = msg.WasModified;
            var reason = msg.ModificationReason;

            // Rule: Mention user name at most once per session
            // (This is tracked externally, but we ensure no double-naming here)
            if (!string.IsNullOrEmpty(userName))
            {
                var nameCount = Regex.Matches(text, $@"\b{Regex.Escape(userName)}\b", RegexOptions.IgnoreCase).Count;
                if (nameCount > 1)
                {
                    // Keep only the first mention
                    var firstMatch = Regex.Match(text, $@"\b{Regex.Escape(userName)}\b", RegexOptions.IgnoreCase);
                    if (firstMatch.Success)
                    {
                        var afterFirst = text.Substring(firstMatch.Index + firstMatch.Length);
                        afterFirst = Regex.Replace(afterFirst, $@"\b{Regex.Escape(userName)}\b", "", RegexOptions.IgnoreCase);
                        text = text.Substring(0, firstMatch.Index + firstMatch.Length) + afterFirst;
                        text = Regex.Replace(text, @"\s+", " ").Trim();
                        modified = true;
                        reason = "Removed duplicate name mentions";
                    }
                }
            }

            // Rule: Greeting should be 1-2 sentences, not a wall of text
            var sentences = SplitIntoSentences(text);
            if (sentences.Count > 3)
            {
                // Keep first 2 sentences for greeting
                text = string.Join(" ", sentences.Take(2));
                modified = true;
                reason = "Trimmed greeting to 2 sentences";
            }

            return new FinalAssistantMessage
            {
                Text = text,
                WasModified = modified,
                ModificationReason = reason
            };
        }

        /// <summary>
        /// Remove excessive self-references (Atlas mentioned more than once)
        /// </summary>
        private static FinalAssistantMessage RemoveExcessiveSelfReferences(FinalAssistantMessage msg)
        {
            if (ResponseQualityGate.HasExcessiveSelfReference(msg.Text))
            {
                var cleaned = ResponseQualityGate.RemoveExcessiveSelfReferences(msg.Text);
                return new FinalAssistantMessage
                {
                    Text = cleaned,
                    WasModified = true,
                    ModificationReason = "Removed excessive self-references"
                };
            }
            return msg;
        }

        /// <summary>
        /// Enhance a too-short greeting response
        /// </summary>
        private static FinalAssistantMessage EnhanceShortGreeting(FinalAssistantMessage msg, string? userName)
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            
            // Add a light follow-up question based on depth
            var followUp = depth switch
            {
                ConversationDepth.ColdStart => "What would you like to work on?",
                ConversationDepth.Warm => "What's on the agenda?",
                ConversationDepth.Familiar => "What's up?",
                _ => "What can I help with?"
            };

            var enhanced = msg.Text.TrimEnd('.', '!', '?') + ". " + followUp;

            return new FinalAssistantMessage
            {
                Text = enhanced,
                WasModified = true,
                ModificationReason = "Enhanced short greeting"
            };
        }

        /// <summary>
        /// Check if response appears to be a greeting
        /// </summary>
        private static bool IsGreetingResponse(string text)
        {
            var lower = text.ToLowerInvariant();
            var greetingPatterns = new[]
            {
                @"^(hello|hi|hey|good\s+(morning|afternoon|evening|day))",
                @"^greetings",
                @"^welcome",
                @"nice to (meet|see) you"
            };

            return greetingPatterns.Any(p => Regex.IsMatch(lower, p));
        }

        /// <summary>
        /// Get a fallback response when LLM fails
        /// </summary>
        private static string GetFallbackResponse(string userInput, string? userName)
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            var namePrefix = !string.IsNullOrEmpty(userName) ? $"{userName}, " : "";

            // Check if it was a greeting
            if (IsGreetingInput(userInput))
            {
                return depth switch
                {
                    ConversationDepth.ColdStart => $"Hello{(string.IsNullOrEmpty(userName) ? "" : $", {userName}")}. What would you like to tackle first - security, system tuning, or something else?",
                    ConversationDepth.Warm => $"Hey{(string.IsNullOrEmpty(userName) ? "" : $" {userName}")}. What's on your mind?",
                    ConversationDepth.Familiar => "What's up?",
                    _ => "Ready when you are."
                };
            }

            // Generic fallback
            return $"{namePrefix}I'm ready to help. What would you like to do?";
        }

        /// <summary>
        /// Check if user input is a greeting
        /// </summary>
        private static bool IsGreetingInput(string input)
        {
            var lower = input.ToLowerInvariant().Trim();
            var greetings = new[] { "hello", "hi", "hey", "good morning", "good afternoon", "good evening", "howdy", "yo", "sup" };
            return greetings.Any(g => lower.StartsWith(g) || lower == g);
        }

        /// <summary>
        /// Split text into sentences
        /// </summary>
        private static List<string> SplitIntoSentences(string text)
        {
            // Simple sentence splitting
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return sentences;
        }
    }

    /// <summary>
    /// The final assistant message - single source of truth for UI and TTS
    /// </summary>
    public class FinalAssistantMessage
    {
        /// <summary>
        /// The final text to display AND speak
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Hash of the text for sync verification
        /// </summary>
        public string TextHash { get; set; } = string.Empty;

        /// <summary>
        /// Whether the response was modified from the original
        /// </summary>
        public bool WasModified { get; set; }

        /// <summary>
        /// Reason for modification (for debugging)
        /// </summary>
        public string? ModificationReason { get; set; }
    }
}
