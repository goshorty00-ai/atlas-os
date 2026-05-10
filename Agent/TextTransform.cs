using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Text Transform - Quick text manipulation utilities.
    /// "Make this uppercase" "Convert to title case" "Remove duplicates"
    /// </summary>
    public static class TextTransform
    {
        /// <summary>
        /// Try to handle a text transform command
        /// </summary>
        public static async Task<string?> TryTransformAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Get clipboard text for transformation
            string? clipboardText = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    clipboardText = Clipboard.GetText();
            });
            
            // Check for transform commands
            if (lower.StartsWith("make ") || lower.StartsWith("convert ") || 
                lower.StartsWith("transform ") || lower.Contains("clipboard"))
            {
                if (string.IsNullOrEmpty(clipboardText))
                    return "📋 Clipboard is empty. Copy some text first!";
                
                string? result = null;
                string? operation = null;
                
                // Case transformations
                if (lower.Contains("uppercase") || lower.Contains("upper case") || lower.Contains("all caps"))
                {
                    result = clipboardText.ToUpperInvariant();
                    operation = "UPPERCASE";
                }
                else if (lower.Contains("lowercase") || lower.Contains("lower case"))
                {
                    result = clipboardText.ToLowerInvariant();
                    operation = "lowercase";
                }
                else if (lower.Contains("title case") || lower.Contains("titlecase"))
                {
                    result = ToTitleCase(clipboardText);
                    operation = "Title Case";
                }
                else if (lower.Contains("sentence case"))
                {
                    result = ToSentenceCase(clipboardText);
                    operation = "Sentence case";
                }
                else if (lower.Contains("camel case") || lower.Contains("camelcase"))
                {
                    result = ToCamelCase(clipboardText);
                    operation = "camelCase";
                }
                else if (lower.Contains("pascal case") || lower.Contains("pascalcase"))
                {
                    result = ToPascalCase(clipboardText);
                    operation = "PascalCase";
                }
                else if (lower.Contains("snake case") || lower.Contains("snakecase") || lower.Contains("snake_case"))
                {
                    result = ToSnakeCase(clipboardText);
                    operation = "snake_case";
                }
                else if (lower.Contains("kebab case") || lower.Contains("kebabcase") || lower.Contains("kebab-case"))
                {
                    result = ToKebabCase(clipboardText);
                    operation = "kebab-case";
                }
                // Text cleaning
                else if (lower.Contains("remove duplicate") || lower.Contains("dedupe") || lower.Contains("unique lines"))
                {
                    result = RemoveDuplicateLines(clipboardText);
                    operation = "Removed duplicates";
                }
                else if (lower.Contains("sort lines") || lower.Contains("alphabetize"))
                {
                    result = SortLines(clipboardText);
                    operation = "Sorted lines";
                }
                else if (lower.Contains("reverse lines"))
                {
                    result = ReverseLines(clipboardText);
                    operation = "Reversed lines";
                }
                else if (lower.Contains("reverse") && !lower.Contains("lines"))
                {
                    result = new string(clipboardText.Reverse().ToArray());
                    operation = "Reversed text";
                }
                else if (lower.Contains("trim") || lower.Contains("remove whitespace"))
                {
                    result = TrimLines(clipboardText);
                    operation = "Trimmed whitespace";
                }
                else if (lower.Contains("number lines") || lower.Contains("add line numbers"))
                {
                    result = NumberLines(clipboardText);
                    operation = "Added line numbers";
                }
                else if (lower.Contains("remove empty") || lower.Contains("remove blank"))
                {
                    result = RemoveEmptyLines(clipboardText);
                    operation = "Removed empty lines";
                }
                // Encoding
                else if (lower.Contains("base64 encode") || lower.Contains("to base64"))
                {
                    result = Convert.ToBase64String(Encoding.UTF8.GetBytes(clipboardText));
                    operation = "Base64 encoded";
                }
                else if (lower.Contains("base64 decode") || lower.Contains("from base64"))
                {
                    try
                    {
                        result = Encoding.UTF8.GetString(Convert.FromBase64String(clipboardText.Trim()));
                        operation = "Base64 decoded";
                    }
                    catch
                    {
                        return "❌ Invalid Base64 string";
                    }
                }
                else if (lower.Contains("url encode"))
                {
                    result = Uri.EscapeDataString(clipboardText);
                    operation = "URL encoded";
                }
                else if (lower.Contains("url decode"))
                {
                    result = Uri.UnescapeDataString(clipboardText);
                    operation = "URL decoded";
                }
                // Stats
                else if (lower.Contains("count") || lower.Contains("stats") || lower.Contains("word count"))
                {
                    return GetTextStats(clipboardText);
                }
                
                if (result != null && operation != null)
                {
                    // Copy result to clipboard
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Clipboard.SetText(result);
                    });
                    
                    var preview = result.Length > 100 ? result.Substring(0, 100) + "..." : result;
                    return $"✓ {operation}! Copied to clipboard.\n\nPreview:\n```\n{preview}\n```";
                }
            }
            
            return null;
        }
        
        private static string ToTitleCase(string text)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
        }
        
        private static string ToSentenceCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            var result = new StringBuilder();
            bool newSentence = true;
            
            foreach (char c in text)
            {
                if (newSentence && char.IsLetter(c))
                {
                    result.Append(char.ToUpper(c));
                    newSentence = false;
                }
                else
                {
                    result.Append(char.ToLower(c));
                }
                
                if (c == '.' || c == '!' || c == '?')
                    newSentence = true;
            }
            
            return result.ToString();
        }
        
        private static string ToCamelCase(string text)
        {
            var words = Regex.Split(text, @"[\s_\-]+")
                .Where(w => !string.IsNullOrEmpty(w))
                .ToArray();
            
            if (words.Length == 0) return text;
            
            var result = words[0].ToLower();
            for (int i = 1; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                    result += char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
            
            return result;
        }
        
        private static string ToPascalCase(string text)
        {
            var words = Regex.Split(text, @"[\s_\-]+")
                .Where(w => !string.IsNullOrEmpty(w))
                .ToArray();
            
            return string.Join("", words.Select(w => 
                char.ToUpper(w[0]) + w.Substring(1).ToLower()));
        }
        
        private static string ToSnakeCase(string text)
        {
            // Handle camelCase/PascalCase
            text = Regex.Replace(text, @"([a-z])([A-Z])", "$1_$2");
            // Replace spaces and hyphens
            text = Regex.Replace(text, @"[\s\-]+", "_");
            return text.ToLower();
        }
        
        private static string ToKebabCase(string text)
        {
            // Handle camelCase/PascalCase
            text = Regex.Replace(text, @"([a-z])([A-Z])", "$1-$2");
            // Replace spaces and underscores
            text = Regex.Replace(text, @"[\s_]+", "-");
            return text.ToLower();
        }
        
        private static string RemoveDuplicateLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return string.Join(Environment.NewLine, lines.Distinct());
        }
        
        private static string SortLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return string.Join(Environment.NewLine, lines.OrderBy(l => l));
        }
        
        private static string ReverseLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            Array.Reverse(lines);
            return string.Join(Environment.NewLine, lines);
        }
        
        private static string TrimLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return string.Join(Environment.NewLine, lines.Select(l => l.Trim()));
        }
        
        private static string NumberLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var width = lines.Length.ToString().Length;
            return string.Join(Environment.NewLine, 
                lines.Select((l, i) => $"{(i + 1).ToString().PadLeft(width)}. {l}"));
        }
        
        private static string RemoveEmptyLines(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }
        
        private static string GetTextStats(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var words = Regex.Matches(text, @"\b\w+\b").Count;
            var chars = text.Length;
            var charsNoSpaces = text.Count(c => !char.IsWhiteSpace(c));
            var sentences = Regex.Matches(text, @"[.!?]+").Count;
            
            return $"📊 **Text Statistics:**\n\n" +
                   $"Characters: {chars:N0}\n" +
                   $"Characters (no spaces): {charsNoSpaces:N0}\n" +
                   $"Words: {words:N0}\n" +
                   $"Lines: {lines.Length:N0}\n" +
                   $"Sentences: {sentences:N0}\n" +
                   $"Avg words/sentence: {(sentences > 0 ? words / (double)sentences : 0):F1}";
        }
    }
}
