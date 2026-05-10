using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Formats responses to be concise, helpful, and human-like.
    /// Follows the pattern: "Got it — you want X. Best approach: Y. Next step: Z."
    /// </summary>
    public class ResponseFormatter
    {
        private readonly ContextStore _context;

        public ResponseFormatter(ContextStore context)
        {
            _context = context;
        }

        /// <summary>
        /// Format a response based on planner decision
        /// </summary>
        public FormattedResponse Format(IntentResult intent, PlannerDecision decision)
        {
            var response = new FormattedResponse();
            
            // Step 1: Restate the goal (shows we understand)
            response.GoalRestatement = FormatGoalRestatement(intent);
            
            // Step 2: State the approach
            response.Approach = FormatApproach(decision);
            
            // Step 3: State the next step
            response.NextStep = FormatNextStep(decision);
            
            // Step 4: Add options if applicable
            response.Options = FormatOptions(decision);
            
            // Step 5: Determine if we can execute
            response.CanExecute = decision.Action == PlannerAction.ExecuteTool;
            response.ExecutionPrompt = response.CanExecute ? "I'll do that now." : null;
            
            return response;
        }

        /// <summary>
        /// Format the goal restatement - shows we understand with JARVIS-like analysis
        /// </summary>
        private string FormatGoalRestatement(IntentResult intent)
        {
            if (string.IsNullOrEmpty(intent.InferredGoal))
                return "";
            
            // JARVIS-style acknowledgements - analytical and precise
            var phrases = new[]
            {
                $"Analyzing request — {intent.InferredGoal.ToLower()}.",
                $"Processing objective — {intent.InferredGoal.ToLower()}.",
                $"I've identified your goal — {intent.InferredGoal.ToLower()}.",
                $"Request parameters understood — {intent.InferredGoal.ToLower()}."
            };
            
            return phrases[Math.Abs(intent.InferredGoal.GetHashCode()) % phrases.Length];
        }

        /// <summary>
        /// Format the approach description with JARVIS-like technical precision
        /// </summary>
        private string FormatApproach(PlannerDecision decision)
        {
            return decision.Action switch
            {
                PlannerAction.ExecuteTool => $"I've identified the optimal approach.",
                PlannerAction.AskClarification => "I require additional parameters to proceed.",
                PlannerAction.Guide => "I'll provide the technical procedure:",
                PlannerAction.ConfirmDestructive => "This operation requires authorization.",
                PlannerAction.OfferToBuild => "This functionality isn't currently implemented, but I can provide alternatives.",
                _ => ""
            };
        }

        /// <summary>
        /// Format the next step with JARVIS-like precision
        /// </summary>
        private string FormatNextStep(PlannerDecision decision)
        {
            return decision.Action switch
            {
                PlannerAction.ExecuteTool => "Executing now...",
                PlannerAction.AskClarification => decision.ClarificationQuestion ?? "Please specify the parameters.",
                PlannerAction.Guide => FormatGuidanceSteps(decision.GuidanceSteps),
                PlannerAction.ConfirmDestructive => FormatConfirmationRequest(decision),
                PlannerAction.OfferToBuild => decision.FallbackPath ?? "I'll demonstrate the manual procedure.",
                _ => ""
            };
        }

        /// <summary>
        /// Format guidance steps as a numbered list
        /// </summary>
        private string FormatGuidanceSteps(List<string> steps)
        {
            if (steps == null || steps.Count == 0)
                return "";
            
            var sb = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {steps[i]}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Format confirmation request for destructive actions with JARVIS-like analysis
        /// </summary>
        private string FormatConfirmationRequest(PlannerDecision decision)
        {
            var action = decision.ToolParameters.GetValueOrDefault("action", "this action")?.ToString();
            var target = decision.ToolParameters.GetValueOrDefault("target", 
                decision.ToolParameters.GetValueOrDefault("app", ""))?.ToString();
            
            var riskWarning = decision.RiskLevel switch
            {
                RiskLevel.Critical => "⚠️ Critical system operation detected. ",
                RiskLevel.High => "⚠️ High-risk operation identified. ",
                RiskLevel.Medium => "",
                _ => ""
            };
            
            if (!string.IsNullOrEmpty(target))
                return $"{riskWarning}Confirm authorization to {action} {target}? (yes/no)";
            
            return $"{riskWarning}Confirm authorization to proceed with {action}? (yes/no)";
        }

        /// <summary>
        /// Format options if multiple approaches exist
        /// </summary>
        private List<string>? FormatOptions(PlannerDecision decision)
        {
            if (decision.Action != PlannerAction.Guide && decision.Action != PlannerAction.OfferToBuild)
                return null;
            
            // Only show options when there are alternatives
            return null;
        }

        /// <summary>
        /// Format a complete user-facing response string
        /// </summary>
        public string FormatFullResponse(IntentResult intent, PlannerDecision decision)
        {
            var formatted = Format(intent, decision);
            var parts = new List<string>();
            
            // Add goal restatement if meaningful
            if (!string.IsNullOrEmpty(formatted.GoalRestatement))
                parts.Add(formatted.GoalRestatement);
            
            // Add approach if not redundant
            if (!string.IsNullOrEmpty(formatted.Approach) && decision.Action != PlannerAction.ExecuteTool)
                parts.Add(formatted.Approach);
            
            // Add next step
            if (!string.IsNullOrEmpty(formatted.NextStep))
                parts.Add(formatted.NextStep);
            
            // Add execution prompt
            if (!string.IsNullOrEmpty(formatted.ExecutionPrompt))
                parts.Add(formatted.ExecutionPrompt);
            
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Format a success message after execution with JARVIS-like precision
        /// </summary>
        public string FormatSuccess(string action, string? target = null)
        {
            // JARVIS-style acknowledgements for completed actions
            var phrases = new[]
            {
                "Operation complete.",
                "Task executed successfully.",
                "System parameters updated.",
                "Process completed.",
                "Execution successful.",
                "Operation nominal."
            };
            
            var phrase = phrases[Math.Abs((action + target).GetHashCode()) % phrases.Length];
            
            if (!string.IsNullOrEmpty(target))
                return $"{phrase} {action} {target}.";
            
            return phrase;
        }

        /// <summary>
        /// Format an error message with JARVIS-like technical analysis
        /// </summary>
        public string FormatError(string action, string error)
        {
            // JARVIS-style error handling - technical and analytical
            return $"Operation encountered complications. {SimplifyError(error)}";
        }

        /// <summary>
        /// Simplify technical error messages for users with JARVIS-like analysis
        /// </summary>
        private string SimplifyError(string error)
        {
            // JARVIS-style error explanations - technical but accessible
            if (error.Contains("not found") || error.Contains("NotFound"))
                return "Target resource not located in system registry.";
            if (error.Contains("access denied") || error.Contains("AccessDenied") || error.Contains("permission"))
                return "Security protocols are preventing access. Attempting alternative route.";
            if (error.Contains("timeout") || error.Contains("Timeout"))
                return "Operation exceeded time parameters. Shall I retry with extended timeout?";
            if (error.Contains("network") || error.Contains("connection"))
                return "Network connectivity issues detected.";
            
            // Truncate long errors
            if (error.Length > 50)
                return error.Substring(0, 47) + "...";
            
            return error;
        }

        /// <summary>
        /// Format a "what can you do" response with JARVIS-like technical capabilities
        /// </summary>
        public string FormatCapabilities()
        {
            return @"Certainly. My operational parameters include:

🎵 Media Control: Audio playback management, volume regulation, system audio coordination
📁 File System: Advanced file operations, directory management, intelligent organization
🖥️ System Administration: Application lifecycle management, power control, process monitoring
🔍 Information Retrieval: Web queries, weather analysis, system diagnostics
🛡️ Security Analysis: Comprehensive threat detection, malware scanning, system hardening
📸 Visual Capture: Screen recording, image analysis, capture history management
🎨 AI Integration: Image generation, visual analysis, content processing
💻 Development Support: Code assistance, debugging, technical documentation

I maintain comprehensive system access and can execute operations with technical precision.";
        }
    }
}
