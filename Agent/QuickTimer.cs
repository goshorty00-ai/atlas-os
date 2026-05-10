using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick Timer - Simple countdown timers and stopwatch.
    /// "Set timer for 5 minutes" "Start stopwatch" "Countdown from 10"
    /// </summary>
    public class QuickTimer
    {
        private static QuickTimer? _instance;
        public static QuickTimer Instance => _instance ??= new QuickTimer();
        
        private readonly Dictionary<string, TimerInstance> _timers = new();
        private StopwatchInstance? _stopwatch;
        
        public event Action<string, string>? OnTimerComplete;
        public event Action<string>? OnTimerTick;
        
        private QuickTimer() { }
        
        /// <summary>
        /// Set a countdown timer
        /// </summary>
        public string SetTimer(int seconds, string? name = null)
        {
            var id = name?.ToLower() ?? $"timer_{_timers.Count + 1}";
            
            // Cancel existing timer with same name
            if (_timers.TryGetValue(id, out var existing))
            {
                existing.Timer.Stop();
                existing.Timer.Dispose();
            }
            
            var endTime = DateTime.Now.AddSeconds(seconds);
            var timer = new System.Timers.Timer(1000);
            var instance = new TimerInstance
            {
                Id = id,
                Name = name ?? $"Timer {_timers.Count + 1}",
                EndTime = endTime,
                TotalSeconds = seconds,
                Timer = timer
            };
            
            timer.Elapsed += (s, e) =>
            {
                var remaining = (instance.EndTime - DateTime.Now).TotalSeconds;
                if (remaining <= 0)
                {
                    timer.Stop();
                    OnTimerComplete?.Invoke(instance.Id, instance.Name);
                    _timers.Remove(instance.Id);
                }
                else if (remaining <= 10 || (remaining <= 60 && remaining % 10 == 0))
                {
                    OnTimerTick?.Invoke($"⏱️ {instance.Name}: {remaining:F0}s remaining");
                }
            };
            
            timer.Start();
            _timers[id] = instance;
            
            var timeStr = FormatDuration(TimeSpan.FromSeconds(seconds));
            return $"⏱️ Timer set for {timeStr}!\nSay 'timer status' to check or 'cancel timer' to stop.";
        }
        
        /// <summary>
        /// Cancel a timer
        /// </summary>
        public string CancelTimer(string? name = null)
        {
            if (name == null)
            {
                // Cancel all timers
                foreach (var t in _timers.Values)
                {
                    t.Timer.Stop();
                    t.Timer.Dispose();
                }
                var count = _timers.Count;
                _timers.Clear();
                return count > 0 ? $"✓ Cancelled {count} timer(s)" : "No active timers";
            }
            
            var id = name.ToLower();
            if (_timers.TryGetValue(id, out var timer))
            {
                timer.Timer.Stop();
                timer.Timer.Dispose();
                _timers.Remove(id);
                return $"✓ Cancelled timer '{timer.Name}'";
            }
            
            return $"❌ Timer '{name}' not found";
        }
        
        /// <summary>
        /// Get timer status
        /// </summary>
        public string GetTimerStatus()
        {
            if (!_timers.Any() && _stopwatch == null)
                return "No active timers or stopwatch.";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("⏱️ **Active Timers:**\n");
            
            foreach (var t in _timers.Values)
            {
                var remaining = t.EndTime - DateTime.Now;
                if (remaining.TotalSeconds > 0)
                {
                    sb.AppendLine($"• {t.Name}: {FormatDuration(remaining)} remaining");
                }
            }
            
            if (_stopwatch != null)
            {
                var elapsed = DateTime.Now - _stopwatch.StartTime;
                if (_stopwatch.IsPaused)
                    elapsed = _stopwatch.PausedAt - _stopwatch.StartTime;
                sb.AppendLine($"\n⏱️ Stopwatch: {FormatDuration(elapsed)} {(_stopwatch.IsPaused ? "(paused)" : "")}");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Start stopwatch
        /// </summary>
        public string StartStopwatch()
        {
            if (_stopwatch != null && !_stopwatch.IsPaused)
                return $"Stopwatch already running! Elapsed: {FormatDuration(DateTime.Now - _stopwatch.StartTime)}";
            
            if (_stopwatch?.IsPaused == true)
            {
                // Resume
                var pausedDuration = DateTime.Now - _stopwatch.PausedAt;
                _stopwatch.StartTime = _stopwatch.StartTime.Add(pausedDuration);
                _stopwatch.IsPaused = false;
                return "▶️ Stopwatch resumed!";
            }
            
            _stopwatch = new StopwatchInstance
            {
                StartTime = DateTime.Now,
                IsPaused = false
            };
            
            return "▶️ Stopwatch started!";
        }
        
        /// <summary>
        /// Stop/pause stopwatch
        /// </summary>
        public string StopStopwatch()
        {
            if (_stopwatch == null)
                return "No stopwatch running.";
            
            if (_stopwatch.IsPaused)
                return $"Stopwatch already paused at {FormatDuration(_stopwatch.PausedAt - _stopwatch.StartTime)}";
            
            _stopwatch.IsPaused = true;
            _stopwatch.PausedAt = DateTime.Now;
            var elapsed = _stopwatch.PausedAt - _stopwatch.StartTime;
            
            return $"⏸️ Stopwatch paused at {FormatDuration(elapsed)}";
        }
        
        /// <summary>
        /// Reset stopwatch
        /// </summary>
        public string ResetStopwatch()
        {
            if (_stopwatch == null)
                return "No stopwatch to reset.";
            
            var elapsed = (_stopwatch.IsPaused ? _stopwatch.PausedAt : DateTime.Now) - _stopwatch.StartTime;
            _stopwatch = null;
            
            return $"🔄 Stopwatch reset. Final time: {FormatDuration(elapsed)}";
        }
        
        /// <summary>
        /// Lap time
        /// </summary>
        public string LapStopwatch()
        {
            if (_stopwatch == null)
                return "No stopwatch running.";
            
            var elapsed = DateTime.Now - _stopwatch.StartTime;
            _stopwatch.Laps.Add(elapsed);
            
            var lapTime = _stopwatch.Laps.Count > 1 
                ? elapsed - _stopwatch.Laps[_stopwatch.Laps.Count - 2]
                : elapsed;
            
            return $"🏁 Lap {_stopwatch.Laps.Count}: {FormatDuration(lapTime)} (Total: {FormatDuration(elapsed)})";
        }
        
        /// <summary>
        /// Parse timer command
        /// </summary>
        public (string Action, int Seconds, string? Name)? ParseTimerCommand(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // "set timer for X minutes/seconds"
            var match = Regex.Match(lower, @"(?:set\s+)?timer\s+(?:for\s+)?(\d+)\s*(second|sec|minute|min|hour|hr)s?(?:\s+(?:called|named)\s+(.+))?");
            if (match.Success)
            {
                var amount = int.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;
                var name = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
                
                var seconds = unit switch
                {
                    "second" or "sec" => amount,
                    "minute" or "min" => amount * 60,
                    "hour" or "hr" => amount * 3600,
                    _ => amount * 60
                };
                
                return ("set", seconds, name);
            }
            
            // "X minute timer"
            match = Regex.Match(lower, @"(\d+)\s*(second|sec|minute|min|hour|hr)s?\s+timer");
            if (match.Success)
            {
                var amount = int.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;
                
                var seconds = unit switch
                {
                    "second" or "sec" => amount,
                    "minute" or "min" => amount * 60,
                    "hour" or "hr" => amount * 3600,
                    _ => amount * 60
                };
                
                return ("set", seconds, null);
            }
            
            // Timer control
            if (lower.Contains("cancel timer") || lower.Contains("stop timer"))
                return ("cancel", 0, null);
            if (lower.Contains("timer status") || lower == "timers")
                return ("status", 0, null);
            
            // Stopwatch
            if (lower.Contains("start stopwatch") || lower == "stopwatch")
                return ("start_stopwatch", 0, null);
            if (lower.Contains("stop stopwatch") || lower.Contains("pause stopwatch"))
                return ("stop_stopwatch", 0, null);
            if (lower.Contains("reset stopwatch"))
                return ("reset_stopwatch", 0, null);
            if (lower.Contains("lap") && lower.Contains("stopwatch") || lower == "lap")
                return ("lap", 0, null);
            
            return null;
        }
        
        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.Seconds}s";
        }
    }
    
    internal class TimerInstance
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime EndTime { get; set; }
        public int TotalSeconds { get; set; }
        public System.Timers.Timer Timer { get; set; } = null!;
    }
    
    internal class StopwatchInstance
    {
        public DateTime StartTime { get; set; }
        public bool IsPaused { get; set; }
        public DateTime PausedAt { get; set; }
        public List<TimeSpan> Laps { get; set; } = new();
    }
}
