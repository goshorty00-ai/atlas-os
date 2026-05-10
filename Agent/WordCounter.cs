using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Word Counter - Count words, characters, sentences, reading time.
    /// </summary>
    public static class WordCounter
    {
        /// <summary>
        /// Handle word count commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("count") && !lower.Contains("word") && !lower.Contains("character") && 
                !lower.Contains("reading time") && !lower.Contains("stats"))
                return Task.FromResult<string?>(null);
            
            // Count clipboard
            if (lower.Contains("clipboard") || lower.Contains("count word") || lower.Contains("word count") ||
                lower.Contains("character count") || lower.Contains("text stats") || lower.Contains("reading time"))
            {
                return Task.FromResult<string?>(CountClipboard());
            }
            
            return Task.FromResult<string?>(null);
        }
        
        private static string CountClipboard()
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy some text to clipboard first!";
            
            var stats = AnalyzeText(text);
            
            return $"📊 **Text Statistics:**\n\n" +
                   $"**Characters:** {stats.Characters:N0}\n" +
                   $"**Characters (no spaces):** {stats.CharactersNoSpaces:N0}\n" +
                   $"**Words:** {stats.Words:N0}\n" +
                   $"**Sentences:** {stats.Sentences:N0}\n" +
                   $"**Paragraphs:** {stats.Paragraphs:N0}\n" +
                   $"**Lines:** {stats.Lines:N0}\n\n" +
                   $"**Avg word length:** {stats.AvgWordLength:F1} chars\n" +
                   $"**Avg sentence length:** {stats.AvgSentenceLength:F1} words\n\n" +
                   $"⏱️ **Reading time:** {stats.ReadingTime}\n" +
                   $"🎤 **Speaking time:** {stats.SpeakingTime}";
        }
        
        private static TextStats AnalyzeText(string text)
        {
            var stats = new TextStats();
            
            stats.Characters = text.Length;
            stats.CharactersNoSpaces = text.Count(c => !char.IsWhiteSpace(c));
            
            // Words
            var words = Regex.Matches(text, @"\b[\w']+\b");
            stats.Words = words.Count;
            
            // Sentences (end with . ! ?)
            var sentences = Regex.Matches(text, @"[.!?]+");
            stats.Sentences = Math.Max(sentences.Count, 1);
            
            // Paragraphs (separated by blank lines)
            var paragraphs = Regex.Split(text, @"\n\s*\n");
            stats.Paragraphs = paragraphs.Count(p => !string.IsNullOrWhiteSpace(p));
            
            // Lines
            stats.Lines = text.Split('\n').Length;
            
            // Averages
            if (stats.Words > 0)
            {
                var totalWordLength = words.Cast<Match>().Sum(m => m.Value.Length);
                stats.AvgWordLength = (double)totalWordLength / stats.Words;
            }
            
            if (stats.Sentences > 0)
            {
                stats.AvgSentenceLength = (double)stats.Words / stats.Sentences;
            }
            
            // Reading time (avg 200 words per minute)
            var readingMinutes = stats.Words / 200.0;
            stats.ReadingTime = FormatTime(readingMinutes);
            
            // Speaking time (avg 150 words per minute)
            var speakingMinutes = stats.Words / 150.0;
            stats.SpeakingTime = FormatTime(speakingMinutes);
            
            return stats;
        }
        
        private static string FormatTime(double minutes)
        {
            if (minutes < 1)
                return $"{(int)(minutes * 60)} seconds";
            if (minutes < 60)
                return $"{(int)minutes} min {(int)((minutes % 1) * 60)} sec";
            
            var hours = (int)(minutes / 60);
            var mins = (int)(minutes % 60);
            return $"{hours} hr {mins} min";
        }
        
        private class TextStats
        {
            public int Characters { get; set; }
            public int CharactersNoSpaces { get; set; }
            public int Words { get; set; }
            public int Sentences { get; set; }
            public int Paragraphs { get; set; }
            public int Lines { get; set; }
            public double AvgWordLength { get; set; }
            public double AvgSentenceLength { get; set; }
            public string ReadingTime { get; set; } = "";
            public string SpeakingTime { get; set; } = "";
        }
    }
}
