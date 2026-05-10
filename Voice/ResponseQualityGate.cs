using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using AtlasAI.Conversation.Models;
using AtlasAI.Core;

namespace AtlasAI.Voice
{
    /// <summary>
    /// STEP 30 + ALIVE MODE: Filters out generic/low-quality LLM responses and provides fallbacks.
    /// 
    /// CRITICAL: This gate evaluates the ASSISTANT'S OUTPUT, not the user input.
    /// 
    /// ALIVE MODE BEHAVIOR (AliveModeEnabled = true):
    /// - LLM output wins by default - templates are LAST RESORT only
    /// - Only reject if: null/empty, request failed/timeout, or contains "As an AI language model..."
    /// - Never reject for "too short" or "filler" phrases - just log them
    /// - Templates must NOT rewrite/replace valid LLM text
    /// 
    /// STRICT MODE BEHAVIOR (AliveModeEnabled = false):
    /// - Original strict quality gates apply
    /// - More template fallbacks for short/generic responses
    /// </summary>
    public static class ResponseQualityGate
    {
        /// <summary>
        /// Check if Alive Mode is enabled (prefer natural AI replies)
        /// </summary>
        public static bool IsAliveModeEnabled => PreferencesStore.Instance.Current.AliveModeEnabled;
        // Base filler phrases to reject (always banned)
        private static readonly string[] BaseFillerPhrases = new[]
        {
            "sure!", "no problem", "hey", "hey there", "hi there",
            "i'm just an ai", "i can help with", "i'd be happy to",
            "absolutely!", "of course!", "great question",
            "that's a great", "thanks for asking", "let me help",
            "i understand", "i see what you mean", "got it!",
            "no worries", "you bet", "for sure", "totally",
            "awesome!", "cool!", "nice!", "sweet!"
        };
        
        // Self-reference phrases to avoid (don't repeatedly say "Atlas")
        private static readonly string[] SelfReferencePhrases = new[]
        {
            "i'm atlas", "i am atlas", "atlas here", "this is atlas",
            "atlas can", "atlas will", "atlas is", "as atlas",
            "my name is atlas", "atlas speaking"
        };

        // Actionable verbs that indicate specificity
        private static readonly string[] ActionableVerbs = new[]
        {
            "open", "check", "run", "try", "set", "copy", "export", "import",
            "click", "select", "navigate", "restart", "update", "install",
            "uninstall", "scan", "verify", "confirm", "enable", "disable",
            "create", "delete", "move", "rename", "search", "find", "test"
        };

        // STEP 30 FIX: Relaxed thresholds - let LLM responses through
        // Previous values were too aggressive and caused template fallbacks
        private const int MinWordCount = 3;  // Was 6 - too strict
        private const int MinSpecificityScore = 0;  // Was 2 - too strict, caused "garbage" responses

        /// <summary>
        /// Get all banned phrases including personality-specific ones.
        /// </summary>
        private static HashSet<string> GetAllBannedPhrases()
        {
            var banned = new HashSet<string>(BaseFillerPhrases, StringComparer.OrdinalIgnoreCase);
            
            // Add self-reference phrases (avoid repeatedly saying "Atlas")
            foreach (var phrase in SelfReferencePhrases)
            {
                banned.Add(phrase.ToLowerInvariant());
            }
            
            // Add personality-specific banned phrases
            var personality = PersonalityProfile.Current;
            foreach (var phrase in personality.BannedPhrases)
            {
                banned.Add(phrase.ToLowerInvariant());
            }
            
            return banned;
        }
        
        /// <summary>
        /// Check if response contains excessive self-references.
        /// Returns true if the response mentions "Atlas" more than once.
        /// </summary>
        public static bool HasExcessiveSelfReference(string response)
        {
            var count = Regex.Matches(response, @"\batlas\b", RegexOptions.IgnoreCase).Count;
            return count > 1;
        }
        
