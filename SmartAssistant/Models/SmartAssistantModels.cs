using System;
using System.Collections.Generic;

namespace AtlasAI.SmartAssistant.Models
{
    // ============ INTENT LEARNING & HABIT MEMORY ============
    
    public class LearnedIntent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Pattern { get; set; } = "";
        public string ResolvedAction { get; set; } = "";
        public int UsageCount { get; set; }
        public DateTime LastUsed { get; set; }
        public double ConfidenceScore { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
    
    public class UserHabit
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public TimeSpan? TypicalTime { get; set; }
        public DayOfWeek[]? TypicalDays { get; set; }
        public List<string> ActionSequence { get; set; } = new();
        public int OccurrenceCount { get; set; }
        public DateTime FirstObserved { get; set; }
        public DateTime LastObserved { get; set; }
        public bool IsConfirmed { get; set; }
    }
    
    // ============ MULTI-STEP TASK EXECUTION ============
    
    public class TaskPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<TaskStep> Steps { get; set; } = new();
        public TaskPlanStatus Status { get; set; } = TaskPlanStatus.Pending;
        public int CurrentStepIndex { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    public class TaskStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Order { get; set; }
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
        public TaskStepStatus Status { get; set; } = TaskStepStatus.Pending;
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public bool RequiresConfirmation { get; set; }
        public TimeSpan? EstimatedDuration { get; set; }
    }
    
    public enum TaskPlanStatus { Pending, Running, Paused, Completed, Failed, Cancelled }
    public enum TaskStepStatus { Pending, Running, Completed, Failed, Skipped }
    
    // ============ TASK RECORDING & REPLAY ============
    
    public class RecordedTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<RecordedAction> Actions { get; set; } = new();
        public DateTime RecordedAt { get; set; } = DateTime.Now;
        public string? TriggerPhrase { get; set; }
        public int PlayCount { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
    
    public class RecordedAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Order { get; set; }
        public string ActionType { get; set; } = "";
        public string Target { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
        public TimeSpan DelayBefore { get; set; }
    }
    
    // ============ SCHEDULED BACKGROUND TASKS ============
    
    public class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ActionCommand { get; set; } = "";
        public ScheduleType ScheduleType { get; set; }
        public TimeSpan? TimeOfDay { get; set; }
        public DayOfWeek[]? DaysOfWeek { get; set; }
        public TimeSpan? Interval { get; set; }
        public DateTime? NextRunTime { get; set; }
        public DateTime? LastRunTime { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool NotifyOnComplete { get; set; } = true;
        public bool RequiresConfirmation { get; set; }
    }
    
    public enum ScheduleType { Once, Daily, Weekly, Interval, OnStartup, OnIdle }
    
    // ============ ASSISTANT PERSONAS ============
    
    public class AssistantPersona
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string SystemPromptAddition { get; set; } = "";
        public PersonaTone Tone { get; set; } = PersonaTone.Professional;
        public string? VoiceId { get; set; }
        public double SpeechRate { get; set; } = 1.0;
        public bool IsBuiltIn { get; set; }
        public Dictionary<string, string> CustomResponses { get; set; } = new();
    }
    
    public enum PersonaTone { Professional, Friendly, Casual, Formal, Playful, Serious }
    
    // ============ MOOD DETECTION ============
    
    public class MoodState
    {
        public UserMood DetectedMood { get; set; } = UserMood.Neutral;
        public double Confidence { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.Now;
        public string? TriggerText { get; set; }
    }
    
    public enum UserMood { Neutral, Happy, Frustrated, Rushed, Curious, Tired, Focused }
    
    // ============ PRIVACY DASHBOARD ============
    
    public class PrivacySettings
    {
        public bool EnableDataCollection { get; set; } = true;
        public bool EnableHabitLearning { get; set; } = true;
        public bool EnableVoiceRecording { get; set; } = true;
        public bool EnableScreenCapture { get; set; } = true;
        public bool EnableClipboardAccess { get; set; } = true;
        public bool EnableFileAccess { get; set; } = true;
        public bool EnableNetworkAccess { get; set; } = true;
        public List<string> BlockedApps { get; set; } = new();
        public List<string> BlockedFolders { get; set; } = new();
        public int DataRetentionDays { get; set; } = 30;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
    
    public class PrivacyAuditEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Category { get; set; } = "";
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
        public bool WasAllowed { get; set; }
    }
    
    // ============ PROJECT CONTEXT ============
    
    public class ProjectContext
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProjectPath { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public ProjectType Type { get; set; }
        public List<string> RecentFiles { get; set; } = new();
        public List<DetectedError> Errors { get; set; } = new();
        public DateTime LastScanned { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
    
    public class DetectedError
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; } = "";
        public ErrorSeverity Severity { get; set; }
        public string? SuggestedFix { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.Now;
    }
    
    public enum ProjectType { CSharp, Python, JavaScript, TypeScript, Unknown }
    public enum ErrorSeverity { Info, Warning, Error, Critical }
}
