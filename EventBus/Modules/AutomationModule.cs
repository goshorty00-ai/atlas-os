using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AtlasAI.EventBus.Modules
{
    /// <summary>
    /// Automation module that executes actions based on AI recommendations and system events.
    /// </summary>
    public class AutomationModule : AtlasModuleBase
    {
        public override string ModuleId => "automation";
        public override string ModuleName => "Automation Module";

        protected override void RegisterEventHandlers()
        {
            Subscribe<AutomationTriggeredEvent>(OnAutomationTriggered);
            Subscribe<AiActionRecommendedEvent>(OnActionRecommended);
        }

        private void OnAutomationTriggered(AutomationTriggeredEvent evt)
        {
            Log($"Automation triggered: {evt.AutomationName}");
            Task.Run(() => ExecuteAutomation(evt));
        }

        private void OnActionRecommended(AiActionRecommendedEvent evt)
        {
            if (evt.AutoExecute && evt.Priority == "critical")
            {
                Log($"Auto-executing critical action: {evt.Action}");
                Task.Run(() => ExecuteAction(evt.Action, evt.Reason));
            }
        }

        private void ExecuteAutomation(AutomationTriggeredEvent evt)
        {
            try
            {
                var success = true;
                var result = "";

                foreach (var action in evt.Actions ?? new())
                {
                    Log($"Executing action: {action}");
                    var actionResult = ExecuteAction(action, evt.Trigger);
                    if (!actionResult)
                    {
                        success = false;
                        result += $"Failed: {action}; ";
                    }
                }

                Publish(new AutomationCompletedEvent
                {
                    AutomationName = evt.AutomationName,
                    Success = success,
                    Result = success ? "All actions completed successfully" : result
                });
            }
            catch (Exception ex)
            {
                Log($"Error executing automation: {ex.Message}");
                Publish(new AutomationCompletedEvent
                {
                    AutomationName = evt.AutomationName,
                    Success = false,
                    Result = $"Error: {ex.Message}"
                });
            }
        }

        private bool ExecuteAction(string action, string reason)
        {
            try
            {
                Log($"Executing: {action} (Reason: {reason})");

                switch (action.ToLowerInvariant())
                {
                    case "optimize_memory":
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        return true;

                    case "analyze_file":
                        return true;

                    case "quarantine_threat":
                        return true;

                    default:
                        Log($"Unknown action: {action}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error executing action {action}: {ex.Message}");
                return false;
            }
        }
    }
}