        /// <summary>
        /// Remove excessive self-references from response.
        /// Keeps at most one "Atlas" mention.
        /// </summary>
        public static string RemoveExcessiveSelfReferences(string response)
        {
            var matches = Regex.Matches(response, @"\batlas\b", RegexOptions.IgnoreCase);
            if (matches.Count <= 1) return response;
            
            // Keep the first mention, remove subsequent ones
            var result = response;
            for (int i = matches.Count - 1; i > 0; i--)
            {
                var match = matches[i];
                // Replace "Atlas" with "I" or remove depending on context
                var before = result.Substring(Math.Max(0, match.Index - 10), Math.Min(10, match.Index));
                if (before.Contains("is") || before.Contains("'s"))
                {
                    // "Atlas is" -> "I am"
                    result = result.Remove(match.Index, match.Length).Insert(match.Index, "I");
                }
                else if (before.Contains("can") || before.Contains("will"))
                {
                    // "Atlas can" -> "I can"
                    result = result.Remove(match.Index, match.Length).Insert(match.Index, "I");
                }
                else
                {
                    // Just remove the name
                    result = result.Remove(match.Index, match.Length);
                }
            }
            
            // Clean up any double spaces
            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result;
        }

        /// <summary>
        /// STEP 30 + ALIVE MODE: Check if response passes quality gate.
        /// 
        /// CRITICAL: This evaluates the ASSISTANT'S OUTPUT (candidateResponse), NOT the user input.
        /// The userInput is only used for context (keyword matching, etc).
        /// 
        /// ALIVE MODE: LLM output wins by default. Only reject for:
        /// - null/empty/whitespace
        /// - request failed/timeout
        /// - contains "As an AI language model..." or system prompt fragments
        /// 
        /// Logs: QualityGate with candidatePreview, passed, rejectionReason, specificityScore, retryCount, aliveModeEnabled.
        /// </summary>
        public static QualityCheckResult Check(string response, string userInput, int retryCount = 0)
        {
            var aliveMode = IsAliveModeEnabled;
            
            // STEP 30: Log what we're actually checking (the assistant output)
            var candidatePreview = response?.Length > 80 
                ? response.Substring(0, 80) + "..." 
                : response ?? "(empty)";
            Debug.WriteLine($"[QualityGate] Evaluating ASSISTANT OUTPUT: '{candidatePreview}'");
            Debug.WriteLine($"[QualityGate] AliveMode: {aliveMode}, User input context: '{userInput?.Substring(0, Math.Min(40, userInput?.Length ?? 0))}...'");
            
            // === ALWAYS REJECT: Empty/null responses ===
            if (string.IsNullOrWhiteSpace(response))
            {
                var emptyResult = new QualityCheckResult
                {
                    Passed = false,
                    Reason = "Empty response",
                    SpecificityScore = 0,
                    SuggestedFallback = GetFallbackResponse(userInput)
                };
                
                LogQualityDecision("(empty)", false, "Empty response", 0, retryCount, aliveMode);
                return emptyResult;
            }

            var lower = response.ToLowerInvariant().Trim();
            var wordCount = response.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            
            // Calculate specificity score (for logging, not rejection in AliveMode)
            var score = CalculateSpecificityScore(response, userInput);
            Debug.WriteLine($"[QualityGate] SpecificityScore: {score}, WordCount: {wordCount}");

            // === ALWAYS REJECT: AI self-reference phrases that indicate confusion ===
            var aiConfusionPhrases = new[]
            {
                "as an ai language model",
                "as a large language model",
                "i don't have access to",
                "i cannot access",
                "i'm not able to",
                "i am not able to"
            };
            
            foreach (var phrase in aiConfusionPhrases)
            {
                if (lower.Contains(phrase))
                {
                    Debug.WriteLine($"[QualityGate] Rejected: AI confusion phrase '{phrase}'");
                    var confusionResult = new QualityCheckResult
                    {
                        Passed = false,
                        Reason = $"AI confusion phrase: {phrase}",
                        SpecificityScore = score,
                        SuggestedFallback = GetFallbackResponse(userInput)
                    };
                    
                    LogQualityDecision(candidatePreview, false, $"AI confusion: {phrase}", score, retryCount, aliveMode);
                    return confusionResult;
                }
            }

            // === ALIVE MODE: Accept almost everything else ===
            if (aliveMode)
            {
                // In AliveMode, only reject exact single-word fillers that add no value
                var exactFillers = new[] { "ok", "okay", "k", "yup", "nope" };
                if (exactFillers.Contains(lower) && wordCount == 1)
                {
                    Debug.WriteLine($"[QualityGate] AliveMode: Rejected single-word filler '{lower}'");
                    var fillerResult = new QualityCheckResult
                    {
                        Passed = false,
                        Reason = $"Single-word filler: {lower}",
                        SpecificityScore = score,
                        SuggestedFallback = GetFallbackResponse(userInput)
                    };
                    
                    LogQualityDecision(candidatePreview, false, $"Single-word filler: {lower}", score, retryCount, aliveMode);
                    return fillerResult;
                }
                
                // AliveMode: ACCEPT the response - LLM wins
                Debug.WriteLine($"[QualityGate] ✅ ALIVE MODE PASS: '{response.Substring(0, Math.Min(50, response.Length))}...'");
                LogQualityDecision(candidatePreview, true, null, score, retryCount, aliveMode);
                return new QualityCheckResult { Passed = true, SpecificityScore = score };
            }

            // === STRICT MODE: Original quality gates ===
            var bannedPhrases = GetAllBannedPhrases();

            // Reject exact single-word filler matches
            var strictFillers = new[] { "hey", "hi", "ok", "okay", "sure", "yes", "no", "yeah", "yep", "nope", "k", "yup" };
            if (strictFillers.Contains(lower))
            {
                Debug.WriteLine($"[QualityGate] Strict: Rejected exact filler match '{lower}'");
                var fillerResult = new QualityCheckResult
                {
                    Passed = false,
                    Reason = $"Exact filler phrase: {lower}",
                    SpecificityScore = score,
                    SuggestedFallback = GetFallbackResponse(userInput)
                };
                
                LogQualityDecision(candidatePreview, false, $"Exact filler: {lower}", score, retryCount, aliveMode);
                return fillerResult;
            }

            // Reject very short responses (under 2 words) that aren't valid confirmations
            if (wordCount < 2 && !IsValidShortResponse(lower))
            {
                Debug.WriteLine($"[QualityGate] Strict: Rejected too short ({wordCount} words)");
                var shortResult = new QualityCheckResult
                {
                    Passed = false,
                    Reason = $"Too short ({wordCount} words)",
                    SpecificityScore = score,
                    SuggestedFallback = GetFallbackResponse(userInput)
                };
                
                LogQualityDecision(candidatePreview, false, $"Too short: {wordCount} words", score, retryCount, aliveMode);
                return shortResult;
            }

            // STRICT MODE: PASSED
            LogQualityDecision(candidatePreview, true, null, score, retryCount, aliveMode);
            Debug.WriteLine($"[QualityGate] ✅ STRICT MODE PASS: '{response.Substring(0, Math.Min(50, response.Length))}...'");
            return new QualityCheckResult { Passed = true, SpecificityScore = score };
        }
        
