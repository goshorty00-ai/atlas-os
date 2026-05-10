using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Clipboard intelligence - understands clipboard content and offers smart actions.
    /// </summary>
    public static class ClipboardIntelligence
    {
        // Clipboard history
        private static readonly Queue<ClipboardEntry> _history = new();
        private const int MaxHistory = 20;
        private static string? _lastContent;
        
        /// <summary>
        /// Analyze clipboard content and suggest actions
        /// </summary>
        public static ClipboardAnalysis AnalyzeClipboard()
        {
            try
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return new ClipboardAnalysis { Type = ClipboardContentType.Empty };
                
                return AnalyzeText(text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Clipboard] Error: {ex.Message}");
                return new ClipboardAnalysis { Type = ClipboardContentType.Unknown };
            }
        }
        
        /// <summary>
        /// Analyze text content
        /// </summary>
        public static ClipboardAnalysis AnalyzeText(string text)
        {
            var analysis = new ClipboardAnalysis { Content = text };
            
            // URL detection
            if (Regex.IsMatch(text, @"^https?://\S+$", RegexOptions.IgnoreCase))
            {
                analysis.Type = ClipboardContentType.Url;
                analysis.SuggestedActions.Add("Open in browser");
                analysis.SuggestedActions.Add("Search for info about this");
                
                if (text.Contains("youtube.com") || text.Contains("youtu.be"))
                    analysis.SuggestedActions.Add("Play video");
                if (text.Contains("spotify.com"))
                    analysis.SuggestedActions.Add("Play on Spotify");
                if (text.Contains("github.com"))
                    analysis.SuggestedActions.Add("Clone repository");
                    
                return analysis;
            }
            
            // Email detection
            if (Regex.IsMatch(text, @"^[\w\.-]+@[\w\.-]+\.\w+$", RegexOptions.IgnoreCase))
            {
                analysis.Type = ClipboardContentType.Email;
                analysis.SuggestedActions.Add("Send email");
                analysis.SuggestedActions.Add("Add to contacts");
                return analysis;
            }
            
            // Phone number detection
            if (Regex.IsMatch(text, @"^[\+]?[\d\s\-\(\)]{10,}$"))
            {
                analysis.Type = ClipboardContentType.PhoneNumber;
                analysis.SuggestedActions.Add("Call number");
                analysis.SuggestedActions.Add("Send SMS");
                return analysis;
            }
            
            // File path detection
            if (Regex.IsMatch(text, @"^[A-Za-z]:\\[\w\\\.\-\s]+$") || text.StartsWith("/"))
            {
                analysis.Type = ClipboardContentType.FilePath;
                if (File.Exists(text))
                {
                    analysis.SuggestedActions.Add("Open file");
                    analysis.SuggestedActions.Add("Open containing folder");
                }
                else if (Directory.Exists(text))
                {
                    analysis.SuggestedActions.Add("Open folder");
                }
                return analysis;
            }
            
            // Code detection
            if (LooksLikeCode(text))
            {
                analysis.Type = ClipboardContentType.Code;
                analysis.SuggestedActions.Add("Explain this code");
                analysis.SuggestedActions.Add("Find bugs");
                analysis.SuggestedActions.Add("Optimize");
                analysis.DetectedLanguage = DetectCodeLanguage(text);
                return analysis;
            }
            
            // JSON detection
            if ((text.TrimStart().StartsWith("{") && text.TrimEnd().EndsWith("}")) ||
                (text.TrimStart().StartsWith("[") && text.TrimEnd().EndsWith("]")))
            {
                analysis.Type = ClipboardContentType.Json;
                analysis.SuggestedActions.Add("Format JSON");
                analysis.SuggestedActions.Add("Validate JSON");
                analysis.SuggestedActions.Add("Convert to C# class");
                return analysis;
            }
            
            // Error message detection
            if (LooksLikeError(text))
            {
                analysis.Type = ClipboardContentType.ErrorMessage;
                analysis.SuggestedActions.Add("Explain this error");
                analysis.SuggestedActions.Add("Search for solution");
                analysis.SuggestedActions.Add("Fix this");
                return analysis;
            }
            
            // Command detection
            if (LooksLikeCommand(text))
            {
                analysis.Type = ClipboardContentType.Command;
                analysis.SuggestedActions.Add("Run this command");
                analysis.SuggestedActions.Add("Explain what it does");
                return analysis;
            }
            
            // Default - plain text
            analysis.Type = ClipboardContentType.Text;
            if (text.Length > 100)
            {
                analysis.SuggestedActions.Add("Summarize");
                analysis.SuggestedActions.Add("Translate");
            }
            analysis.SuggestedActions.Add("Search for this");
            
            return analysis;
        }
        
        /// <summary>
        /// Copy text to clipboard
        /// </summary>
        public static async Task<string> CopyToClipboardAsync(string text)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Clipboard.SetText(text);
                });
                
                // Add to history
                AddToHistory(text);
                
                return "✓ Copied to clipboard";
            }
            catch (Exception ex)
            {
                return $"❌ Couldn't copy: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Get clipboard history
        /// </summary>
        public static List<ClipboardEntry> GetHistory() => _history.ToList();
        
        /// <summary>
        /// Get clipboard content
        /// </summary>
        public static string? GetClipboardText()
        {
            try
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                return text;
            }
            catch
            {
                return null;
            }
        }
        
        private static void AddToHistory(string text)
        {
            if (text == _lastContent) return;
            _lastContent = text;
            
            _history.Enqueue(new ClipboardEntry
            {
                Content = text.Length > 200 ? text.Substring(0, 200) + "..." : text,
                Timestamp = DateTime.Now,
                Type = AnalyzeText(text).Type
            });
            
            while (_history.Count > MaxHistory)
                _history.Dequeue();
        }
        
        private static bool LooksLikeCode(string text)
        {
            // Check for common code patterns
            var codePatterns = new[]
            {
                @"^\s*(public|private|protected|internal|class|interface|enum|struct|void|int|string|bool|var|const|let|function|def|import|using|namespace|package)\s",
                @"[{};]\s*$",
                @"^\s*(if|else|for|while|switch|try|catch|return)\s*[\(\{]",
                @"=>\s*{",
                @"\(\s*\)\s*{",
                @"^\s*//|^\s*/\*|^\s*#",
            };
            
            return codePatterns.Any(p => Regex.IsMatch(text, p, RegexOptions.Multiline | RegexOptions.IgnoreCase));
        }
        
        private static string? DetectCodeLanguage(string text)
        {
            if (Regex.IsMatch(text, @"\busing\s+System|namespace\s+\w+|public\s+class", RegexOptions.IgnoreCase))
                return "C#";
            if (Regex.IsMatch(text, @"\bfunction\s+\w+|const\s+\w+\s*=|let\s+\w+\s*=|=>\s*{", RegexOptions.IgnoreCase))
                return "JavaScript";
            if (Regex.IsMatch(text, @"\bdef\s+\w+|import\s+\w+|from\s+\w+\s+import", RegexOptions.IgnoreCase))
                return "Python";
            if (Regex.IsMatch(text, @"\bpublic\s+static\s+void\s+main|System\.out\.print", RegexOptions.IgnoreCase))
                return "Java";
            if (Regex.IsMatch(text, @"<\?php|\$\w+\s*=", RegexOptions.IgnoreCase))
                return "PHP";
            if (Regex.IsMatch(text, @"^\s*<[a-z]+[^>]*>|</[a-z]+>", RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return "HTML";
            if (Regex.IsMatch(text, @"^\s*SELECT|INSERT|UPDATE|DELETE|CREATE\s+TABLE", RegexOptions.IgnoreCase))
                return "SQL";
            return null;
        }
        
        private static bool LooksLikeError(string text)
        {
            var errorPatterns = new[]
            {
                @"\b(error|exception|failed|failure|fatal|critical)\b",
                @"at\s+\w+\.\w+\(",
                @"stack\s*trace",
                @"line\s+\d+",
                @":\d+:\d+",
                @"Traceback",
                @"ENOENT|EACCES|EPERM",
            };
            
            return errorPatterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
        }
        
        private static bool LooksLikeCommand(string text)
        {
            var commandPatterns = new[]
            {
                @"^\s*(npm|yarn|pip|dotnet|git|docker|kubectl|az|aws|gcloud)\s+",
                @"^\s*(cd|ls|dir|mkdir|rm|cp|mv|cat|echo|grep|find)\s+",
                @"^\s*\$\s*\w+",
                @"^\s*>\s*\w+",
            };
            
            return commandPatterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
        }
    }
    
    public class ClipboardAnalysis
    {
        public ClipboardContentType Type { get; set; }
        public string? Content { get; set; }
        public string? DetectedLanguage { get; set; }
        public List<string> SuggestedActions { get; set; } = new();
    }
    
    public class ClipboardEntry
    {
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public ClipboardContentType Type { get; set; }
    }
    
    public enum ClipboardContentType
    {
        Empty,
        Unknown,
        Text,
        Url,
        Email,
        PhoneNumber,
        FilePath,
        Code,
        Json,
        ErrorMessage,
        Command
    }
}
