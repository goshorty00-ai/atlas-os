using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Lorem Ipsum Generator - Generate placeholder text.
    /// </summary>
    public static class LoremIpsum
    {
        private static readonly string[] Words = {
            "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
            "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
            "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
            "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
            "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
            "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
            "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
            "deserunt", "mollit", "anim", "id", "est", "laborum"
        };
        
        private static readonly string ClassicParagraph = 
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor " +
            "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud " +
            "exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute " +
            "irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla " +
            "pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia " +
            "deserunt mollit anim id est laborum.";
        
        /// <summary>
        /// Handle lorem ipsum commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("lorem") && !lower.Contains("placeholder") && !lower.Contains("dummy text"))
                return Task.FromResult<string?>(null);
            
            // Parse count
            var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*(word|sentence|paragraph|char)");
            
            string result;
            string type;
            
            if (match.Success)
            {
                var count = Math.Min(int.Parse(match.Groups[1].Value), 100);
                var unit = match.Groups[2].Value;
                
                result = unit switch
                {
                    "word" => GenerateWords(count),
                    "sentence" => GenerateSentences(count),
                    "paragraph" => GenerateParagraphs(count),
                    "char" => GenerateCharacters(Math.Min(count, 5000)),
                    _ => ClassicParagraph
                };
                type = $"{count} {unit}(s)";
            }
            else if (lower.Contains("word"))
            {
                result = GenerateWords(50);
                type = "50 words";
            }
            else if (lower.Contains("sentence"))
            {
                result = GenerateSentences(5);
                type = "5 sentences";
            }
            else if (lower.Contains("paragraph"))
            {
                result = GenerateParagraphs(3);
                type = "3 paragraphs";
            }
            else
            {
                result = ClassicParagraph;
                type = "classic paragraph";
            }
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(result));
            
            var preview = result.Length > 200 ? result.Substring(0, 197) + "..." : result;
            
            return Task.FromResult<string?>($"📝 **Lorem Ipsum ({type}):**\n\n{preview}\n\n✓ Full text copied to clipboard!");
        }
        
        private static string GenerateWords(int count)
        {
            var random = new Random();
            var words = new string[count];
            for (int i = 0; i < count; i++)
                words[i] = Words[random.Next(Words.Length)];
            
            // Capitalize first word
            words[0] = char.ToUpper(words[0][0]) + words[0].Substring(1);
            return string.Join(" ", words) + ".";
        }
        
        private static string GenerateSentences(int count)
        {
            var random = new Random();
            var sb = new StringBuilder();
            
            for (int i = 0; i < count; i++)
            {
                var wordCount = random.Next(8, 16);
                var words = new string[wordCount];
                for (int j = 0; j < wordCount; j++)
                    words[j] = Words[random.Next(Words.Length)];
                
                words[0] = char.ToUpper(words[0][0]) + words[0].Substring(1);
                sb.Append(string.Join(" ", words));
                sb.Append(". ");
            }
            
            return sb.ToString().Trim();
        }
        
        private static string GenerateParagraphs(int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.AppendLine(GenerateSentences(5));
                if (i < count - 1) sb.AppendLine();
            }
            return sb.ToString().Trim();
        }
        
        private static string GenerateCharacters(int count)
        {
            var text = GenerateParagraphs(10);
            while (text.Length < count)
                text += " " + GenerateParagraphs(5);
            return text.Substring(0, count);
        }
    }
}
