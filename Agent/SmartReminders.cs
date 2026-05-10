using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Smart Reminders - Natural language reminders with voice alerts.
    /// "Remind me to call mom in 30 minutes"
    /// "Remind me at 5pm to take a break"
    /// </summary>
    public class SmartReminders
    {
        private static SmartReminders? _instance;
        public static SmartReminders Instance => _instance ??= new SmartReminders();
        
        private readonly string _remindersFile;
        private List<Reminder> _reminders = new();
        private readonly Dictionary<string, System.Timers.Timer> _activeTimers = new();
        
        public event Action<Reminder>? OnReminderTriggered;
        
        private SmartReminders()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AtlasAI");
            Directory.CreateDirectory(appData);
            _remindersFile = Path.Combine(appData, "reminders.json");
            LoadReminders();
            SetupActiveReminders();
        }
        
        /// <summary>
        /// Create a reminder from natural language
        /// </summary>
        public string CreateReminder(string input)
        {
            var parsed = ParseReminderRequest(input);
            if (parsed == null)
                return "❌ Couldn't understand that reminder. Try: 'remind me to [task] in [time]' or 'remind me at [time] to [task]'";
            
            var (message, triggerTime) = parsed.Value;
            
            var reminder = new Reminder
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Message = message,
                TriggerTime = triggerTime,
                CreatedAt = DateTime.Now,
                IsActive = true
            };
            
            _reminders.Add(reminder);
            SaveReminders();
            SetupTimer(reminder);
            
            var timeUntil = triggerTime - DateTime.Now;
            string timeStr;
            if (timeUntil.TotalMinutes < 60)
                timeStr = $"{timeUntil.TotalMinutes:F0} minutes";
            else if (timeUntil.TotalHours < 24)
                timeStr = $"{timeUntil.TotalHours:F1} hours";
            else
                timeStr = $"{timeUntil.TotalDays:F1} days";
            
            return $"⏰ Reminder set! I'll remind you to '{message}' in {timeStr} ({triggerTime:g})";
        }
        
        /// <summary>
        /// Parse natural language reminder
        /// </summary>
        private (string Message, DateTime TriggerTime)? ParseReminderRequest(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Remove "remind me" prefix
            lower = Regex.Replace(lower, @"^remind\s+me\s+", "");
            input = Regex.Replace(input, @"^remind\s+me\s+", "", RegexOptions.IgnoreCase);
            
            // "to X in Y" pattern
            var match = Regex.Match(input, @"to\s+(.+?)\s+in\s+(\d+)\s*(minute|min|hour|hr|day|second|sec)s?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var message = match.Groups[1].Value.Trim();
                var amount = int.Parse(match.Groups[2].Value);
                var unit = match.Groups[3].Value.ToLower();
                
                var triggerTime = unit switch
                {
                    "second" or "sec" => DateTime.Now.AddSeconds(amount),
                    "minute" or "min" => DateTime.Now.AddMinutes(amount),
                    "hour" or "hr" => DateTime.Now.AddHours(amount),
                    "day" => DateTime.Now.AddDays(amount),
                    _ => DateTime.Now.AddMinutes(amount)
                };
                
                return (message, triggerTime);
            }
            
            // "at X to Y" pattern
            match = Regex.Match(input, @"at\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var hour = int.Parse(match.Groups[1].Value);
                var minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
                var ampm = match.Groups[3].Value.ToLower();
                var message = match.Groups[4].Value.Trim();
                
                if (ampm == "pm" && hour < 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;
                
                var now = DateTime.Now;
                var triggerTime = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
                if (triggerTime <= now)
                    triggerTime = triggerTime.AddDays(1);
                
                return (message, triggerTime);
            }
            
            // "to X at Y" pattern
            match = Regex.Match(input, @"to\s+(.+?)\s+at\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var message = match.Groups[1].Value.Trim();
                var hour = int.Parse(match.Groups[2].Value);
                var minute = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
                var ampm = match.Groups[4].Value.ToLower();
                
                if (ampm == "pm" && hour < 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;
                
                var now = DateTime.Now;
                var triggerTime = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
                if (triggerTime <= now)
                    triggerTime = triggerTime.AddDays(1);
                
                return (message, triggerTime);
            }
            
            // "in X to Y" pattern (e.g., "in 5 minutes to check email")
            match = Regex.Match(input, @"in\s+(\d+)\s*(minute|min|hour|hr|day|second|sec)s?\s+to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var amount = int.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value.ToLower();
                var message = match.Groups[3].Value.Trim();
                
                var triggerTime = unit switch
                {
                    "second" or "sec" => DateTime.Now.AddSeconds(amount),
                    "minute" or "min" => DateTime.Now.AddMinutes(amount),
                    "hour" or "hr" => DateTime.Now.AddHours(amount),
                    "day" => DateTime.Now.AddDays(amount),
                    _ => DateTime.Now.AddMinutes(amount)
                };
                
                return (message, triggerTime);
            }
            
            return null;
        }
        
        /// <summary>
        /// List all reminders
        /// </summary>
        public string ListReminders()
        {
            var active = _reminders.Where(r => r.IsActive && r.TriggerTime > DateTime.Now).ToList();
            
            if (!active.Any())
                return "No active reminders. Say 'remind me to...' to create one!";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("⏰ **Your Reminders:**\n");
            
            foreach (var r in active.OrderBy(r => r.TriggerTime))
            {
                var timeUntil = r.TriggerTime - DateTime.Now;
                string timeStr;
                if (timeUntil.TotalMinutes < 60)
                    timeStr = $"in {timeUntil.TotalMinutes:F0}m";
                else if (timeUntil.TotalHours < 24)
                    timeStr = $"in {timeUntil.TotalHours:F1}h";
                else
                    timeStr = r.TriggerTime.ToString("MMM d, h:mm tt");
                
                sb.AppendLine($"• {r.Message} ({timeStr})");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Cancel a reminder
        /// </summary>
        public string CancelReminder(string query)
        {
            var lower = query.ToLowerInvariant();
            var reminder = _reminders.FirstOrDefault(r => 
                r.IsActive && r.Message.ToLower().Contains(lower));
            
            if (reminder == null)
                return $"❌ No reminder found matching '{query}'";
            
            reminder.IsActive = false;
            if (_activeTimers.TryGetValue(reminder.Id, out var timer))
            {
                timer.Stop();
                timer.Dispose();
                _activeTimers.Remove(reminder.Id);
            }
            SaveReminders();
            
            return $"✓ Cancelled reminder: '{reminder.Message}'";
        }
        
        /// <summary>
        /// Snooze a triggered reminder
        /// </summary>
        public string SnoozeReminder(string id, int minutes = 10)
        {
            var reminder = _reminders.FirstOrDefault(r => r.Id == id);
            if (reminder == null)
                return "❌ Reminder not found";
            
            reminder.TriggerTime = DateTime.Now.AddMinutes(minutes);
            reminder.IsActive = true;
            SaveReminders();
            SetupTimer(reminder);
            
            return $"⏰ Snoozed for {minutes} minutes";
        }
        
        private void SetupActiveReminders()
        {
            foreach (var reminder in _reminders.Where(r => r.IsActive && r.TriggerTime > DateTime.Now))
            {
                SetupTimer(reminder);
            }
        }
        
        private void SetupTimer(Reminder reminder)
        {
            var delay = (reminder.TriggerTime - DateTime.Now).TotalMilliseconds;
            if (delay <= 0) return;
            
            // Cancel existing timer if any
            if (_activeTimers.TryGetValue(reminder.Id, out var existingTimer))
            {
                existingTimer.Stop();
                existingTimer.Dispose();
            }
            
            var timer = new System.Timers.Timer(delay);
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                reminder.IsActive = false;
                SaveReminders();
                OnReminderTriggered?.Invoke(reminder);
                _activeTimers.Remove(reminder.Id);
            };
            timer.AutoReset = false;
            timer.Start();
            
            _activeTimers[reminder.Id] = timer;
        }
        
        private void LoadReminders()
        {
            try
            {
                if (File.Exists(_remindersFile))
                {
                    var json = File.ReadAllText(_remindersFile);
                    _reminders = JsonSerializer.Deserialize<List<Reminder>>(json) ?? new();
                }
            }
            catch { _reminders = new(); }
        }
        
        private void SaveReminders()
        {
            try
            {
                var json = JsonSerializer.Serialize(_reminders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_remindersFile, json);
            }
            catch { }
        }
    }
    
    public class Reminder
    {
        public string Id { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime TriggerTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
