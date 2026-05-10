using System;
using System.Collections.Generic;

namespace AtlasAI.Autopilot.Models
{
    // ============ AUTONOMY LEVELS ============
    
    public enum AutonomyLevel
    {
        /// <summary>Only observe and log - never take action</summary>
        Observe = 0,
        
        /// <summary>Ask before every action - wait for approval</summary>
        Ask = 1,
        
        /// <summary>Auto-execute within approved rules - log everything</summary>
        AutoExecute = 2
    }
    
    // ============ AUTOPILOT CONFIGURATION ============
    
    public class AutopilotConfig
    {
        public bool IsEnabled { get; set; } = false; // OFF by default
        public AutonomyLevel DefaultAutonomyLevel { get; set; } = AutonomyLevel.Ask;
        public bool RequirePasswordForAutoExecute { get; set; } = true;
        public int MaxActionsPerSession { get; set; } = 50;
        public int MaxActionsPerMinute { get; set; } = 5;
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromHours(4);
        public bool NotifyOnEveryAction { get; set; } = true;
        public bool PauseOnError { get; set; } = true;
        public List<string> BlockedActions { get; set; } = new() 
        { 
            "delete_system_files", "format_drive", "modify_registry", 
            "send_email", "make_purchase", "change_password" 
        };
        public DateTime LastModified { get; set; } = DateTime.Now;
    }
    
    // ============ USER-DEFINED RULES (Plain English) ============
    
    public class AutopilotRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string PlainEnglishRule { get; set; } = ""; // e.g., "When I'm away, clean up my downloads folder"
        public string ParsedCondition { get; set; } = ""; // Parsed trigger condition
        public string ParsedAction { get; set; } = ""; // Parsed action to take
        public AutonomyLevel AutonomyLevel { get; set; } = AutonomyLevel.Ask;
        public bool IsEnabled { get; set; } = true;
        public bool RequiresConfirmation { get; set; } = true;
        public RuleTrigger Trigger { get; set; } = new();
        public List<string> AllowedActions { get; set; } = new();
        public int ExecutionCount { get; set; }
        public DateTime? LastExecuted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
    
    public class RuleTrigger
    {
        public TriggerType Type { get; set; } = TriggerType.Manual;
        public TimeSpan? TimeOfDay { get; set; }
        public DayOfWeek[]? DaysOfWeek { get; set; }
        public int? IdleMinutes { get; set; }
        public string? AppContext { get; set; }
        public string? Condition { get; set; }
    }
    
    public enum TriggerType
    {
        Manual,
        Scheduled,
        OnIdle,
        OnAwayMode,
        OnAppOpen,
        OnCondition,
        OnSystemEvent
    }
    
    // ============ AWAY MODE ============
    
    public class AwaySession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : DateTime.Now - StartTime;
        public List<AutopilotAction> ActionsPerformed { get; set; } = new();
        public List<AutopilotSuggestion> SuggestionsGenerated { get; set; } = new();
        public List<SystemObservation> Observations { get; set; } = new();
        public AwaySessionSummary? Summary { get; set; }
        public bool WasInterrupted { get; set; }
    }
    
    public class AwaySessionSummary
    {
        public int TotalActions { get; set; }
        public int SuccessfulActions { get; set; }
        public int FailedActions { get; set; }
        public int PendingApprovals { get; set; }
        public List<string> Highlights { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public string NarrativeSummary { get; set; } = "";
    }
    
    // ============ AUTOPILOT ACTIONS ============
    
    public class AutopilotAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string RuleId { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Reasoning { get; set; } = ""; // "Why did you do this?"
        public Dictionary<string, object> Parameters { get; set; } = new();
        public ActionStatus Status { get; set; } = ActionStatus.Pending;
        public AutonomyLevel RequiredLevel { get; set; }
        public bool WasAutoExecuted { get; set; }
        public bool WasApproved { get; set; }
        public string? ApprovalNote { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ExecutedAt { get; set; }
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public ActionContext Context { get; set; } = new();
    }
    
    public class ActionContext
    {
        public string? ActiveApp { get; set; }
        public string? ActiveWindow { get; set; }
        public TimeSpan TimeOfDay { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public int IdleMinutes { get; set; }
        public bool IsAwayMode { get; set; }
        public Dictionary<string, string> AdditionalContext { get; set; } = new();
    }
    
    public enum ActionStatus
    {
        Pending,
        AwaitingApproval,
        Approved,
        Rejected,
        Executing,
        Completed,
        Failed,
        Cancelled
    }
    
    // ============ SUGGESTIONS ============
    
    public class AutopilotSuggestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public SuggestionType Type { get; set; }
        public SuggestionPriority Priority { get; set; }
        public string? ProposedAction { get; set; }
        public bool WasAccepted { get; set; }
        public bool WasDismissed { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
    
    public enum SuggestionType
    {
        Optimization,
        Security,
        Maintenance,
        Reminder,
        Habit,
        Proactive
    }
    
    public enum SuggestionPriority
    {
        Low,
        Medium,
        High,
        Urgent
    }
    
    // ============ SYSTEM MONITORING ============
    
    public class SystemObservation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ObservationType Type { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<string, object> Data { get; set; } = new();
        public bool RequiresAttention { get; set; }
    }
    
    public enum ObservationType
    {
        SystemHealth,
        DiskSpace,
        MemoryUsage,
        NetworkActivity,
        SecurityEvent,
        AppCrash,
        UpdateAvailable,
        UnusualActivity
    }
    
    // ============ AUDIT LOG ============
    
    public class AuditLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string ActionId { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Reasoning { get; set; } = ""; // Explainable AI
        public string RuleId { get; set; } = "";
        public string RuleName { get; set; } = "";
        public AutonomyLevel AutonomyLevel { get; set; }
        public bool WasAutoExecuted { get; set; }
        public bool WasSuccessful { get; set; }
        public string? ErrorDetails { get; set; }
        public ActionContext Context { get; set; } = new();
        public string? UserFeedback { get; set; }
    }
    
    // ============ TASK CHAINS / WORKFLOWS ============
    
    public class AutopilotWorkflow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<WorkflowStep> Steps { get; set; } = new();
        public RuleTrigger Trigger { get; set; } = new();
        public AutonomyLevel AutonomyLevel { get; set; } = AutonomyLevel.Ask;
        public bool IsEnabled { get; set; } = true;
        public bool StopOnError { get; set; } = true;
        public int ExecutionCount { get; set; }
        public DateTime? LastExecuted { get; set; }
    }
    
    public class WorkflowStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Order { get; set; }
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
        public string? Condition { get; set; }
        public TimeSpan? DelayBefore { get; set; }
        public bool RequiresConfirmation { get; set; }
    }
}
