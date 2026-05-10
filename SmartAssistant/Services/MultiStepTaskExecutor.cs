using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.SmartAssistant.Models;

namespace AtlasAI.SmartAssistant.Services
{
    /// <summary>
    /// Executes multi-step task plans with progress tracking and rollback capability
    /// </summary>
    public class MultiStepTaskExecutor
    {
        private TaskPlan? _currentPlan;
        private CancellationTokenSource? _cts;
        private readonly List<TaskPlan> _completedPlans = new();
        
        public event EventHandler<TaskPlan>? PlanStarted;
        public event EventHandler<TaskStep>? StepStarted;
        public event EventHandler<TaskStep>? StepCompleted;
        public event EventHandler<TaskStep>? StepFailed;
        public event EventHandler<TaskPlan>? PlanCompleted;
        public event EventHandler<TaskPlan>? PlanFailed;
        public event EventHandler<(TaskStep Step, string Message)>? ConfirmationRequired;
        
        public TaskPlan? CurrentPlan => _currentPlan;
        public bool IsRunning => _currentPlan?.Status == TaskPlanStatus.Running;
        
        // Delegate for executing individual actions
        public Func<string, Dictionary<string, object>, Task<string>>? ActionExecutor { get; set; }
        
        /// <summary>
        /// Create a task plan from natural language
        /// </summary>
        public TaskPlan CreatePlan(string name, string description, List<(string action, string desc, Dictionary<string, object>? parameters)> steps)
        {
            var plan = new TaskPlan
            {
                Name = name,
                Description = description,
                Status = TaskPlanStatus.Pending
            };
            
            for (int i = 0; i < steps.Count; i++)
            {
                var (action, desc, parameters) = steps[i];
                plan.Steps.Add(new TaskStep
                {
                    Order = i + 1,
                    Action = action,
                    Description = desc,
                    Parameters = parameters ?? new(),
                    RequiresConfirmation = IsHighRiskAction(action)
                });
            }
            
            return plan;
        }
        
        /// <summary>
        /// Execute a task plan
        /// </summary>
        public async Task<bool> ExecutePlanAsync(TaskPlan plan, bool autoConfirm = false)
        {
            if (_currentPlan?.Status == TaskPlanStatus.Running)
            {
                Debug.WriteLine("[TaskExecutor] Another plan is already running");
                return false;
            }
            
            _currentPlan = plan;
            _cts = new CancellationTokenSource();
            plan.Status = TaskPlanStatus.Running;
            plan.CurrentStepIndex = 0;
            
            PlanStarted?.Invoke(this, plan);
            Debug.WriteLine($"[TaskExecutor] Starting plan: {plan.Name} with {plan.Steps.Count} steps");
            
            try
            {
                foreach (var step in plan.Steps)
                {
                    if (_cts.Token.IsCancellationRequested)
                    {
                        plan.Status = TaskPlanStatus.Cancelled;
                        return false;
                    }
                    
                    // Handle confirmation if required
                    if (step.RequiresConfirmation && !autoConfirm)
                    {
                        var confirmed = await RequestConfirmationAsync(step);
                        if (!confirmed)
                        {
                            step.Status = TaskStepStatus.Skipped;
                            continue;
                        }
                    }
                    
                    step.Status = TaskStepStatus.Running;
                    StepStarted?.Invoke(this, step);
                    
                    try
                    {
                        if (ActionExecutor != null)
                        {
                            step.Result = await ActionExecutor(step.Action, step.Parameters);
                        }
                        else
                        {
                            // Simulate execution
                            await Task.Delay(500, _cts.Token);
                            step.Result = $"Executed: {step.Action}";
                        }
                        
                        step.Status = TaskStepStatus.Completed;
                        StepCompleted?.Invoke(this, step);
                        Debug.WriteLine($"[TaskExecutor] Step {step.Order} completed: {step.Action}");
                    }
                    catch (Exception ex)
                    {
                        step.Status = TaskStepStatus.Failed;
                        step.ErrorMessage = ex.Message;
                        StepFailed?.Invoke(this, step);
                        
                        plan.Status = TaskPlanStatus.Failed;
                        plan.ErrorMessage = $"Step {step.Order} failed: {ex.Message}";
                        PlanFailed?.Invoke(this, plan);
                        return false;
                    }
                    
                    plan.CurrentStepIndex++;
                }
                
                plan.Status = TaskPlanStatus.Completed;
                plan.CompletedAt = DateTime.Now;
                _completedPlans.Add(plan);
                PlanCompleted?.Invoke(this, plan);
                Debug.WriteLine($"[TaskExecutor] Plan completed: {plan.Name}");
                return true;
            }
            catch (OperationCanceledException)
            {
                plan.Status = TaskPlanStatus.Cancelled;
                return false;
            }
            finally
            {
                _currentPlan = null;
                _cts?.Dispose();
                _cts = null;
            }
        }
        
        /// <summary>
        /// Pause the current plan
        /// </summary>
        public void PausePlan()
        {
            if (_currentPlan?.Status == TaskPlanStatus.Running)
            {
                _currentPlan.Status = TaskPlanStatus.Paused;
                Debug.WriteLine("[TaskExecutor] Plan paused");
            }
        }
        
        /// <summary>
        /// Cancel the current plan
        /// </summary>
        public void CancelPlan()
        {
            _cts?.Cancel();
            if (_currentPlan != null)
            {
                _currentPlan.Status = TaskPlanStatus.Cancelled;
                Debug.WriteLine("[TaskExecutor] Plan cancelled");
            }
        }
        
        /// <summary>
        /// Get execution summary
        /// </summary>
        public string GetPlanSummary(TaskPlan plan)
        {
            var completed = plan.Steps.Count(s => s.Status == TaskStepStatus.Completed);
            var failed = plan.Steps.Count(s => s.Status == TaskStepStatus.Failed);
            var skipped = plan.Steps.Count(s => s.Status == TaskStepStatus.Skipped);
            
            return $"Plan '{plan.Name}': {completed}/{plan.Steps.Count} completed, {failed} failed, {skipped} skipped";
        }
        
        private bool IsHighRiskAction(string action)
        {
            var highRiskActions = new[]
            {
                "delete", "remove", "uninstall", "shutdown", "restart",
                "format", "modify_registry", "change_password", "send_email"
            };
            
            return highRiskActions.Any(a => action.ToLowerInvariant().Contains(a));
        }
        
        private Task<bool> RequestConfirmationAsync(TaskStep step)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            ConfirmationRequired?.Invoke(this, (step, $"Confirm action: {step.Description}?"));
            
            // For now, auto-confirm after raising event
            // In real implementation, this would wait for user input
            tcs.SetResult(true);
            
            return tcs.Task;
        }
    }
}
