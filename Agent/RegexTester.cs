using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Regex Tester - Test and explain regular expressions.
    /// </summary>
    public static class RegexTester
    {
        /// <summary>
        /// Handle regex commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("regex") && !lower.Contains("regular expression") && !lower.Contains("pattern"))
                return Task.FromResult<string?>(null);
            
            // Test regex against clipboard
            if (lower.Contains("test") || lower.Contains("match") || lower.Contains("find"))
            {
                var patternMatch = Regex.Match(input, @"(?:regex|pattern)\s+[""'/](.+?)[""'/]", RegexOptions.IgnoreCase);
                if (patternMatch.Success)
                {
                    return Task.FromResult<string?>(TestRegex(patternMatch.Groups[1].Value));
                }
                return Task.FromResult<string?>("Specify a pattern: `test regex \"pattern\"`");
            }
            
            // Common regex patterns
            if (lower.Contains("common") || lower.Contains("example") || lower.Contains("cheat"))
            {
                return Task.FromResult<string?>(GetCommonPatterns());
            }
            
            // Explain regex
            if (lower.Contains("explain"))
            {
                var patternMatch = Regex.Match(input, @"[""'/](.+?)[""'/]");
                if (patternMatch.Success)
                {
                    return Task.FromResult<string?>(ExplainRegex(patternMatch.Groups[1].Value));
                }
            }
            
            // Generate regex for common use cases
            if (lower.Contains("email"))
                return Task.FromResult<string?>(GetPatternInfo("Email", @"^[\w\.-]+@[\w\.-]+\.\w+$"));
            if (lower.Contains("phone"))
                return Task.FromResult<string?>(GetPatternInfo("Phone", @"^\+?[\d\s\-\(\)]{10,}$"));
            if (lower.Contains("url") || lower.Contains("link"))
                return Task.FromResult<string?>(GetPatternInfo("URL", @"https?://[\w\-\.]+\.\w+[\w\-\._~:/?#\[\]@!$&'()*+,;=%]*"));
            if (lower.Contains("ip"))
                return Task.FromResult<string?>(GetPatternInfo("IP Address", @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b"));
            if (lower.Contains("date"))
                return Task.FromResult<string?>(GetPatternInfo("Date (YYYY-MM-DD)", @"\d{4}-\d{2}-\d{2}"));
            
            return Task.FromResult<string?>(GetCommonPatterns());
        }
        
        private static string TestRegex(string pattern)
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy some text to clipboard first, then test the regex!";
            
            try
            {
                var regex = new Regex(pattern, RegexOptions.Multiline);
                var matches = regex.Matches(text);
                
                if (matches.Count == 0)
                    return $"🔍 **No matches found**\n\nPattern: `{pattern}`\nText length: {text.Length} chars";
                
                var sb = new StringBuilder();
                sb.AppendLine($"🎯 **Found {matches.Count} match(es):**\n");
                sb.AppendLine($"Pattern: `{pattern}`\n");
                
                var shown = 0;
                foreach (Match match in matches)
                {
                    if (shown >= 10)
                    {
                        sb.AppendLine($"... and {matches.Count - 10} more");
                        break;
                    }
                    
                    var value = match.Value.Length > 50 ? match.Value.Substring(0, 47) + "..." : match.Value;
                    sb.AppendLine($"{shown + 1}. `{value}` (pos {match.Index})");
                    
                    // Show groups if any
                    if (match.Groups.Count > 1)
                    {
                        for (int i = 1; i < match.Groups.Count && i <= 5; i++)
                        {
                            if (match.Groups[i].Success)
                                sb.AppendLine($"   Group {i}: `{match.Groups[i].Value}`");
                        }
                    }
                    shown++;
                }
                
                return sb.ToString();
            }
            catch (RegexParseException ex)
            {
                return $"❌ **Invalid regex:**\n{ex.Message}";
            }
        }
        
        private static string ExplainRegex(string pattern)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📖 **Regex Explanation:**\n");
            sb.AppendLine($"Pattern: `{pattern}`\n");
            
            // Simple explanations for common tokens
            var explanations = new (string Token, string Meaning)[]
            {
                ("^", "Start of string/line"),
                ("$", "End of string/line"),
                (".", "Any character except newline"),
                ("*", "Zero or more of previous"),
                ("+", "One or more of previous"),
                ("?", "Zero or one of previous"),
                ("\\d", "Any digit (0-9)"),
                ("\\w", "Word character (a-z, A-Z, 0-9, _)"),
                ("\\s", "Whitespace (space, tab, newline)"),
                ("\\b", "Word boundary"),
                ("[...]", "Character class - match any inside"),
                ("[^...]", "Negated class - match any NOT inside"),
                ("(...)", "Capture group"),
                ("(?:...)", "Non-capturing group"),
                ("|", "OR - match either side"),
                ("{n}", "Exactly n times"),
                ("{n,m}", "Between n and m times"),
            };
            
            foreach (var (token, meaning) in explanations)
            {
                if (pattern.Contains(token.TrimEnd('.').TrimStart('\\')))
                    sb.AppendLine($"• `{token}` → {meaning}");
            }
            
            return sb.ToString();
        }
        
        private static string GetPatternInfo(string name, string pattern)
        {
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(pattern));
            
            return $"🔍 **{name} Regex:**\n```\n{pattern}\n```\n✓ Copied to clipboard!";
        }
        
        private static string GetCommonPatterns()
        {
            return "🔍 **Common Regex Patterns:**\n\n" +
                   "• `regex email` - Email validation\n" +
                   "• `regex phone` - Phone number\n" +
                   "• `regex url` - URL/link\n" +
                   "• `regex ip` - IP address\n" +
                   "• `regex date` - Date (YYYY-MM-DD)\n\n" +
                   "**Testing:**\n" +
                   "• `test regex \"pattern\"` - Test against clipboard\n" +
                   "• `explain regex \"pattern\"` - Explain pattern";
        }
    }
}