        /// <summary>
        /// Log quality gate decision with AliveMode flag
        /// </summary>
        private static void LogQualityDecision(string candidatePreview, bool passed, string? reason, int score, int retryCount, bool aliveMode)
        {
            AI.AIDebugLogger.LogQualityGate(
                AI.AIDebugLogger.GenerateRequestId(),
                candidatePreview,
                passed,
                reason,
                score,
                retryCount
            );
            
            StabilizationLogger.LogQualityGateEvaluation(
                "N/A", candidatePreview, passed, reason, score, retryCount);
            
            // Additional AliveMode-specific logging
            Debug.WriteLine($"[QualityGate] Decision: {(passed ? "ACCEPTED" : "REJECTED")}, AliveMode={aliveMode}, Reason={reason ?? "N/A"}, Score={score}, Retry={retryCount}");
        }

        /// <summary>
        /// Calculate specificity score using heuristics.
        /// </summary>
        public static int CalculateSpecificityScore(string response, string userInput)
        {
            var score = 0;
            var responseLower = response.ToLowerInvariant();
            var inputLower = userInput.ToLowerInvariant();
            var bannedPhrases = GetAllBannedPhrases();

            // +1 if echoes at least one keyword from user input
            var keywords = ExtractKeywords(userInput);
            if (keywords.Any(k => responseLower.Contains(k)))
            {
                score++;
                Debug.WriteLine("[SpecificityScore] +1 for keyword echo");
            }

            // +1 if references ActiveProblem
            var activeProblem = ConversationWorkingMemory.Instance.ActiveProblem;
            if (!string.IsNullOrEmpty(activeProblem))
            {
                var problemKeywords = ExtractKeywords(activeProblem);
                if (problemKeywords.Any(k => responseLower.Contains(k)))
                {
                    score++;
                    Debug.WriteLine("[SpecificityScore] +1 for ActiveProblem reference");
                }
            }

            // +1 if contains actionable verb
            if (ActionableVerbs.Any(v => responseLower.Contains(v)))
            {
                score++;
                Debug.WriteLine("[SpecificityScore] +1 for actionable verb");
            }

            // DISABLED: Vague input detection causes false positives on capability questions
            // +1 if asks exactly one targeted question when input is vague
            /*
            if (NextBestQuestion.IsVagueInput(userInput))
            {
                var questionCount = Regex.Matches(response, @"\?").Count;
                if (questionCount == 1)
                {
                    score++;
                    Debug.WriteLine("[SpecificityScore] +1 for single clarifying question");
                }
            }
            */

            // -2 if contains filler/generic openers
            foreach (var filler in bannedPhrases)
            {
                if (responseLower.Contains(filler))
                {
                    score -= 2;
                    Debug.WriteLine($"[SpecificityScore] -2 for filler: {filler}");
                    break;
                }
            }

            return score;
        }

