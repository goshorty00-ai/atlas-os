using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Epoch/Unix Timestamp Converter - Convert between timestamps and dates.
    /// </summary>
    public static class EpochConverter
    {
        /// <summary>
        /// Handle epoch/timestamp commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Current timestamp
            if (lower.Contains("current") && (lower.Contains("timestamp") || lower.Contains("epoch") || lower.Contains("unix")))
            {
                return Task.FromResult<string?>(GetCurrentTimestamp());
            }
            
            if (lower == "timestamp" || lower == "epoch" || lower == "unix time")
            {
                return Task.FromResult<string?>(GetCurrentTimestamp());
            }
            
            // Convert timestamp to date
            var timestampMatch = Regex.Match(input, @"\b(\d{10,13})\b");
            if (timestampMatch.Success && (lower.Contains("convert") || lower.Contains("timestamp") || lower.Contains("epoch") || lower.Contains("date")))
            {
                var timestamp = long.Parse(timestampMatch.Groups[1].Value);
                return Task.FromResult<string?>(TimestampToDate(timestamp));
            }
            
            // Convert date to timestamp
            if (lower.Contains("to timestamp") || lower.Contains("to epoch") || lower.Contains("to unix"))
            {
                var dateMatch = Regex.Match(input, @"(\d{4}[-/]\d{1,2}[-/]\d{1,2}(?:\s+\d{1,2}:\d{2}(?::\d{2})?)?)", RegexOptions.IgnoreCase);
                if (dateMatch.Success)
                {
                    return Task.FromResult<string?>(DateToTimestamp(dateMatch.Groups[1].Value));
                }
                
                // Try clipboard
                string? clipText = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        clipText = Clipboard.GetText()?.Trim();
                });
                
                if (!string.IsNullOrEmpty(clipText))
                    return Task.FromResult<string?>(DateToTimestamp(clipText));
                
                return Task.FromResult<string?>("Specify a date: `2024-01-15 to timestamp`");
            }
            
            return Task.FromResult<string?>(null);
        }
        
        private static string GetCurrentTimestamp()
        {
            var now = DateTimeOffset.UtcNow;
            var seconds = now.ToUnixTimeSeconds();
            var millis = now.ToUnixTimeMilliseconds();
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(seconds.ToString()));
            
            return $"⏱️ **Current Timestamp:**\n\n" +
                   $"**Seconds:** `{seconds}`\n" +
                   $"**Milliseconds:** `{millis}`\n\n" +
                   $"**UTC:** {now:yyyy-MM-dd HH:mm:ss} UTC\n" +
                   $"**Local:** {now.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n\n" +
                   $"✓ Seconds timestamp copied to clipboard!";
        }
        
        private static string TimestampToDate(long timestamp)
        {
            try
            {
                DateTimeOffset dto;
                
                // Detect if milliseconds (13 digits) or seconds (10 digits)
                if (timestamp > 9999999999)
                {
                    dto = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
                }
                else
                {
                    dto = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                }
                
                var formatted = dto.ToString("yyyy-MM-dd HH:mm:ss");
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(formatted));
                
                var relative = GetRelativeTime(dto);
                
                return $"📅 **Timestamp Converted:**\n\n" +
                       $"**Input:** `{timestamp}`\n\n" +
                       $"**UTC:** {dto:yyyy-MM-dd HH:mm:ss} UTC\n" +
                       $"**Local:** {dto.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n" +
                       $"**ISO 8601:** {dto:O}\n\n" +
                       $"**Relative:** {relative}\n\n" +
                       $"✓ Date copied to clipboard!";
            }
            catch
            {
                return $"❌ Invalid timestamp: {timestamp}";
            }
        }
        
        private static string DateToTimestamp(string dateStr)
        {
            try
            {
                if (DateTime.TryParse(dateStr, out var date))
                {
                    var dto = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
                    var seconds = dto.ToUnixTimeSeconds();
                    var millis = dto.ToUnixTimeMilliseconds();
                    
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(seconds.ToString()));
                    
                    return $"⏱️ **Date to Timestamp:**\n\n" +
                           $"**Input:** {dateStr}\n\n" +
                           $"**Seconds:** `{seconds}`\n" +
                           $"**Milliseconds:** `{millis}`\n\n" +
                           $"✓ Seconds timestamp copied to clipboard!";
                }
                
                return $"❌ Couldn't parse date: {dateStr}\n\nTry format: `2024-01-15` or `2024-01-15 14:30:00`";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        private static string GetRelativeTime(DateTimeOffset dto)
        {
            var diff = DateTimeOffset.UtcNow - dto;
            
            if (diff.TotalSeconds < 0)
            {
                diff = -diff;
                if (diff.TotalDays > 365) return $"in {(int)(diff.TotalDays / 365)} year(s)";
                if (diff.TotalDays > 30) return $"in {(int)(diff.TotalDays / 30)} month(s)";
                if (diff.TotalDays > 1) return $"in {(int)diff.TotalDays} day(s)";
                if (diff.TotalHours > 1) return $"in {(int)diff.TotalHours} hour(s)";
                if (diff.TotalMinutes > 1) return $"in {(int)diff.TotalMinutes} minute(s)";
                return "in a few seconds";
            }
            
            if (diff.TotalDays > 365) return $"{(int)(diff.TotalDays / 365)} year(s) ago";
            if (diff.TotalDays > 30) return $"{(int)(diff.TotalDays / 30)} month(s) ago";
            if (diff.TotalDays > 1) return $"{(int)diff.TotalDays} day(s) ago";
            if (diff.TotalHours > 1) return $"{(int)diff.TotalHours} hour(s) ago";
            if (diff.TotalMinutes > 1) return $"{(int)diff.TotalMinutes} minute(s) ago";
            return "just now";
        }
    }
}
