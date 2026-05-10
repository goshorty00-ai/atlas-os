using System;
using System.Collections.Generic;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Core models for the Understanding & Reasoning Layer
    /// </summary>
    
    /// <summary>
    /// Structured intent object produced from user input
    /// </summary>
    public class IntentResult
    {
        public string Intent { get; set; } = "unknown";
        public Dictionary<string, string> Entities { get; set; } = new();
        public float Confidence { get; set; } = 0f;
        public string PlannedAction { get; set; } = "guide";
        public bool NeedsConfirmation { get; set; } = false;
        public string? MissingCapability { get; set; }
        public string? ClarificationQuestion { get; set; }
        public List<string> Constraints { get; set; } = new();
        public string? InferredGoal { get; set; }
    }

    /// <summary>
    /// Context entry for tracking conversation state
    /// </summary>
    public class ContextEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string UserInput { get; set; } = "";
        public string? AssistantResponse { get; set; }
        public IntentResult? Intent { get; set; }
        public string? ActiveFeature { get; set; }
        public List<string> ReferencedFiles { get; set; } = new();
        public List<string> ReferencedFolders { get; set; } = new();
        public List<string> ReferencedApps { get; set; } = new();
        public string? LastScanResult { get; set; }
        public string? LastActionOutcome { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Planner decision output
    /// </summary>
    public class PlannerDecision
    {
        public PlannerAction Action { get; set; } = PlannerAction.Guide;
        public string? ToolToExecute { get; set; }
        public Dictionary<string, object> ToolParameters { get; set; } = new();
        public string? ClarificationQuestion { get; set; }
        public List<string> GuidanceSteps { get; set; } = new();
        public string Reasoning { get; set; } = "";
        public bool RequiresConfirmation { get; set; } = false;
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
        public string? FallbackPath { get; set; }
    }

    public enum PlannerAction
    {
        ExecuteTool,
        AskClarification,
        Guide,
        ConfirmDestructive,
        OfferToBuild
    }

    public enum RiskLevel
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Formatted response for user
    /// </summary>
    public class FormattedResponse
    {
        public string GoalRestatement { get; set; } = "";
        public string Approach { get; set; } = "";
        public string NextStep { get; set; } = "";
        public List<string>? Options { get; set; }
        public bool CanExecute { get; set; } = false;
        public string? ExecutionPrompt { get; set; }
    }

    /// <summary>
    /// Audit log entry for executed actions
    /// </summary>
    public class AuditLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; } = "";
        public string? Target { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool UserConfirmed { get; set; }
        public string? RollbackInfo { get; set; }
    }

    /// <summary>
    /// Capability module definition
    /// </summary>
    public class CapabilityModule
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> SupportedIntents { get; set; } = new();
        public bool IsImplemented { get; set; } = true;
        public string? BuildPlan { get; set; }
    }
}