        /// <summary>
        /// Get the regeneration prompt for LLM retry.
        /// </summary>
        public static string GetRegenerationPrompt(string originalResponse, string userInput)
        {
            var activeProblem = ConversationWorkingMemory.Instance.ActiveProblem;
            var problemContext = !string.IsNullOrEmpty(activeProblem) 
                ? $" The user's current issue is: {activeProblem}." 
                : "";
            
            var personality = PersonalityProfile.Current;
            var toneHint = personality.Id switch
            {
                PersonalityId.Cold => "Keep it terse and direct.",
                PersonalityId.Serious => "Keep it professional and precise.",
                PersonalityId.Funny => "Keep Jarvis tone with subtle wit.",
                PersonalityId.Friendly => "Keep it warm and helpful.",
                _ => "Keep Jarvis tone."
            };
            
            return $"Your previous response was too generic.{problemContext} Be specific. Reference the user's request about '{string.Join(", ", ExtractKeywords(userInput).Take(3))}' and propose the next concrete step. {toneHint} 1-3 sentences.";
        }

        private static bool IsValidShortResponse(string lower)
        {
            // Valid short confirmations
            var validShort = new[]
            {
                "understood", "done", "complete", "finished", "processing",
                "on it", "right away", "certainly", "very well", "of course",
                "one moment", "working on it", "straightaway", "consider it done"
            };

            return validShort.Any(v => lower.Contains(v));
        }

        private static bool ReferencesUserRequest(string response, string userInput)
        {
            var keywords = ExtractKeywords(userInput);
            var responseLower = response.ToLowerInvariant();

            // At least one keyword should appear
            return keywords.Any(k => responseLower.Contains(k));
        }

        private static List<string> ExtractKeywords(string input)
        {
            var stopWords = new HashSet<string>
            {
                "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
                "have", "has", "had", "do", "does", "did", "will", "would", "could",
                "should", "may", "might", "must", "shall", "can", "need", "i", "you",
                "me", "my", "your", "it", "this", "that", "what", "how", "why", "when",
                "where", "who", "please", "can", "could", "would", "just", "want", "to"
            };

            var words = Regex.Split(input.ToLowerInvariant(), @"\W+")
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .ToList();

            return words;
        }

        private static string GetFallbackResponse(string userInput)
        {
            var depth = ConversationContext.Instance.CurrentDepth;
            
            // DISABLED: Vague input detection causes false positives on capability questions
            // Just return a depth-appropriate acknowledgement instead
            /*
            // Check if input is vague and needs clarification
            if (NextBestQuestion.IsVagueInput(userInput))
            {
                var question = NextBestQuestion.GetClarifyingQuestion(userInput);
                return question;
            }
            */

            // Return depth-appropriate acknowledgement
            return DepthAwareTemplates.GetAcknowledgement(depth);
        }

