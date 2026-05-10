using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AtlasAI.Agent.Macros
{
    /// <summary>
    /// Event Viewer Highlights - Last critical/error events (read-only)
    /// </summary>
    public class EventViewerMacro : AgentMacroDefinition
    {
        public override string Id => "event-viewer";
        public override string Title => "Event Viewer Highlights";
        public override string Description => "Recent critical and error events";
        public override string Icon => "📜";
        public override MacroRiskLevel Risk => MacroRiskLevel.SafeReadOnly;
        public override string[] Keywords => new[] { "events", "errors", "logs", "event viewer", "critical", "warnings", "system log" };

        public override async Task<MacroResult> ExecuteAsync()
        {
            return await Task.Run(() =>
            {
                var result = new MacroResult { Success = true };
                var cards = new List<MacroResultCard>();

                try
                {
                    // System Events
                    var systemCard = new MacroResultCard
                    {
                        Title = "System Events (Last 24h)",
                        Icon = "🖥️",
                        StatusColor = "cyan"
                    };

                    var systemEvents = GetRecentEvents("System", 10);
                    var criticalCount = systemEvents.Count(e => e.Level == "Critical");
                    var errorCount = systemEvents.Count(e => e.Level == "Error");

                    systemCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Critical Events",
                        Value = criticalCount.ToString(),
                        Icon = "🔴",
                        ValueColor = criticalCount > 0 ? "red" : "green"
                    });

                    systemCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Error Events",
                        Value = errorCount.ToString(),
                        Icon = "🟠",
                        ValueColor = errorCount > 5 ? "yellow" : "green"
                    });

                    foreach (var evt in systemEvents.Take(5))
                    {
                        systemCard.Rows.Add(new MacroResultRow
                        {
                            Label = evt.Time.ToString("HH:mm"),
                            Value = TruncateMessage(evt.Message, 40),
                            Icon = GetLevelIcon(evt.Level),
                            ValueColor = GetLevelColor(evt.Level)
                        });
                    }

                    if (!systemEvents.Any())
                        systemCard.Rows.Add(new MacroResultRow { Label = "No recent events", Value = "", Icon = "✓" });

                    cards.Add(systemCard);

                    // Application Events
                    var appCard = new MacroResultCard
                    {
                        Title = "Application Events (Last 24h)",
                        Icon = "📦",
                        StatusColor = "violet"
                    };

                    var appEvents = GetRecentEvents("Application", 10);
                    var appCritical = appEvents.Count(e => e.Level == "Critical");
                    var appErrors = appEvents.Count(e => e.Level == "Error");

                    appCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Critical Events",
                        Value = appCritical.ToString(),
                        Icon = "🔴",
                        ValueColor = appCritical > 0 ? "red" : "green"
                    });

                    appCard.Rows.Add(new MacroResultRow
                    {
                        Label = "Error Events",
                        Value = appErrors.ToString(),
                        Icon = "🟠",
                        ValueColor = appErrors > 5 ? "yellow" : "green"
                    });

                    foreach (var evt in appEvents.Take(5))
                    {
                        appCard.Rows.Add(new MacroResultRow
                        {
                            Label = evt.Time.ToString("HH:mm"),
                            Value = TruncateMessage(evt.Message, 40),
                            Icon = GetLevelIcon(evt.Level),
                            ValueColor = GetLevelColor(evt.Level)
                        });
                    }

                    if (!appEvents.Any())
                        appCard.Rows.Add(new MacroResultRow { Label = "No recent events", Value = "", Icon = "✓" });

                    cards.Add(appCard);

                    // Summary
                    var totalCritical = criticalCount + appCritical;
                    var totalErrors = errorCount + appErrors;

                    result.Summary = totalCritical > 0
                        ? $"⚠ {totalCritical} critical, {totalErrors} errors in last 24h"
                        : totalErrors > 0
                            ? $"📋 {totalErrors} errors in last 24h"
                            : "✓ No critical issues in last 24h";

                    result.Cards = cards;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private List<EventInfo> GetRecentEvents(string logName, int maxCount)
        {
            var events = new List<EventInfo>();

            try
            {
                var log = new EventLog(logName);
                var cutoff = DateTime.Now.AddHours(-24);

                var entries = log.Entries.Cast<EventLogEntry>()
                    .Where(e => e.TimeGenerated >= cutoff &&
                               (e.EntryType == EventLogEntryType.Error ||
                                e.EntryType == EventLogEntryType.Warning))
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(maxCount);

                foreach (var entry in entries)
                {
                    events.Add(new EventInfo
                    {
                        Time = entry.TimeGenerated,
                        Source = entry.Source,
                        Level = entry.EntryType == EventLogEntryType.Error ? "Error" :
                               entry.EntryType == EventLogEntryType.Warning ? "Warning" : "Critical",
                        Message = entry.Message
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EventViewerMacro] Error reading {logName}: {ex.Message}");
            }

            return events;
        }

        private string GetLevelIcon(string level)
        {
            return level switch
            {
                "Critical" => "🔴",
                "Error" => "🟠",
                "Warning" => "🟡",
                _ => "🔵"
            };
        }

        private string GetLevelColor(string level)
        {
            return level switch
            {
                "Critical" => "red",
                "Error" => "yellow",
                _ => null
            } ?? "";
        }

        private string TruncateMessage(string message, int maxLen)
        {
            if (string.IsNullOrEmpty(message)) return "";
            
            // Get first line only
            var firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? message;

            if (firstLine.Length <= maxLen) return firstLine;
            return firstLine.Substring(0, maxLen - 3) + "...";
        }

        private class EventInfo
        {
            public DateTime Time { get; set; }
            public string Source { get; set; } = "";
            public string Level { get; set; } = "";
            public string Message { get; set; } = "";
        }
    }
}
