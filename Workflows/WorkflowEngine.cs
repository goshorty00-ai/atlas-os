using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Agent;
using AtlasAI.Core;

namespace AtlasAI.Workflows
{
    /// <summary>
    /// Workflow Engine - Executes workflow chains step-by-step.
    /// 
    /// SAFETY: All workflows are READ-ONLY.
    /// - Only executes SafeReadOnly macros
    /// - No registry writes, no deletions, no service changes
    /// - User controls each step via "Run Next Step"
    /// - Session-only (no persistence)
    /// </summary>
    public class WorkflowEngine
    {
        private static WorkflowEngine? _instance;
        public static WorkflowEngine Instance => _instance ??= new WorkflowEngine();

        private readonly List<WorkflowChainDefinition> _definitions;
        private WorkflowChainInstance? _activeWorkflow;

        // Events for UI binding
        public event EventHandler<WorkflowChainInstance>? WorkflowStarted;
        public event EventHandler<WorkflowStep>? StepStarted;
        public event EventHandler<WorkflowStep>? StepCompleted;
        public event EventHandler<WorkflowChainInstance>? WorkflowCompleted;
        public event EventHandler? WorkflowCancelled;

        /// <summary>
        /// Currently active workflow instance (null if none)
        /// </summary>
        public WorkflowChainInstance? ActiveWorkflow => _activeWorkflow;

        /// <summary>
        /// Whether a workflow is currently active
        /// </summary>
        public bool IsWorkflowActive => _activeWorkflow != null && !_activeWorkflow.IsComplete;

        /// <summary>
        /// All available workflow definitions
        /// </summary>
        public IReadOnlyList<WorkflowChainDefinition> Definitions => _definitions.AsReadOnly();

        private WorkflowEngine()
        {
            _definitions = WorkflowDefinitions.GetAll();
        }

        /// <summary>
        /// Find workflows matching the input query
        /// </summary>
        public List<WorkflowChainDefinition> FindMatching(string input, int maxResults = 5)
        {
            if (string.IsNullOrWhiteSpace(input))
                return _definitions.Take(maxResults).ToList();

            return _definitions
                .Where(w => w.Matches(input))
                .OrderByDescending(w => w.GetMatchScore(input))
                .Take(maxResults)
                .ToList();
        }