        /// <summary>
        /// Check if response contains at least one meaningful keyword from user input.
        /// Prevents generic responses that don't address the user's actual request.
        /// </summary>
        private static bool ContainsUserKeyword(string response, string userInput)
        {
            var keywords = ExtractKeywords(userInput);
            if (keywords.Count == 0) return true; // No keywords to check
            
            var responseLower = response.ToLowerInvariant();
            
            // Must contain at least one keyword from user input
            return keywords.Any(k => responseLower.Contains(k));
        }
    }

    public class QualityCheckResult
    {
        public bool Passed { get; set; }
        public string? Reason { get; set; }
        public int SpecificityScore { get; set; }
        public string? SuggestedFallback { get; set; }
    }

    /// <summary>
    /// STEP 30: Security domain quality rules.
    /// Ensures security responses use actual scan data when available.
    /// </summary>
    public static class SecurityResponseValidator
    {
        private static readonly string[] SecurityIntentPhrases = new[]
        {
            "what are the threats", "show me the threats", "list detections",
            "what did you find", "explain threats", "security results",
            "scan results", "what was detected", "show detections",
            "any threats", "any malware", "any viruses"
        };

        private static readonly string[] GenericSecurityResponses = new[]
        {
            "i need the report", "i need the scan", "i don't have access",
            "please run a scan", "i can't see the results", "no scan data",
            "i would need to see", "i don't have the scan"
        };

        /// <summary>
        /// Check if user input is asking about security scan results
        /// </summary>
        public static bool IsSecurityExplainIntent(string userInput)
        {
            var lower = userInput.ToLowerInvariant();
            return SecurityIntentPhrases.Any(p => lower.Contains(p));
        }

        /// <summary>
        /// Validate security response uses actual data when available.
        /// Returns (isValid, reason) tuple.
        /// </summary>
        public static (bool IsValid, string? Reason) ValidateSecurityResponse(
            string response, 
            bool scanResultExists,
            int detectionCount)
        {
            var lower = response.ToLowerInvariant();

            // If scan result exists, response must NOT use generic "I need the report" phrases
            if (scanResultExists)
            {
                foreach (var generic in GenericSecurityResponses)
                {
                    if (lower.Contains(generic))
                    {
                        return (false, $"Response uses generic phrase '{generic}' but scan result exists");
                    }
                }

                // If there are detections, response should mention at least one specific thing
                if (detectionCount > 0)
                {
                    // Check for specificity markers
                    var hasSpecifics = 
                        lower.Contains("found") ||
                        lower.Contains("detected") ||
                        lower.Contains("critical") ||
                        lower.Contains("high") ||
                        lower.Contains("medium") ||
                        lower.Contains("low") ||
                        System.Text.RegularExpressions.Regex.IsMatch(lower, @"\d+\s*(threat|detection|issue|item)");

                    if (!hasSpecifics)
                    {
                        return (false, "Security response lacks specifics despite detections existing");
                    }
                }
            }

            return (true, null);
        }

        /// <summary>
        /// Get regeneration prompt for security domain
        /// </summary>
        public static string GetSecurityRegenerationPrompt(int detectionCount, string topThreatName)
        {
            if (detectionCount == 0)
            {
                return "The scan completed with no threats detected. Confirm the system is clean.";
            }

            return $"Use the LastScanResult data. Found {detectionCount} detection(s). " +
                   $"Top threat: {topThreatName}. List severity breakdown and top items. " +
                   "Do NOT say 'I need the report' - you have the data.";
        }
    }

    /// <summary>
    /// Detects underspecified input and provides targeted clarifying questions.
    /// </summary>
    public static class NextBestQuestion
    {
        // Vague input patterns
        private static readonly string[] VaguePatterns = new[]
        {
            @"^it'?s?\s+broken",
            @"^help$",
            @"^help\s+me$",
            @"^fix\s+it$",
            @"^make\s+it\s+(work|better)$",
            @"^something'?s?\s+wrong",
            @"^not\s+working",
            @"^doesn'?t\s+work",
            @"^it\s+won'?t",
            @"^can'?t\s+do",
            @"^having\s+(issues?|problems?|trouble)",
            @"^there'?s?\s+a\s+problem"
        };

