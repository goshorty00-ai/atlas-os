using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Hash Generator - Generate MD5, SHA1, SHA256, SHA512 hashes.
    /// </summary>
    public static class HashGenerator
    {
        /// <summary>
        /// Handle hash commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Hash clipboard text
            if (lower.Contains("hash") && (lower.Contains("clipboard") || lower.Contains("text")))
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Copy some text to clipboard first!";
                
                return HashText(text);
            }
            
            // Hash file
            if (lower.Contains("hash") && lower.Contains("file"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, @"file\s+[""']?(.+?)[""']?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var path = match.Groups[1].Value.Trim();
                    return await HashFileAsync(path);
                }
                return "Specify a file path: 'hash file C:\\path\\to\\file.txt'";
            }
            
            // Generate specific hash
            if (lower.Contains("md5") || lower.Contains("sha1") || lower.Contains("sha256") || lower.Contains("sha512"))
            {
                string? text = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        text = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(text))
                    return "📋 Copy some text to clipboard first!";
                
                string hash;
                string algorithm;
                
                if (lower.Contains("md5"))
                {
                    hash = ComputeMD5(text);
                    algorithm = "MD5";
                }
                else if (lower.Contains("sha1"))
                {
                    hash = ComputeSHA1(text);
                    algorithm = "SHA1";
                }
                else if (lower.Contains("sha512"))
                {
                    hash = ComputeSHA512(text);
                    algorithm = "SHA512";
                }
                else
                {
                    hash = ComputeSHA256(text);
                    algorithm = "SHA256";
                }
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(hash));
                
                return $"🔐 **{algorithm} Hash:**\n```\n{hash}\n```\n✓ Copied to clipboard!";
            }
            
            // Generate UUID/GUID
            if (lower.Contains("uuid") || lower.Contains("guid"))
            {
                var guid = Guid.NewGuid().ToString();
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(guid));
                return $"🆔 **Generated UUID:**\n```\n{guid}\n```\n✓ Copied to clipboard!";
            }
            
            // Verify hash
            if (lower.Contains("verify") && lower.Contains("hash"))
            {
                return "To verify a hash:\n1. Copy the expected hash\n2. Say 'hash file [path]'\n3. Compare the results";
            }
            
            return null;
        }
        
        private static string HashText(string text)
        {
            var md5 = ComputeMD5(text);
            var sha1 = ComputeSHA1(text);
            var sha256 = ComputeSHA256(text);
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(sha256));
            
            return $"🔐 **Text Hashes:**\n\n" +
                   $"**MD5:**\n`{md5}`\n\n" +
                   $"**SHA1:**\n`{sha1}`\n\n" +
                   $"**SHA256:**\n`{sha256}`\n\n" +
                   $"✓ SHA256 copied to clipboard!";
        }
        
        private static async Task<string> HashFileAsync(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return $"❌ File not found: {path}";
                
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > 100 * 1024 * 1024) // 100MB limit
                    return "❌ File too large (max 100MB)";
                
                var bytes = await File.ReadAllBytesAsync(path);
                
                var md5 = ComputeHash(MD5.Create(), bytes);
                var sha1 = ComputeHash(SHA1.Create(), bytes);
                var sha256 = ComputeHash(SHA256.Create(), bytes);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(sha256));
                
                return $"🔐 **File Hashes:**\n" +
                       $"File: {Path.GetFileName(path)}\n" +
                       $"Size: {fileInfo.Length:N0} bytes\n\n" +
                       $"**MD5:**\n`{md5}`\n\n" +
                       $"**SHA1:**\n`{sha1}`\n\n" +
                       $"**SHA256:**\n`{sha256}`\n\n" +
                       $"✓ SHA256 copied to clipboard!";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        private static string ComputeMD5(string text)
        {
            using var md5 = MD5.Create();
            return ComputeHash(md5, Encoding.UTF8.GetBytes(text));
        }
        
        private static string ComputeSHA1(string text)
        {
            using var sha1 = SHA1.Create();
            return ComputeHash(sha1, Encoding.UTF8.GetBytes(text));
        }
        
        private static string ComputeSHA256(string text)
        {
            using var sha256 = SHA256.Create();
            return ComputeHash(sha256, Encoding.UTF8.GetBytes(text));
        }
        
        private static string ComputeSHA512(string text)
        {
            using var sha512 = SHA512.Create();
            return ComputeHash(sha512, Encoding.UTF8.GetBytes(text));
        }
        
        private static string ComputeHash(HashAlgorithm algorithm, byte[] data)
        {
            var hash = algorithm.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
