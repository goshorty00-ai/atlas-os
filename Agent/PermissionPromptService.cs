using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using AtlasAI.Core;

namespace AtlasAI.Agent
{
    /// <summary>
    /// User's response to a permission prompt.
    /// </summary>
    public enum PermissionResponse
    {
        /// <summary>User granted permission.</summary>
        Allowed,
        /// <summary>User granted temporary permission.</summary>
        AllowedTemporary,
        /// <summary>User denied permission.</summary>
        Denied,
        /// <summary>User dismissed without choosing.</summary>
        Dismissed
    }

    /// <summary>
    /// Result of a permission prompt.
    /// </summary>
    public class PermissionPromptResult
    {
        public PermissionResponse Response { get; set; }
        public int? DurationMinutes { get; set; }
        public string? UserMessage { get; set; }
    }

    /// <summary>
    /// Service for showing standardized permission prompts.
    /// Three types: Online, HighRisk, Microphone.
    /// </summary>
    public class PermissionPromptService
    {
        private static PermissionPromptService? _instance;
        private static readonly object _lock = new();

        public static PermissionPromptService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new PermissionPromptService();
                    }
                }
                return _instance;
            }
        }

        // Events for UI to subscribe to
        public event Func<OnlinePermissionRequest, Task<PermissionPromptResult>>? OnlinePermissionRequested;
        public event Func<HighRiskPermissionRequest, Task<PermissionPromptResult>>? HighRiskPermissionRequested;
        public event Func<MicrophonePermissionRequest, Task<PermissionPromptResult>>? MicrophonePermissionRequested;

        private PermissionPromptService()
        {
            Debug.WriteLine("[PermissionPromptService] Initialized");
        }

        #region Online Permission

        /// <summary>
        /// Request online access permission.
        /// Buttons: Allow once / Allow 10 minutes / Not now
        /// </summary>
        public async Task<PermissionPromptResult> RequestOnlinePermissionAsync(string query, string prompt)
        {
            Debug.WriteLine($"[PermissionPrompt] Requesting online permission for: {query}");

            var request = new OnlinePermissionRequest
            {
                Query = query,
                Prompt = prompt,
                Title = "Online Access",
                Description = "I can look that up online if you'd like. It's read-only and won't change anything on your system."
            };

            if (OnlinePermissionRequested != null)
            {
                var result = await OnlinePermissionRequested.Invoke(request);
                IntentLogger.LogPermissionResponse("OnlineAccess", result.Response.ToString(), query);
                return result;
            }

            // Fallback: use OnlineModeManager's consent flow
            var accessResult = await OnlineModeManager.Instance.RequestAccessAsync(query);
            var response = accessResult switch
            {
                OnlineAccessResult.Allowed => PermissionResponse.Allowed,
                OnlineAccessResult.Denied => PermissionResponse.Denied,
                _ => PermissionResponse.Dismissed
            };

            IntentLogger.LogPermissionResponse("OnlineAccess", response.ToString(), query);
            return new PermissionPromptResult { Response = response };
        }

        #endregion

        #region High-Risk Permission

        /// <summary>
        /// Request permission for high-risk local action.
        /// Buttons: Enable for this action / Not now
        /// Shows what will happen in plain English.
        /// </summary>
        public async Task<PermissionPromptResult> RequestHighRiskPermissionAsync(
            string actionDescription, 
            string plainEnglishExplanation,
            string? safeAlternative = null)
        {
            Debug.WriteLine($"[PermissionPrompt] Requesting high-risk permission: {actionDescription}");

            var request = new HighRiskPermissionRequest
            {
                ActionDescription = actionDescription,
                PlainEnglishExplanation = plainEnglishExplanation,
                SafeAlternative = safeAlternative,
                Title = "Confirm Action",
                Warning = "This action cannot be undone."
            };

            if (HighRiskPermissionRequested != null)
            {
                var result = await HighRiskPermissionRequested.Invoke(request);
                IntentLogger.LogPermissionResponse("HighRiskAction", result.Response.ToString(), actionDescription);
                return result;
            }

            // Fallback: deny by default (safe)
            Debug.WriteLine("[PermissionPrompt] No handler for high-risk permission, denying by default");
            IntentLogger.LogPermissionResponse("HighRiskAction", "Denied", "No handler registered");
            return new PermissionPromptResult { Response = PermissionResponse.Denied };
        }

        #endregion

        #region Microphone Permission

        /// <summary>
        /// Request microphone permission.
        /// Buttons: Enable mic / Not now
        /// </summary>
        public async Task<PermissionPromptResult> RequestMicrophonePermissionAsync()
        {
            Debug.WriteLine("[PermissionPrompt] Requesting microphone permission");

            var request = new MicrophonePermissionRequest
            {
                Title = "Microphone Access",
                Description = "Voice input requires microphone access.",
                Alternative = "You can type your request or use push-to-talk instead."
            };

            if (MicrophonePermissionRequested != null)
            {
                var result = await MicrophonePermissionRequested.Invoke(request);
                IntentLogger.LogPermissionResponse("Microphone", result.Response.ToString());
                return result;
            }

            // Fallback: suggest typing
            Debug.WriteLine("[PermissionPrompt] No handler for mic permission");
            IntentLogger.LogPermissionResponse("Microphone", "Dismissed", "No handler registered");
            return new PermissionPromptResult 
            { 
                Response = PermissionResponse.Dismissed,
                UserMessage = "You can type your request instead."
            };
        }

        #endregion

        #region Voice-Friendly Prompts

        /// <summary>
        /// Get voice-friendly prompt for online permission.
        /// </summary>
        public static string GetVoiceOnlinePrompt()
        {
            return "I can look that up online if you'd like. Should I proceed?";
        }

        /// <summary>
        /// Get voice-friendly response for online permission granted.
        /// </summary>
        public static string GetVoiceOnlineGranted(int? minutes = null)
        {
            if (minutes.HasValue)
                return $"Online access enabled for {minutes} minutes. Let me check that for you.";
            return "Understood. Let me look that up.";
        }

        /// <summary>
        /// Get voice-friendly response for online permission denied.
        /// </summary>
        public static string GetVoiceOnlineDenied()
        {
            return "No problem. I'll stay offline.";
        }

        /// <summary>
        /// Get voice-friendly prompt for high-risk action.
        /// </summary>
        public static string GetVoiceHighRiskPrompt(string action)
        {
            return $"This will {action}. Should I proceed?";
        }

        /// <summary>
        /// Get voice-friendly response for high-risk denied.
        /// </summary>
        public static string GetVoiceHighRiskDenied(string? alternative = null)
        {
            if (!string.IsNullOrEmpty(alternative))
                return $"Understood. {alternative}";
            return "Understood. I won't make any changes.";
        }

        #endregion
    }

    #region Request Models

    public class OnlinePermissionRequest
    {
        public string Query { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Title { get; set; } = "Online Access";
        public string Description { get; set; } = "";
    }

    public class HighRiskPermissionRequest
    {
        public string ActionDescription { get; set; } = "";
        public string PlainEnglishExplanation { get; set; } = "";
        public string? SafeAlternative { get; set; }
        public string Title { get; set; } = "Confirm Action";
        public string Warning { get; set; } = "";
    }

    public class MicrophonePermissionRequest
    {
        public string Title { get; set; } = "Microphone Access";
        public string Description { get; set; } = "";
        public string Alternative { get; set; } = "";
    }

    #endregion
}
