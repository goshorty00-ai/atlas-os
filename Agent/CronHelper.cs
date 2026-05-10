using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Cron Helper - Explain and generate cron expressions.
    /// </summary>
    public static class CronHelper
    {
        /// <summary>
        /// Handle cron commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("cron"))
                return Task.FromResult<string?>(null);
            
            // Explain cron expression
            var cronMatch = Regex.Match(input, @"[""']?([\d\*\-,/]+\s+[\d\*\-,/]+\s+[\d\*\-,/]+\s+[\d\*\-,/]+\s+[\d\*\-,/]+)[""']?");
            if (cronMatch.Success)
            {
                return Task.FromResult<string?>(ExplainCron(cronMatch.Groups[1].Value));
            }
            
            // Generate cron for common schedules
            if (lower.Contains("every minute"))
                return Task.FromResult<string?>(GetCronInfo("Every minute", "* * * * *"));
            if (lower.Contains("every hour"))
                return Task.FromResult<string?>(GetCronInfo("Every hour", "0 * * * *"));
            if (lower.Contains("every day") || lower.Contains("daily"))
                return Task.FromResult<string?>(GetCronInfo("Every day at midnight", "0 0 * * *"));
            if (lower.Contains("every week") || lower.Contains("weekly"))
                return Task.FromResult<string?>(GetCronInfo("Every Sunday at midnight", "0 0 * * 0"));
            if (lower.Contains("every month") || lower.Contains("monthly"))
                return Task.FromResult<string?>(GetCronInfo("First day of month at midnight", "0 0 1 * *"));
            if (lower.Contains("every year") || lower.Contains("yearly") || lower.Contains("annually"))
                return Task.FromResult<string?>(GetCronInfo("January 1st at midnight", "0 0 1 1 *"));
            
            // Specific times
            var timeMatch = Regex.Match(lower, @"at\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?");
            if (timeMatch.Success)
            {
                var hour = int.Parse(timeMatch.Groups[1].Value);
                var minute = timeMatch.Groups[2].Success ? int.Parse(timeMatch.Groups[2].Value) : 0;
                var ampm = timeMatch.Groups[3].Value;
                
                if (ampm == "pm" && hour < 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;
                
                var cron = $"{minute} {hour} * * *";
                return Task.FromResult<string?>(GetCronInfo($"Every day at {hour:D2}:{minute:D2}", cron));
            }
            
            // Weekday patterns
            if (lower.Contains("weekday") || lower.Contains("monday") && lower.Contains("friday"))
                return Task.FromResult<string?>(GetCronInfo("Weekdays at 9 AM", "0 9 * * 1-5"));
            if (lower.Contains("weekend"))
                return Task.FromResult<string?>(GetCronInfo("Weekends at 10 AM", "0 10 * * 0,6"));
            
            // Show help
            return Task.FromResult<string?>(GetCronHelp());
        }
        
        private static string ExplainCron(string cron)
        {
            var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
                return "❌ Invalid cron expression. Expected 5 fields: minute hour day month weekday";
            
            var sb = new StringBuilder();
            sb.AppendLine($"⏰ **Cron Expression:** `{cron}`\n");
            
            var fields = new[] { "Minute", "Hour", "Day", "Month", "Weekday" };
            var ranges = new[] { "0-59", "0-23", "1-31", "1-12", "0-6 (Sun-Sat)" };
            
            for (int i = 0; i < 5; i++)
            {
                var explanation = ExplainField(parts[i], fields[i], i);
                sb.AppendLine($"**{fields[i]}** ({parts[i]}): {explanation}");
            }
            
            sb.AppendLine();
            sb.AppendLine($"**Meaning:** {GetHumanReadable(parts)}");
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(cron));
            sb.AppendLine("\n✓ Copied to clipboard!");
            
            return sb.ToString();
        }
        
        private static string ExplainField(string field, string name, int index)
        {
            if (field == "*") return "Every " + name.ToLower();
            if (field.Contains("/"))
            {
                var parts = field.Split('/');
                return $"Every {parts[1]} {name.ToLower()}(s)";
            }
            if (field.Contains("-"))
            {
                var parts = field.Split('-');
                return $"From {parts[0]} to {parts[1]}";
            }
            if (field.Contains(","))
            {
                return $"At {field.Replace(",", ", ")}";
            }
            
            // Weekday names
            if (index == 4)
            {
                var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
                if (int.TryParse(field, out var day) && day >= 0 && day <= 6)
                    return days[day];
            }
            
            return $"At {field}";
        }
        
        private static string GetHumanReadable(string[] parts)
        {
            var minute = parts[0] == "*" ? "every minute" : $"at minute {parts[0]}";
            var hour = parts[1] == "*" ? "every hour" : $"at {parts[1]}:00";
            var day = parts[2] == "*" ? "" : $"on day {parts[2]}";
            var month = parts[3] == "*" ? "" : $"in month {parts[3]}";
            var weekday = parts[4] == "*" ? "" : GetWeekdayName(parts[4]);
            
            if (parts[0] != "*" && parts[1] != "*")
            {
                var time = $"{int.Parse(parts[1]):D2}:{int.Parse(parts[0]):D2}";
                if (parts[2] == "*" && parts[3] == "*" && parts[4] == "*")
                    return $"Every day at {time}";
                if (parts[4] != "*")
                    return $"{weekday} at {time}";
            }
            
            return $"Runs {minute}, {hour} {day} {month} {weekday}".Trim();
        }
        
        private static string GetWeekdayName(string field)
        {
            var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            
            if (field == "1-5") return "Monday through Friday";
            if (field == "0,6") return "Saturday and Sunday";
            if (int.TryParse(field, out var day) && day >= 0 && day <= 6)
                return $"on {days[day]}";
            
            return $"on weekday {field}";
        }
        
        private static string GetCronInfo(string description, string cron)
        {
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(cron));
            
            return $"⏰ **{description}:**\n```\n{cron}\n```\n✓ Copied to clipboard!";
        }
        
        private static string GetCronHelp()
        {
            return "⏰ **Cron Expression Helper:**\n\n" +
                   "**Format:** `minute hour day month weekday`\n\n" +
                   "**Quick Generate:**\n" +
                   "• `cron every minute`\n" +
                   "• `cron every hour`\n" +
                   "• `cron daily` / `cron every day`\n" +
                   "• `cron weekly`\n" +
                   "• `cron at 9:30 am`\n" +
                   "• `cron weekdays`\n\n" +
                   "**Explain:**\n" +
                   "• `cron \"0 9 * * 1-5\"` - Explain expression\n\n" +
                   "**Special Characters:**\n" +
                   "• `*` = every\n" +
                   "• `,` = multiple values\n" +
                   "• `-` = range\n" +
                   "• `/` = step";
        }
    }
}
