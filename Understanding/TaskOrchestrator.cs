using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Understanding
{
    /// <summary>
    /// Task Orchestrator - Plans and executes multi-step tasks to avoid timeouts.
    /// Breaks complex tasks into manageable steps with progress tracking and resume capability.
    /// </summary>
    public class TaskOrchestrator
    {
        private static readonly string TaskStateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtlasAI", "task_state");
        
        private const int MaxFilesPerStep = 3;
        private const int MaxTokensPerStep = 2000;
        private const int MaxSteps = 7;
        private const int MinSteps = 3;
        
        /// <summary>
        /// Current active task (if any)
        /// </summary>
        public TaskState? CurrentTask { get; private set; }
        
        public TaskOrchestrator()
        {
            // Ensure task state directory exists
            Directory.CreateDirectory(TaskStateDir);
            
            // Try to load last task if it was incomplete
            LoadLastTask();
        }
        
        /// <summary>
        /// Determine if a request needs orchestration (multi-step planning)
        /// </summary>
        public static bool NeedsOrchestration(IntentRouting routing)
        {
            // Only orchestrate complex tasks
            if (routing.Category == IntentCategory.GeneralChat || 
                routing.Category == IntentCategory.CreativeWriting)
            {
                return false;
            }
            
            // Check if multi-step flag is set
            if (routing.Tools.HasFlag(RequiredTools.MultiStep))
            {
                return true;
            }
            
            // DocumentTask and FileTask usually don't need orchestration unless complex
            if (routing.Category == IntentCategory.DocumentTask || 
                routing.Category == IntentCategory.FileTask)
            {
                return false;
            }
            
            // CodingTask and Troubleshooting benefit from orchestration
            return routing.Category == IntentCategory.CodingTask || 
                   routing.Category == IntentCategory.Troubleshooting;
        }
        
        /// <summary>
        /// Create a new task with a plan
        /// </summary>
        public async Task<TaskState> CreateTaskAsync(string userRequest, IntentRouting routing, CancellationToken ct = default)
        {
            Debug.WriteLine($"[Orchestrator] Creating task for: {userRequest}");
            
            var task = new TaskState
            {
                OriginalRequest = userRequest,
                Category = routing.Category,
                Status = TaskStatus.Planning
            };
            
            // Generate plan based on category
            task.Steps = await GeneratePlanAsync(userRequest, routing, ct);
            
            if (task.Steps.Count == 0)
            {
                Debug.WriteLine("[Orchestrator] Failed to generate plan - falling back to direct execution");
                return task;
            }
            
            task.Status = TaskStatus.InProgress;
            CurrentTask = task;
            
            // Persist task state
            await SaveTaskAsync(task);
            
            Debug.WriteLine($"[Orchestrator] Task created with {task.Steps.Count} steps");
            return task;
        }
        
        /// <summary>
        /// Generate a plan (3-7 steps) for the task
        /// </summary>
        private async Task<List<TaskStep>> GeneratePlanAsync(string userRequest, IntentRouting routing, CancellationToken ct)
        {
            var steps = new List<TaskStep>();
            var lowerRequest = userRequest.ToLower();
            
            // Generate plan based on category and request content
            switch (routing.Category)
            {
                case IntentCategory.CodingTask:
                    steps = GenerateCodingPlan(lowerRequest);
                    break;
                    
                case IntentCategory.DocumentTask:
                    steps = GenerateDocumentPlan(lowerRequest);
                    break;
                    
                case IntentCategory.FileTask:
                    steps = GenerateFilePlan(lowerRequest);
                    break;
                    
                case IntentCategory.Troubleshooting:
                    steps = GenerateTroubleshootingPlan(lowerRequest);
                    break;
            }
            
            // Ensure step numbers are set
            for (int i = 0; i < steps.Count; i++)
            {
                steps[i].StepNumber = i + 1;
            }
            
            return await Task.FromResult(steps);
        }
        
        /// <summary>
        /// Generate plan for coding tasks
        /// </summary>
        private List<TaskStep> GenerateCodingPlan(string request)
        {
            var steps = new List<TaskStep>();
            
            // Common coding task patterns
            if (request.Contains("add") && request.Contains("page"))
            {
                // Adding a new page/component
                steps.Add(new TaskStep { Description = "Analyze existing project structure and identify where to add the page" });
                steps.Add(new TaskStep { Description = "Create the XAML file for the new page" });
                steps.Add(new TaskStep { Description = "Create the code-behind file (.cs)" });
                steps.Add(new TaskStep { Description = "Register the page in navigation/routing" });
                steps.Add(new TaskStep { Description = "Build and verify no compile errors" });
            }
            else if (request.Contains("refactor"))
            {
                // Refactoring task
                steps.Add(new TaskStep { Description = "Analyze current code structure and identify refactoring targets" });
                steps.Add(new TaskStep { Description = "Extract common functionality into helper methods" });
                steps.Add(new TaskStep { Description = "Update references and imports" });
                steps.Add(new TaskStep { Description = "Build and verify functionality unchanged" });
            }
            else if (request.Contains("implement") || request.Contains("create"))
            {
                // General implementation
                steps.Add(new TaskStep { Description = "Design the implementation approach" });
                steps.Add(new TaskStep { Description = "Create necessary classes/interfaces" });
                steps.Add(new TaskStep { Description = "Implement core functionality" });
                steps.Add(new TaskStep { Description = "Integrate with existing code" });
                steps.Add(new TaskStep { Description = "Build and verify" });
            }
            else
            {
                // Generic coding task
                steps.Add(new TaskStep { Description = "Analyze the request and locate relevant files" });
                steps.Add(new TaskStep { Description = "Make necessary code changes" });
                steps.Add(new TaskStep { Description = "Build and verify no errors" });
            }
            
            return steps;
        }
        
        /// <summary>
        /// Generate plan for document tasks
        /// </summary>
        private List<TaskStep> GenerateDocumentPlan(string request)
        {
            var steps = new List<TaskStep>
            {
                new TaskStep { Description = "Gather information and structure the document" },
                new TaskStep { Description = "Create the Word document with content" },
                new TaskStep { Description = "Save document and confirm location" }
            };
            
            return steps;
        }
        
        /// <summary>
        /// Generate plan for file tasks
        /// </summary>
        private List<TaskStep> GenerateFilePlan(string request)
        {
            var steps = new List<TaskStep>();
            
            if (request.Contains("search") || request.Contains("find"))
            {
                steps.Add(new TaskStep { Description = "Search project files for the target" });
                steps.Add(new TaskStep { Description = "Analyze and present results" });
            }
            else
            {
                steps.Add(new TaskStep { Description = "Locate target files" });
                steps.Add(new TaskStep { Description = "Perform file operation" });
                steps.Add(new TaskStep { Description = "Verify operation completed successfully" });
            }
            
            return steps;
        }
        
        /// <summary>
        /// Generate plan for troubleshooting tasks
        /// </summary>
        private List<TaskStep> GenerateTroubleshootingPlan(string request)
        {
            var steps = new List<TaskStep>
            {
                new TaskStep { Description = "Analyze the error and identify root cause" },
                new TaskStep { Description = "Locate the problematic code" },
                new TaskStep { Description = "Apply fix to resolve the issue" },
                new TaskStep { Description = "Build and verify error is resolved" }
            };
            
            return steps;
        }
        
        /// <summary>
        /// Execute the next step in the current task
        /// </summary>
        public async Task<StepExecutionResult> ExecuteNextStepAsync(
            Func<string, CancellationToken, Task<string>> executeStepFunc,
            CancellationToken ct = default)
        {
            if (CurrentTask == null)
            {
                return new StepExecutionResult
                {
                    Success = false,
                    Message = "No active task"
                };
            }
            
            var step = CurrentTask.CurrentStep;
            if (step == null)
            {
                CurrentTask.Status = TaskStatus.Completed;
                await SaveTaskAsync(CurrentTask);
                
                return new StepExecutionResult
                {
                    Success = true,
                    IsComplete = true,
                    Message = $"✅ Task completed! All {CurrentTask.Steps.Count} steps finished."
                };
            }
            
            Debug.WriteLine($"[Orchestrator] Executing step {step.StepNumber}/{CurrentTask.Steps.Count}: {step.Description}");
            
            step.Status = StepStatus.InProgress;
            step.StartedAt = DateTime.Now;
            CurrentTask.UpdatedAt = DateTime.Now;
            
            try
            {
                // Execute the step
                var stepPrompt = $"Step {step.StepNumber}/{CurrentTask.Steps.Count}: {step.Description}\n\nOriginal request: {CurrentTask.OriginalRequest}";
                var result = await executeStepFunc(stepPrompt, ct);
                
                step.Result = result;
                step.Status = StepStatus.Completed;
                step.CompletedAt = DateTime.Now;
                
                // Move to next step
                CurrentTask.CurrentStepIndex++;
                CurrentTask.UpdatedAt = DateTime.Now;
                
                await SaveTaskAsync(CurrentTask);
                
                var isComplete = CurrentTask.CurrentStepIndex >= CurrentTask.Steps.Count;
                if (isComplete)
                {
                    CurrentTask.Status = TaskStatus.Completed;
                    await SaveTaskAsync(CurrentTask);
                }
                
                return new StepExecutionResult
                {
                    Success = true,
                    IsComplete = isComplete,
                    Message = result,
                    StepNumber = step.StepNumber,
                    TotalSteps = CurrentTask.Steps.Count,
                    Progress = CurrentTask.ProgressPercentage
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Orchestrator] Step {step.StepNumber} failed: {ex.Message}");
                
                step.Status = StepStatus.Failed;
                step.ErrorMessage = ex.Message;
                step.CompletedAt = DateTime.Now;
                
                CurrentTask.Status = TaskStatus.Failed;
                CurrentTask.ErrorMessage = ex.Message;
                CurrentTask.UpdatedAt = DateTime.Now;
                
                await SaveTaskAsync(CurrentTask);
                
                return new StepExecutionResult
                {
                    Success = false,
                    Message = $"❌ Step {step.StepNumber} failed: {ex.Message}",
                    StepNumber = step.StepNumber,
                    TotalSteps = CurrentTask.Steps.Count,
                    Progress = CurrentTask.ProgressPercentage
                };
            }
        }
        
        /// <summary>
        /// Get a summary of the current task progress
        /// </summary>
        public string GetProgressSummary()
        {
            if (CurrentTask == null)
                return "No active task.";
            
            var completed = CurrentTask.Steps.Count(s => s.Status == StepStatus.Completed);
            var total = CurrentTask.Steps.Count;
            
            var summary = $"📋 Task Progress: {completed}/{total} steps completed ({CurrentTask.ProgressPercentage}%)\n\n";
            
            foreach (var step in CurrentTask.Steps)
            {
                var icon = step.Status switch
                {
                    StepStatus.Completed => "✅",
                    StepStatus.InProgress => "⏳",
                    StepStatus.Failed => "❌",
                    _ => "⭕"
                };
                
                summary += $"{icon} Step {step.StepNumber}: {step.Description}\n";
            }
            
            return summary;
        }
        
        /// <summary>
        /// Resume a paused task
        /// </summary>
        public bool ResumeTask()
        {
            if (CurrentTask == null || !CurrentTask.CanResume)
                return false;
            
            CurrentTask.Status = TaskStatus.InProgress;
            Debug.WriteLine($"[Orchestrator] Resuming task at step {CurrentTask.CurrentStepIndex + 1}");
            return true;
        }
        
        /// <summary>
        /// Cancel the current task
        /// </summary>
        public void CancelTask()
        {
            if (CurrentTask != null)
            {
                CurrentTask.Status = TaskStatus.Failed;
                CurrentTask.ErrorMessage = "Cancelled by user";
                SaveTaskAsync(CurrentTask).Wait();
                CurrentTask = null;
            }
        }
        
        /// <summary>
        /// Save task state to disk
        /// </summary>
        private async Task SaveTaskAsync(TaskState task)
        {
            try
            {
                var filePath = Path.Combine(TaskStateDir, $"task_{task.TaskId}.json");
                var json = JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                
                // Also save as "current.json" for easy resume
                var currentPath = Path.Combine(TaskStateDir, "current.json");
                await File.WriteAllTextAsync(currentPath, json);
                
                Debug.WriteLine($"[Orchestrator] Task state saved: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Orchestrator] Failed to save task state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load the last incomplete task
        /// </summary>
        private void LoadLastTask()
        {
            try
            {
                var currentPath = Path.Combine(TaskStateDir, "current.json");
                if (!File.Exists(currentPath))
                    return;
                
                var json = File.ReadAllText(currentPath);
                var task = JsonSerializer.Deserialize<TaskState>(json);
                
                if (task != null && !task.IsComplete)
                {
                    CurrentTask = task;
                    Debug.WriteLine($"[Orchestrator] Loaded incomplete task: {task.OriginalRequest} ({task.ProgressPercentage}% complete)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Orchestrator] Failed to load last task: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Result of executing a single step
    /// </summary>
    public class StepExecutionResult
    {
        public bool Success { get; set; }
        public bool IsComplete { get; set; }
        public string Message { get; set; } = "";
        public int StepNumber { get; set; }
        public int TotalSteps { get; set; }
        public int Progress { get; set; }
    }
}