        // Clarifying questions by category
        private static readonly Dictionary<string, string[]> ClarifyingQuestions = new()
        {
            ["general"] = new[]
            {
                "Which part is failing - voice, chat, or commands.",
                "What did you expect to happen, and what happened instead.",
                "When did this start occurring.",
                "What were you trying to do when this happened."
            },
            ["voice"] = new[]
            {
                "Is the issue with speech recognition, wake word, or text-to-speech.",
                "Are you hearing any audio at all.",
                "Does the microphone indicator show activity."
            },
            ["file"] = new[]
            {
                "Which file or folder is affected.",
                "What operation were you attempting.",
                "Do you see any error message."
            },
            ["app"] = new[]
            {
                "Which application is having the issue.",
                "Does it happen every time or intermittently.",
                "Have you tried restarting the application."
            },
            ["system"] = new[]
            {
                "Is this affecting the whole system or just one application.",
                "When did you first notice this.",
                "Have there been any recent changes to your system."
            }
        };

        private static readonly Random _random = new();

        // Conversational phrases that should NOT be treated as vague
        private static readonly string[] ConversationalPhrases = new[]
        {
            "how are you", "how's it going", "what's up", "how do you do",
            "good morning", "good afternoon", "good evening", "good night",
            "hello", "hi", "hey", "greetings", "nice to meet you",
            "thank you", "thanks", "cheers", "goodbye", "bye", "see you",
            "what can you do", "who are you", "what are you"
        };

        /// <summary>
        /// Check if input is too vague to act on.
        /// </summary>
        public static bool IsVagueInput(string input)
        {
            var lower = input.ToLowerInvariant().Trim();

            // Never treat conversational phrases as vague
            foreach (var phrase in ConversationalPhrases)
            {
                if (lower.Contains(phrase))
                {
                    return false;
                }
            }

            // Very short inputs are often vague (but not conversational ones)
            if (lower.Split(' ').Length <= 3 && !IsSpecificCommand(lower) && !IsConversational(lower))
            {
                return true;
            }

            // Check against vague patterns
            foreach (var pattern in VaguePatterns)
            {
                if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsConversational(string lower)
        {
            // Check for greeting/small talk patterns
            var conversationalPatterns = new[]
            {
                @"^(hi|hey|hello|greetings|yo|sup)\b",
                @"^good\s+(morning|afternoon|evening|night|day)\b",
                @"how\s+(are|is|do)\s+you",
                @"what'?s\s+up",
                @"nice\s+to\s+meet",
                @"thank",
                @"^(bye|goodbye|see\s+you)"
            };

            foreach (var pattern in conversationalPatterns)
            {
                if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get a single targeted clarifying question.
        /// </summary>
        public static string GetClarifyingQuestion(string input)
        {
            var lower = input.ToLowerInvariant();
            var depth = ConversationContext.Instance.CurrentDepth;

            // Determine category
            string category = "general";
            if (lower.Contains("voice") || lower.Contains("speak") || lower.Contains("hear") || lower.Contains("listen"))
                category = "voice";
            else if (lower.Contains("file") || lower.Contains("folder") || lower.Contains("document"))
                category = "file";
            else if (lower.Contains("app") || lower.Contains("program") || lower.Contains("software"))
                category = "app";
            else if (lower.Contains("system") || lower.Contains("computer") || lower.Contains("pc"))
                category = "system";

            var questions = ClarifyingQuestions[category];
            var question = questions[_random.Next(questions.Length)];

            // Adjust formality based on depth
            if (depth == ConversationDepth.Familiar)
            {
                // Shorter, more direct
                question = question.Replace("Could you ", "").Replace("Can you ", "");
            }
            else if (depth == ConversationDepth.ColdStart)
            {
                // Add polite prefix if not present
                if (!question.StartsWith("Could") && !question.StartsWith("Would"))
                {
                    question = "Could you tell me: " + char.ToLower(question[0]) + question.Substring(1);
                }
            }

            return question;
        }

        private static bool IsSpecificCommand(string input)
        {
            var commandStarters = new[]
            {
                "open", "close", "start", "stop", "run", "play", "pause", "search",
                "find", "show", "hide", "create", "delete", "scan", "check"
            };

            return commandStarters.Any(c => input.StartsWith(c));
        }
    }
}
