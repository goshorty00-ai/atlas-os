using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.Workflows
{
    /// <summary>
    /// Type of step in a workflow chain
    /// </summary>
    public enum WorkflowStepType
    {
        Macro,      // Execute a read-only macro
        Action,     // Execute a low-risk action
        Insight     // Display an insight/recommendation (no execution)
    }

    /// <summary>
    /// Execution status of a workflow step
    /// </summary>
    public enum WorkflowStepStatus
    {
        Pending,    // Not yet executed
        Running,    // Currently executing
        Complete,   // Successfully completed
        Failed,     // Execution failed
        Skipped     // User skipped this step
    }

    /// <summary>
    /// A single step in a workflow chain
    /// </summary>
    public class WorkflowStep
    {
        /// <summary>Step number (1-based)</summary>
        public int StepNumber { get; set; }

        /// <summary>Type of step (Macro, Action, or Insight)</summary>
        public WorkflowStepType Type { get; set; }

        /// <summary>Target ID (MacroId or ActionId, null for Insight)</summary>
        public string? TargetId { get; set; }

        /// <summary>Human-readable description of this step</summary>
        public string Description { get; set; } = "";

        /// <summary>Icon for display</summary>
        public string Icon { get; set; } = "▸";

        /// <summary>Estimated duration for display (e.g., "~5s")</summary>
        public string EstimatedDuration { get; set; } = "~2s";

        /// <summary>Current execution status</summary>
        public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;

        /// <summary>Result summary after execution</summary>
        public string? ResultSummary { get; set; }

        /// <summary>Detailed result data (for macros)</summary>
        public object? ResultData { get; set; }

        /// <summary>Error message if failed</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Actual execution time</summary>
        public TimeSpan? ExecutionTime { get; set; }

        /// <summary>Insight text (for Insight type steps)</summary>
        public string? InsightText { get; set; }

        /// <summary>Insight template with placeholders (e.g., "{cpu_status}")</summary>
        public string? InsightTemplate { get; set; }

        /// <summary>
        /// Get display icon based on type
        /// </summary>
        public string TypeIcon => Type switch
        {
            WorkflowStepType.Macro => "📊",
            WorkflowStepType.Action => "⚡",
            WorkflowStepType.Insight => "💡",
            _ => "▸"
        };

        /// <summary>
        /// Get status icon
        /// </summary>
        public string StatusIcon => Status switch
        {
            WorkflowStepStatus.Pending => "○",
            WorkflowStepStatus.Running => "◐",
            WorkflowStepStatus.Complete => "●",
            WorkflowStepStatus.Failed => "✕",
            WorkflowStepStatus.Skipped => "◌",
            _ => "○"
        };

        /// <summary>
        /// Get status color
        /// </summary>
        public string StatusColor => Status switch
        {
            WorkflowStepStatus.Pending => "#64748b",
            WorkflowStepStatus.Running => "#f59e0b",
            WorkflowStepStatus.Complete => "#22c55e",
            WorkflowStepStatus.Failed => "#ef4444",
            WorkflowStepStatus.Skipped => "#94a3b8",
            _ => "#64748b"
        };
    }

    /// <summary>
    /// Definition of a workflow chain
    /// </summary>
    public class WorkflowChainDefinition
    {
        /// <summary>Unique identifier</summary>
        public string Id { get; set; } = "";

        /// <summary>Display title</summary>
        public string Title { get; set; } = "";

        /// <summary>Description of what this workflow does</summary>
        public string Description { get; set; } = "";

        /// <summary>Icon for display</summary>
        public string Icon { get; set; } = "🔗";

        /// <summary>Keywords for matching user input</summary>
        public string[] TriggerKeywords { get; set; } = Array.Empty<string>();

        /// <summary>Steps in this workflow</summary>
        public List<WorkflowStep> Steps { get; set; } = new();

        /// <summary>Category for organization</summary>
        public string Category { get; set; } = "General";

        /// <summary>Estimated total duration</summary>
        public string EstimatedTotalDuration { get; set; } = "~30s";

        /// <summary>
        /// Check if this workflow matches the given input
        /// </summary>
        public bool Matches(string input)
        {
            var lower = input.ToLowerInvariant();
            foreach (var keyword in TriggerKeywords)
            {
                if (lower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Calculate match score (higher = better match)
        /// </summary>
        public int GetMatchScore(string input)
        {
            var lower = input.ToLowerInvariant();
            int score = 0;
            foreach (var keyword in TriggerKeywords)
            {
                if (lower.Contains(keyword.ToLowerInvariant()))
                    score += keyword.Length;
            }
            return score;
        }

        /// <summary>
        /// Create a fresh instance for execution (clones steps)
        /// </summary>
        public WorkflowChainInstance CreateInstance()
        {
            return new WorkflowChainInstance
            {
                DefinitionId = Id,
                Title = Title,
                Description = Description,
                Icon = Icon,
                Steps = Steps.ConvertAll(s => new WorkflowStep
                {
                    StepNumber = s.StepNumber,
                    Type = s.Type,
                    TargetId = s.TargetId,
                    Description = s.Description,
                    Icon = s.Icon,
                    EstimatedDuration = s.EstimatedDuration,
                    Status = WorkflowStepStatus.Pending,
                    InsightTemplate = s.InsightTemplate
                }),
                StartedAt = DateTime.Now
            };
        }
    }

    /// <summary>
    /// A running instance of a workflow chain
    /// </summary>
    public class WorkflowChainInstance
    {
        /// <summary>Unique instance ID</summary>
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>Definition ID this instance is based on</summary>
        public string DefinitionId { get; set; } = "";

        /// <summary>Display title</summary>
        public string Title { get; set; } = "";

        /// <summary>Description</summary>
        public string Description { get; set; } = "";

        /// <summary>Icon</summary>
        public string Icon { get; set; } = "🔗";

        /// <summary>Steps with current status</summary>
        public List<WorkflowStep> Steps { get; set; } = new();

        /// <summary>When this instance was started</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>When this instance completed (if finished)</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Current step index (0-based)</summary>
        public int CurrentStepIndex { get; set; } = 0;

        /// <summary>Overall status</summary>
        public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Ready;

        /// <summary>Final insight/summary after completion</summary>
        public string? FinalInsight { get; set; }

        /// <summary>
        /// Get the current step (or null if complete)
        /// </summary>
        public WorkflowStep? CurrentStep =>
            CurrentStepIndex < Steps.Count ? Steps[CurrentStepIndex] : null;

        /// <summary>
        /// Get the next step (or null if none)
        /// </summary>
        public WorkflowStep? NextStep =>
            CurrentStepIndex + 1 < Steps.Count ? Steps[CurrentStepIndex + 1] : null;

        /// <summary>
        /// Check if workflow is complete
        /// </summary>
        public bool IsComplete => CurrentStepIndex >= Steps.Count || 
                                  Status == WorkflowInstanceStatus.Completed ||
                                  Status == WorkflowInstanceStatus.Cancelled;

        /// <summary>
        /// Progress percentage (0-100)
        /// </summary>
        public int ProgressPercent => Steps.Count > 0 
            ? (int)((double)Steps.Count(s => s.Status == WorkflowStepStatus.Complete) / Steps.Count * 100)
            : 0;
    }

    /// <summary>
    /// Status of a workflow instance
    /// </summary>
    public enum WorkflowInstanceStatus
    {
        Ready,      // Created but not started
        Running,    // Currently executing a step
        Paused,     // Waiting for user to continue
        Completed,  // All steps finished
        Cancelled   // User cancelled
    }
}
