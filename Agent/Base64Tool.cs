using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Base64 Tool - Encode/decode Base64, handle files and images.
    /// </summary>
    public static class Base64Tool
    {
        /// <summary>
        /// Handle Base64 commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("base64") && !lower.Contains("encode") && !lower.Contains("decode"))
                return null;
            
            // Encode clipboard to Base64
            if (lower.Contains("encode") && (lower.Contains("clipboard") || lower.Contains("text") || lower.Contains("base64")))
            {
                return EncodeClipboard();
            }
            
            // Decode Base64 from clipboard
            if (lower.Contains("decode") && (lower.Contains("clipboard") || lower.Contains("base64")))
            {
                return DecodeClipboard();
            }
            
            // Encode file to Base64
            if (lower.Contains("encode") && lower.Contains("file"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, @"file\s+[""']?(.+?)[""']?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return await EncodeFileAsync(match.Groups[1].Value.Trim());
                return "Specify a file: `encode file C:\\path\\to\\file`";
            }
            
            // URL-safe Base64
            if (lower.Contains("url") && lower.Contains("safe"))
            {
                return EncodeUrlSafe();
            }
            
            // Default: try to detect and encode/decode
            if (lower.Contains("base64"))
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Copy some text to clipboard first!";
                
                // Try to detect if it's Base64
                if (IsBase64(text))
                    return DecodeClipboard();
                else
                    return EncodeClipboard();
            }
            
            return null;
        }
        
        private static string EncodeClipboard()
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy some text to clipboard first!";
            
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(encoded));
            
            var preview = encoded.Length > 100 ? encoded.Substring(0, 97) + "..." : encoded;
            
            return $"🔐 **Base64 Encoded:**\n```\n{preview}\n```\n" +
                   $"Original: {text.Length} chars → Encoded: {encoded.Length} chars\n" +
                   $"✓ Copied to clipboard!";
        }
        
        private static string DecodeClipboard()
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText()?.Trim();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy some Base64 text to clipboard first!";
            
            try
            {
                // Handle URL-safe Base64
                var normalized = text.Replace('-', '+').Replace('_', '/');
                // Add padding if needed
                switch (normalized.Length % 4)
                {
                    case 2: normalized += "=="; break;
                    case 3: normalized += "="; break;
                }
                
                var bytes = Convert.FromBase64String(normalized);
                var decoded = Encoding.UTF8.GetString(bytes);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(decoded));
                
                var preview = decoded.Length > 200 ? decoded.Substring(0, 197) + "..." : decoded;
                
                return $"🔓 **Base64 Decoded:**\n```\n{preview}\n```\n✓ Copied to clipboard!";
            }
            catch
            {
                return "❌ Invalid Base64 string in clipboard";
            }
        }
        
        private static async Task<string> EncodeFileAsync(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return $"❌ File not found: {path}";
                
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > 10 * 1024 * 1024) // 10MB limit
                    return "❌ File too large (max 10MB)";
                
                var bytes = await File.ReadAllBytesAsync(path);
                var encoded = Convert.ToBase64String(bytes);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(encoded));
                
                return $"🔐 **File Encoded to Base64:**\n\n" +
                       $"File: {Path.GetFileName(path)}\n" +
                       $"Size: {fileInfo.Length:N0} bytes\n" +
                       $"Encoded: {encoded.Length:N0} chars\n\n" +
                       $"✓ Copied to clipboard!";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        private static string EncodeUrlSafe()
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy some text to clipboard first!";
            
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(encoded));
            
            return $"🔐 **URL-Safe Base64:**\n```\n{encoded}\n```\n✓ Copied to clipboard!";
        }
        
        private static bool IsBase64(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            
            // Check if it looks like Base64
            if (text.Length % 4 != 0 && !text.Contains('-') && !text.Contains('_'))
                return false;
            
            // Check for valid Base64 characters
            foreach (var c in text)
            {
                if (!char.IsLetterOrDigit(c) && c != '+' && c != '/' && c != '=' && c != '-' && c != '_')
                    return false;
            }
            
            // Try to decode
            try
            {
                var normalized = text.Replace('-', '+').Replace('_', '/');
                switch (normalized.Length % 4)
                {
                    case 2: normalized += "=="; break;
                    case 3: normalized += "="; break;
                }
                Convert.FromBase64String(normalized);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
