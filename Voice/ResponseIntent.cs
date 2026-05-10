using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AtlasAI.Voice
{
    public enum ResponseIntentType
    {
        Greeting,
        Introduction,
        SmallTalk,
        Question,
        Command,
        ClarificationNeeded,
        Error,
        Denied,
        Unknown
    }

    public class ResponseIntentResult
    {
        public ResponseIntentType Intent { get; set; }
        public float Confidence { get; set; }
        public string? ExtractedName { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    public static class ResponseIntentClassifier
    {
        private static readonly string[] GreetingPatterns = new[]
        {
            @"^hey\b", @"^hi\b", @"^hello\b", @"^good\s+(morning|afternoon|evening|day)\b",
            @"^howdy\b", @"^greetings\b", @"^yo\b", @"^sup\b", @"^what'?s\s+up\b"
        };

        private static readonly string[] IntroductionPatterns = new[]
        {
            @"\bi'?m\s+(\w+)", @"\bim\s+(\w+)", @"\bmy\s+name\s+is\s+(\w+)", @"\bcall\s+me\s+(\w+)",
            @"\bthis\s+is\s+(\w+)", @"\bname'?s\s+(\w+)", @"\bi\s+am\s+(\w+)"
        };

        private static readonly string[] SmallTalkPatterns = new[]
        {
            @"nice\s+to\s+meet", @"how\s+are\s+you", @"how'?s\s+it\s+going",
            @"what'?s\s+new", @"how\s+do\s+you\s+do", @"pleasure\s+to\s+meet",
            @"good\s+to\s+see\s+you", @"long\s+time\s+no\s+see", @"how\s+have\s+you\s+been",
            @"what'?s\s+happening", @"how'?s\s+your\s+day", @"how'?s\s+everything"
        };

        private static readonly string[] CommandKeywords = new[]
        {
            "open", "close", "start", "stop", "run", "execute", "launch", "show", "hide",
            "create", "delete", "remove", "install", "uninstall", "search", "find", "scan",
            "check", "analyze", "clean", "clear", "set", "change", "update", "download",
            "upload", "send", "copy", "paste", "move", "rename", "shutdown", "restart",
            "lock", "unlock", "mute", "unmute", "play", "pause", "skip", "volume",
            "brightness", "wifi", "bluetooth", "screenshot", "record", "capture", "save",
            "load", "export", "import", "backup", "restore", "schedule", "remind", "timer",
            "alarm", "calculate", "convert", "translate", "generate", "summarize", "explain"
        };

        private static readonly string[] ClarificationPatterns = new[]
        {
            @"what\s+do\s+you\s+mean", @"can\s+you\s+clarify", @"i\s+don'?t\s+understand",
            @"what\s+was\s+that", @"say\s+that\s+again", @"repeat\s+that", @"pardon",
            @"excuse\s+me", @"sorry\s+what", @"huh\??"
        };

        private static readonly string[] DenialPatterns = new[]
        {
            @"^no\b", @"^nope\b", @"^never\s+mind", @"^cancel\b", @"^stop\b", @"^don'?t\b",
            @"^forget\s+it", @"^nevermind"
        };

        public static ResponseIntentResult Classify(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new ResponseIntentResult
                {
                    Intent = ResponseIntentType.Unknown,
                    Confidence = 0f,
                    OriginalText = input ?? string.Empty
                };
            }

            var text = input.Trim().ToLowerInvariant();
            System.Diagnostics.Debug.WriteLine($"[IntentClassifier] Input: \"{text}\"");
            
            var result = new ResponseIntentResult
            {
                OriginalText = input,
                Confidence = 0f
            };

            // Check for introduction first (highest priority for name extraction)
            var introMatch = CheckIntroduction(text);
            if (introMatch.matched)
            {
                result.Intent = ResponseIntentType.Introduction;
                result.Confidence = introMatch.confidence;
                result.ExtractedName = introMatch.name;
                
                if (CheckPatterns(text, GreetingPatterns).matched)
                    result.Parameters["hasGreeting"] = "true";
                if (CheckPatterns(text, SmallTalkPatterns).matched)
                    result.Parameters["hasSmallTalk"] = "true";
                    
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → Introduction (name: {result.ExtractedName})");
                return result;
            }

            // Check small talk (before greeting, as "hello nice to meet you" should be small talk)
            var smallTalkCheck = CheckPatterns(text, SmallTalkPatterns);
            if (smallTalkCheck.matched)
            {
                result.Intent = ResponseIntentType.SmallTalk;
                result.Confidence = smallTalkCheck.confidence;
                if (CheckPatterns(text, GreetingPatterns).matched)
                    result.Parameters["hasGreeting"] = "true";
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → SmallTalk");
                return result;
            }

            // Check greeting
            var greetingCheck = CheckPatterns(text, GreetingPatterns);
            if (greetingCheck.matched)
            {
                result.Intent = ResponseIntentType.Greeting;
                result.Confidence = greetingCheck.confidence;
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → Greeting");
                return result;
            }

            // Check denial
            if (CheckPatterns(text, DenialPatterns).matched)
            {
                result.Intent = ResponseIntentType.Denied;
                result.Confidence = 0.9f;
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → Denied");
                return result;
            }

            // Check clarification
            if (CheckPatterns(text, ClarificationPatterns).matched)
            {
                result.Intent = ResponseIntentType.ClarificationNeeded;
                result.Confidence = 0.85f;
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → ClarificationNeeded");
                return result;
            }

            // Check for commands
            var commandCheck = CheckCommand(text);
            if (commandCheck.matched)
            {
                result.Intent = ResponseIntentType.Command;
                result.Confidence = commandCheck.confidence;
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → Command");
                return result;
            }

            // Check for questions (non-command)
            if (IsQuestion(text))
            {
                result.Intent = ResponseIntentType.Question;
                result.Confidence = 0.8f;
                System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → Question");
                return result;
            }

            // Default to unknown
            result.Intent = ResponseIntentType.Unknown;
            result.Confidence = 0.3f;
            System.Diagnostics.Debug.WriteLine($"[IntentClassifier] → Unknown");
            return result;
        }

        private static (bool matched, float confidence) CheckPatterns(string text, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    return (true, 0.85f);
                }
            }
            return (false, 0f);
        }

        private static (bool matched, float confidence, string? name) CheckIntroduction(string text)
        {
            foreach (var pattern in IntroductionPatterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    var name = match.Groups[1].Value;
                    System.Diagnostics.Debug.WriteLine($"[IntentClassifier] Pattern '{pattern}' matched, extracted: '{name}'");
                    
                    // Validate name (not a common word)
                    if (!IsCommonWord(name) && name.Length >= 2 && name.Length <= 20)
                    {
                        // Capitalize first letter
                        name = char.ToUpper(name[0]) + name.Substring(1).ToLower();
                        return (true, 0.9f, name);
                    }
                }
            }
            return (false, 0f, null);
        }

        private static bool IsCommonWord(string word)
        {
            var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
                "have", "has", "had", "do", "does", "did", "will", "would", "could",
                "should", "may", "might", "must", "shall", "can", "need", "dare",
                "here", "there", "just", "also", "very", "really", "actually"
            };
            return commonWords.Contains(word);
        }

        private static (bool matched, float confidence) CheckCommand(string text)
        {
            var words = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Check if starts with command keyword
            if (words.Length > 0 && CommandKeywords.Contains(words[0]))
            {
                return (true, 0.9f);
            }

            // Check if contains command keyword with action context
            foreach (var keyword in CommandKeywords)
            {
                if (text.Contains(keyword))
                {
                    var index = text.IndexOf(keyword);
                    var confidence = index < 20 ? 0.85f : 0.7f;
                    return (true, confidence);
                }
            }

            // Check for imperative patterns
            if (Regex.IsMatch(text, @"^(please\s+)?(can\s+you\s+|could\s+you\s+|would\s+you\s+)?", RegexOptions.IgnoreCase))
            {
                var afterPolite = Regex.Replace(text, @"^(please\s+)?(can\s+you\s+|could\s+you\s+|would\s+you\s+)?", "", RegexOptions.IgnoreCase);
                var firstWord = afterPolite.Split(' ').FirstOrDefault();
                if (firstWord != null && CommandKeywords.Contains(firstWord))
                {
                    return (true, 0.85f);
                }
            }

            return (false, 0f);
        }

        private static bool IsQuestion(string text)
        {
            if (text.TrimEnd().EndsWith("?"))
                return true;

            var questionStarters = new[] { "what", "who", "where", "when", "why", "how", "which", "whose", "whom", "is", "are", "do", "does", "did", "can", "could", "would", "should", "will" };
            var firstWord = text.Split(' ').FirstOrDefault()?.ToLower();
            
            return firstWord != null && questionStarters.Contains(firstWord);
        }
    }
}
