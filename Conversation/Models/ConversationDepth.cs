using System;
using System.Diagnostics;
using AtlasAI.Core;

namespace AtlasAI.Conversation.Models
{
    /// <summary>
    /// Conversation familiarity level - affects response formality.
    /// </summary>
    public enum ConversationDepth
    {
        /// <summary>Initial state - formal, measured responses.</summary>
        ColdStart,
        /// <summary>After some interaction - slightly less stiff.</summary>
        Warm,
        /// <summary>Extended interaction - friendly but still Jarvis-like.</summary>
        Familiar
    }

    /// <summary>
    /// Tracks conversation depth within a session. Manages escalation/de-escalation.
    /// </summary>
    public class ConversationContext
    {
        private static ConversationContext? _instance;
        private static readonly object _lock = new();

        public static ConversationContext Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConversationContext();
                    }
                }
                return _instance;
            }
        }

        // Counters
        public int TurnCount { get; private set; }
        public int SuccessfulHelpCount { get; private set; }
        public DateTime SessionStart { get; private set; }
        public DateTime LastInteraction { get; private set; }
        public string? KnownUserName { get; set; }

        // Current depth
        public ConversationDepth CurrentDepth { get; private set; } = ConversationDepth.ColdStart;

        // Escalation thresholds
        private const int TurnsForWarm = 4;
        private const int TurnsForFamiliar = 12;
        private const int HelpsForWarm = 1;
        private const int HelpsForFamiliar = 3;
        private const int IdleMinutesForDowngrade = 30;

        // Frustration patterns
        private static readonly string[] FrustrationPatterns = new[]
        {
            "stop", "you're not listening", "not listening", "wrong", "that's wrong",
            "no that's not", "forget it", "nevermind", "useless", "stupid",
            "doesn't work", "not working", "broken", "ugh", "ffs", "wtf"
        };

        private ConversationContext()
        {
            SessionStart = DateTime.Now;
            LastInteraction = DateTime.Now;
            Debug.WriteLine("[ConversationContext] Initialized at ColdStart");
        }

        /// <summary>
        /// Record a user turn. Call this on each user message.
        /// </summary>
        public void RecordTurn(string userInput)
        {
            // Check for idle downgrade first
            CheckIdleDowngrade();

            TurnCount++;
            LastInteraction = DateTime.Now;

            // Check for frustration
            if (DetectFrustration(userInput))
            {
                Downgrade("frustration detected");
                return;
            }

            // Check for escalation
            EvaluateEscalation();

            Debug.WriteLine($"[ConversationContext] Turn {TurnCount}, Depth: {CurrentDepth}");
        }

        /// <summary>
        /// Record a successful help (macro completion, action success, user thanks).
        /// </summary>
        public void RecordSuccessfulHelp()
        {
            SuccessfulHelpCount++;
            LastInteraction = DateTime.Now;
            EvaluateEscalation();
            Debug.WriteLine($"[ConversationContext] Help count: {SuccessfulHelpCount}, Depth: {CurrentDepth}");
        }

        /// <summary>
        /// Check if user input indicates thanks (triggers success count).
        /// </summary>
        public bool IsThanks(string input)
        {
            var lower = input.ToLowerInvariant();
            return lower.Contains("thank") || lower.Contains("thanks") || 
                   lower.Contains("cheers") || lower.Contains("perfect") ||
                   lower.Contains("great job") || lower.Contains("nice work") ||
                   lower.Contains("well done") || lower.Contains("awesome");
        }

        /// <summary>
        /// Check if Alive Mode is enabled (sticky depth, less downgrades)
        /// </summary>
        private static bool IsAliveModeEnabled => PreferencesStore.Instance.Current.AliveModeEnabled;

        // Strong frustration patterns that trigger downgrade even in AliveMode
        private static readonly string[] StrongFrustrationPatterns = new[]
        {
            "useless", "stupid", "idiot", "dumb", "hate you", "you suck",
            "worst", "terrible", "awful", "garbage", "trash", "pathetic"
        };

        private bool DetectFrustration(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // In AliveMode, only strong frustration triggers downgrade
            if (IsAliveModeEnabled)
            {
                foreach (var pattern in StrongFrustrationPatterns)
                {
                    if (lower.Contains(pattern))
                    {
                        Debug.WriteLine($"[ConversationContext] AliveMode: STRONG frustration detected: {pattern}");
                        return true;
                    }
                }
                // Minor frustration is logged but doesn't trigger downgrade in AliveMode
                foreach (var pattern in FrustrationPatterns)
                {
                    if (lower.Contains(pattern))
                    {
                        Debug.WriteLine($"[ConversationContext] AliveMode: Minor frustration logged (no downgrade): {pattern}");
                    }
                }
                return false;
            }
            
            // Strict mode: original behavior
            foreach (var pattern in FrustrationPatterns)
            {
                if (lower.Contains(pattern))
                {
                    Debug.WriteLine($"[ConversationContext] Frustration pattern detected: {pattern}");
                    return true;
                }
            }
            return false;
        }

        private void CheckIdleDowngrade()
        {
            // In AliveMode, disable automatic idle downgrade
            if (IsAliveModeEnabled)
            {
                var idleMinutes = (DateTime.Now - LastInteraction).TotalMinutes;
                if (idleMinutes >= IdleMinutesForDowngrade && CurrentDepth != ConversationDepth.ColdStart)
                {
                    Debug.WriteLine($"[ConversationContext] AliveMode: Idle {idleMinutes:F0}min - depth preserved (no downgrade)");
                }
                return;
            }
            
            // Strict mode: original behavior
            var idle = (DateTime.Now - LastInteraction).TotalMinutes;
            if (idle >= IdleMinutesForDowngrade && CurrentDepth != ConversationDepth.ColdStart)
            {
                Downgrade($"idle for {idle:F0} minutes");
            }
        }

        private void EvaluateEscalation()
        {
            var newDepth = CurrentDepth;

            // ColdStart → Warm
            if (CurrentDepth == ConversationDepth.ColdStart)
            {
                if (TurnCount >= TurnsForWarm || SuccessfulHelpCount >= HelpsForWarm)
                {
                    newDepth = ConversationDepth.Warm;
                }
            }
            // Warm → Familiar
            else if (CurrentDepth == ConversationDepth.Warm)
            {
                if (TurnCount >= TurnsForFamiliar || SuccessfulHelpCount >= HelpsForFamiliar)
                {
                    newDepth = ConversationDepth.Familiar;
                }
            }

            if (newDepth != CurrentDepth)
            {
                Debug.WriteLine($"[ConversationContext] Escalating: {CurrentDepth} → {newDepth}");
                var oldDepth = CurrentDepth;
                CurrentDepth = newDepth;
                
                // Log depth change
                try
                {
                    AtlasAI.Agent.IntentLogger.LogDepthChange(oldDepth.ToString(), newDepth.ToString(), 
                        $"turns={TurnCount}, helps={SuccessfulHelpCount}");
                }
                catch { }
            }
        }

        private void Downgrade(string reason)
        {
            var oldDepth = CurrentDepth;
            if (CurrentDepth == ConversationDepth.Familiar)
            {
                CurrentDepth = ConversationDepth.Warm;
                Debug.WriteLine($"[ConversationContext] Downgraded to Warm ({reason})");
            }
            else if (CurrentDepth == ConversationDepth.Warm)
            {
                CurrentDepth = ConversationDepth.ColdStart;
                Debug.WriteLine($"[ConversationContext] Downgraded to ColdStart ({reason})");
            }
            
            if (oldDepth != CurrentDepth)
            {
                // Log depth change
                try
                {
                    AtlasAI.Agent.IntentLogger.LogDepthChange(oldDepth.ToString(), CurrentDepth.ToString(), reason);
                }
                catch { }
            }
        }

        /// <summary>
        /// Reset session state.
        /// </summary>
        public void Reset()
        {
            TurnCount = 0;
            SuccessfulHelpCount = 0;
            SessionStart = DateTime.Now;
            LastInteraction = DateTime.Now;
            CurrentDepth = ConversationDepth.ColdStart;
            KnownUserName = null;
            Debug.WriteLine("[ConversationContext] Reset to ColdStart");
        }

        /// <summary>
        /// Get LLM instruction for current depth.
        /// </summary>
        public string GetDepthInstructionForLLM()
        {
            return CurrentDepth switch
            {
                ConversationDepth.ColdStart => 
                    "ConversationDepth = ColdStart. Use formal, measured responses. Address user as 'sir'. Be polite and professional.",
                ConversationDepth.Warm => 
                    "ConversationDepth = Warm. Reduce formality slightly. Still professional but less stiff. Can use contractions occasionally.",
                ConversationDepth.Familiar => 
                    "ConversationDepth = Familiar. Friendly but still Jarvis-like. British cadence, slightly robotic restraint. No slang, no emojis, no exclamation marks.",
                _ => ""
            };
        }
    }
}
