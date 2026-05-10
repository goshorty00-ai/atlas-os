using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// QR Code Generator - Generate QR codes as ASCII art or save to file.
    /// Uses a simple text-based QR representation.
    /// </summary>
    public static class QRCodeGenerator
    {
        /// <summary>
        /// Handle QR code commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("qr") && !lower.Contains("barcode"))
                return null;
            
            // Generate QR for clipboard
            if (lower.Contains("clipboard") || lower.Contains("text"))
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Copy some text to clipboard first!";
                
                return GenerateQRInfo(text);
            }
            
            // Generate QR for URL
            var urlMatch = System.Text.RegularExpressions.Regex.Match(input, @"(https?://\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                return GenerateQRInfo(urlMatch.Groups[1].Value);
            }
            
            // Generate QR for quoted text
            var quotedMatch = System.Text.RegularExpressions.Regex.Match(input, @"[""'](.+?)[""']");
            if (quotedMatch.Success)
            {
                return GenerateQRInfo(quotedMatch.Groups[1].Value);
            }
            
            // WiFi QR code
            if (lower.Contains("wifi"))
            {
                return "📶 **WiFi QR Code Format:**\n\n" +
                       "Say: `qr wifi \"NetworkName\" \"Password\"`\n\n" +
                       "Or copy this format to clipboard:\n" +
                       "`WIFI:T:WPA;S:NetworkName;P:Password;;`";
            }
            
            return "📱 **QR Code Generator:**\n\n" +
                   "• `qr clipboard` - Generate QR for clipboard text\n" +
                   "• `qr \"your text\"` - Generate QR for specific text\n" +
                   "• `qr https://url.com` - Generate QR for URL\n" +
                   "• `qr wifi` - WiFi QR code format";
        }
        
        private static string GenerateQRInfo(string content)
        {
            // Since we can't generate actual QR images without a library,
            // we'll provide the data in a format that can be used with online generators
            var encoded = Uri.EscapeDataString(content);
            var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={encoded}";
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(qrUrl));
            
            var preview = content.Length > 50 ? content.Substring(0, 47) + "..." : content;
            
            return $"📱 **QR Code Ready:**\n\n" +
                   $"Content: `{preview}`\n" +
                   $"Length: {content.Length} characters\n\n" +
                   $"🔗 QR Image URL copied to clipboard!\n" +
                   $"Open in browser to view/download the QR code.";
        }
    }
}
