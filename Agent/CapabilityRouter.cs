using System;
using System.Collections.Generic;
using System.Diagnostics;
using AtlasAI.Core;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Capability decision outcomes.
    /// </summary>
    public enum CapabilityDecision
    {
        /// <summary>Execute the action immediately.</summary>
        Proceed,
        /// <summary>Request permission from user first.</summary>
        RequestPermission,
        /// <summary>Capability unavailable - offer alternative.</summary>
        OfferAlternative
    }

    /// <summary>
    /// Permission types that can be requested.
    /// </summary>
    public enum PermissionType
    {
        None,
        OnlineAccess,
        HighRiskAction,
        Microphone,
        WakeWord
    }

    /// <summary>
    /// Result of capability routing decision.
    /// </summary>
    public class CapabilityResult
    {
        public CapabilityDecision Decision { get; set; }
        public PermissionType PermissionNeeded { get; set; } = PermissionType.None;
        public string? AlternativeResponse { get; set; }
        public string? PermissionPrompt { get; set; }
        public string? ActionDescription { get; set; }
        public string DebugReason { get; set; } = "";
        
        /// <summary>
        /// For WebResearch denied - offline guidance + clarifying question.
        /// </summary>
        public string? OfflineGuidance { get; set; }
        
        /// <summary>
        /// For HighRisk blocked - safe alternative macro or manual steps.
        /// </summary>
        public string? SafeAlternative { get; set; }
    }

    /// <summary>
    /// Current capability states snapshot.
    /// </summary>
    public class CapabilityState
    {
        public OnlineModeSetting OnlineMode { get; set; } = OnlineModeSetting.Off;
        public bool IsOnlineAccessActive { get; set; }
        public bool DangerousActionsEnabled { get; set; }
        public bool AllowRegistryCleanup { get; set; }
        public bool MicEnabled { get; set; }
        public bool WakeWordEnabled { get; set; }
        
        public static CapabilityState GetCurrent()
        {
            var prefs = PreferencesStore.Instance.Current;
            var onlineManager = OnlineModeManager.Instance;
            
            return new CapabilityState
            {
                OnlineMode = onlineManager.Setting,
                IsOnlineAccessActive = onlineManager.IsOnlineAccessActive,
                DangerousActionsEnabled = prefs.DangerousActionsEnabled,
                AllowRegistryCleanup = prefs.AllowRegistryCleanup,
                MicEnabled = prefs.EnableMicrophone,
                WakeWordEnabled = prefs.EnableWakeWord
            };
        }
    }

    /// <summary>
    /// Single decision layer that runs after intent classification and before action.
    /// Routes to: Proceed, RequestPermission, or OfferAlternative.
    /// 
    /// Rule: Never show "blocked" unless user asked for high-risk action explicitly.
    /// </summary>
    public class CapabilityRouter
    {
        private static CapabilityRouter? _instance;
        private static readonly object _lock = new();

        public static CapabilityRouter Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new CapabilityRouter();
                    }
                }
                return _instance;
            }
        }

        private CapabilityRouter()
        {
            Debug.WriteLine("[CapabilityRouter] Initialized");
        }

        /// <summary>
        /// Route a classified intent through capability checks.
        /// Returns decision: Proceed, RequestPermission, or OfferAlternative.
        /// </summary>
        public CapabilityResult Route(RoutingResult intent, string userInput, bool isVoice = false)
        {
            var state = CapabilityState.GetCurrent();
            
            Debug.WriteLine($"[CapabilityRouter] Routing: {intent.Pipeline} ({intent.Intent})");
            Debug.WriteLine($"[CapabilityRouter] State: Online={state.OnlineMode}, Dangerous={state.DangerousActionsEnabled}, Mic={state.MicEnabled}");

            var result = intent.Pipeline switch
            {
                RoutingPipeline.Conversation => HandleConversation(intent, state),
                RoutingPipeline.Greeting => HandleGreeting(intent, state),
                RoutingPipeline.WebResearch => HandleWebResearch(intent, userInput, state),
                RoutingPipeline.MacroReadOnly => HandleMacroReadOnly(intent, state),
                RoutingPipeline.SystemQuery => HandleSystemQuery(intent, state),
                RoutingPipeline.ActionLowRisk => HandleActionLowRisk(intent, state),
                RoutingPipeline.BlockedOrUnsafe => HandleBlockedOrUnsafe(intent, userInput, state),
                _ => new CapabilityResult { Decision = CapabilityDecision.Proceed }
            };

            // Check voice-specific requirements
            if (isVoice && !state.MicEnabled)
            {
                result = HandleMicDisabled(intent, result);
            }

            // Log the decision
            IntentLogger.LogCapabilityDecision(
                userInput,
                intent.Pipeline.ToString(),
                result.Decision.ToString(),
                result.PermissionNeeded.ToString(),
                result.DebugReason);

            Debug.WriteLine($"[CapabilityRouter] Decision: {result.Decision} ({result.DebugReason})");
            return result;
        }

        #region Pipeline Handlers

        private CapabilityResult HandleConversation(RoutingResult intent, CapabilityState state)
        {
            // Conversation always proceeds - no capability requirements
            return new CapabilityResult
            {
                Decision = CapabilityDecision.Proceed,
                DebugReason = "Conversation requires no special capabilities"
            };
        }

        private CapabilityResult HandleGreeting(RoutingResult intent, CapabilityState state)
        {
            // Greetings always proceed
            return new CapabilityResult
            {
                Decision = CapabilityDecision.Proceed,
                DebugReason = "Greeting requires no special capabilities"
            };
        }

        private CapabilityResult HandleWebResearch(RoutingResult intent, string userInput, CapabilityState state)
        {
            // Check if online access is already active
            if (state.IsOnlineAccessActive)
            {
                return new CapabilityResult
                {
                    Decision = CapabilityDecision.Proceed,
                    DebugReason = "Online access already active"
                };
            }

            // Check online mode setting
            if (state.OnlineMode == OnlineModeSetting.AlwaysAllow)
            {
                return new CapabilityResult
                {
                    Decision = CapabilityDecision.Proceed,
                    DebugReason = "Online mode set to AlwaysAllow"
                };
            }

            // Need to request permission
            return new CapabilityResult
            {
                Decision = CapabilityDecision.RequestPermission,
                PermissionNeeded = PermissionType.OnlineAccess,
                PermissionPrompt = GetOnlinePermissionPrompt(userInput),
                OfflineGuidance = GetOfflineGuidance(userInput, intent),
                DebugReason = "Online access required, requesting permission"
            };
        }

        private CapabilityResult HandleMacroReadOnly(RoutingResult intent, CapabilityState state)
        {
            // Read-only macros always proceed - no dangerous capabilities
            return new CapabilityResult
            {
                Decision = CapabilityDecision.Proceed,
                DebugReason = "Read-only macro requires no special capabilities"
            };
        }

        private CapabilityResult HandleSystemQuery(RoutingResult intent, CapabilityState state)
        {
            // System queries (read-only info) always proceed
            return new CapabilityResult
            {
                Decision = CapabilityDecision.Proceed,
                DebugReason = "System query is read-only"
            };
        }

        private CapabilityResult HandleActionLowRisk(RoutingResult intent, CapabilityState state)
        {
            // Low-risk actions proceed without permission
            return new CapabilityResult
            {
                Decision = CapabilityDecision.Proceed,
                DebugReason = "Low-risk action proceeds without permission"
            };
        }

        private CapabilityResult HandleBlockedOrUnsafe(RoutingResult intent, string userInput, CapabilityState state)
        {
            // Check if this is an explicit high-risk request
            if (IsExplicitHighRiskRequest(userInput))
            {
                // User explicitly asked for something dangerous
                if (state.DangerousActionsEnabled)
                {
                    // Dangerous actions enabled - request confirmation
                    return new CapabilityResult
                    {
                        Decision = CapabilityDecision.RequestPermission,
                        PermissionNeeded = PermissionType.HighRiskAction,
                        PermissionPrompt = GetHighRiskPermissionPrompt(userInput, intent),
                        ActionDescription = GetActionDescription(userInput, intent),
                        SafeAlternative = GetSafeAlternative(userInput, intent),
                        DebugReason = "Explicit high-risk request, requesting confirmation"
                    };
                }
                else
                {
                    // Dangerous actions disabled - offer alternative (not "blocked")
                    return new CapabilityResult
                    {
                        Decision = CapabilityDecision.OfferAlternative,
                        AlternativeResponse = GetSmartAlternativeResponse(userInput, intent),
                        SafeAlternative = GetSafeAlternative(userInput, intent),
                        DebugReason = "High-risk action disabled, offering alternative"
                    };
                }
            }
            else
            {
                // Not an explicit request - treat as conversation/guidance
                return new CapabilityResult
                {
                    Decision = CapabilityDecision.OfferAlternative,
                    AlternativeResponse = GetGuidanceResponse(userInput, intent),
                    DebugReason = "Implicit high-risk topic, providing guidance"
                };
            }
        }

        private CapabilityResult HandleMicDisabled(RoutingResult intent, CapabilityResult currentResult)
        {
            // Don't override if already offering alternative
            if (currentResult.Decision == CapabilityDecision.OfferAlternative)
                return currentResult;

            return new CapabilityResult
            {
                Decision = CapabilityDecision.RequestPermission,
                PermissionNeeded = PermissionType.Microphone,
                PermissionPrompt = "Voice input requires microphone access.",
                AlternativeResponse = "You can type your request or use push-to-talk instead.",
                DebugReason = "Microphone disabled for voice input"
            };
        }

        #endregion

        #region Explicit Request Detection

        /// <summary>
        /// Detect if user explicitly requested a high-risk action (not just asking about it).
        /// </summary>
        private bool IsExplicitHighRiskRequest(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Explicit action verbs + dangerous targets
            var explicitPatterns = new[]
            {
                @"^(delete|remove|erase|wipe|format)\s+(all|my|the|system)",
                @"^(clean|clear)\s+(registry|temp|cache)\s*(files?)?$",
                @"^(run|execute)\s+.*(cleanup|cleaner|optimizer)",
                @"^(disable|stop|kill)\s+(antivirus|firewall|defender)",
                @"^(shutdown|restart|reboot)\s+(computer|pc|system|now)"
            };

            foreach (var pattern in explicitPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, pattern))
                    return true;
            }

            return false;
        }

        #endregion

        #region Permission Prompts

        private string GetOnlinePermissionPrompt(string userInput)
        {
            return "I can look that up online if you'd like. It's read-only and won't change anything on your system.";
        }

        private string GetHighRiskPermissionPrompt(string userInput, RoutingResult intent)
        {
            var action = GetActionDescription(userInput, intent);
            return $"This will {action}. This action cannot be undone.";
        }

        private string GetActionDescription(string userInput, RoutingResult intent)
        {
            var lower = userInput.ToLowerInvariant();
            
            if (lower.Contains("registry"))
                return "modify Windows registry entries";
            if (lower.Contains("delete") || lower.Contains("remove"))
                return "permanently delete files or data";
            if (lower.Contains("clean") || lower.Contains("clear"))
                return "remove temporary files and cached data";
            if (lower.Contains("shutdown") || lower.Contains("restart"))
                return "restart your computer";
            if (lower.Contains("disable"))
                return "disable a system feature";
            
            return "perform a system-level change";
        }

        #endregion

        #region Smart Fallback Responses

        /// <summary>
        /// Get offline guidance when web research is denied.
        /// Must include: acknowledgement + offline guidance + one clarifying question.
        /// </summary>
        private string GetOfflineGuidance(string userInput, RoutingResult intent)
        {
            var lower = userInput.ToLowerInvariant();
            
            // Product search - LED lights, electronics, etc.
            if (lower.Contains("led") || lower.Contains("light") || lower.Contains("strip"))
            {
                return "No problem, I'll stay offline. For LED strip lights, the main things to consider are length, brightness (lumens), and whether you need RGB color changing or just white. What's your budget range, and where will you be using these?";
            }
            
            // General shopping/buying
            if (lower.Contains("buy") || lower.Contains("find") || lower.Contains("shop") || lower.Contains("purchase"))
            {
                return "No problem, I'll stay offline. I can help you figure out what to look for. What features matter most to you, and do you have a budget in mind?";
            }
            
            // Weather
            if (lower.Contains("weather"))
            {
                return "I can't check the weather right now. You can check your phone's weather app or weather.com for current conditions.";
            }
            
            // Price/cost comparison
            if (lower.Contains("price") || lower.Contains("cost") || lower.Contains("cheap") || lower.Contains("expensive"))
            {
                return "No problem, I'll stay offline. I can help you think through what you need. What's most important - price, quality, or specific features?";
            }
            
            // General search/lookup
            if (lower.Contains("search") || lower.Contains("look up") || lower.Contains("google"))
            {
                return "No problem, I'll stay offline. Can you tell me more about what you're trying to find? I might be able to help from what I know.";
            }
            
            // Reviews/recommendations
            if (lower.Contains("review") || lower.Contains("best") || lower.Contains("recommend"))
            {
                return "No problem, I'll stay offline. I can share general guidance on what to look for. What's the main use case you have in mind?";
            }
            
            // Default - always includes acknowledgement + guidance + question
            return "No problem, I'll stay offline. What specifically are you trying to find out? I might be able to help another way.";
        }

        /// <summary>
        /// Get smart alternative response for blocked high-risk actions.
        /// Never says "blocked" - offers calm explanation + guidance.
        /// </summary>
        private string GetSmartAlternativeResponse(string userInput, RoutingResult intent)
        {
            var lower = userInput.ToLowerInvariant();
            
            // Registry operations
            if (lower.Contains("registry"))
            {
                return "I can't modify the registry automatically right now. I can show you how to do it safely step by step, or run a read-only check to see what's there. Which would be more helpful?";
            }
            
            // Delete/cleanup operations
            if (lower.Contains("delete") && (lower.Contains("all") || lower.Contains("everything")))
            {
                return "I can't delete files automatically right now. I can help you review what's there first, or guide you through doing it manually so you stay in control.";
            }
            
            // Temp/cache cleanup
            if (lower.Contains("clean") || lower.Contains("clear") || lower.Contains("temp") || lower.Contains("cache"))
            {
                return "I can't clean files automatically right now. I can show you how much space they're using, or walk you through the built-in Windows Disk Cleanup tool.";
            }
            
            // Shutdown/restart
            if (lower.Contains("shutdown") || lower.Contains("restart") || lower.Contains("reboot"))
            {
                return "I can't restart your computer automatically. Would you like me to remind you to save your work first, or help you schedule a restart for later?";
            }
            
            // Disable security features
            if (lower.Contains("disable") && (lower.Contains("antivirus") || lower.Contains("firewall") || lower.Contains("defender")))
            {
                return "I can't disable security features. If something's being blocked that shouldn't be, I can help you add an exception instead. What's happening?";
            }
            
            // Kill/terminate processes
            if (lower.Contains("kill") || lower.Contains("terminate") || lower.Contains("end"))
            {
                return "I can't force-close programs automatically. I can show you what's running so you can decide what to close, or help troubleshoot if something's frozen.";
            }
            
            // Default - calm, helpful, offers alternative
            return "I can't do that automatically right now. I can guide you through it step by step, or run a safe diagnostic check instead. What would help most?";
        }

        /// <summary>
        /// Get guidance response for implicit high-risk topics (user asking about, not requesting).
        /// </summary>
        private string GetGuidanceResponse(string userInput, RoutingResult intent)
        {
            var lower = userInput.ToLowerInvariant();
            
            // "can you help me fix registry"
            if (lower.Contains("help") && lower.Contains("registry"))
            {
                return "I can help you understand registry issues. What's happening that makes you think the registry needs attention?";
            }
            
            // "how do I clean up"
            if (lower.Contains("how") && (lower.Contains("clean") || lower.Contains("delete")))
            {
                return "I can walk you through that. What specifically are you trying to clean up?";
            }
            
            // Default - conversational
            return "";  // Empty means pass to LLM for natural conversation
        }

        /// <summary>
        /// Get safe alternative macro or manual steps.
        /// </summary>
        private string GetSafeAlternative(string userInput, RoutingResult intent)
        {
            var lower = userInput.ToLowerInvariant();
            
            if (lower.Contains("registry"))
            {
                return "I can run a read-only registry scan to show you what's there without changing anything.";
            }
            
            if (lower.Contains("clean") || lower.Contains("temp"))
            {
                return "I can show you how much space temporary files are using, so you can decide what to clean.";
            }
            
            if (lower.Contains("delete"))
            {
                return "I can list the files that would be affected, so you can review before deciding.";
            }
            
            return "I can run a safe diagnostic check instead.";
        }

        #endregion
    }
}