        /// <summary>
        /// Get a workflow definition by ID
        /// </summary>
        public WorkflowChainDefinition? GetDefinition(string workflowId)
        {
            return _definitions.FirstOrDefault(w => 
                w.Id.Equals(workflowId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Start a new workflow from a definition
        /// </summary>
        public WorkflowChainInstance? StartWorkflow(string workflowId)
        {
            // Cancel any existing workflow
            if (_activeWorkflow != null && !_activeWorkflow.IsComplete)
            {
                CancelWorkflow();
            }

            var definition = GetDefinition(workflowId);
            if (definition == null)
            {
                Debug.WriteLine($"[WorkflowEngine] Workflow not found: {workflowId}");
                return null;
            }

            _activeWorkflow = definition.CreateInstance();
            _activeWorkflow.Status = WorkflowInstanceStatus.Paused;

            // Update presence model for HUD pulse
            PresenceVisualModel.Instance.IsWorkflowActive = true;

            Debug.WriteLine($"[WorkflowEngine] Started workflow: {definition.Title} ({_activeWorkflow.InstanceId})");
            WorkflowStarted?.Invoke(this, _activeWorkflow);

            return _activeWorkflow;
        }

        /// <summary>
        /// Execute the next step in the active workflow.
        /// Returns the step result, or null if no more steps.
        /// </summary>
        public async Task<WorkflowStep?> RunNextStepAsync()
        {
            if (_activeWorkflow == null || _activeWorkflow.IsComplete)
            {
                Debug.WriteLine("[WorkflowEngine] No active workflow or already complete");
                return null;
            }

            var step = _activeWorkflow.CurrentStep;
            if (step == null)
            {
                CompleteWorkflow();
                return null;
            }

            // Mark step as running
            step.Status = WorkflowStepStatus.Running;
            _activeWorkflow.Status = WorkflowInstanceStatus.Running;
            StepStarted?.Invoke(this, step);

            var sw = Stopwatch.StartNew();

            try
            {
                switch (step.Type)
                {
                    case WorkflowStepType.Macro:
                        await ExecuteMacroStepAsync(step);
                        break;

                    case WorkflowStepType.Action:
                        await ExecuteActionStepAsync(step);
                        break;

                    case WorkflowStepType.Insight:
                        ExecuteInsightStep(step);
                        break;
                }

                sw.Stop();
                step.ExecutionTime = sw.Elapsed;

                if (step.Status != WorkflowStepStatus.Failed)
                {
                    step.Status = WorkflowStepStatus.Complete;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                step.ExecutionTime = sw.Elapsed;
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = ex.Message;
                Debug.WriteLine($"[WorkflowEngine] Step {step.StepNumber} failed: {ex.Message}");
            }

            // Move to next step
            _activeWorkflow.CurrentStepIndex++;
            _activeWorkflow.Status = WorkflowInstanceStatus.Paused;

            StepCompleted?.Invoke(this, step);

            // Check if workflow is complete
            if (_activeWorkflow.CurrentStepIndex >= _activeWorkflow.Steps.Count)
            {
                CompleteWorkflow();
            }

            return step;
        }

        /// <summary>
        /// Skip the current step
        /// </summary>
        public WorkflowStep? SkipCurrentStep()
        {
            if (_activeWorkflow == null || _activeWorkflow.IsComplete)
                return null;

            var step = _activeWorkflow.CurrentStep;
            if (step == null)
                return null;

            step.Status = WorkflowStepStatus.Skipped;
            step.ResultSummary = "Skipped by user";

            _activeWorkflow.CurrentStepIndex++;
            StepCompleted?.Invoke(this, step);

            if (_activeWorkflow.CurrentStepIndex >= _activeWorkflow.Steps.Count)
            {
                CompleteWorkflow();
            }

            return step;
        }

        /// <summary>
        /// Cancel the active workflow
        /// </summary>
        public void CancelWorkflow()
        {
            if (_activeWorkflow == null)
                return;

            _activeWorkflow.Status = WorkflowInstanceStatus.Cancelled;
            _activeWorkflow.CompletedAt = DateTime.Now;

            // Update presence model
            PresenceVisualModel.Instance.IsWorkflowActive = false;

            Debug.WriteLine($"[WorkflowEngine] Workflow cancelled: {_activeWorkflow.InstanceId}");
            WorkflowCancelled?.Invoke(this, EventArgs.Empty);

            _activeWorkflow = null;
        }

        private void CompleteWorkflow()
        {
            if (_activeWorkflow == null)
                return;

            _activeWorkflow.Status = WorkflowInstanceStatus.Completed;
            _activeWorkflow.CompletedAt = DateTime.Now;

            // Generate final insight
            _activeWorkflow.FinalInsight = WorkflowInsightGenerator.GenerateFinalInsight(_activeWorkflow);

            // Update presence model
            PresenceVisualModel.Instance.IsWorkflowActive = false;

            Debug.WriteLine($"[WorkflowEngine] Workflow completed: {_activeWorkflow.InstanceId}");
            WorkflowCompleted?.Invoke(this, _activeWorkflow);
        }

        private async Task ExecuteMacroStepAsync(WorkflowStep step)
        {
            if (string.IsNullOrEmpty(step.TargetId))
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = "No macro ID specified";
                return;
            }

            var result = await AgentMacroEngine.Instance.ExecuteByIdAsync(step.TargetId);

            if (result == null)
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = $"Macro not found: {step.TargetId}";
                return;
            }

            step.ResultData = result;
            step.ResultSummary = result.Summary ?? (result.Success ? "Completed" : result.ErrorMessage);

            if (!result.Success)
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = result.ErrorMessage;
            }
        }

        private async Task ExecuteActionStepAsync(WorkflowStep step)
        {
            if (string.IsNullOrEmpty(step.TargetId))
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = "No action ID specified";
                return;
            }

            // Only allow safe actions (open settings, etc.)
            var safeActions = new[] { "open-network-settings", "open-disk-cleanup", "open-task-manager" };
            if (!safeActions.Contains(step.TargetId))
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = "Action not allowed in workflow";
                return;
            }

            var result = await AgentActionEngine.Instance.ExecuteByIdAsync(step.TargetId);

            if (result == null)
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = $"Action not found: {step.TargetId}";
                return;
            }

            step.ResultSummary = result.Message ?? (result.Success ? "Completed" : result.ErrorMessage);

            if (!result.Success)
            {
                step.Status = WorkflowStepStatus.Failed;
                step.ErrorMessage = result.ErrorMessage;
            }
        }

        private void ExecuteInsightStep(WorkflowStep step)
        {
            // Generate insight based on previous step results
            step.InsightText = WorkflowInsightGenerator.GenerateStepInsight(_activeWorkflow!, step);
            step.ResultSummary = step.InsightText;
        }
    }
}
