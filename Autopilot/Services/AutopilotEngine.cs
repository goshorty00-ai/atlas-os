using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AtlasAI.Autopilot.Models;

namespace AtlasAI.Autopilot.Services
{
    /// <summary>
    /// Core autopilot engine - manages rule execution and action processing
    /// </summary>
    public class AutopilotEngine
    {
        private readonly RuleParser _ruleParser;
        private readonly ActionAuditLog _auditLog;
        private AutopilotConfig _config = new();
        private List<AutopilotRule> _rules = new();
        private List<AutopilotAction> _pendingActions = new();
        private int _actionsThisSession;
        private DateTime _sessionStart = DateTime.Now;
        private DateTime _lastActionTime = DateTime.MinValue;
        private int _actionsThisMinute;
        
        public event EventHandler<AutopilotAction>? ActionPending;
        public event EventHandler<AutopilotAction>? ActionExecuted;
        public event EventHandler<AutopilotAction>? ActionFailed;
        public event EventHandler<AutopilotSuggestion>? SuggestionGenerated;
        public event EventHandler<string>? StatusChanged;
        
        public AutopilotConfig Config => _config;
        public IReadOnlyList<AutopilotRule> Rules => _rules.AsReadOnly();
        public IReadOnlyList<AutopilotAction> PendingActions => _pendingActions.AsReadOnly();
        public bool IsEnabled => _config.IsEnabled;
        
        // Delegate for executing actions
        public Func<string, Dictionary<string, object>, Task<string>>? ActionExecutor { get; set; }
        
        public AutopilotEngine(RuleParser ruleParser, ActionAuditLog auditLog)
        {
            _ruleParser = ruleParser;
            _auditLog = auditLog;
        }
        
        public void SetConfig(AutopilotConfig config) => _config = config;
        public void SetRules(List<AutopilotRule> rules) => _rules = rules;
        
        /// <summary>
        /// Process a potential action based on current context
        /// </summary>
        public async Task<AutopilotAction?> ProcessActionAsync(
            string actionType,
            string description,
            string reasoning,
            Dictionary<string, object>? parameters = null,
            ActionContext? context = null)
        {
            if (!_config.IsEnabled) return null;
            
            // Check blocked actions
            if (_config.BlockedActions.Contains(actionType, StringComparer.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[Autopilot] Action blocked: {actionType}");
                return null;
            }
            
            // Check rate limits
            if (!CheckRateLimits())
            {
                Debug.WriteLine("[Autopilot] Rate limit exceeded");
                return null;
            }
            
            var action = new AutopilotAction
            {
                ActionType = actionType,
                Description = description,
                Reasoning = reasoning,
                Parameters = parameters ?? new(),
                Context = context ?? CreateCurrentContext(),
                RequiredLevel = DetermineRequiredLevel(actionType)
            };
            
            // Find matching rule
            var matchingRule = FindMatchingRule(action);
            if (matchingRule != null)
            {
                action.RuleId = matchingRule.Id;
                action.RequiredLevel = matchingRule.AutonomyLevel;
            }
            
            // Determine execution path
            if (action.RequiredLevel == AutonomyLevel.AutoExecute && 
                _config.DefaultAutonomyLevel == AutonomyLevel.AutoExecute)
            {
                // Auto-execute
                action.WasAutoExecuted = true;
                return await ExecuteActionAsync(action);
            }
            else if (action.RequiredLevel == AutonomyLevel.Observe ||
                     _config.DefaultAutonomyLevel == AutonomyLevel.Observe)
            {
                // Just observe and log
                action.Status = ActionStatus.Completed;
                await _auditLog.LogActionAsync(action, "Observed only");
                return action;
            }
            else
            {
                // Queue for approval
                action.Status = ActionStatus.AwaitingApproval;
                _pendingActions.Add(action);
                ActionPending?.Invoke(this, action);
                return action;
            }
        }
        
        /// <summary>
        /// Approve a pending action
        /// </summary>
        public async Task<bool> ApproveActionAsync(string actionId, string? note = null)
        {
            var action = _pendingActions.FirstOrDefault(a => a.Id == actionId);
            if (action == null) return false;
            
            action.WasApproved = true;
            action.ApprovalNote = note;
            action.Status = ActionStatus.Approved;
            
            _pendingActions.Remove(action);
            await ExecuteActionAsync(action);
            return true;
        }
        
        /// <summary>
        /// Reject a pending action
        /// </summary>
        public async Task RejectActionAsync(string actionId, string? reason = null)
        {
            var action = _pendingActions.FirstOrDefault(a => a.Id == actionId);
            if (action == null) return;
            
            action.Status = ActionStatus.Rejected;
            action.ApprovalNote = reason;
            _pendingActions.Remove(action);
            
            await _auditLog.LogActionAsync(action, $"Rejected: {reason}");
        }
        
        /// <summary>
        /// Execute an approved action
        /// </summary>
        private async Task<AutopilotAction> ExecuteActionAsync(AutopilotAction action)
        {
            action.Status = ActionStatus.Executing;
            
            try
            {
                if (ActionExecutor != null)
                {
                    action.Result = await ActionExecutor(action.ActionType, action.Parameters);
                }
                
                action.Status = ActionStatus.Completed;
                action.ExecutedAt = DateTime.Now;
                _actionsThisSession++;
                UpdateRateLimit();
                
                // Update rule stats
                var rule = _rules.FirstOrDefault(r => r.Id == action.RuleId);
                if (rule != null)
                {
                    rule.ExecutionCount++;
                    rule.LastExecuted = DateTime.Now;
                }
                
                await _auditLog.LogActionAsync(action, "Executed successfully");
                ActionExecuted?.Invoke(this, action);
                
                if (_config.NotifyOnEveryAction)
                {
                    StatusChanged?.Invoke(this, $"Completed: {action.Description}");
                }
            }
            catch (Exception ex)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorMessage = ex.Message;
                
                await _auditLog.LogActionAsync(action, $"Failed: {ex.Message}");
                ActionFailed?.Invoke(this, action);
                
                if (_config.PauseOnError)
                {
                    _config.IsEnabled = false;
                    StatusChanged?.Invoke(this, "Autopilot paused due to error");
                }
            }
            
            return action;
        }
        
