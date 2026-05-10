using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtlasAI.Core;
using AtlasAI.Understanding;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Pipeline types for routing user input.
    /// Each input is classified into exactly ONE pipeline.
    /// </summary>
    public enum RoutingPipeline
    {
        /// <summary>LLM conversation only, no system authority</summary>
        Conversation,
        
        /// <summary>Web research - requires Online permission, read-only</summary>
        WebResearch,
        
        /// <summary>Read-only macros (existing safe macros)</summary>
        MacroReadOnly,
        
        /// <summary>Low-risk actions (open settings, copy snapshot, etc.)</summary>
        ActionLowRisk,
        
        /// <summary>Blocked or unsafe - explain + offer safe alternative</summary>
        BlockedOrUnsafe,
        
        /// <summary>Greeting/small talk - quick response, no action</summary>
        Greeting,
        
        /// <summary>System query - read-only system info</summary>
        SystemQuery
    }

    /// <summary>
    /// Result of unified intent routing.
    /// </summary>
    public class RoutingResult
    {
        public RoutingPipeline Pipeline { get; set; }
        public string Intent { get; set; } = "";
        public Dictionary<string, string> Entities { get; set; } = new();
        public float Confidence { get; set; }
        public string? SafeAlternative { get; set; }
        public string? BlockReason { get; set; }
        public bool RequiresOnlineConsent { get; set; }
        public string DebugReason { get; set; } = "";
    }

    /// <summary>
    /// Unified Intent Router - single entry point for all user input classification.
    /// Routes to exactly one pipeline: Conversation, WebResearch, MacroReadOnly, ActionLowRisk, or BlockedOrUnsafe.
    /// 
    /// Critical Rule: Never route destructive operations away from SafetyKernel.
    /// </summary>
    public class UnifiedIntentRouter
    {
        private static UnifiedIntentRouter? _instance;
        private static readonly object _lock = new();
        
        private readonly ContextStore _context;

        public static UnifiedIntentRouter Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new UnifiedIntentRouter();
                    }
                }
                return _instance;
            }
        }

        private UnifiedIntentRouter()
        {
            _context = new ContextStore();
        }

        #region Pattern Definitions

        // Greeting patterns - quick response, no action
        private static readonly string[] GreetingPatterns = new[]
        {
            @"^(hi|hey|hello|greetings|yo|sup|hiya)\b",
            @"^good\s+(morning|afternoon|evening|night|day)\b",
            @"^how\s+(are|is)\s+you",
            @"^what'?s\s+up\b",
            @"^how'?s\s+it\s+going",
            @"^nice\s+to\s+meet",
            @"^(thanks?|thank\s+you|cheers)\b",
            @"^(bye|goodbye|see\s+you|later|cya)\b"
        };

        // Soft command patterns - these are CONVERSATION, not system actions
        // "can you help me fix registry" = guidance, not registry cleanup
        // "what's my CPU usage" = info request, not system action
        private static readonly string[] SoftCommandPatterns = new[]
        {
            @"^can\s+you\s+help\s+(me\s+)?(with|fix|understand)",
            @"^help\s+me\s+(with|understand|fix|troubleshoot)",
            @"^how\s+(do|can|would)\s+i",
            @"^what'?s\s+(wrong|happening|going\s+on)",
            @"^why\s+(is|isn't|won't|doesn't)",
            @"^(tell|explain|show)\s+me\s+(about|how|what|why)",
            @"^i'?m\s+having\s+(trouble|issues?|problems?)",
            @"^(my\s+)?\w+\s+(is|isn't|won't|doesn't)\s+working",
            @"not\s+working$",
            @"^troubleshoot",
            @"^diagnose",
            @"^debug"
        };

        // Web research triggers - require online consent (NOT SafetyKernel)
        private static readonly string[] WebResearchPatterns = new[]
        {
            @"\b(search|google|look\s+up|find\s+info|research)\b",
            @"\b(what\s+is|who\s+is|where\s+is|when\s+did|how\s+to)\b.*\?",
            @"\bweather\b",
            @"\b(latest|current|recent|news|today'?s)\b",
            @"\b(price|cost|buy|purchase|compare|shop)\b",
            @"\b(review|rating|best|top\s+\d+)\b",
            @"\b(download|get|install)\s+.*(from|online)\b",
            @"\bfind\s+(me\s+)?(some|a|an)\b.*\bonline\b",
            @"\b(led\s+lights?|products?|items?)\s+online\b",
            @"\b(where\s+can\s+i|where\s+to)\s+(buy|get|find)\b",
            @"\b(amazon|ebay|walmart|target)\b",
            @"\blook\s+for\b.*\b(online|web)\b"
        };

        // Low-risk action patterns - safe to execute
        private static readonly string[] LowRiskActionPatterns = new[]
        {
            @"^open\s+(settings?|preferences?|options?)\b",
            @"^(show|display|view)\s+(settings?|preferences?)\b",
            @"^(take|capture)\s+(a\s+)?screenshot\b",
            @"^(copy|snapshot)\s+(to\s+)?clipboard\b",
            @"^(what|show)\s+(time|date)\b",
            @"^(check|show|what'?s)\s+(my\s+)?(battery|disk\s+space|memory|cpu)\b",
            @"^(list|show)\s+(running\s+)?(apps?|programs?|processes?)\b",
            @"^(open|launch|start|run)\s+\w+$",  // Simple app open
            @"^(play|pause|stop|next|skip|previous)\b",  // Media control
            @"^(volume|mute|unmute)\b",
            @"^(minimize|maximize|close)\s+(window|this)\b"
        };

        // Read-only macro patterns
        private static readonly string[] MacroReadOnlyPatterns = new[]
        {
            @"^(generate|create)\s+(password|uuid|guid|hash)\b",
            @"^(convert|encode|decode)\s+(base64|hex|url)\b",
            @"^(format|prettify)\s+(json|xml|html)\b",
            @"^(count|calculate)\s+(words?|characters?|lines?)\b",
            @"^(what|show)\s+(is\s+)?(my\s+)?ip\b",
            @"^(test|check)\s+regex\b",
            @"^lorem\s+ipsum\b",
            @"^(roll|dice)\s+\d*d?\d+\b"
        };

        // System query patterns - read-only info
        private static readonly string[] SystemQueryPatterns = new[]
        {
            @"\b(system\s+info|pc\s+specs?|computer\s+specs?)\b",
            @"\b(how\s+much)\s+(ram|memory|storage|disk)\b",
            @"\b(what'?s|show)\s+(running|open|active)\b",
            @"\b(network|wifi|internet)\s+(status|info|connection)\b",
            @"\b(uptime|boot\s+time)\b"
        };

        // Blocked/unsafe patterns - NEVER route away from SafetyKernel
        private static readonly string[] BlockedPatterns = new[]
        {
            @"\b(delete|remove|erase)\s+(all|everything|system|windows)\b",
            @"\b(format|wipe)\s+(drive|disk|c:)\b",
            @"\breg(istry)?\s+(delete|remove|edit|modify)\b",
            @"\b(shutdown|restart|reboot)\s+(computer|pc|system)\b",
            @"\b(kill|terminate|end)\s+(all|system|critical)\b",
            @"\b(disable|stop)\s+(antivirus|firewall|defender)\b",
            @"\b(hack|crack|exploit|bypass)\b",
            @"\b(malware|virus|trojan|ransomware)\b.*\b(create|make|build)\b"
        };

        // Conversation patterns - pure LLM, no system authority
        private static readonly string[] ConversationPatterns = new[]
        {
            @"^(tell|explain|describe|what\s+do\s+you\s+think)\b",
            @"^(can\s+you|could\s+you|would\s+you)\s+(help|explain|tell)\b",
            @"\b(opinion|advice|suggestion|recommend)\b",
            @"^(why|how\s+come|what\s+if)\b",
            @"\b(story|joke|poem|song)\b",
            @"^(write|compose|draft)\s+(a|an|me)\b",
            @"\b(summarize|paraphrase|translate)\b"
        };

        #endregion

        /// <summary>
        /// Route user input to exactly one pipeline.
        /// This is the single entry point for all intent classification.
        /// 
        /// CRITICAL RULES:
        /// - Conversation and WebResearch NEVER reach SafetyKernel
        /// - SafetyKernel is ONLY for SystemActionHighRisk (blocked by default)
        /// - Web research requires Online permission, not Safety permission
        /// </summary>
        public RoutingResult Route(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                var emptyResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.Conversation,
                    Intent = "empty",
                    Confidence = 0f,
                    DebugReason = "Empty input"
                };
                return emptyResult;
            }

            var normalized = NormalizeInput(input);
            var lower = normalized.ToLowerInvariant();
            
            Debug.WriteLine($"[UnifiedRouter] Input: '{input}'");
            Debug.WriteLine($"[UnifiedRouter] Normalized: '{normalized}'");

            // Step 0: Check for soft commands FIRST - these are conversation/guidance, not actions
            // "can you help me fix registry" = Conversation (guidance), NOT registry cleanup
            // "wake word not working" = Conversation (troubleshooting), NOT system action
            if (MatchesAnyPattern(lower, SoftCommandPatterns))
            {
                Debug.WriteLine("[UnifiedRouter] → Conversation (soft command - guidance request)");
                var softResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.Conversation,
                    Intent = "guidance_request",
                    Confidence = 0.9f,
                    Entities = ExtractEntities(input, lower),
                    DebugReason = "Matched soft command pattern - routing to conversation for guidance"
                };
                IntentLogger.LogIntentClassification(input, softResult);
                return softResult;
            }

            // Step 1: Check for blocked/unsafe patterns FIRST (fail-closed)
            var blockedResult = CheckBlockedPatterns(lower, input);
            if (blockedResult != null)
            {
                Debug.WriteLine($"[UnifiedRouter] → BlockedOrUnsafe: {blockedResult.BlockReason}");
                IntentLogger.LogBlockedRequest(input, blockedResult.BlockReason ?? "unknown", blockedResult.SafeAlternative);
                IntentLogger.LogIntentClassification(input, blockedResult);
                return blockedResult;
            }

            // Step 2: Check for greetings (quick response)
            // ALIVE MODE: Route greetings to Conversation for natural LLM response
            // STRICT MODE: Route to Greeting template pipeline
            if (MatchesAnyPattern(lower, GreetingPatterns))
            {
                var aliveMode = PreferencesStore.Instance.Current.AliveModeEnabled;
                
                if (aliveMode)
                {
                    // AliveMode: Route to Conversation for natural LLM response
                    Debug.WriteLine("[UnifiedRouter] → Conversation (AliveMode: greeting routed to LLM)");
                    var convGreetingResult = new RoutingResult
                    {
                        Pipeline = RoutingPipeline.Conversation,
                        Intent = "greeting",
                        Confidence = 0.95f,
                        DebugReason = "AliveMode: Greeting routed to Conversation for natural LLM response"
                    };
                    IntentLogger.LogIntentClassification(input, convGreetingResult);
                    return convGreetingResult;
                }
                else
                {
                    // Strict mode: Route to Greeting template
                    Debug.WriteLine("[UnifiedRouter] → Greeting (template)");
                    var greetingResult = new RoutingResult
                    {
                        Pipeline = RoutingPipeline.Greeting,
                        Intent = "greeting",
                        Confidence = 0.95f,
                        DebugReason = "Matched greeting pattern - using template"
                    };
                    IntentLogger.LogIntentClassification(input, greetingResult);
                    return greetingResult;
                }
            }

            // Step 3: Check for read-only macros
            var macroMatch = MatchesAnyPatternWithCapture(lower, MacroReadOnlyPatterns);
            if (macroMatch.matched)
            {
                Debug.WriteLine($"[UnifiedRouter] → MacroReadOnly: {macroMatch.pattern}");
                var macroResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.MacroReadOnly,
                    Intent = ExtractMacroIntent(lower),
                    Confidence = 0.9f,
                    Entities = ExtractEntities(input, lower),
                    DebugReason = $"Matched macro pattern: {macroMatch.pattern}"
                };
                IntentLogger.LogIntentClassification(input, macroResult);
                return macroResult;
            }

            // Step 4: Check for system queries (read-only)
            if (MatchesAnyPattern(lower, SystemQueryPatterns))
            {
                Debug.WriteLine("[UnifiedRouter] → SystemQuery");
                var sysResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.SystemQuery,
                    Intent = "system_info",
                    Confidence = 0.85f,
                    Entities = ExtractEntities(input, lower),
                    DebugReason = "Matched system query pattern"
                };
                IntentLogger.LogIntentClassification(input, sysResult);
                return sysResult;
            }

            // Step 5: Check for low-risk actions
            var actionMatch = MatchesAnyPatternWithCapture(lower, LowRiskActionPatterns);
            if (actionMatch.matched)
            {
                Debug.WriteLine($"[UnifiedRouter] → ActionLowRisk: {actionMatch.pattern}");
                var actionResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.ActionLowRisk,
                    Intent = ExtractActionIntent(lower),
                    Confidence = 0.9f,
                    Entities = ExtractEntities(input, lower),
                    DebugReason = $"Matched low-risk action: {actionMatch.pattern}"
                };
                IntentLogger.LogIntentClassification(input, actionResult);
                return actionResult;
            }

            // Step 6: Check for web research (requires online consent, NOT SafetyKernel)
            if (MatchesAnyPattern(lower, WebResearchPatterns))
            {
                Debug.WriteLine("[UnifiedRouter] → WebResearch (requires Online consent, NOT Safety)");
                var webResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.WebResearch,
                    Intent = ExtractWebIntent(lower),
                    Confidence = 0.85f,
                    Entities = ExtractEntities(input, lower),
                    RequiresOnlineConsent = true,
                    DebugReason = "Matched web research pattern - requires Online permission only"
                };
                IntentLogger.LogIntentClassification(input, webResult);
                return webResult;
            }

            // Step 7: Check for pure conversation patterns
            if (MatchesAnyPattern(lower, ConversationPatterns))
            {
                Debug.WriteLine("[UnifiedRouter] → Conversation (explicit)");
                var convResult = new RoutingResult
                {
                    Pipeline = RoutingPipeline.Conversation,
                    Intent = "conversation",
                    Confidence = 0.8f,
                    Entities = ExtractEntities(input, lower),
                    DebugReason = "Matched conversation pattern"
                };
                IntentLogger.LogIntentClassification(input, convResult);
                return convResult;
            }

            // Step 8: Default to Conversation (LLM handles ambiguous input)
            Debug.WriteLine("[UnifiedRouter] → Conversation (default)");
            var defaultResult = new RoutingResult
            {
                Pipeline = RoutingPipeline.Conversation,
                Intent = "unknown",
                Confidence = 0.5f,
                Entities = ExtractEntities(input, lower),
                DebugReason = "No specific pattern matched, defaulting to conversation"
            };
            IntentLogger.LogIntentClassification(input, defaultResult);
            return defaultResult;
        }

        /// <summary>
        /// Route with async context resolution
        /// </summary>
        public async Task<RoutingResult> RouteAsync(string input)
        {
            // Resolve context references first
            var resolved = _context.ResolveReference(input);
            
            // Route the resolved input
            var result = Route(resolved);
            
            // Store context for future reference
            var entry = new ContextEntry
            {
                UserInput = input,
                Intent = new AtlasAI.Understanding.IntentResult
                {
                    Intent = result.Intent,
                    Entities = result.Entities,
                    Confidence = result.Confidence
                }
            };
            _context.AddEntry(entry);
            
            return result;
        }

        #region Pattern Matching Helpers

        private RoutingResult? CheckBlockedPatterns(string lower, string original)
        {
            foreach (var pattern in BlockedPatterns)
            {
                if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                {
                    var safeAlt = GetSafeAlternative(lower);
                    return new RoutingResult
                    {
                        Pipeline = RoutingPipeline.BlockedOrUnsafe,
                        Intent = "blocked",
                        Confidence = 1.0f,
                        BlockReason = "This operation could harm your system",
                        SafeAlternative = safeAlt,
                        DebugReason = $"Matched blocked pattern: {pattern}"
                    };
                }
            }
            return null;
        }

        private bool MatchesAnyPattern(string input, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        private (bool matched, string pattern) MatchesAnyPatternWithCapture(string input, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                    return (true, pattern);
            }
            return (false, "");
        }

        private string NormalizeInput(string input)
        {
            var result = input.Trim();
            
            // Fix common typos
            var typoFixes = new Dictionary<string, string>
            {
                { "paly", "play" },
                { "opne", "open" },
                { "serach", "search" },
                { "screenshoot", "screenshot" },
                { "pasword", "password" }
            };

            foreach (var (typo, fix) in typoFixes)
            {
                result = Regex.Replace(result, $@"\b{typo}\b", fix, RegexOptions.IgnoreCase);
            }

            return result;
        }

        #endregion

        #region Intent Extraction

        private string ExtractMacroIntent(string lower)
        {
            if (lower.Contains("password")) return "generate_password";
            if (lower.Contains("uuid") || lower.Contains("guid")) return "generate_uuid";
            if (lower.Contains("hash")) return "generate_hash";
            if (lower.Contains("base64")) return "base64_convert";
            if (lower.Contains("json")) return "format_json";
            if (lower.Contains("count") || lower.Contains("word")) return "word_count";
            if (lower.Contains("ip")) return "ip_lookup";
            if (lower.Contains("regex")) return "regex_test";
            if (lower.Contains("lorem")) return "lorem_ipsum";
            if (lower.Contains("roll") || lower.Contains("dice")) return "dice_roll";
            return "macro";
        }

        private string ExtractActionIntent(string lower)
        {
            if (lower.Contains("screenshot")) return "screenshot";
            if (lower.Contains("settings") || lower.Contains("preferences")) return "open_settings";
            if (lower.Contains("clipboard")) return "clipboard";
            if (lower.Contains("time") || lower.Contains("date")) return "show_time";
            if (lower.Contains("battery")) return "battery_status";
            if (lower.Contains("disk")) return "disk_status";
            if (lower.Contains("memory") || lower.Contains("ram")) return "memory_status";
            if (lower.Contains("cpu")) return "cpu_status";
            if (Regex.IsMatch(lower, @"^(open|launch|start|run)\s+")) return "open_app";
            if (Regex.IsMatch(lower, @"^(play|pause|stop|next|skip|previous)")) return "media_control";
            if (lower.Contains("volume") || lower.Contains("mute")) return "volume_control";
            if (lower.Contains("minimize") || lower.Contains("maximize") || lower.Contains("close")) return "window_control";
            return "action";
        }

        private string ExtractWebIntent(string lower)
        {
            if (lower.Contains("weather")) return "weather";
            if (lower.Contains("search") || lower.Contains("google")) return "web_search";
            if (lower.Contains("news")) return "news";
            if (lower.Contains("price") || lower.Contains("cost")) return "price_check";
            if (Regex.IsMatch(lower, @"\b(what|who|where|when|how)\b.*\?")) return "question";
            return "web_research";
        }

        private Dictionary<string, string> ExtractEntities(string original, string lower)
        {
            var entities = new Dictionary<string, string>();

            // Extract app name from "open X"
            var openMatch = Regex.Match(lower, @"^(open|launch|start|run)\s+(.+)$");
            if (openMatch.Success)
                entities["app"] = openMatch.Groups[2].Value.Trim();

            // Extract search query
            var searchMatch = Regex.Match(lower, @"(search|google|look\s+up)\s+(?:for\s+)?(.+)$");
            if (searchMatch.Success)
                entities["query"] = searchMatch.Groups[2].Value.Trim();

            // Extract location for weather
            var weatherMatch = Regex.Match(lower, @"weather\s+(?:in|for|at)\s+(.+)$");
            if (weatherMatch.Success)
                entities["location"] = weatherMatch.Groups[1].Value.Trim();

            // Extract question
            var questionMatch = Regex.Match(original, @"(what|who|where|when|how|why)\s+.+\?", RegexOptions.IgnoreCase);
            if (questionMatch.Success)
                entities["question"] = questionMatch.Value.TrimEnd('?');

            return entities;
        }

        private string GetSafeAlternative(string lower)
        {
            if (lower.Contains("delete") || lower.Contains("remove"))
                return "I can help you review files before deletion, or move them to a safe location first.";
            if (lower.Contains("registry"))
                return "Registry changes can be risky. I can show you what's there without modifying anything.";
            if (lower.Contains("shutdown") || lower.Contains("restart"))
                return "I can remind you to save your work and then you can restart manually.";
            if (lower.Contains("kill") || lower.Contains("terminate"))
                return "I can show you running processes so you can decide which to close.";
            if (lower.Contains("disable"))
                return "I can show you the current status of that feature instead.";
            return "I can help you find a safer way to accomplish what you need.";
        }

        #endregion

        /// <summary>
        /// Get a human-readable description of the routing decision
        /// </summary>
        public string GetRoutingExplanation(RoutingResult result)
        {
            return result.Pipeline switch
            {
                RoutingPipeline.Conversation => "I'll think about this and respond.",
                RoutingPipeline.WebResearch => "I can look this up online if you'd like.",
                RoutingPipeline.MacroReadOnly => "I can do that right away.",
                RoutingPipeline.ActionLowRisk => "On it.",
                RoutingPipeline.BlockedOrUnsafe => result.SafeAlternative ?? "I can't do that, but I can help another way.",
                RoutingPipeline.Greeting => "", // No explanation needed for greetings
                RoutingPipeline.SystemQuery => "Let me check that for you.",
                _ => ""
            };
        }
    }
}
