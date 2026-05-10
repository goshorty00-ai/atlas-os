using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Markdown Preview - Convert markdown to HTML and preview.
    /// </summary>
    public static class MarkdownPreview
    {
        /// <summary>
        /// Handle markdown commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("markdown") && !lower.Contains("md to html"))
                return null;
            
            // Convert clipboard markdown to HTML
            if (lower.Contains("convert") || lower.Contains("to html") || lower.Contains("preview"))
            {
                return ConvertClipboardToHtml();
            }
            
            // Generate markdown table
            if (lower.Contains("table"))
            {
                return GenerateMarkdownTable();
            }
            
            // Markdown cheatsheet
            if (lower.Contains("cheat") || lower.Contains("help") || lower.Contains("syntax"))
            {
                return GetMarkdownCheatsheet();
            }
            
            return null;
        }
        
        private static string ConvertClipboardToHtml()
        {
            string? markdown = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    markdown = Clipboard.GetText();
            });
            
            if (string.IsNullOrEmpty(markdown))
                return "📋 Copy some markdown to clipboard first!";
            
            var html = ConvertToHtml(markdown);
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(html));
            
            var preview = html.Length > 500 ? html.Substring(0, 497) + "..." : html;
            
            return $"📝 **Markdown → HTML:**\n```html\n{preview}\n```\n✓ Copied to clipboard!";
        }
        
        private static string ConvertToHtml(string markdown)
        {
            var html = markdown;
            
            // Headers
            html = Regex.Replace(html, @"^######\s+(.+)$", "<h6>$1</h6>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^#####\s+(.+)$", "<h5>$1</h5>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^####\s+(.+)$", "<h4>$1</h4>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^###\s+(.+)$", "<h3>$1</h3>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^##\s+(.+)$", "<h2>$1</h2>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^#\s+(.+)$", "<h1>$1</h1>", RegexOptions.Multiline);
            
            // Bold and italic
            html = Regex.Replace(html, @"\*\*\*(.+?)\*\*\*", "<strong><em>$1</em></strong>");
            html = Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            html = Regex.Replace(html, @"\*(.+?)\*", "<em>$1</em>");
            html = Regex.Replace(html, @"__(.+?)__", "<strong>$1</strong>");
            html = Regex.Replace(html, @"_(.+?)_", "<em>$1</em>");
            
            // Strikethrough
            html = Regex.Replace(html, @"~~(.+?)~~", "<del>$1</del>");
            
            // Code blocks
            html = Regex.Replace(html, @"```(\w*)\n([\s\S]*?)```", "<pre><code class=\"$1\">$2</code></pre>");
            html = Regex.Replace(html, @"`(.+?)`", "<code>$1</code>");
            
            // Links and images
            html = Regex.Replace(html, @"!\[(.+?)\]\((.+?)\)", "<img src=\"$2\" alt=\"$1\">");
            html = Regex.Replace(html, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\">$1</a>");
            
            // Lists
            html = Regex.Replace(html, @"^\*\s+(.+)$", "<li>$1</li>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^-\s+(.+)$", "<li>$1</li>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^\d+\.\s+(.+)$", "<li>$1</li>", RegexOptions.Multiline);
            
            // Blockquotes
            html = Regex.Replace(html, @"^>\s+(.+)$", "<blockquote>$1</blockquote>", RegexOptions.Multiline);
            
            // Horizontal rule
            html = Regex.Replace(html, @"^---+$", "<hr>", RegexOptions.Multiline);
            html = Regex.Replace(html, @"^\*\*\*+$", "<hr>", RegexOptions.Multiline);
            
            // Paragraphs (simple)
            html = Regex.Replace(html, @"\n\n", "</p><p>");
            
            return html;
        }
        
        private static string GenerateMarkdownTable()
        {
            var table = @"| Column 1 | Column 2 | Column 3 |
|----------|----------|----------|
| Row 1    | Data     | Data     |
| Row 2    | Data     | Data     |
| Row 3    | Data     | Data     |";
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(table));
            
            return $"📊 **Markdown Table Template:**\n```markdown\n{table}\n```\n✓ Copied to clipboard!";
        }
        
        private static string GetMarkdownCheatsheet()
        {
            return "📝 **Markdown Cheatsheet:**\n\n" +
                   "**Headers:**\n" +
                   "```\n# H1  ## H2  ### H3\n```\n\n" +
                   "**Emphasis:**\n" +
                   "```\n*italic*  **bold**  ***both***  ~~strike~~\n```\n\n" +
                   "**Links & Images:**\n" +
                   "```\n[text](url)  ![alt](image.png)\n```\n\n" +
                   "**Lists:**\n" +
                   "```\n- item  * item  1. numbered\n```\n\n" +
                   "**Code:**\n" +
                   "```\n`inline`  ```block```\n```\n\n" +
                   "**Other:**\n" +
                   "```\n> quote  ---  | table |\n```";
        }
    }
}
