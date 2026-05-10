using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Daily Briefing - Morning summary of what's happening.
    /// Weather, calendar, reminders, news headlines.
    /// </summary>
    public class DailyBriefing
    {
        private static DailyBriefing? _instance;
        public static DailyBriefing Instance => _instance ??= new DailyBriefing();
        
        private DateTime _lastBriefing;
        
        private DailyBriefing() { }
        
        /// <summary>
        /// Generate morning briefing
        /// </summary>
        public async Task<string> GetMorningBriefingAsync()
        {
            var sb = new StringBuilder();
            var now = DateTime.Now;
            
            // Greeting
            sb.AppendLine($"☀️ **Good {GetTimeOfDay()}, {GetUserName()}!**");
            sb.AppendLine($"It's {now:dddd, MMMM d} at {now:h:mm tt}\n");
            
            // Weather (placeholder - would need API)
            sb.AppendLine("🌤️ **Weather:** Checking...");
            sb.AppendLine();
            
            // Today's reminders
            var reminders = SmartReminders.Instance.ListReminders();
            if (!reminders.Contains("No active reminders"))
            {
                sb.AppendLine("⏰ **Today's Reminders:**");
                sb.AppendLine(reminders);
            }
            
            // Focus stats from yesterday
            var focusCount = FocusMode.Instance.PomodoroCount;
            if (focusCount > 0)
            {
                sb.AppendLine($"🎯 **Yesterday:** {focusCount} focus sessions completed");
            }
            
            // Quick suggestions
            sb.AppendLine("\n💡 **Suggestions:**");
            var suggestions = GetMorningSuggestions();
            foreach (var s in suggestions)
                sb.AppendLine($"  • {s}");
            
            // Quote of the day
            sb.AppendLine($"\n✨ \"{GetMotivationalQuote()}\"");
            
            _lastBriefing = now;
            return sb.ToString();
        }
        
        /// <summary>
        /// Generate evening summary
        /// </summary>
        public async Task<string> GetEveningSummaryAsync()
        {
            var sb = new StringBuilder();
            var now = DateTime.Now;
            
            sb.AppendLine($"🌙 **Evening Summary**");
            sb.AppendLine($"{now:dddd, MMMM d}\n");
            
            // App usage
            var usage = ContextAwareness.Instance.GetUsageSummary();
            sb.AppendLine(usage);
            
            // Focus sessions
            var focusCount = FocusMode.Instance.PomodoroCount;
            if (focusCount > 0)
            {
                sb.AppendLine($"\n🎯 **Focus Sessions:** {focusCount} completed today");
            }
            
            // Pending reminders
            var reminders = SmartReminders.Instance.ListReminders();
            if (!reminders.Contains("No active reminders"))
            {
                sb.AppendLine("\n⏰ **Upcoming Reminders:**");
                sb.AppendLine(reminders);
            }
            
            // Tomorrow preview
            sb.AppendLine($"\n📅 **Tomorrow:** {now.AddDays(1):dddd, MMMM d}");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Quick status check
        /// </summary>
        public string GetQuickStatus()
        {
            var sb = new StringBuilder();
            var now = DateTime.Now;
            
            sb.AppendLine($"📊 **Quick Status** ({now:h:mm tt})");
            
            // Current activity
            var currentApp = ContextAwareness.Instance.CurrentApp;
            if (currentApp != null)
                sb.AppendLine($"Currently: {currentApp}");
            
            // Focus mode
            if (FocusMode.Instance.IsActive)
                sb.AppendLine($"🎯 Focus: {FocusMode.Instance.TimeRemaining.TotalMinutes:F0}m left");
            
            // Pending reminders count
            var reminderText = SmartReminders.Instance.ListReminders();
            if (!reminderText.Contains("No active"))
            {
                var count = reminderText.Split('\n').Count(l => l.StartsWith("•"));
                sb.AppendLine($"⏰ Reminders: {count} pending");
            }
            
            return sb.ToString();
        }
        
        private string GetTimeOfDay()
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 12 => "morning",
                < 17 => "afternoon",
                < 21 => "evening",
                _ => "night"
            };
        }
        
        private string GetUserName()
        {
            return ConversationMemory.Instance.GetName("user") ?? Environment.UserName;
        }
        
        private List<string> GetMorningSuggestions()
        {
            var suggestions = new List<string>();
            var dayOfWeek = DateTime.Now.DayOfWeek;
            var hour = DateTime.Now.Hour;
            
            if (dayOfWeek == DayOfWeek.Monday)
                suggestions.Add("Review your week's goals");
            
            if (hour < 10)
                suggestions.Add("Check emails and messages");
            
            suggestions.Add("Start a focus session: 'start pomodoro'");
            
            if (dayOfWeek == DayOfWeek.Friday)
                suggestions.Add("Plan for next week");
            
            return suggestions.Take(3).ToList();
        }
        
        private string GetMotivationalQuote()
        {
            var quotes = new[]
            {
                "The secret of getting ahead is getting started. - Mark Twain",
                "Focus on being productive instead of busy. - Tim Ferriss",
                "The way to get started is to quit talking and begin doing. - Walt Disney",
                "Don't watch the clock; do what it does. Keep going. - Sam Levenson",
                "Success is not final, failure is not fatal: it is the courage to continue that counts. - Winston Churchill",
                "The only way to do great work is to love what you do. - Steve Jobs",
                "Believe you can and you're halfway there. - Theodore Roosevelt",
                "It does not matter how slowly you go as long as you do not stop. - Confucius",
                "Quality is not an act, it is a habit. - Aristotle",
                "The future depends on what you do today. - Mahatma Gandhi"
            };
            
            var index = DateTime.Now.DayOfYear % quotes.Length;
            return quotes[index];
        }
    }
}
