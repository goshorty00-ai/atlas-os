using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Autopilot.Models;

namespace AtlasAI.Autopilot.Services
{
    /// <summary>
    /// Manages Away Mode - tracks user absence and generates session summaries
    /// </summary>
    public class AwayModeManager
    {
        private readonly AutopilotEngine _engine;
        private readonly ActionAuditLog _auditLog;
        private AwaySession? _currentSession;
        private Timer? _idleTimer;
        private DateTime _lastActivity = DateTime.Now;
        private int _idleThresholdMinutes = 15;
        private bool _autoDetectAway = true;
        
        public event EventHandler<AwaySession>? AwayModeStarted;
        public event EventHandler<AwaySession>? AwayModeEnded;
        public event EventHandler<AwaySessionSummary>? SummaryReady;
        
        public bool IsAway => _currentSession != null && !_currentSession.EndTime.HasValue;
        public AwaySession? CurrentSession => _currentSession;
        public int IdleThresholdMinutes { get => _idleThresholdMinutes; set => _idleThresholdMinutes = value; }
        public bool AutoDetectAway { get => _autoDetectAway; set => _autoDetectAway = value; }

        public AwayModeManager(AutopilotEngine engine, ActionAuditLog auditLog)
        {
            _engine = engine;
            _auditLog = auditLog;
        }
        
        public void Initialize()
        {
            if (_autoDetectAway)
            {
                _idleTimer = new Timer(CheckIdleState, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
            Debug.WriteLine("[AwayMode] Initialized");
        }
        
        /// <summary>
        /// Manually start away mode
        /// </summary>
        public void StartAwayMode(string? reason = null)
        {
            if (IsAway) return;
            
            _currentSession = new AwaySession
            {
                StartTime = DateTime.Now
            };
            
            if (!string.IsNullOrEmpty(reason))
            {
                _currentSession.Observations.Add(new SystemObservation
                {
                    Type = ObservationType.SystemHealth,
                    Description = $"Away mode started: {reason}"
                });
            }
            
            AwayModeStarted?.Invoke(this, _currentSession);
            Debug.WriteLine($"[AwayMode] Started at {_currentSession.StartTime}");
        }
        
        /// <summary>
        /// End away mode and generate summary
        /// </summary>
        public async Task<AwaySessionSummary?> EndAwayModeAsync()
        {
            if (!IsAway || _currentSession == null) return null;
            
            _currentSession.EndTime = DateTime.Now;
            var summary = await GenerateSummaryAsync(_currentSession);
            _currentSession.Summary = summary;
            
            AwayModeEnded?.Invoke(this, _currentSession);
            SummaryReady?.Invoke(this, summary);
            
            Debug.WriteLine($"[AwayMode] Ended. Duration: {_currentSession.Duration}");
            
            var completedSession = _currentSession;
            _currentSession = null;
            
            return summary;
        }

        /// <summary>
        /// Record an action performed during away mode
        /// </summary>
        public void RecordAction(AutopilotAction action)
        {
            if (!IsAway || _currentSession == null) return;
            _currentSession.ActionsPerformed.Add(action);
        }
        
        /// <summary>
        /// Record a suggestion generated during away mode
        /// </summary>
        public void RecordSuggestion(AutopilotSuggestion suggestion)
        {
            if (!IsAway || _currentSession == null) return;
            _currentSession.SuggestionsGenerated.Add(suggestion);
        }
        
        /// <summary>
        /// Record a system observation during away mode
        /// </summary>
        public void RecordObservation(SystemObservation observation)
        {
            if (!IsAway || _currentSession == null) return;
            _currentSession.Observations.Add(observation);
        }
        
        /// <summary>
        /// Record user activity (resets idle timer)
        /// </summary>
        public void RecordActivity()
        {
            _lastActivity = DateTime.Now;
            
            // If away and activity detected, end away mode
            if (IsAway && _autoDetectAway)
            {
                _ = EndAwayModeAsync();
            }
        }
        
        private void CheckIdleState(object? state)
        {
            var idleMinutes = (DateTime.Now - _lastActivity).TotalMinutes;
            
            if (!IsAway && idleMinutes >= _idleThresholdMinutes)
            {
                StartAwayMode("Auto-detected idle");
            }
        }

        private async Task<AwaySessionSummary> GenerateSummaryAsync(AwaySession session)
        {
            var summary = new AwaySessionSummary
            {
                TotalActions = session.ActionsPerformed.Count,
                SuccessfulActions = session.ActionsPerformed.Count(a => a.Status == ActionStatus.Completed),
                FailedActions = session.ActionsPerformed.Count(a => a.Status == ActionStatus.Failed),
                PendingApprovals = session.ActionsPerformed.Count(a => a.Status == ActionStatus.AwaitingApproval)
            };
            
            // Generate highlights
            foreach (var action in session.ActionsPerformed.Where(a => a.Status == ActionStatus.Completed))
            {
                summary.Highlights.Add($"✓ {action.Description}");
            }
            
            // Generate issues
            foreach (var action in session.ActionsPerformed.Where(a => a.Status == ActionStatus.Failed))
            {
                summary.Issues.Add($"✗ {action.Description}: {action.ErrorMessage}");
            }
            
            foreach (var obs in session.Observations.Where(o => o.RequiresAttention))
            {
                summary.Issues.Add($"⚠ {obs.Description}");
            }
            
            // Generate narrative summary
            summary.NarrativeSummary = GenerateNarrative(session, summary);
            
            await Task.CompletedTask;
            return summary;
        }
        
        private string GenerateNarrative(AwaySession session, AwaySessionSummary summary)
        {
            var duration = session.Duration;
            var durationText = duration.TotalHours >= 1 
                ? $"{(int)duration.TotalHours} hours and {duration.Minutes} minutes"
                : $"{(int)duration.TotalMinutes} minutes";
            
            var narrative = $"While you were away for {durationText}, ";
            
            if (summary.TotalActions == 0)
            {
                narrative += "everything was quiet. No actions were needed.";
            }
            else
            {
                narrative += $"I completed {summary.SuccessfulActions} task(s)";
                if (summary.FailedActions > 0)
                    narrative += $", {summary.FailedActions} failed";
                if (summary.PendingApprovals > 0)
                    narrative += $", and {summary.PendingApprovals} need your approval";
                narrative += ".";
            }
            
            if (session.Observations.Any(o => o.RequiresAttention))
            {
                narrative += $" There are {session.Observations.Count(o => o.RequiresAttention)} items that need your attention.";
            }
            
            return narrative;
        }
        
        public void Shutdown()
        {
            _idleTimer?.Dispose();
            _idleTimer = null;
        }
    }
}
