using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Smart intent router - understands what you mean and routes to the right handler.
    /// Like Kiro - just does it without asking clarifying questions.
    /// </summary>
    public static class SmartIntentRouter
    {
        // Context memory - remembers recent actions and topics
        private static readonly Queue<ContextItem> _recentContext = new();
        private const int MaxContextItems = 10;
        
        // Last action result for "that worked" / "that didn't work" feedback
        private static string? _lastActionType;
        private static string? _lastActionTarget;
        private static bool _lastActionSuccess;
        
        /// <summary>
        /// Classify the intent of a user message
        /// </summary>
        public static IntentResult ClassifyIntent(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new IntentResult { Type = IntentType.Unknown };
            
            var text = input.Trim();
            var lower = text.ToLowerInvariant();
            
            // Resolve contextual references first ("that", "it", "this")
            text = ResolveContext(text, lower);
            lower = text.ToLowerInvariant();
            
            // Check for feedback on last action
            var feedback = CheckFeedback(lower);
            if (feedback != null) return feedback;
            
            // === DIRECT ACTIONS (no AI needed) ===
            
            // App control
            if (IsKillIntent(lower))
                return new IntentResult { Type = IntentType.KillApp, Target = ExtractTarget(text, lower, KillPrefixes), Confidence = 0.95 };
            
            if (IsOpenIntent(lower))
                return new IntentResult { Type = IntentType.OpenApp, Target = ExtractTarget(text, lower, OpenPrefixes), Confidence = 0.95 };
            
            // Music
            if (IsPlayMusicIntent(lower))
                return new IntentResult { Type = IntentType.PlayMusic, Target = ExtractMusicQuery(text, lower), Confidence = 0.95 };
            
            if (IsMediaControlIntent(lower))
                return new IntentResult { Type = IntentType.MediaControl, Target = ExtractMediaAction(lower), Confidence = 0.95 };
            
            // System
            if (IsVolumeIntent(lower))
                return new IntentResult { Type = IntentType.Volume, Target = ExtractVolumeAction(lower), Confidence = 0.9 };
            
            if (IsPowerIntent(lower))
                return new IntentResult { Type = IntentType.Power, Target = ExtractPowerAction(lower), Confidence = 0.9 };
            
            // Undo
            if (IsUndoIntent(lower))
                return new IntentResult { Type = IntentType.Undo, Target = ExtractUndoCount(lower), Confidence = 0.95 };
            
            // Screenshot
            if (lower.Contains("screenshot") || lower.Contains("screen capture") || lower.Contains("snip"))
                return new IntentResult { Type = IntentType.Screenshot, Confidence = 0.95 };
            
            // Weather
            if (lower.Contains("weather"))
                return new IntentResult { Type = IntentType.Weather, Target = ExtractLocation(text), Confidence = 0.9 };
            
            // Web search
            if (IsSearchIntent(lower))
                return new IntentResult { Type = IntentType.WebSearch, Target = ExtractSearchQuery(text, lower), Confidence = 0.85 };
            
            // === AGENT TASKS (need AI) ===
            
            // File operations
            if (IsFileIntent(lower))
                return new IntentResult { Type = IntentType.AgentTask, SubType = "file", Target = text, Confidence = 0.8 };
            
            // Software installation
            if (IsInstallIntent(lower))
                return new IntentResult { Type = IntentType.AgentTask, SubType = "install", Target = ExtractInstallTarget(text, lower), Confidence = 0.85 };
            
            // Code generation
            if (IsCodeIntent(lower))
                return new IntentResult { Type = IntentType.AgentTask, SubType = "code", Target = text, Confidence = 0.8 };
            
            // System info/diagnostics
            if (IsSystemInfoIntent(lower))
                return new IntentResult { Type = IntentType.SystemInfo, Target = text, Confidence = 0.85 };
            
            // === AGENT ACTIONS (one-click utilities) ===
            var agentAction = AgentActionEngine.Instance.FindAction(lower);
            if (agentAction != null)
                return new IntentResult { Type = IntentType.AgentAction, Target = agentAction.Id, Confidence = 0.9 };
            
            // Single word app names
            if (Tools.DirectActionHandler.IsKnownApp(lower))
                return new IntentResult { Type = IntentType.OpenApp, Target = lower, Confidence = 0.9 };
            
            // Default to chat/AI
            return new IntentResult { Type = IntentType.Chat, Target = text, Confidence = 0.5 };
        }

        #region Intent Detection Helpers
        
        private static readonly string[] KillPrefixes = { "get rid of ", "turn off ", "shut down ", "kill ", "close ", "stop ", "end ", "quit ", "exit ", "disable " };
        private static readonly string[] OpenPrefixes = { "open ", "launch ", "start ", "run " };
        private static readonly string[] MusicPrefixes = { "play me ", "play some ", "put on some ", "listen to ", "play ", "put on ", "spotify " };
        private static readonly string[] SearchPrefixes = { "search for ", "search ", "google ", "look up ", "find info " };
        
        private static bool IsKillIntent(string lower)
        {
            return lower.StartsWith("kill ") || lower.StartsWith("close ") || 
                   lower.StartsWith("stop ") || lower.StartsWith("end ") ||
                   lower.StartsWith("quit ") || lower.StartsWith("exit ") ||
                   lower.Contains("get rid of") || lower.Contains("shut down ") ||
                   lower.Contains("turn off ") || lower.Contains("disable ");
        }

        private static bool IsOpenIntent(string lower)
        {
            return lower.StartsWith("open ") || lower.StartsWith("launch ") ||
                   lower.StartsWith("start ") || lower.StartsWith("run ");
        }

        private static bool IsPlayMusicIntent(string lower)
        {
            return lower.StartsWith("play ") || lower.StartsWith("put on ") ||
                   lower.StartsWith("spotify ") || lower.StartsWith("listen to ") ||
                   lower.Contains("play some ") || lower.Contains("play me ");
        }

        private static bool IsMediaControlIntent(string lower)
        {
            return lower == "pause" || lower == "stop" || lower == "resume" ||
                   lower == "next" || lower == "skip" || lower == "previous" ||
                   lower == "back" || lower.Contains("pause music") ||
                   lower.Contains("stop music") || lower.Contains("next song") ||
                   lower.Contains("skip song") || lower.Contains("previous song");
        }

        private static bool IsVolumeIntent(string lower)
        {
            return lower.Contains("volume") || lower == "mute" || lower == "unmute" ||
                   lower.Contains("louder") || lower.Contains("quieter");
        }

        private static bool IsPowerIntent(string lower)
        {
            return lower.Contains("shutdown") || lower.Contains("shut down") ||
                   lower.Contains("restart") || lower.Contains("reboot") ||
                   lower.Contains("sleep") || lower.Contains("hibernate") ||
                   lower.Contains("lock computer") || lower.Contains("lock pc") ||
                   lower.Contains("lock screen");
        }

        private static bool IsSearchIntent(string lower)
        {
            return lower.StartsWith("search ") || lower.StartsWith("google ") ||
                   lower.StartsWith("look up ") || lower.StartsWith("find info ");
        }
        
        private static bool IsUndoIntent(string lower)
        {
            return lower == "undo" || lower == "undo that" || lower == "undo last" ||
                   lower.StartsWith("undo ") || lower.Contains("undo last action");
        }
        
        private static bool IsFileIntent(string lower)
        {
            return lower.Contains("create file") || lower.Contains("make file") ||
                   lower.Contains("delete file") || lower.Contains("rename file") ||
                   lower.Contains("move file") || lower.Contains("copy file") ||
                   lower.Contains("create folder") || lower.Contains("make folder") ||
                   lower.Contains("delete folder");
        }
        
        private static bool IsInstallIntent(string lower)
        {
            return lower.StartsWith("install ") || lower.Contains("download and install") ||
                   lower.Contains("get me ") || lower.Contains("set up ");
        }
        
        private static bool IsCodeIntent(string lower)
        {
            return lower.Contains("write code") || lower.Contains("create script") ||
                   lower.Contains("make a program") || lower.Contains("code me") ||
                   lower.Contains("generate code");
        }
        
        private static bool IsSystemInfoIntent(string lower)
        {
            return lower.Contains("system info") || lower.Contains("pc specs") ||
                   lower.Contains("computer specs") || lower.Contains("how much ram") ||
                   lower.Contains("cpu usage") || lower.Contains("disk space") ||
                   lower.Contains("what's running");
        }
        
        #endregion
        
        #region Target Extraction
        
        private static string ExtractTarget(string text, string lower, string[] prefixes)
        {
            var target = text;
            foreach (var p in prefixes)
            {
                if (lower.StartsWith(p))
                {
                    target = text.Substring(p.Length).Trim();
                    break;
                }
                var idx = lower.IndexOf(p);
                if (idx >= 0)
                {
                    target = text.Substring(idx + p.Length).Trim();
                    break;
                }
            }
            
            // Remove "the" prefix
            if (target.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                target = target.Substring(4);
            
            return target;
        }
        
        private static string ExtractMusicQuery(string text, string lower)
        {
            var query = ExtractTarget(text, lower, MusicPrefixes);
            // Remove "on spotify" suffix
            query = Regex.Replace(query, @"\s+on\s+spotify.*$", "", RegexOptions.IgnoreCase);
            return query;
        }
        
        private static string ExtractMediaAction(string lower)
        {
            if (lower.Contains("pause") || lower == "stop") return "pause";
            if (lower.Contains("resume") || lower == "play") return "play";
            if (lower.Contains("next") || lower.Contains("skip")) return "next";
            if (lower.Contains("previous") || lower.Contains("back")) return "previous";
            return "pause";
        }
        
        private static string ExtractVolumeAction(string lower)
        {
            if (lower.Contains("up") || lower.Contains("louder")) return "up";
            if (lower.Contains("down") || lower.Contains("quieter")) return "down";
            if (lower.Contains("mute")) return "mute";
            if (lower.Contains("unmute")) return "unmute";
            
            // Extract percentage
            var match = Regex.Match(lower, @"(\d+)\s*%?");
            if (match.Success) return match.Groups[1].Value;
            
            return "50";
        }
        
        private static string ExtractPowerAction(string lower)
        {
            if (lower.Contains("shutdown") || lower.Contains("shut down")) return "shutdown";
            if (lower.Contains("restart") || lower.Contains("reboot")) return "restart";
            if (lower.Contains("sleep") || lower.Contains("hibernate")) return "sleep";
            if (lower.Contains("lock")) return "lock";
            return "lock";
        }
        
        private static string ExtractUndoCount(string lower)
        {
            if (lower.Contains("all") || lower.Contains("everything")) return "all";
            var match = Regex.Match(lower, @"undo\s+(\d+)");
            if (match.Success) return match.Groups[1].Value;
            return "1";
        }
        
        private static string ExtractSearchQuery(string text, string lower)
        {
            return ExtractTarget(text, lower, SearchPrefixes);
        }
        
        private static string? ExtractLocation(string text)
        {
            var match = Regex.Match(text, @"weather\s+(?:in|for|at)\s+(.+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        
        private static string ExtractInstallTarget(string text, string lower)
        {
            var patterns = new[] { "install ", "download and install ", "get me ", "set up " };
            return ExtractTarget(text, lower, patterns);
        }
        
        #endregion
        
        #region Context Management
        
        private static string ResolveContext(string text, string lower)
        {
            // Handle "that", "it", "this" references
            if (!lower.Contains("that") && !lower.Contains(" it") && !lower.Contains("this"))
                return text;
            
            // Check recent context for what "that/it/this" refers to
            var recent = _recentContext.LastOrDefault();
            if (recent == null) return text;
            
            // "close that" -> "close [last app]"
            if ((lower == "close that" || lower == "kill that" || lower == "stop that") && recent.Type == "app")
                return $"close {recent.Value}";
            
            // "open that" -> "open [last app]"
            if ((lower == "open that" || lower == "start that") && recent.Type == "app")
                return $"open {recent.Value}";
            
            // "play that" -> "play [last music]"
            if (lower == "play that" && recent.Type == "music")
                return $"play {recent.Value}";
            
            // Generic "that overlay" -> "nvidia overlay"
            if (lower.Contains("that overlay") || lower.Contains("the overlay"))
                return text.Replace("that overlay", "nvidia overlay", StringComparison.OrdinalIgnoreCase)
                           .Replace("the overlay", "nvidia overlay", StringComparison.OrdinalIgnoreCase);
            
            return text;
        }
        
        private static IntentResult? CheckFeedback(string lower)
        {
            // "that worked" / "that didn't work" feedback
            if (lower.Contains("that worked") || lower.Contains("perfect") || lower.Contains("thanks"))
            {
                _lastActionSuccess = true;
                return new IntentResult { Type = IntentType.Feedback, Target = "positive", Confidence = 0.95 };
            }
            
            if (lower.Contains("didn't work") || lower.Contains("not working") || lower.Contains("wrong"))
            {
                _lastActionSuccess = false;
                return new IntentResult { Type = IntentType.Feedback, Target = "negative", Confidence = 0.95 };
            }
            
            return null;
        }
        
        /// <summary>
        /// Record an action for context memory
        /// </summary>
        public static void RecordAction(string type, string target, bool success = true)
        {
            _lastActionType = type;
            _lastActionTarget = target;
            _lastActionSuccess = success;
            
            _recentContext.Enqueue(new ContextItem { Type = type, Value = target, Timestamp = DateTime.Now });
            while (_recentContext.Count > MaxContextItems)
                _recentContext.Dequeue();
        }
        
        #endregion
    }
    
    #region Models
    
    public class IntentResult
    {
        public IntentType Type { get; set; }
        public string? SubType { get; set; }
        public string? Target { get; set; }
        public double Confidence { get; set; }
    }
    
    public enum IntentType
    {
        Unknown,
        Chat,
        Feedback,
        
        // Direct actions
        KillApp,
        OpenApp,
        PlayMusic,
        MediaControl,
        Volume,
        Power,
        Screenshot,
        Weather,
        WebSearch,
        Undo,
        SystemInfo,
        
        // Agent actions (one-click utilities)
        AgentAction,
        
        // Agent tasks
        AgentTask
    }
    
    internal class ContextItem
    {
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
    
    #endregion
}
