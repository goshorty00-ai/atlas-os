using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Focus Mode - Block distractions and track productivity.
    /// "Start focus mode for 25 minutes" (Pomodoro)
    /// "Block social media"
    /// </summary>
    public class FocusMode
    {
        private static FocusMode? _instance;
        public static FocusMode Instance => _instance ??= new FocusMode();
        
        private bool _isActive;
        private DateTime _startTime;
        private DateTime _endTime;
        private System.Timers.Timer? _focusTimer;
        private System.Timers.Timer? _reminderTimer;
        private int _pomodoroCount;
        private readonly List<string> _blockedApps = new();
        private readonly List<string> _blockedSites = new();
        
        // Default distractions
        private static readonly string[] DefaultBlockedApps = 
        {
            "discord", "slack", "teams", "telegram", "whatsapp",
            "twitter", "facebook", "instagram", "tiktok", "reddit"
        };
        
        private static readonly string[] SocialMediaProcesses =
        {
            "Discord", "Slack", "Teams", "Telegram"
        };
        
        public bool IsActive => _isActive;
        public TimeSpan TimeRemaining => _isActive ? _endTime - DateTime.Now : TimeSpan.Zero;
        public int PomodoroCount => _pomodoroCount;
        
        public event Action<string>? OnFocusEvent;
        public event Action? OnFocusEnded;
        public event Action? OnBreakTime;
        
        private FocusMode() { }
        
        /// <summary>
        /// Start focus mode
        /// </summary>
        public string StartFocus(int minutes = 25, bool blockDistractions = true)
        {
            if (_isActive)
                return $"Focus mode already active! {TimeRemaining.TotalMinutes:F0} minutes remaining.";
            
            _isActive = true;
            _startTime = DateTime.Now;
            _endTime = DateTime.Now.AddMinutes(minutes);
            
            // Set up end timer
            _focusTimer?.Stop();
            _focusTimer = new System.Timers.Timer(minutes * 60 * 1000);
            _focusTimer.Elapsed += (s, e) => EndFocus(true);
            _focusTimer.AutoReset = false;
            _focusTimer.Start();
            
            // Set up reminder at halfway
            if (minutes > 10)
            {
                _reminderTimer?.Stop();
                _reminderTimer = new System.Timers.Timer((minutes / 2) * 60 * 1000);
                _reminderTimer.Elapsed += (s, e) =>
                {
                    OnFocusEvent?.Invoke($"⏱️ Halfway there! {TimeRemaining.TotalMinutes:F0} minutes left.");
                };
                _reminderTimer.AutoReset = false;
                _reminderTimer.Start();
            }
            
            if (blockDistractions)
            {
                _blockedApps.AddRange(DefaultBlockedApps);
                _ = MonitorDistractionsAsync();
            }
            
            OnFocusEvent?.Invoke($"🎯 Focus mode started for {minutes} minutes!");
            
            return $"🎯 Focus mode started!\n" +
                   $"Duration: {minutes} minutes\n" +
                   $"End time: {_endTime:h:mm tt}\n" +
                   (blockDistractions ? "Distractions will be blocked." : "");
        }
        
        /// <summary>
        /// Start Pomodoro session (25 min work, 5 min break)
        /// </summary>
        public string StartPomodoro()
        {
            var result = StartFocus(25, true);
            return $"🍅 Pomodoro #{_pomodoroCount + 1} started!\n{result}";
        }
        
        /// <summary>
        /// End focus mode
        /// </summary>
        public string EndFocus(bool completed = false)
        {
            if (!_isActive)
                return "Focus mode is not active.";
            
            _isActive = false;
            _focusTimer?.Stop();
            _reminderTimer?.Stop();
            _blockedApps.Clear();
            
            var duration = DateTime.Now - _startTime;
            
            if (completed)
            {
                _pomodoroCount++;
                OnFocusEnded?.Invoke();
                
                // Suggest break after 4 pomodoros
                if (_pomodoroCount % 4 == 0)
                {
                    OnBreakTime?.Invoke();
                    return $"🎉 Focus session complete! ({duration.TotalMinutes:F0} min)\n" +
                           $"You've done {_pomodoroCount} sessions today!\n" +
                           $"Time for a longer break (15-30 min).";
                }
                
                return $"🎉 Focus session complete! ({duration.TotalMinutes:F0} min)\n" +
                       $"Sessions today: {_pomodoroCount}\n" +
                       $"Take a 5 minute break, then say 'start pomodoro' for another session.";
            }
            
            return $"Focus mode ended early. You focused for {duration.TotalMinutes:F0} minutes.";
        }
        
        /// <summary>
        /// Get focus status
        /// </summary>
        public string GetStatus()
        {
            if (!_isActive)
                return $"Focus mode is off. Sessions today: {_pomodoroCount}\n" +
                       "Say 'start focus' or 'start pomodoro' to begin.";
            
            var remaining = TimeRemaining;
            var elapsed = DateTime.Now - _startTime;
            
            return $"🎯 **Focus Mode Active**\n" +
                   $"Time remaining: {remaining.TotalMinutes:F0} minutes\n" +
                   $"Time focused: {elapsed.TotalMinutes:F0} minutes\n" +
                   $"End time: {_endTime:h:mm tt}\n" +
                   $"Sessions today: {_pomodoroCount}";
        }
        
        /// <summary>
        /// Add app to block list
        /// </summary>
        public string BlockApp(string appName)
        {
            var lower = appName.ToLowerInvariant();
            if (!_blockedApps.Contains(lower))
            {
                _blockedApps.Add(lower);
                return $"✓ Added '{appName}' to block list";
            }
            return $"'{appName}' is already blocked";
        }
        
        /// <summary>
        /// Remove app from block list
        /// </summary>
        public string UnblockApp(string appName)
        {
            var lower = appName.ToLowerInvariant();
            if (_blockedApps.Remove(lower))
                return $"✓ Removed '{appName}' from block list";
            return $"'{appName}' was not blocked";
        }
        
        /// <summary>
        /// Extend focus time
        /// </summary>
        public string ExtendFocus(int minutes)
        {
            if (!_isActive)
                return "Focus mode is not active.";
            
            _endTime = _endTime.AddMinutes(minutes);
            
            // Reset timer
            _focusTimer?.Stop();
            var remaining = (_endTime - DateTime.Now).TotalMilliseconds;
            _focusTimer = new Timer(remaining);
            _focusTimer.Elapsed += (s, e) => EndFocus(true);
            _focusTimer.AutoReset = false;
            _focusTimer.Start();
            
            return $"✓ Extended focus by {minutes} minutes. New end time: {_endTime:h:mm tt}";
        }
        
        /// <summary>
        /// Monitor and close distracting apps
        /// </summary>
        private async Task MonitorDistractionsAsync()
        {
            while (_isActive)
            {
                try
                {
                    foreach (var appName in SocialMediaProcesses)
                    {
                        if (!_blockedApps.Any(b => appName.ToLower().Contains(b))) continue;
                        
                        var procs = Process.GetProcessesByName(appName);
                        foreach (var proc in procs)
                        {
                            try
                            {
                                // Minimize instead of kill (less aggressive)
                                if (proc.MainWindowHandle != IntPtr.Zero)
                                {
                                    ShowWindow(proc.MainWindowHandle, SW_MINIMIZE);
                                    OnFocusEvent?.Invoke($"🚫 Minimized {appName} - stay focused!");
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                
                await Task.Delay(5000); // Check every 5 seconds
            }
        }
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_MINIMIZE = 6;
        
        /// <summary>
        /// Parse focus command
        /// </summary>
        public (string Action, int Minutes)? ParseFocusCommand(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // "start focus" or "focus mode"
            if (lower == "start focus" || lower == "focus mode" || lower == "focus")
                return ("start", 25);
            
            // "start pomodoro"
            if (lower.Contains("pomodoro"))
                return ("pomodoro", 25);
            
            // "start focus for X minutes"
            var match = System.Text.RegularExpressions.Regex.Match(lower,
                @"(?:start\s+)?focus(?:\s+mode)?\s+(?:for\s+)?(\d+)\s*(?:min|minute|m)?");
            if (match.Success)
                return ("start", int.Parse(match.Groups[1].Value));
            
            // "end focus" or "stop focus"
            if (lower.Contains("end focus") || lower.Contains("stop focus") || lower == "stop focusing")
                return ("end", 0);
            
            // "extend focus by X"
            match = System.Text.RegularExpressions.Regex.Match(lower,
                @"extend\s+(?:focus\s+)?(?:by\s+)?(\d+)\s*(?:min|minute|m)?");
            if (match.Success)
                return ("extend", int.Parse(match.Groups[1].Value));
            
            // "focus status"
            if (lower.Contains("focus status") || lower == "am i focusing")
                return ("status", 0);
            
            return null;
        }
    }
}
