using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Translator - Translate text between languages using free API.
    /// </summary>
    public static class Translator
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
        
        private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "english", "en" }, { "en", "en" },
            { "spanish", "es" }, { "es", "es" },
            { "french", "fr" }, { "fr", "fr" },
            { "german", "de" }, { "de", "de" },
            { "italian", "it" }, { "it", "it" },
            { "portuguese", "pt" }, { "pt", "pt" },
            { "russian", "ru" }, { "ru", "ru" },
            { "japanese", "ja" }, { "ja", "ja" },
            { "korean", "ko" }, { "ko", "ko" },
            { "chinese", "zh" }, { "zh", "zh" },
            { "arabic", "ar" }, { "ar", "ar" },
            { "hindi", "hi" }, { "hi", "hi" },
            { "dutch", "nl" }, { "nl", "nl" },
            { "polish", "pl" }, { "pl", "pl" },
            { "turkish", "tr" }, { "tr", "tr" },
            { "swedish", "sv" }, { "sv", "sv" },
            { "norwegian", "no" }, { "no", "no" },
            { "danish", "da" }, { "da", "da" },
            { "finnish", "fi" }, { "fi", "fi" },
            { "greek", "el" }, { "el", "el" },
            { "hebrew", "he" }, { "he", "he" },
            { "thai", "th" }, { "th", "th" },
            { "vietnamese", "vi" }, { "vi", "vi" },
            { "indonesian", "id" }, { "id", "id" },
            { "czech", "cs" }, { "cs", "cs" },
            { "romanian", "ro" }, { "ro", "ro" },
            { "hungarian", "hu" }, { "hu", "hu" },
            { "ukrainian", "uk" }, { "uk", "uk" },
        };
        
        /// <summary>
        /// Handle translation commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("translate") && !lower.Contains("to english") && 
                !lower.Contains("in spanish") && !lower.Contains("in french") &&
                !lower.Contains("in german") && !lower.Contains("in japanese"))
                return null;
            
            // Parse "translate X to Y" or "translate to Y"
            var match = System.Text.RegularExpressions.Regex.Match(input, 
                @"translate\s+(?:(?:""(.+?)""|'(.+?)'|(.+?))\s+)?to\s+(\w+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var text = match.Groups[1].Value;
                if (string.IsNullOrEmpty(text)) text = match.Groups[2].Value;
                if (string.IsNullOrEmpty(text)) text = match.Groups[3].Value;
                var targetLang = match.Groups[4].Value;
                
                // If no text specified, use clipboard
                if (string.IsNullOrEmpty(text) || text.Trim().ToLower() == "clipboard")
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (Clipboard.ContainsText())
                            text = Clipboard.GetText();
                    });
                }
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Copy some text to clipboard or specify text to translate!";
                
                return await TranslateTextAsync(text.Trim(), targetLang);
            }
            
            // "translate" alone - translate clipboard to English
            if (lower.Trim() == "translate" || lower.Contains("translate clipboard"))
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Copy some text to clipboard first!";
                
                return await TranslateTextAsync(text, "english");
            }
            
            // List languages
            if (lower.Contains("language") && (lower.Contains("list") || lower.Contains("supported")))
            {
                return GetSupportedLanguages();
            }
            
            return null;
        }
        
        private static async Task<string> TranslateTextAsync(string text, string targetLang)
        {
            if (!LanguageCodes.TryGetValue(targetLang, out var targetCode))
            {
                return $"❌ Unknown language: {targetLang}\n\nSay 'list languages' to see supported languages.";
            }
            
            try
            {
                // Using LibreTranslate API (free, no key required)
                var encoded = Uri.EscapeDataString(text);
                var url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair=auto|{targetCode}";
                
                var response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                
                var translatedText = doc.RootElement
                    .GetProperty("responseData")
                    .GetProperty("translatedText")
                    .GetString();
                
                if (string.IsNullOrEmpty(translatedText))
                    return "❌ Translation failed";
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(translatedText));
                
                var preview = text.Length > 100 ? text.Substring(0, 97) + "..." : text;
                var translatedPreview = translatedText.Length > 200 ? translatedText.Substring(0, 197) + "..." : translatedText;
                
                return $"🌐 **Translation to {GetLanguageName(targetCode)}:**\n\n" +
                       $"**Original:**\n{preview}\n\n" +
                       $"**Translated:**\n{translatedPreview}\n\n" +
                       $"✓ Copied to clipboard!";
            }
            catch (Exception ex)
            {
                return $"❌ Translation error: {ex.Message}";
            }
        }
        
        private static string GetLanguageName(string code)
        {
            foreach (var kvp in LanguageCodes)
            {
                if (kvp.Value == code && kvp.Key.Length > 2)
                    return char.ToUpper(kvp.Key[0]) + kvp.Key.Substring(1);
            }
            return code.ToUpper();
        }
        
        private static string GetSupportedLanguages()
        {
            var languages = new HashSet<string>();
            foreach (var kvp in LanguageCodes)
            {
                if (kvp.Key.Length > 2)
                    languages.Add(char.ToUpper(kvp.Key[0]) + kvp.Key.Substring(1));
            }
            
            var sorted = languages.OrderBy(l => l).ToArray();
            
            return $"🌐 **Supported Languages ({sorted.Length}):**\n\n" +
                   string.Join(", ", sorted) + "\n\n" +
                   "**Usage:** `translate \"text\" to spanish`";
        }
    }
}
