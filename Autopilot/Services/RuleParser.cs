using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using AtlasAI.Autopilot.Models;

namespace AtlasAI.Autopilot.Services
{
    /// <summary>
    /// Parses plain English rules into executable conditions and actions
    /// </summary>
    public class RuleParser
    {
        private static readonly Dictionary<string, TriggerType> TriggerKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "when i'm away", TriggerType.OnAwayMode },
            { "when im away", TriggerType.OnAwayMode },
            { "while away", TriggerType.OnAwayMode },
            { "when idle", TriggerType.OnIdle },
            { "after idle", TriggerType.OnIdle },
            { "every day", TriggerType.Scheduled },
            { "daily", TriggerType.Scheduled },
            { "every morning", TriggerType.Scheduled },
            { "every evening", TriggerType.Scheduled },
            { "every week", TriggerType.Scheduled },
            { "weekly", TriggerType.Scheduled },
            { "when i open", TriggerType.OnAppOpen },
            { "when i start", TriggerType.OnAppOpen },
            { "if", TriggerType.OnCondition },
            { "whenever", TriggerType.OnCondition }
        };
        
        private static readonly Dictionary<string, string> ActionKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "clean up", "cleanup" },
            { "organize", "organize" },
            { "delete", "delete" },
            { "move", "move" },
            { "backup", "backup" },
            { "empty", "empty" },
            { "clear", "clear" },
            { "close", "close" },
            { "open", "open" },
            { "run", "execute" },
            { "start", "start" },
            { "stop", "stop" },
            { "remind", "remind" },
            { "notify", "notify" },
            { "check", "check" },
            { "scan", "scan" },
            { "update", "update" }
        };
        
        /// <summary>
        /// Parse a plain English rule into structured components
        /// </summary>
        public AutopilotRule ParseRule(string plainEnglishRule)
        {
            var rule = new AutopilotRule
            {
                PlainEnglishRule = plainEnglishRule,
                Name = GenerateRuleName(plainEnglishRule)
            };
            
            var lowerRule = plainEnglishRule.ToLowerInvariant();
            
            // Parse trigger
            rule.Trigger = ParseTrigger(lowerRule);
            rule.ParsedCondition = ExtractCondition(lowerRule);
            
            // Parse action
            rule.ParsedAction = ExtractAction(lowerRule);
            rule.AllowedActions = ExtractAllowedActions(lowerRule);
            
            // Determine autonomy level based on action risk
            rule.AutonomyLevel = DetermineAutonomyLevel(rule.ParsedAction);
            rule.RequiresConfirmation = rule.AutonomyLevel != AutonomyLevel.AutoExecute;
            
            Debug.WriteLine($"[RuleParser] Parsed: '{plainEnglishRule}' -> Trigger: {rule.Trigger.Type}, Action: {rule.ParsedAction}");
            
            return rule;
        }
        
        /// <summary>
        /// Check if a condition matches the current context
        /// </summary>
        public bool MatchesCondition(string condition, ActionContext context)
        {
            if (string.IsNullOrEmpty(condition)) return true;
            
            var lowerCondition = condition.ToLowerInvariant();
            
            // Time-based conditions
            if (lowerCondition.Contains("morning") && context.TimeOfDay.Hours >= 5 && context.TimeOfDay.Hours < 12)
                return true;
            if (lowerCondition.Contains("afternoon") && context.TimeOfDay.Hours >= 12 && context.TimeOfDay.Hours < 17)
                return true;
            if (lowerCondition.Contains("evening") && context.TimeOfDay.Hours >= 17 && context.TimeOfDay.Hours < 21)
                return true;
            if (lowerCondition.Contains("night") && (context.TimeOfDay.Hours >= 21 || context.TimeOfDay.Hours < 5))
                return true;
            
            // Day-based conditions
            if (lowerCondition.Contains("weekday") && context.DayOfWeek != DayOfWeek.Saturday && context.DayOfWeek != DayOfWeek.Sunday)
                return true;
            if (lowerCondition.Contains("weekend") && (context.DayOfWeek == DayOfWeek.Saturday || context.DayOfWeek == DayOfWeek.Sunday))
                return true;
            
            // Idle conditions
            if (lowerCondition.Contains("idle") && context.IdleMinutes > 5)
                return true;
            
            // Away mode
            if (lowerCondition.Contains("away") && context.IsAwayMode)
                return true;
            
            // App context
            if (!string.IsNullOrEmpty(context.ActiveApp) && lowerCondition.Contains(context.ActiveApp.ToLowerInvariant()))
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Validate a rule for safety
        /// </summary>
        public (bool IsValid, string? Error) ValidateRule(AutopilotRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.PlainEnglishRule))
                return (false, "Rule cannot be empty");
            
            if (string.IsNullOrWhiteSpace(rule.ParsedAction))
                return (false, "Could not parse action from rule");
            
            // Check for dangerous patterns
            var dangerous = new[] { "format", "wipe", "destroy", "registry", "system32" };
            if (dangerous.Any(d => rule.PlainEnglishRule.Contains(d, StringComparison.OrdinalIgnoreCase)))
                return (false, "Rule contains potentially dangerous actions");
            
            return (true, null);
        }
        
        private RuleTrigger ParseTrigger(string rule)
        {
            var trigger = new RuleTrigger();
            
            foreach (var (keyword, type) in TriggerKeywords)
            {
                if (rule.Contains(keyword))
                {
                    trigger.Type = type;
                    break;
                }
            }
            
            // Parse time if scheduled
            if (trigger.Type == TriggerType.Scheduled)
            {
                trigger.TimeOfDay = ParseTimeFromRule(rule);
                trigger.DaysOfWeek = ParseDaysFromRule(rule);
            }
            
            // Parse idle minutes
            if (trigger.Type == TriggerType.OnIdle)
            {
                trigger.IdleMinutes = ParseIdleMinutes(rule);
            }
            
            // Parse app context
            if (trigger.Type == TriggerType.OnAppOpen)
            {
                trigger.AppContext = ParseAppContext(rule);
            }
            
            return trigger;
        }
        
        private string ExtractCondition(string rule)
        {
            // Extract the "when/if" part
            var patterns = new[] { @"when\s+(.+?)\s*,", @"if\s+(.+?)\s*,", @"while\s+(.+?)\s*," };
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(rule, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
            
            return "";
        }
        
        private string ExtractAction(string rule)
        {
            // Extract the action part (after comma or trigger)
            var parts = rule.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                return parts.Last().Trim();
            }
            
            // Try to find action keywords
            foreach (var (keyword, action) in ActionKeywords)
            {
                if (rule.Contains(keyword))
                {
                    var idx = rule.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                    return rule.Substring(idx).Trim();
                }
            }
            
            return rule;
        }
        
        private List<string> ExtractAllowedActions(string rule)
        {
            var actions = new List<string>();
            
            foreach (var (keyword, action) in ActionKeywords)
            {
                if (rule.Contains(keyword))
                {
                    actions.Add(action);
                }
            }
            
            return actions.Distinct().ToList();
        }
        
        private AutonomyLevel DetermineAutonomyLevel(string action)
        {
            var highRisk = new[] { "delete", "remove", "uninstall", "format", "wipe" };
            var mediumRisk = new[] { "move", "modify", "change", "update" };
            
            if (highRisk.Any(r => action.Contains(r, StringComparison.OrdinalIgnoreCase)))
                return AutonomyLevel.Ask;
            
            if (mediumRisk.Any(r => action.Contains(r, StringComparison.OrdinalIgnoreCase)))
                return AutonomyLevel.Ask;
            
            return AutonomyLevel.AutoExecute;
        }
        
        private TimeSpan? ParseTimeFromRule(string rule)
        {
            // Match patterns like "at 9am", "at 14:00", "at 9:30 pm"
            var timePattern = @"at\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?";
            var match = Regex.Match(rule, timePattern, RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var hour = int.Parse(match.Groups[1].Value);
                var minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
                var ampm = match.Groups[3].Value.ToLowerInvariant();
                
                if (ampm == "pm" && hour < 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;
                
                return new TimeSpan(hour, minute, 0);
            }
            
            // Default times
            if (rule.Contains("morning")) return new TimeSpan(9, 0, 0);
            if (rule.Contains("evening")) return new TimeSpan(18, 0, 0);
            if (rule.Contains("night")) return new TimeSpan(21, 0, 0);
            
            return null;
        }
        
        private DayOfWeek[]? ParseDaysFromRule(string rule)
        {
            var days = new List<DayOfWeek>();
            
            if (rule.Contains("monday")) days.Add(DayOfWeek.Monday);
            if (rule.Contains("tuesday")) days.Add(DayOfWeek.Tuesday);
            if (rule.Contains("wednesday")) days.Add(DayOfWeek.Wednesday);
            if (rule.Contains("thursday")) days.Add(DayOfWeek.Thursday);
            if (rule.Contains("friday")) days.Add(DayOfWeek.Friday);
            if (rule.Contains("saturday")) days.Add(DayOfWeek.Saturday);
            if (rule.Contains("sunday")) days.Add(DayOfWeek.Sunday);
            
            if (rule.Contains("weekday"))
                days.AddRange(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday });
            
            if (rule.Contains("weekend"))
                days.AddRange(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
            
            return days.Count > 0 ? days.Distinct().ToArray() : null;
        }
        
        private int? ParseIdleMinutes(string rule)
        {
            var match = Regex.Match(rule, @"(\d+)\s*min", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value);
            }
            return 10; // Default 10 minutes
        }
        
        private string? ParseAppContext(string rule)
        {
            var match = Regex.Match(rule, @"(?:open|start)\s+(\w+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
        
        private string GenerateRuleName(string rule)
        {
            // Generate a short name from the rule
            var words = rule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5);
            return string.Join(" ", words).Trim(',', '.', ':');
        }
    }
}
