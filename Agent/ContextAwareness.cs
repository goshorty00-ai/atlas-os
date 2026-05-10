using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Context Awareness - Knows what you're doing and adapts.
    /// Tracks active app, time of day, and activity patterns.
    /// </summary>
    public class ContextAwareness
    {
        private static ContextAwareness? _instance;
        public static ContextAwareness Instance => _instance ??= new ContextAwareness();
        
        private readonly System.Timers.Timer _pollTimer;
        private string? _currentApp;
        private string? _currentWindowTitle;
        private DateTime _appStartTime;
        private readonly Dictionary<string, TimeSpan> _appUsageToday = new();
        private readonly List<ActivityEvent> _activityLog = new();
        
        public string? CurrentApp => _currentApp;
        public string? CurrentWindowTitle => _currentWindowTitle;
        
        public event Action<string, string>? OnAppChanged;
        public event Action<string>? OnContextSuggestion;
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        private ContextAwareness()
        {
            _pollTimer = new System.Timers.Timer(2000); // Poll every 2 seconds
            _pollTimer.Elapsed += PollActiveWindow;
            _pollTimer.Start();
        }
        
        private void PollActiveWindow(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var hwnd = GetForegroundWindow();
                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, 256);
                var windowTitle = title.ToString();
                
                GetWindowThreadProcessId(hwnd, out uint pid);
                string appName = "";
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    appName = proc.ProcessName;
                }
                catch { return; }
                
                // Track app change
                if (appName != _currentApp)
                {
                    // Log time spent in previous app
                    if (_currentApp != null)
                    {
                        var timeSpent = DateTime.Now - _appStartTime;
                        _appUsageToday[_currentApp] = _appUsageToday.GetValueOrDefault(_currentApp, TimeSpan.Zero) + timeSpent;
                        
                        _activityLog.Add(new ActivityEvent
                        {
                            App = _currentApp,
                            WindowTitle = _currentWindowTitle ?? "",
                            StartTime = _appStartTime,
                            EndTime = DateTime.Now,
                            Duration = timeSpent
                        });
                        
                        // Keep log manageable
                        while (_activityLog.Count > 100)
                            _activityLog.RemoveAt(0);
                    }
                    
                    var previousApp = _currentApp;
                    _currentApp = appName;
                    _currentWindowTitle = windowTitle;
                    _appStartTime = DateTime.Now;
                    
                    OnAppChanged?.Invoke(appName, windowTitle);
                    
                    // Check for context-based suggestions
                    CheckContextSuggestions(appName, windowTitle);
                }
                else if (windowTitle != _currentWindowTitle)
                {
                    _currentWindowTitle = windowTitle;
                }
            }
            catch { }
        }
        
        private void CheckContextSuggestions(string app, string title)
        {
            var lower = app.ToLower();
            var titleLower = title.ToLower();
            
            // Coding context
            if (lower == "code" || lower == "devenv" || lower == "rider64")
            {
                if (titleLower.Contains("error") || titleLower.Contains("exception"))
                    OnContextSuggestion?.Invoke("I see you might have an error. Need help debugging?");
            }
            
            // Browser context
            if (lower == "chrome" || lower == "firefox" || lower == "msedge")
            {
                if (titleLower.Contains("stackoverflow") || titleLower.Contains("github"))
                    OnContextSuggestion?.Invoke("Researching code? I can help explain or find solutions.");
            }
            
            // Long session warning
            var currentSessionTime = DateTime.Now - _appStartTime;
            var totalAppTime = _appUsageToday.GetValueOrDefault(app, TimeSpan.Zero) + currentSessionTime;
            if (totalAppTime.TotalHours > 2 && !FocusMode.Instance.IsActive)
            {
                OnContextSuggestion?.Invoke($"You've been using {app} for over 2 hours. Consider taking a break!");
            }
        }
        
        /// <summary>
        /// Get current context summary
        /// </summary>
        public string GetContextSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🎯 **Current Context:**\n");
            
            if (_currentApp != null)
            {
                var sessionTime = DateTime.Now - _appStartTime;
                sb.AppendLine($"Active app: **{_currentApp}** ({sessionTime.TotalMinutes:F0}m this session)");
                if (!string.IsNullOrEmpty(_currentWindowTitle))
                    sb.AppendLine($"Window: {_currentWindowTitle}");
            }
            
            sb.AppendLine($"\nTime: {DateTime.Now:h:mm tt} ({GetTimeOfDayContext()})");
            sb.AppendLine($"Day: {DateTime.Now:dddd}");
            
            if (FocusMode.Instance.IsActive)
                sb.AppendLine($"\n🎯 Focus mode active ({FocusMode.Instance.TimeRemaining.TotalMinutes:F0}m left)");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Get app usage summary for today
        /// </summary>
        public string GetUsageSummary()
        {
            // Include current app time
            var usage = new Dictionary<string, TimeSpan>(_appUsageToday);
            if (_currentApp != null)
            {
                var current = DateTime.Now - _appStartTime;
                usage[_currentApp] = usage.GetValueOrDefault(_currentApp, TimeSpan.Zero) + current;
            }
            
            if (!usage.Any())
                return "No usage data yet today.";
            
            var sb = new StringBuilder();
            sb.AppendLine("📊 **App Usage Today:**\n");
            
            var sorted = usage.OrderByDescending(kv => kv.Value).Take(10);
            foreach (var (app, time) in sorted)
            {
                var hours = time.TotalHours;
                var bar = new string('█', (int)Math.Min(hours * 2, 10));
                sb.AppendLine($"{app}: {time.TotalMinutes:F0}m {bar}");
            }
            
            var total = usage.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
            sb.AppendLine($"\n**Total: {total.TotalHours:F1} hours**");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Get activity log
        /// </summary>
        public string GetActivityLog(int count = 10)
        {
            var recent = _activityLog.TakeLast(count).Reverse().ToList();
            if (!recent.Any())
                return "No activity logged yet.";
            
            var sb = new StringBuilder();
            sb.AppendLine("📋 **Recent Activity:**\n");
            
            foreach (var activity in recent)
            {
                var time = activity.StartTime.ToString("h:mm tt");
                var duration = activity.Duration.TotalMinutes;
                sb.AppendLine($"• {time} - {activity.App} ({duration:F0}m)");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Get contextual greeting based on time and activity
        /// </summary>
        public string GetContextualGreeting()
        {
            var hour = DateTime.Now.Hour;
            var dayOfWeek = DateTime.Now.DayOfWeek;
            
            string greeting = hour switch
            {
                < 6 => "You're up early! ",
                < 12 => "Good morning! ",
                < 17 => "Good afternoon! ",
                < 21 => "Good evening! ",
                _ => "Working late? "
            };
            
            // Add context
            if (dayOfWeek == DayOfWeek.Friday && hour >= 17)
                greeting += "Happy Friday! 🎉";
            else if (dayOfWeek == DayOfWeek.Monday && hour < 12)
                greeting += "Let's start the week strong! 💪";
            else if (_currentApp != null)
            {
                var appContext = _currentApp.ToLower() switch
                {
                    "code" or "devenv" or "rider64" => "Coding session? I'm here to help!",
                    "chrome" or "firefox" or "msedge" => "Browsing? Let me know if you need anything.",
                    "spotify" => "Nice tunes! 🎵",
                    "discord" or "slack" or "teams" => "Chatting? I'll keep it brief.",
                    _ => "How can I help?"
                };
                greeting += appContext;
            }
            else
            {
                greeting += "How can I help?";
            }
            
            return greeting;
        }
        
        private string GetTimeOfDayContext()
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 6 => "late night",
                < 9 => "early morning",
                < 12 => "morning",
                < 14 => "midday",
                < 17 => "afternoon",
                < 20 => "evening",
                _ => "night"
            };
        }
        
        /// <summary>
        /// Get smart suggestions based on context
        /// </summary>
        public List<string> GetSmartSuggestions()
        {
            var suggestions = new List<string>();
            var hour = DateTime.Now.Hour;
            var dayOfWeek = DateTime.Now.DayOfWeek;
            
            // Time-based suggestions
            if (hour >= 9 && hour <= 10 && dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday)
                suggestions.Add("Check emails");
            
            if (hour >= 12 && hour <= 13)
                suggestions.Add("Take a lunch break");
            
            if (hour >= 17 && hour <= 18)
                suggestions.Add("Review today's tasks");
            
            // App-based suggestions
            if (_currentApp?.ToLower() == "code" || _currentApp?.ToLower() == "devenv")
            {
                suggestions.Add("Run tests");
                suggestions.Add("Commit changes");
            }
            
            // Usage-based suggestions
            var totalUsage = _appUsageToday.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
            if (totalUsage.TotalHours > 4 && !FocusMode.Instance.IsActive)
                suggestions.Add("Take a break - you've been working for a while!");
            
            return suggestions;
        }
    }
    
    internal class ActivityEvent
    {
        public string App { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
