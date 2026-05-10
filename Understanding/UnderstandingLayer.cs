using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Main entry point for the Understanding & Reasoning Layer.
    /// Coordinates IntentClassifier, ContextStore, Planner, ResponseFormatter, and ConfirmationGate.
    /// 
    /// Flow:
    /// 1. User input → IntentClassifier → {intent, entities, confidence}
    /// 2. Intent + Context → Planner → {action, tool, parameters, confirmation_needed}
    /// 3. Decision → ResponseFormatter → user-facing response
    /// 4. If destructive → ConfirmationGate → await confirmation
    /// 5. Execute → Update ContextStore → Audit log
    /// </summary>
    public class UnderstandingLayer
    {
        private readonly ContextStore _context;
        private readonly IntentClassifier _classifier;
        private readonly Planner _planner;
        private readonly ResponseFormatter _formatter;
        private readonly ConfirmationGate _confirmationGate;
        
        // Pending confirmation state
        private IntentResult? _pendingIntent;
        private PlannerDecision? _pendingDecision;

        public UnderstandingLayer()
        {
            _context = new ContextStore();
            _classifier = new IntentClassifier(_context);
            _planner = new Planner(_context);
            _formatter = new ResponseFormatter(_context);
            _confirmationGate = new ConfirmationGate();
        }

        /// <summary>
        /// Process user input through the understanding pipeline.
        /// Returns a structured result with intent, decision, and formatted response.
        /// </summary>
        public async Task<UnderstandingResult> ProcessAsync(string userInput)
        {
            Debug.WriteLine($"[UnderstandingLayer] Processing: '{userInput}'");
            
            var result = new UnderstandingResult
            {
                OriginalInput = userInput
            };
            
            try
            {
                // Check if this is a confirmation response
                if (_pendingIntent != null && _pendingDecision != null)
                {
                    return HandleConfirmationResponse(userInput);
                }
                
                // Step 1: Classify intent
                result.Intent = await _classifier.ClassifyAsync(userInput);
                Debug.WriteLine($"[UnderstandingLayer] Intent: {result.Intent.Intent} ({result.Intent.Confidence:P0})");
                
                // Step 2: Plan action
                result.Decision = _planner.Plan(result.Intent);
                Debug.WriteLine($"[UnderstandingLayer] Decision: {result.Decision.Action}");
                
                // Step 3: Check if confirmation needed
                if (result.Decision.RequiresConfirmation)
                {
                    _pendingIntent = result.Intent;
                    _pendingDecision = result.Decision;
                    result.AwaitingConfirmation = true;
                    result.ConfirmationPrompt = _confirmationGate.GenerateConfirmationPrompt(
                        result.Decision.ToolToExecute ?? result.Intent.Intent,
                        result.Decision.ToolParameters);
                    result.FormattedResponse = result.ConfirmationPrompt;
                    return result;
                }
                
                // Step 4: Format response
                result.FormattedResponse = _formatter.FormatFullResponse(result.Intent, result.Decision);
                
                // Step 5: Update context
                UpdateContext(userInput, result);
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UnderstandingLayer] Error: {ex.Message}");
                result.Error = ex.Message;
                result.FormattedResponse = "Sorry, I had trouble understanding that. Could you try rephrasing?";
                return result;
            }
        }

        /// <summary>
        /// Handle a confirmation response (yes/no)
        /// </summary>
        private UnderstandingResult HandleConfirmationResponse(string response)
        {
            var result = new UnderstandingResult
            {
                OriginalInput = response,
                Intent = _pendingIntent!,
                Decision = _pendingDecision!
            };
            
            var lower = response.ToLower().Trim();
            var confirmed = lower == "yes" || lower == "y" || lower == "confirm" || lower == "ok" || lower == "sure";
            var denied = lower == "no" || lower == "n" || lower == "cancel" || lower == "stop" || lower == "nevermind";
            
            if (confirmed)
            {
                result.ConfirmationReceived = true;
                result.UserConfirmed = true;
                result.FormattedResponse = "Got it, proceeding...";
                
                _confirmationGate.RecordConfirmation(
                    result.Decision.ToolToExecute ?? result.Intent.Intent,
                    result.Decision.ToolParameters,
                    true);
            }
            else if (denied)
            {
                result.ConfirmationReceived = true;
                result.UserConfirmed = false;
                result.FormattedResponse = "Okay, cancelled.";
                
                _confirmationGate.RecordConfirmation(
                    result.Decision.ToolToExecute ?? result.Intent.Intent,
                    result.Decision.ToolParameters,
                    false);
            }
            else
            {
                // Not a clear yes/no, ask again
                result.AwaitingConfirmation = true;
                result.ConfirmationPrompt = "Please confirm with 'yes' or 'no'.";
                result.FormattedResponse = result.ConfirmationPrompt;
                return result;
            }
            
            // Clear pending state
            _pendingIntent = null;
            _pendingDecision = null;
            
            return result;
        }

        /// <summary>
        /// Record the outcome of an executed action
        /// </summary>
        public void RecordOutcome(string action, bool success, string? error = null)
        {
            if (_pendingDecision != null)
            {
                _confirmationGate.RecordExecution(action, _pendingDecision.ToolParameters, success, error);
            }
            
            _context.UpdateLastOutcome(success ? "Success" : $"Failed: {error}");
        }

        /// <summary>
        /// Update context with the current interaction
        /// </summary>
        private void UpdateContext(string userInput, UnderstandingResult result)
        {
            var entry = new ContextEntry
            {
                UserInput = userInput,
                Intent = result.Intent,
                ActiveFeature = DetermineActiveFeature(result.Intent)
            };
            
            // Extract referenced entities
            if (result.Intent.Entities.TryGetValue("target", out var target))
            {
                if (target.Contains(".") || target.Contains("\\") || target.Contains("/"))
                    entry.ReferencedFiles.Add(target);
                else
                    entry.ReferencedFolders.Add(target);
            }
            
            if (result.Intent.Entities.TryGetValue("app", out var app))
                entry.ReferencedApps.Add(app);
            
            _context.AddEntry(entry);
        }

        /// <summary>
        /// Determine the active feature based on intent
        /// </summary>
        private string DetermineActiveFeature(IntentResult intent)
        {
            return intent.Intent switch
            {
                "play_music" or "play_video" or "media_control" or "volume_control" => "Media",
                "open_app" or "close_app" or "power_control" or "system_control" => "System",
                "file_operation" or "organize_files" or "find_files" or "open_folder" => "Files",
                "web_search" or "weather" or "system_info" => "Information",
                "security_scan" => "Security",
                "screenshot" or "reminder" => "Productivity",
                "generate_image" or "analyze_image" => "AI",
                "code_help" => "Development",
                _ => "Chat"
            };
        }

        /// <summary>
        /// Get the current context store
        /// </summary>
        public ContextStore Context => _context;

        /// <summary>
        /// Get the confirmation gate
        /// </summary>
        public ConfirmationGate ConfirmationGate => _confirmationGate;

        /// <summary>
        /// Get the response formatter
        /// </summary>
        public ResponseFormatter Formatter => _formatter;

        /// <summary>
        /// Check if awaiting confirmation
        /// </summary>
        public bool IsAwaitingConfirmation => _pendingIntent != null;

        /// <summary>
        /// Clear any pending state
        /// </summary>
        public void ClearPendingState()
        {
            _pendingIntent = null;
            _pendingDecision = null;
            _confirmationGate.ClearPending();
        }

        /// <summary>
        /// Get capabilities description
        /// </summary>
        public string GetCapabilitiesDescription()
        {
            return _formatter.FormatCapabilities();
        }
    }

    /// <summary>
    /// Result from the understanding pipeline
    /// </summary>
    public class UnderstandingResult
    {
        public string OriginalInput { get; set; } = "";
        public IntentResult Intent { get; set; } = new();
        public PlannerDecision Decision { get; set; } = new();
        public string FormattedResponse { get; set; } = "";
        public bool AwaitingConfirmation { get; set; }
        public string? ConfirmationPrompt { get; set; }
        public bool ConfirmationReceived { get; set; }
        public bool UserConfirmed { get; set; }
        public string? Error { get; set; }
        
        /// <summary>
        /// Should the tool be executed?
        /// </summary>
        public bool ShouldExecute => 
            Decision.Action == PlannerAction.ExecuteTool && 
            !AwaitingConfirmation &&
            (ConfirmationReceived ? UserConfirmed : true);
        
        /// <summary>
        /// Get the tool to execute
        /// </summary>
        public string? ToolToExecute => Decision.ToolToExecute;
        
        /// <summary>
        /// Get tool parameters
        /// </summary>
        public Dictionary<string, object> ToolParameters => Decision.ToolParameters;
    }
}
