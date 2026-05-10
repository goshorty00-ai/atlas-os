using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Chooses the best action path: execute tool, ask clarifying question, or provide guide.
    /// Reasons step-by-step to determine the simplest safe path.
    /// </summary>
    public class Planner
    {
        private readonly ContextStore _context;
        private readonly CapabilityRegistry _capabilities;

        public Planner(ContextStore context)
        {
            _context = context;
            _capabilities = new CapabilityRegistry();
        }

        /// <summary>
        /// Plan the best action based on classified intent
        /// </summary>
        public PlannerDecision Plan(IntentResult intent)
        {
            Debug.WriteLine($"[Planner] Planning for intent: {intent.Intent} (confidence: {intent.Confidence:P0})");
            
            var decision = new PlannerDecision();
            
            // Step 1: Check if we understand the request well enough
            if (intent.Confidence < 0.5f || intent.Intent == "unknown")
            {
                decision.Action = PlannerAction.AskClarification;
                decision.ClarificationQuestion = GenerateClarificationQuestion(intent);
                decision.Reasoning = "Low confidence or unknown intent - need more information";
                return decision;
            }
            
            // Step 2: Check if capability exists
            var capability = _capabilities.GetCapability(intent.Intent);
            if (capability == null || !capability.IsImplemented)
            {
                decision.Action = PlannerAction.OfferToBuild;
                decision.Reasoning = $"Capability '{intent.Intent}' is not yet implemented";
                decision.FallbackPath = GenerateFallbackPath(intent);
                decision.GuidanceSteps = GenerateManualSteps(intent);
                return decision;
            }
            
            // Step 3: Assess risk level
            decision.RiskLevel = AssessRisk(intent);
            
            // Step 4: Check if confirmation is needed
            if (intent.NeedsConfirmation || decision.RiskLevel >= RiskLevel.High)
            {
                decision.Action = PlannerAction.ConfirmDestructive;
                decision.RequiresConfirmation = true;
                decision.ToolToExecute = MapIntentToTool(intent.Intent);
                decision.ToolParameters = BuildToolParameters(intent);
                decision.Reasoning = $"Action requires confirmation due to {decision.RiskLevel} risk level";
                return decision;
            }
            
            // Step 5: Check if we have all required parameters
            var missingParams = GetMissingParameters(intent);
            if (missingParams.Count > 0)
            {
                decision.Action = PlannerAction.AskClarification;
                decision.ClarificationQuestion = $"I need to know: {string.Join(", ", missingParams)}";
                decision.Reasoning = "Missing required parameters";
                return decision;
            }
            
            // Step 6: Ready to execute
            decision.Action = PlannerAction.ExecuteTool;
            decision.ToolToExecute = MapIntentToTool(intent.Intent);
            decision.ToolParameters = BuildToolParameters(intent);
            decision.Reasoning = $"All requirements met, executing {decision.ToolToExecute}";
            
            return decision;
        }

        /// <summary>
        /// Generate a clarification question based on what's unclear
        /// </summary>
        private string GenerateClarificationQuestion(IntentResult intent)
        {
            // Only ask ONE question, be specific
            if (intent.Intent == "unknown")
            {
                // Try to guess what they might want
                var lastContext = _context.LastActiveFeature;
                if (!string.IsNullOrEmpty(lastContext))
                    return $"I'm not sure what you mean. Are you asking about {lastContext}?";
                
                return "Could you tell me more about what you'd like to do?";
            }
            
            // Intent-specific clarifications
            return intent.Intent switch
            {
                "play_music" when !intent.Entities.ContainsKey("query") => 
                    "What would you like me to play?",
                "open_app" when !intent.Entities.ContainsKey("app") => 
                    "Which app should I open?",
                "file_operation" when !intent.Entities.ContainsKey("target") => 
                    "Which file or folder?",
                "organize_files" when !intent.Entities.ContainsKey("target") => 
                    "Which folder should I organize?",
                "web_search" when !intent.Entities.ContainsKey("query") => 
                    "What should I search for?",
                "weather" when !intent.Entities.ContainsKey("location") => 
                    "For which location?",
                _ => "Could you be more specific?"
            };
        }

        /// <summary>
        /// Assess the risk level of an action
        /// </summary>
        private RiskLevel AssessRisk(IntentResult intent)
        {
            // Critical: System-wide destructive actions
            if (intent.Intent == "power_control")
            {
                var action = intent.Entities.GetValueOrDefault("action", "");
                if (action == "shutdown" || action == "restart") return RiskLevel.Critical;
                if (action == "sleep" || action == "lock") return RiskLevel.Low;
            }
            
            // High: File deletion, registry changes
            if (intent.Intent == "file_operation")
            {
                var action = intent.Entities.GetValueOrDefault("action", "");
                if (action == "delete") return RiskLevel.High;
                if (action == "move" || action == "rename") return RiskLevel.Medium;
            }
            
            // Medium: App management, system settings
            if (intent.Intent == "close_app") return RiskLevel.Medium;
            if (intent.Intent == "system_control") return RiskLevel.Medium;
            if (intent.Intent == "install_software") return RiskLevel.Medium;
            
            // Low: Information retrieval, media control
            if (intent.Intent == "web_search") return RiskLevel.None;
            if (intent.Intent == "weather") return RiskLevel.None;
            if (intent.Intent == "play_music") return RiskLevel.None;
            if (intent.Intent == "media_control") return RiskLevel.None;
            if (intent.Intent == "volume_control") return RiskLevel.None;
            if (intent.Intent == "screenshot") return RiskLevel.Low;
            if (intent.Intent == "open_app") return RiskLevel.Low;
            
            return RiskLevel.Low;
        }

        /// <summary>
        /// Get list of missing required parameters
        /// </summary>
        private List<string> GetMissingParameters(IntentResult intent)
        {
            var missing = new List<string>();
            
            var requiredParams = intent.Intent switch
            {
                "play_music" => new[] { "query" },
                "open_app" => new[] { "app" },
                "close_app" => new[] { "app" },
                "file_operation" => new[] { "target", "action" },
                "web_search" => new[] { "query" },
                "power_control" => new[] { "action" },
                _ => Array.Empty<string>()
            };
            
            foreach (var param in requiredParams)
            {
                if (!intent.Entities.ContainsKey(param) || string.IsNullOrEmpty(intent.Entities[param]))
                    missing.Add(param);
            }
            
            return missing;
        }

        /// <summary>
        /// Map intent to the appropriate tool/executor
        /// </summary>
        private string MapIntentToTool(string intent)
        {
            return intent switch
            {
                "play_music" => "MediaPlayerTool",
                "play_video" => "MediaPlayerTool",
                "media_control" => "MediaPlayerTool",
                "volume_control" => "SystemTool.Volume",
                "open_app" => "SystemTool.OpenApp",
                "close_app" => "SystemTool.CloseApp",
                "power_control" => "SystemTool.Power",
                "file_operation" => "FileSystemTool",
                "organize_files" => "FileSystemTool.Organize",
                "find_files" => "FileSystemTool.Find",
                "open_folder" => "FileSystemTool.OpenFolder",
                "web_search" => "WebSearchTool",
                "weather" => "WebSearchTool.Weather",
                "system_info" => "SystemTool.Info",
                "security_scan" => "SecurityScanner",
                "screenshot" => "ScreenCaptureTool",
                "reminder" => "ReminderTool",
                "generate_image" => "ImageGeneratorTool",
                "code_help" => "CodeAssistant",
                "install_software" => "SoftwareInstaller",
                _ => "AIChat"
            };
        }

        /// <summary>
        /// Build parameters for tool execution
        /// </summary>
        private Dictionary<string, object> BuildToolParameters(IntentResult intent)
        {
            var parameters = new Dictionary<string, object>();
            
            foreach (var entity in intent.Entities)
            {
                parameters[entity.Key] = entity.Value;
            }
            
            // Add context-derived parameters
            if (!parameters.ContainsKey("target") && !string.IsNullOrEmpty(_context.LastReferencedFolder))
                parameters["contextFolder"] = _context.LastReferencedFolder;
            
            if (!parameters.ContainsKey("app") && !string.IsNullOrEmpty(_context.LastReferencedApp))
                parameters["contextApp"] = _context.LastReferencedApp;
            
            return parameters;
        }

        /// <summary>
        /// Generate fallback path when capability is missing
        /// </summary>
        private string GenerateFallbackPath(IntentResult intent)
        {
            return intent.Intent switch
            {
                "reminder" => "Use Windows Task Scheduler or set a phone reminder",
                "install_software" => "Download from official website and run installer",
                _ => "I can guide you through the manual steps"
            };
        }

        /// <summary>
        /// Generate manual steps when automation isn't available
        /// </summary>
        private List<string> GenerateManualSteps(IntentResult intent)
        {
            return intent.Intent switch
            {
                "reminder" => new List<string>
                {
                    "Open Windows Settings (Win + I)",
                    "Search for 'Task Scheduler'",
                    "Create a new basic task",
                    "Set your trigger time and action"
                },
                "install_software" => new List<string>
                {
                    $"Go to the official website for {intent.Entities.GetValueOrDefault("query", "the software")}",
                    "Download the installer",
                    "Run the installer and follow prompts",
                    "I can help if you run into issues"
                },
                _ => new List<string> { "Let me know what specific help you need" }
            };
        }
    }

    /// <summary>
    /// Registry of available capabilities/tools
    /// </summary>
    public class CapabilityRegistry
    {
        private readonly Dictionary<string, CapabilityModule> _capabilities = new();

        public CapabilityRegistry()
        {
            // Register all implemented capabilities
            Register("play_music", "Media", "Play music on various platforms", true);
            Register("play_video", "Media", "Play videos on YouTube", true);
            Register("media_control", "Media", "Pause, play, skip media", true);
            Register("volume_control", "System", "Control system volume", true);
            Register("open_app", "System", "Open applications", true);
            Register("close_app", "System", "Close applications", true);
            Register("power_control", "System", "Shutdown, restart, sleep, lock", true);
            Register("file_operation", "Files", "Create, delete, move, copy files", true);
            Register("organize_files", "Files", "Organize files by type", true);
            Register("find_files", "Files", "Search for files", true);
            Register("open_folder", "Files", "Open folder in Explorer", true);
            Register("web_search", "Information", "Search the web", true);
            Register("weather", "Information", "Get weather information", true);
            Register("system_info", "Information", "Get system information", true);
            Register("security_scan", "Security", "Scan for malware/threats", true);
            Register("screenshot", "Productivity", "Capture screen", true);
            Register("generate_image", "AI", "Generate images with AI", true);
            Register("code_help", "Development", "Help with code", true);
            Register("install_software", "System", "Install software", true);
            
            // Not yet implemented
            Register("reminder", "Productivity", "Set reminders", false, 
                "Build a reminder system using Windows Task Scheduler API");
            Register("email", "Communication", "Send emails", false,
                "Integrate with Outlook/Gmail API");
        }

        private void Register(string intent, string category, string description, bool implemented, string? buildPlan = null)
        {
            _capabilities[intent] = new CapabilityModule
            {
                Name = intent,
                Category = category,
                Description = description,
                IsImplemented = implemented,
                BuildPlan = buildPlan,
                SupportedIntents = new List<string> { intent }
            };
        }

        public CapabilityModule? GetCapability(string intent)
        {
            return _capabilities.TryGetValue(intent, out var cap) ? cap : null;
        }

        public IEnumerable<CapabilityModule> GetAllCapabilities()
        {
            return _capabilities.Values;
        }

        public IEnumerable<CapabilityModule> GetCapabilitiesByCategory(string category)
        {
            return _capabilities.Values.Where(c => c.Category == category);
        }
    }
}
