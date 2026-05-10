using System;
using System.Collections.Generic;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Represents the current state of a multi-step task for persistence and resume capability
    /// </summary>
    public class TaskState
    {
        /// <summary>
        /// Unique identifier for this task
        /// </summary>
        public Guid TaskId { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Original user request that started this task
        /// </summary>
        public string OriginalRequest { get; set; } = "";
        
        /// <summary>
        /// Intent category from router
        /// </summary>
        public IntentCategory Category { get; set; }
        
        /// <summary>
        /// Current status of the task
        /// </summary>
        public TaskStatus Status { get; set; } = TaskStatus.Planning;
        
        /// <summary>
        /// List of planned steps (3-7 steps)
        /// </summary>
        public List<TaskStep> Steps { get; set; } = new();
        
        /// <summary>
        /// Index of the current step being executed (0-based)
        /// </summary>
        public int CurrentStepIndex { get; set; } = 0;
        
        /// <summary>
        /// When this task was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// When this task was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Error message if task failed
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Files modified so far in this task
        /// </summary>
        public List<string> ModifiedFiles { get; set; } = new();
        
        /// <summary>
        /// Get the current step being executed
        /// </summary>
        public TaskStep? CurrentStep => CurrentStepIndex < Steps.Count ? Steps[CurrentStepIndex] : null;
        
        /// <summary>
        /// Check if task is complete
        /// </summary>
        public bool IsComplete => Status == TaskStatus.Completed || Status == TaskStatus.Failed;
        
        /// <summary>
        /// Check if task can be resumed
        /// </summary>
        public bool CanResume => Status == TaskStatus.Paused || Status == TaskStatus.WaitingForInput;
        
        /// <summary>
        /// Get progress percentage (0-100)
        /// </summary>
        public int ProgressPercentage => Steps.Count > 0 ? (CurrentStepIndex * 100) / Steps.Count : 0;
    }
    
    /// <summary>
    /// Status of a multi-step task
    /// </summary>
    public enum TaskStatus
    {
        Planning,           // Generating plan
        InProgress,         // Executing steps
        Paused,            // Paused (can resume)
        WaitingForInput,   // Waiting for user input
        Completed,         // Successfully completed
        Failed             // Failed with error
    }
    
    /// <summary>
    /// Represents a single step in a multi-step task
    /// </summary>
    public class TaskStep
    {
        /// <summary>
        /// Step number (1-based for display)
        /// </summary>
        public int StepNumber { get; set; }
        
        /// <summary>
        /// Description of what this step does
        /// </summary>
        public string Description { get; set; } = "";
        
        /// <summary>
        /// Status of this step
        /// </summary>
        public StepStatus Status { get; set; } = StepStatus.Pending;
        
        /// <summary>
        /// Files to be modified in this step (max 3)
        /// </summary>
        public List<string> FilesToModify { get; set; } = new();
        
        /// <summary>
        /// Result/output from executing this step
        /// </summary>
        public string? Result { get; set; }
        
        /// <summary>
        /// Error message if step failed
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// When this step started executing
        /// </summary>
        public DateTime? StartedAt { get; set; }
        
        /// <summary>
        /// When this step completed
        /// </summary>
        public DateTime? CompletedAt { get; set; }
        
        /// <summary>
        /// Estimated tokens for this step (for budget control)
        /// </summary>
        public int EstimatedTokens { get; set; } = 1000;
    }
    
    /// <summary>
    /// Status of a single step
    /// </summary>
    public enum StepStatus
    {
        Pending,        // Not started yet
        InProgress,     // Currently executing
        Completed,      // Successfully completed
        Failed,         // Failed with error
        Skipped         // Skipped (dependency failed)
    }
}
