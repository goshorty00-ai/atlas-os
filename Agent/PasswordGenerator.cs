using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Password Generator - Generate secure passwords and passphrases.
    /// </summary>
    public static class PasswordGenerator
    {
        private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
        private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Numbers = "0123456789";
        private const string Symbols = "!@#$%^&*()_+-=[]{}|;:,.<>?";
        
        private static readonly string[] CommonWords = {
            "apple", "banana", "cherry", "dragon", "eagle", "falcon", "guitar", "hammer",
            "island", "jungle", "knight", "lemon", "mango", "ninja", "orange", "piano",
            "queen", "rocket", "silver", "tiger", "umbrella", "violet", "wizard", "yellow",
            "zebra", "anchor", "bridge", "castle", "diamond", "engine", "forest", "garden",
            "harbor", "iceberg", "jasmine", "kingdom", "lantern", "mountain", "neptune", "ocean",
            "phoenix", "quartz", "rainbow", "sunset", "thunder", "universe", "volcano", "winter"
        };
        
        /// <summary>
        /// Generate a random password
        /// </summary>
        public static string GeneratePassword(int length = 16, bool includeSymbols = true)
        {
            var chars = Lowercase + Uppercase + Numbers;
            if (includeSymbols) chars += Symbols;
            
            var password = new StringBuilder();
            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            
            // Ensure at least one of each type
            password.Append(Lowercase[bytes[0] % Lowercase.Length]);
            password.Append(Uppercase[bytes[1] % Uppercase.Length]);
            password.Append(Numbers[bytes[2] % Numbers.Length]);
            if (includeSymbols)
                password.Append(Symbols[bytes[3] % Symbols.Length]);
            
            // Fill the rest
            for (int i = password.Length; i < length; i++)
            {
                password.Append(chars[bytes[i] % chars.Length]);
            }
            
            // Shuffle
            return new string(password.ToString().OrderBy(_ => RandomNumberGenerator.GetInt32(1000)).ToArray());
        }
        
        /// <summary>
        /// Generate a memorable passphrase
        /// </summary>
        public static string GeneratePassphrase(int wordCount = 4, string separator = "-")
        {
            var words = new string[wordCount];
            var bytes = new byte[wordCount];
            RandomNumberGenerator.Fill(bytes);
            
            for (int i = 0; i < wordCount; i++)
            {
                var word = CommonWords[bytes[i] % CommonWords.Length];
                // Capitalize first letter
                words[i] = char.ToUpper(word[0]) + word.Substring(1);
            }
            
            // Add a random number
            var number = RandomNumberGenerator.GetInt32(100, 999);
            
            return string.Join(separator, words) + separator + number;
        }
        
        /// <summary>
        /// Generate a PIN
        /// </summary>
        public static string GeneratePIN(int length = 4)
        {
            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            return string.Join("", bytes.Select(b => (b % 10).ToString()));
        }
        
        /// <summary>
        /// Check password strength
        /// </summary>
        public static (string Strength, string[] Tips) CheckPasswordStrength(string password)
        {
            var score = 0;
            var tips = new System.Collections.Generic.List<string>();
            
            // Length
            if (password.Length >= 8) score++;
            if (password.Length >= 12) score++;
            if (password.Length >= 16) score++;
            if (password.Length < 8) tips.Add("Use at least 8 characters");
            
            // Character types
            if (password.Any(char.IsLower)) score++;
            else tips.Add("Add lowercase letters");
            
            if (password.Any(char.IsUpper)) score++;
            else tips.Add("Add uppercase letters");
            
            if (password.Any(char.IsDigit)) score++;
            else tips.Add("Add numbers");
            
            if (password.Any(c => Symbols.Contains(c))) score++;
            else tips.Add("Add symbols (!@#$%...)");
            
            // Patterns to avoid
            if (password.ToLower().Contains("password") || password.Contains("123456"))
            {
                score -= 2;
                tips.Add("Avoid common patterns");
            }
            
            var strength = score switch
            {
                <= 2 => "🔴 Weak",
                <= 4 => "🟡 Fair",
                <= 6 => "🟢 Strong",
                _ => "💪 Very Strong"
            };
            
            return (strength, tips.ToArray());
        }
        
        /// <summary>
        /// Handle password commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Generate password
            if (lower.Contains("generate password") || lower.Contains("new password") || lower.Contains("random password"))
            {
                var length = 16;
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*(?:char|character|digit|long)");
                if (match.Success)
                    length = Math.Clamp(int.Parse(match.Groups[1].Value), 8, 64);
                
                var includeSymbols = !lower.Contains("no symbol") && !lower.Contains("simple");
                var password = GeneratePassword(length, includeSymbols);
                
                // Copy to clipboard
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(password));
                
                return $"🔐 **Generated Password:**\n```\n{password}\n```\n✓ Copied to clipboard!";
            }
            
            // Generate passphrase
            if (lower.Contains("passphrase") || lower.Contains("memorable password"))
            {
                var wordCount = 4;
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*word");
                if (match.Success)
                    wordCount = Math.Clamp(int.Parse(match.Groups[1].Value), 3, 8);
                
                var passphrase = GeneratePassphrase(wordCount);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(passphrase));
                
                return $"🔐 **Generated Passphrase:**\n```\n{passphrase}\n```\n✓ Copied to clipboard!";
            }
            
            // Generate PIN
            if (lower.Contains("generate pin") || lower.Contains("new pin") || lower.Contains("random pin"))
            {
                var length = 4;
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*digit");
                if (match.Success)
                    length = Math.Clamp(int.Parse(match.Groups[1].Value), 4, 8);
                
                var pin = GeneratePIN(length);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(pin));
                
                return $"🔢 **Generated PIN:**\n```\n{pin}\n```\n✓ Copied to clipboard!";
            }
            
            // Check password strength
            if (lower.Contains("check password") || lower.Contains("password strength"))
            {
                string? clipboardText = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        clipboardText = Clipboard.GetText();
                });
                
                if (string.IsNullOrEmpty(clipboardText))
                    return "📋 Copy a password to clipboard first, then say 'check password strength'";
                
                var (strength, tips) = CheckPasswordStrength(clipboardText);
                var result = $"🔐 **Password Strength:** {strength}\n";
                if (tips.Any())
                    result += "\n💡 Tips:\n" + string.Join("\n", tips.Select(t => $"  • {t}"));
                
                return result;
            }
            
            return null;
        }
    }
}