        /// <summary>
        /// Generate a proactive suggestion
        /// </summary>
        public void GenerateSuggestion(
            string title,
            string description,
            string reasoning,
            SuggestionType type,
            SuggestionPriority priority,
            string? proposedAction = null)
        {
            var suggestion = new AutopilotSuggestion
            {
                Title = title,
                Description = description,
                Reasoning = reasoning,
                Type = type,
                Priority = priority,
                ProposedAction = proposedAction
            };
            
            SuggestionGenerated?.Invoke(this, suggestion);
        }
        
        private AutopilotRule? FindMatchingRule(AutopilotAction action)
        {
            return _rules
                .Where(r => r.IsEnabled)
                .FirstOrDefault(r => r.AllowedActions.Contains(action.ActionType, StringComparer.OrdinalIgnoreCase) ||
                                    _ruleParser.MatchesCondition(r.ParsedCondition, action.Context));
        }
        
        private AutonomyLevel DetermineRequiredLevel(string actionType)
        {
            // High-risk actions always require approval
            var highRisk = new[] { "delete", "modify", "send", "install", "uninstall" };
            if (highRisk.Any(h => actionType.Contains(h, StringComparison.OrdinalIgnoreCase)))
            {
                return AutonomyLevel.Ask;
            }
            
            return _config.DefaultAutonomyLevel;
        }
        
        private ActionContext CreateCurrentContext()
        {
            return new ActionContext
            {
                TimeOfDay = DateTime.Now.TimeOfDay,
                DayOfWeek = DateTime.Now.DayOfWeek,
                IsAwayMode = false
            };
        }
        
        private bool CheckRateLimits()
        {
            // Check session limit
            if (_actionsThisSession >= _config.MaxActionsPerSession) return false;
            
            // Check per-minute limit
            if (DateTime.Now - _lastActionTime < TimeSpan.FromMinutes(1))
            {
                if (_actionsThisMinute >= _config.MaxActionsPerMinute) return false;
            }
            
            // Check session timeout
            if (DateTime.Now - _sessionStart > _config.SessionTimeout)
            {
                _config.IsEnabled = false;
                StatusChanged?.Invoke(this, "Session timeout - autopilot disabled");
                return false;
            }
            
            return true;
        }
        
        private void UpdateRateLimit()
        {
            if (DateTime.Now - _lastActionTime >= TimeSpan.FromMinutes(1))
            {
                _actionsThisMinute = 0;
            }
            _actionsThisMinute++;
            _lastActionTime = DateTime.Now;
        }
        
        public void ResetSession()
        {
            _actionsThisSession = 0;
            _sessionStart = DateTime.Now;
            _actionsThisMinute = 0;
        }
    }
}
