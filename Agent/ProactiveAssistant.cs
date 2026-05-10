using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Proactive assistant - learns patterns and suggests actions.
    /// Watches what you do and offers help before you ask.
    /// </summary>
    public class ProactiveAssistant
    {
        private static ProactiveAssistant? _instance;
        public static ProactiveAssistant Instance => _instance ??= new ProactiveAssistant();
        
        // Activity tracking
        private readonly Queue<UserActivity> _recentActivities = new();
        private readonly Dictionary<string, int> _actionFrequency = new();
        private readonly Dictionary<string, List<string>> _actionSequences = new();
        private const int MaxActivities = 100;
        
        // Time-based patterns
        private readonly Dictionary<int, List<string>> _hourlyPatterns = new(); // Hour -> common actions
        private readonly Dictionary<DayOfWeek, List<string>> _dailyPatterns = new();
        
        // App context
        private string? _lastForegroundApp;
        private DateTime _lastAppChange;
        private readonly Dictionary<string, TimeSpan> _appUsageTime = new();
        
        // Suggestions
        private readonly List<Suggestion> _pendingSuggestions = new();
        private DateTime _lastSuggestionTime = DateTime.MinValue;
        private const int SuggestionCooldownSeconds = 300; // 5 min between suggestions
        
        public event Action<Suggestion>? OnSuggestion;
        
        private ProactiveAssistant() { }
        
        /// <summary>
        /// Record a user action for pattern learning
        /// </summary>
        public void RecordAction(string actionType, string target, bool wasSuccessful = true)
        {
            var activity = new UserActivity
            {
                ActionType = actionType,
                Target = target,
                Timestamp = DateTime.Now,
                Hour = DateTime.Now.Hour,
                DayOfWeek = DateTime.Now.DayOfWeek,
                WasSuccessful = wasSuccessful,
                ForegroundApp = _lastForegroundApp
            };
            
            _recentActivities.Enqueue(activity);
            while (_recentActivities.Count > MaxActivities)
                _recentActivities.Dequeue();
            
            // Track frequency
            var key = $"{actionType}:{target}";
            _actionFrequency[key] = _actionFrequency.GetValueOrDefault(key, 0) + 1;
            
            // Track sequences (what follows what)
            var recent = _recentActivities.TakeLast(2).ToList();
            if (recent.Count == 2)
            {
                var prevKey = $"{recent[0].ActionType}:{recent[0].Target}";
                if (!_actionSequences.ContainsKey(prevKey))
                    _actionSequences[prevKey] = new List<string>();
                _actionSequences[prevKey].Add(key);
            }
            
            // Track hourly patterns
            if (!_hourlyPatterns.ContainsKey(activity.Hour))
                _hourlyPatterns[activity.Hour] = new List<string>();
            _hourlyPatterns[activity.Hour].Add(key);
            
            // Track daily patterns
            if (!_dailyPatterns.ContainsKey(activity.DayOfWeek))
                _dailyPatterns[activity.DayOfWeek] = new List<string>();
            _dailyPatterns[activity.DayOfWeek].Add(key);
            
            Debug.WriteLine($"[Proactive] Recorded: {actionType} -> {target}");
            
            // Check if we should make a suggestion
            _ = CheckForSuggestionsAsync();
        }
        
        /// <summary>
        /// Update the current foreground app
        /// </summary>
        public void UpdateForegroundApp(string appName)
        {
            if (_lastForegroundApp != null && _lastForegroundApp != appName)
            {
                // Track time spent in previous app
                var timeSpent = DateTime.Now - _lastAppChange;
                _appUsageTime[_lastForegroundApp] = _appUsageTime.GetValueOrDefault(_lastForegroundApp, TimeSpan.Zero) + timeSpent;
            }
            
            _lastForegroundApp = appName;
            _lastAppChange = DateTime.Now;
        }
        
        /// <summary>
        /// Get suggestions based on current context
        /// </summary>
        public List<Suggestion> GetContextualSuggestions()
        {
            var suggestions = new List<Suggestion>();
            var now = DateTime.Now;
            
            // Time-based suggestions
            if (_hourlyPatterns.TryGetValue(now.Hour, out var hourlyActions))
            {
                var common = hourlyActions
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Where(g => g.Count() >= 3) // At least 3 occurrences
                    .ToList();
                
                foreach (var action in common)
                {
                    var parts = action.Key.Split(':');
                    if (parts.Length == 2)
                    {
                        suggestions.Add(new Suggestion
                        {
                            Type = SuggestionType.TimeBased,
                            Action = parts[0],
                            Target = parts[1],
                            Reason = $"You often do this around {now.Hour}:00",
                            Confidence = Math.Min(0.9, action.Count() * 0.1)
                        });
                    }
                }
            }
            
            // Sequence-based suggestions (what usually comes next)
            var lastActivity = _recentActivities.LastOrDefault();
            if (lastActivity != null)
            {
                var lastKey = $"{lastActivity.ActionType}:{lastActivity.Target}";
                if (_actionSequences.TryGetValue(lastKey, out var nextActions))
                {
                    var common = nextActions
                        .GroupBy(x => x)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();
                    
                    if (common != null && common.Count() >= 2)
                    {
                        var parts = common.Key.Split(':');
                        if (parts.Length == 2)
                        {
                            suggestions.Add(new Suggestion
                            {
                                Type = SuggestionType.SequenceBased,
                                Action = parts[0],
                                Target = parts[1],
                                Reason = $"You usually do this after {lastActivity.ActionType} {lastActivity.Target}",
                                Confidence = Math.Min(0.85, common.Count() * 0.15)
                            });
                        }
                    }
                }
            }
            
            return suggestions.OrderByDescending(s => s.Confidence).Take(3).ToList();
        }
        
        /// <summary>
        /// Get the most frequent actions
        /// </summary>
        public List<(string Action, string Target, int Count)> GetTopActions(int count = 10)
        {
            return _actionFrequency
                .OrderByDescending(kv => kv.Value)
                .Take(count)
                .Select(kv =>
                {
                    var parts = kv.Key.Split(':');
                    return (parts[0], parts.Length > 1 ? parts[1] : "", kv.Value);
                })
                .ToList();
        }
        
        /// <summary>
        /// Get app usage statistics
        /// </summary>
        public Dictionary<string, TimeSpan> GetAppUsageStats()
        {
            // Include current app time
            if (_lastForegroundApp != null)
            {
                var current = DateTime.Now - _lastAppChange;
                var result = new Dictionary<string, TimeSpan>(_appUsageTime);
                result[_lastForegroundApp] = result.GetValueOrDefault(_lastForegroundApp, TimeSpan.Zero) + current;
                return result;
            }
            return new Dictionary<string, TimeSpan>(_appUsageTime);
        }
        
        private async Task CheckForSuggestionsAsync()
        {
            // Cooldown check
            if ((DateTime.Now - _lastSuggestionTime).TotalSeconds < SuggestionCooldownSeconds)
                return;
            
            var suggestions = GetContextualSuggestions();
            var topSuggestion = suggestions.FirstOrDefault(s => s.Confidence >= 0.7);
            
            if (topSuggestion != null)
            {
                _lastSuggestionTime = DateTime.Now;
                OnSuggestion?.Invoke(topSuggestion);
            }
        }
        
        /// <summary>
        /// Get a quick status summary
        /// </summary>
        public string GetStatusSummary()
        {
            var topActions = GetTopActions(3);
            var suggestions = GetContextualSuggestions();
            
            var summary = "📊 Activity Summary:\n";
            
            if (topActions.Any())
            {
                summary += "Top actions: " + string.Join(", ", topActions.Select(a => $"{a.Action} {a.Target} ({a.Count}x)")) + "\n";
            }
            
            if (suggestions.Any())
            {
                summary += "Suggestions: " + string.Join(", ", suggestions.Select(s => $"{s.Action} {s.Target}"));
            }
            
            return summary;
        }
    }
    
    public class UserActivity
    {
        public string ActionType { get; set; } = "";
        public string Target { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int Hour { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public bool WasSuccessful { get; set; }
        public string? ForegroundApp { get; set; }
    }
    
    public class Suggestion
    {
        public SuggestionType Type { get; set; }
        public string Action { get; set; } = "";
        public string Target { get; set; } = "";
        public string Reason { get; set; } = "";
        public double Confidence { get; set; }
        
        public string ToCommand() => $"{Action} {Target}".Trim();
        public override string ToString() => $"💡 {Action} {Target}? ({Reason})";
    }
    
    public enum SuggestionType
    {
        TimeBased,      // "You usually do X at this time"
        SequenceBased,  // "You usually do Y after X"
        AppBased,       // "When using Chrome, you often..."
        FrequencyBased  // "You do this a lot"
    }
}
